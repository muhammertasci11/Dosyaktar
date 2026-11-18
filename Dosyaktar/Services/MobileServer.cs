using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Dosyaktar.Services
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Shared file (PC → phone)
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class SharedFile
    {
        public string Id     { get; init; } = "";
        public string Name   { get; init; } = "";
        public string Path   { get; init; } = "";
        public long   Size   { get; init; }
        public string SizeText => FileTransferService.FormatSize(Size);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Live transfer snapshot (phone ↔ PC)
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class MobileTransfer
    {
        public bool   Active;
        public string Direction = "up";
        public string Name = "";
        public long   Bytes;
        public long   Total;
        public double SpeedMBps;

        public int Percent => Total > 0 ? (int)Math.Min(100, Bytes * 100.0 / Total) : 0;
    }

    public sealed class MobileSession
    {
        public string   Token  { get; init; } = "";
        public string   Name   { get; set; } = "Telefon";
        public string   Address { get; init; } = "";
        public DateTime Since  { get; init; } = DateTime.UtcNow;
        public DateTime Seen   { get; set; } = DateTime.UtcNow;

        public string SinceText
        {
            get
            {
                var local = Since.ToLocalTime();
                var span  = DateTime.Now - local;
                if (span.TotalMinutes < 1)  return "az önce bağlandı";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} dk önce bağlandı";
                if (span.TotalHours   < 24) return $"{(int)span.TotalHours} sa önce bağlandı";
                return local.ToString("dd MMM HH:mm");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MobileServer — a tiny HTTP/1.1 server over raw TcpListener.
    //  Raw sockets avoid the http.sys URL ACL requirement entirely, so the
    //  bridge works without administrator rights or netsh reservations.
    // ═══════════════════════════════════════════════════════════════════════
    public sealed class MobileServer : IDisposable
    {
        public const int DefaultPort = 5050;

        private const string CookieName = "dzt";
        private const int    LockoutThreshold = 5;
        private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

        private readonly object _gate = new();
        private readonly ConcurrentDictionary<string, SharedFile> _outbox = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MobileSession> _sessions = new(StringComparer.Ordinal);
        private readonly List<MobileSession> _sessionOrder = new();
        private readonly SemaphoreSlim _uploadGate = new(4, 4);

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoop;

        private string _pin = "";
        private DateTime _pinIssuedUtc = DateTime.UtcNow;
        private DateTime _lockUntilUtc = DateTime.MinValue;
        private readonly Queue<DateTime> _failedAttempts = new();

        private readonly MobileTransfer _transfer = new();
        private long _speedBytes;
        private DateTime _speedStamp = DateTime.UtcNow;

        public int    Port        { get; private set; } = DefaultPort;
        public string SaveFolder  { get; set; } = AppSettings.DefaultSaveDir;
        public string DeviceName  { get; set; } = Environment.MachineName;
        public bool   IsRunning   => _listener != null;
        public string IpAddress   { get; private set; } = "127.0.0.1";
        public string BaseUrl     => $"http://{IpAddress}:{Port}/";

        public string Pin
        {
            get
            {
                lock (_gate)
                {
                    if (_pin.Length == 0 || DateTime.UtcNow - _pinIssuedUtc > TimeSpan.FromMinutes(10))
                        RotatePinLocked();
                    return _pin;
                }
            }
        }

        public event Action<string>?                Log;
        public event Action<SharedFile>?            FileReceived;
        public event Action<MobileTransfer>?        TransferChanged;
        public event Action?                        SessionsChanged;

        private void Trace(string message) => Log?.Invoke(message);

        // ═══════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════
        public void Start(int port)
        {
            if (_listener != null) return;

            Port = port > 0 ? port : DefaultPort;
            IpAddress = GetLocalIPv4();

            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
            }
            catch (Exception ex)
            {
                Port = FindFreePort(Port + 1);
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                Trace($"Port meşguldü, {Port} kullanılıyor. ({ex.Message})");
            }

            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));

            lock (_gate) RotatePinLocked();

            Trace($"Mobil köprü açıldı: {BaseUrl}");
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;

            lock (_gate)
            {
                _sessions.Clear();
                _sessionOrder.Clear();
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _uploadGate.Dispose();
        }

        private static int FindFreePort(int start)
        {
            for (int p = start; p < start + 40; p++)
            {
                TcpListener? probe = null;
                try
                {
                    probe = new TcpListener(IPAddress.Any, p);
                    probe.Start();
                    return p;
                }
                catch { }
                finally { try { probe?.Stop(); } catch { } }
            }
            return DefaultPort;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PIN / SESSIONS
        // ═══════════════════════════════════════════════════════════════════
        public string RefreshPin()
        {
            lock (_gate)
            {
                RotatePinLocked();
                return _pin;
            }
        }

        private void RotatePinLocked()
        {
            _pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
            _pinIssuedUtc = DateTime.UtcNow;
        }

        private static string NewToken() =>
            Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();

        public IReadOnlyList<MobileSession> Sessions
        {
            get { lock (_gate) return _sessionOrder.ToList(); }
        }

        public void Disconnect(MobileSession session)
        {
            lock (_gate)
            {
                _sessions.Remove(session.Token);
                _sessionOrder.Remove(session);
            }
            SessionsChanged?.Invoke();
        }

        public void DisconnectAll()
        {
            lock (_gate)
            {
                _sessions.Clear();
                _sessionOrder.Clear();
            }
            SessionsChanged?.Invoke();
        }

        private bool TryAuthorize(string? token, out MobileSession? session)
        {
            session = null;
            if (string.IsNullOrEmpty(token)) return false;

            lock (_gate)
            {
                if (!_sessions.TryGetValue(token, out var found)) return false;
                if (DateTime.UtcNow - found.Seen > SessionLifetime)
                {
                    _sessions.Remove(token);
                    _sessionOrder.Remove(found);
                    return false;
                }
                found.Seen = DateTime.UtcNow;
                session = found;
                return true;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  OUTBOX
        // ═══════════════════════════════════════════════════════════════════
        public SharedFile? AddSharedFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return null;

                var item = new SharedFile
                {
                    Id   = Guid.NewGuid().ToString("N")[..10],
                    Name = info.Name,
                    Path = info.FullName,
                    Size = info.Length
                };
                _outbox[item.Id] = item;
                return item;
            }
            catch { return null; }
        }

        public IReadOnlyList<SharedFile> SharedFiles =>
            _outbox.Values.OrderByDescending(f => f.Size).ToList();

        public void ClearSharedFiles() => _outbox.Clear();

        public void RemoveSharedFile(string id) => _outbox.TryRemove(id, out _);

        // ═══════════════════════════════════════════════════════════════════
        //  ACCEPT LOOP
        // ═══════════════════════════════════════════════════════════════════
        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) when (ct.IsCancellationRequested) { return; }
                catch (SocketException) { await Task.Delay(60, ct).ConfigureAwait(false); continue; }

                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 60000;

                NetworkStream? stream = null;
                try
                {
                    stream = client.GetStream();
                    var reader = new HttpReader(stream);

                    string? requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(requestLine)) return;

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 3) return;

                    string method = parts[0].ToUpperInvariant();
                    string target = parts[1];
                    string path   = target;
                    string query  = "";

                    int qi = target.IndexOf('?');
                    if (qi >= 0)
                    {
                        path  = target[..qi];
                        query = target[(qi + 1)..];
                    }

                    // headers
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct).ConfigureAwait(false)))
                    {
                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;
                        headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                    }

                    long contentLength = 0;
                    if (headers.TryGetValue("Content-Length", out var cl) &&
                        long.TryParse(cl, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        contentLength = parsed;

                    if (headers.TryGetValue("Expect", out var expect) &&
                        expect.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
                    {
                        var cont = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                        await stream.WriteAsync(cont, ct).ConfigureAwait(false);
                    }

                    await RouteAsync(stream, reader, method, path, query, headers, contentLength, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (SocketException) { }
                catch (Exception ex)
                {
                    Trace($"İstek hatası: {ex.Message}");
                    try
                    {
                        if (stream != null)
                            await WriteJsonAsync(stream, 500, new { ok = false, error = "Sunucu hatası" }, ct)
                                .ConfigureAwait(false);
                    }
                    catch { }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  ROUTING
        // ═══════════════════════════════════════════════════════════════════
        private async Task RouteAsync(
            Stream stream, HttpReader reader, string method, string path,
            string query, Dictionary<string, string> headers, long contentLength, CancellationToken ct)
        {
            string? token = ExtractCookie(headers, CookieName);

            // ─── static shell ────────────────────────────────────────────────
            if (method == "GET" && (path == "/" || path == "/index.html"))
            {
                var body = ReadAsset("index.html");
                if (body == null)
                {
                    await WriteHtmlAsync(stream, 503, FallbackPage(), ct).ConfigureAwait(false);
                    return;
                }
                await WriteBytesAsync(stream, 200, "text/html; charset=utf-8", body, ct, "no-cache").ConfigureAwait(false);
                return;
            }

            if (method == "GET" && path == "/manifest.webmanifest")
            {
                var body = ReadAsset("manifest.webmanifest");
                if (body == null) { await WriteNotFoundAsync(stream, ct).ConfigureAwait(false); return; }
                await WriteBytesAsync(stream, 200, "application/manifest+json; charset=utf-8", body, ct, "max-age=600").ConfigureAwait(false);
                return;
            }

            if (method == "GET" && path == "/icon.png")
            {
                var body = ReadAppAsset("icon.png");
                if (body == null) { await WriteNotFoundAsync(stream, ct).ConfigureAwait(false); return; }
                await WriteBytesAsync(stream, 200, "image/png", body, ct, "max-age=86400").ConfigureAwait(false);
                return;
            }

            if (method == "GET" && path == "/favicon.svg")
            {
                var body = Encoding.UTF8.GetBytes(PrismSvg);
                await WriteBytesAsync(stream, 200, "image/svg+xml", body, ct, "max-age=86400").ConfigureAwait(false);
                return;
            }

            // ─── api ─────────────────────────────────────────────────────────
            if (path == "/api/state" && method == "GET")
            {
                bool paired = TryAuthorize(token, out var session);
                var payload = BuildState(paired, session);
                await WriteJsonAsync(stream, 200, payload, ct).ConfigureAwait(false);
                return;
            }

            if (path == "/api/pin" && method == "POST")
            {
                var body = await reader.ReadBodyAsync(contentLength, ct).ConfigureAwait(false);
                string pin = ExtractString(body, "pin");
                await HandleLoginAsync(stream, pin, ExtractString(body, "name"), ct).ConfigureAwait(false);
                return;
            }

            if (path == "/api/login" && method == "POST")
            {
                var body = await reader.ReadBodyAsync(contentLength, ct).ConfigureAwait(false);
                string pin  = ExtractString(body, "pin");
                string name = ExtractString(body, "name");
                await HandleLoginAsync(stream, pin, name, ct).ConfigureAwait(false);
                return;
            }

            // Everything below requires a session.
            if (!TryAuthorize(token, out var active))
            {
                await WriteJsonAsync(stream, 401, new { ok = false, error = "Oturum gerekli", paired = false }, ct)
                    .ConfigureAwait(false);
                return;
            }

            if (path == "/api/logout" && method == "POST")
            {
                lock (_gate)
                {
                    _sessions.Remove(active!.Token);
                    _sessionOrder.Remove(active);
                }
                SessionsChanged?.Invoke();
                await WriteJsonAsync(stream, 200, new { ok = true }, ct,
                    $"{CookieName}=; Path=/; HttpOnly; Max-Age=0; SameSite=Lax").ConfigureAwait(false);
                return;
            }

            if (path == "/api/files" && method == "GET")
            {
                var files = SharedFiles.Select(ToFileJson).ToArray();
                await WriteJsonAsync(stream, 200, new { files }, ct).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/api/download/", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                string id = Uri.UnescapeDataString(path["/api/download/".Length..]);
                if (!_outbox.TryGetValue(id, out var file) || !File.Exists(file.Path))
                {
                    await WriteJsonAsync(stream, 404, new { ok = false, error = "Dosya bulunamadı" }, ct)
                        .ConfigureAwait(false);
                    return;
                }
                await StreamDownloadAsync(stream, file, headers, ct).ConfigureAwait(false);
                return;
            }

            if (path == "/api/upload" && method == "PUT")
            {
                var q = ParseQuery(query);
                q.TryGetValue("name", out var rawName);
                q.TryGetValue("path", out var relPath);

                await ReceiveUploadAsync(stream, reader, rawName ?? "dosya", relPath, contentLength, ct)
                    .ConfigureAwait(false);
                return;
            }

            await WriteNotFoundAsync(stream, ct).ConfigureAwait(false);
        }

        // ─── login ───────────────────────────────────────────────────────────
        private async Task HandleLoginAsync(Stream stream, string pin, string? name, CancellationToken ct)
        {
            CleanupAttempts();

            DateTime now = DateTime.UtcNow;
            if (_lockUntilUtc > now)
            {
                int wait = Math.Max(1, (int)(_lockUntilUtc - now).TotalSeconds);
                await WriteJsonAsync(stream, 429,
                    new { ok = false, error = $"Çok fazla deneme. {wait} saniye sonra tekrar deneyin." }, ct)
                    .ConfigureAwait(false);
                return;
            }

            string cleanPin = new string((pin ?? "").Where(char.IsDigit).ToArray());

            if (cleanPin.Length != 6 || !string.Equals(cleanPin, Pin, StringComparison.Ordinal))
            {
                _failedAttempts.Enqueue(now);
                if (_failedAttempts.Count >= LockoutThreshold)
                {
                    _lockUntilUtc = now.AddMinutes(1);
                    _failedAttempts.Clear();
                }
                await WriteJsonAsync(stream, 401, new { ok = false, error = "PIN hatalı" }, ct).ConfigureAwait(false);
                return;
            }

            _failedAttempts.Clear();
            RotatePinAfterUse();

            string sessionName = string.IsNullOrWhiteSpace(name) ? "Telefon" : name!.Trim();
            if (sessionName.Length > 40) sessionName = sessionName[..40];

            var session = new MobileSession
            {
                Token   = NewToken(),
                Name    = sessionName,
                Address = "yerel ağ",
                Since   = now,
                Seen    = now
            };

            lock (_gate)
            {
                _sessions[session.Token] = session;
                _sessionOrder.Add(session);
            }

            SessionsChanged?.Invoke();
            Trace($"{sessionName} bağlandı.");

            await WriteJsonAsync(stream, 200, new { ok = true, token = session.Token, name = session.Name }, ct,
                $"{CookieName}={session.Token}; Path=/; HttpOnly; Max-Age={((int)SessionLifetime.TotalSeconds)}; SameSite=Lax")
                .ConfigureAwait(false);
        }

        private void RotatePinAfterUse()
        {
            lock (_gate) RotatePinLocked();
        }

        private void CleanupAttempts()
        {
            DateTime cutoff = DateTime.UtcNow - LockoutWindow;
            while (_failedAttempts.Count > 0 && _failedAttempts.Peek() < cutoff)
                _failedAttempts.Dequeue();
        }

        // ─── upload ──────────────────────────────────────────────────────────
        private async Task ReceiveUploadAsync(
            Stream stream, HttpReader reader, string rawName, string? relPath, long contentLength, CancellationToken ct)
        {
            await _uploadGate.WaitAsync(ct).ConfigureAwait(false);
            string? destination = null;

            try
            {
                if (contentLength <= 0)
                {
                    await WriteJsonAsync(stream, 400, new { ok = false, error = "Boş dosya" }, ct).ConfigureAwait(false);
                    return;
                }

                string safeName = SanitizeName(rawName);
                string dir = SaveFolder;
                if (!string.IsNullOrWhiteSpace(relPath))
                {
                    string cleanRel = SanitizeRelative(relPath!);
                    if (cleanRel.Length > 0) dir = Path.Combine(dir, cleanRel);
                }

                Directory.CreateDirectory(dir);
                destination = UniquePath(dir, safeName);

                BeginTransfer("up", safeName, contentLength);

                await using (var fs = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                     128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await reader.CopyToAsync(fs, contentLength, ct, AdvanceTransfer).ConfigureAwait(false);
                }

                var info = new FileInfo(destination);
                EndTransfer();

                var shared = new SharedFile
                {
                    Id   = Guid.NewGuid().ToString("N")[..10],
                    Name = Path.GetFileName(destination),
                    Path = destination,
                    Size = info.Length
                };

                Trace($"Telefondan alındı: {shared.Name} ({shared.SizeText})");
                FileReceived?.Invoke(shared);

                await WriteJsonAsync(stream, 200, new
                {
                    ok    = true,
                    saved = Path.GetFileName(destination),
                    size  = info.Length
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                EndTransfer();
                try { if (destination != null && File.Exists(destination)) File.Delete(destination); } catch { }

                await WriteJsonAsync(stream, 500, new { ok = false, error = ex.Message }, ct).ConfigureAwait(false);
            }
            finally
            {
                _uploadGate.Release();
            }
        }

        // ─── download ────────────────────────────────────────────────────────
        private async Task StreamDownloadAsync(
            Stream stream,
            SharedFile file,
            Dictionary<string, string> headers,
            CancellationToken ct)
        {
            FileStream fs;
            try
            {
                fs = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (Exception ex)
            {
                await WriteJsonAsync(stream, 404, new { ok = false, error = ex.Message }, ct).ConfigureAwait(false);
                return;
            }

            await using (fs)
            {
                long total = fs.Length;
                var (start, length, partial) = ResolveRange(headers, total);

                if (partial == RangeResult.Invalid)
                {
                    string bad =
                        "HTTP/1.1 416 Range Not Satisfiable\r\n" +
                        "Content-Range: bytes */" + total.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                        "Content-Length: 0\r\n" +
                        "Connection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(bad), ct).ConfigureAwait(false);
                    return;
                }

                string encoded = Uri.EscapeDataString(file.Name);
                string head =
                    (partial == RangeResult.Partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n") +
                    "Content-Type: application/octet-stream\r\n" +
                    "Content-Length: " + length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                    (partial == RangeResult.Partial
                        ? "Content-Range: bytes " + start.ToString(CultureInfo.InvariantCulture) + "-" +
                          (start + length - 1).ToString(CultureInfo.InvariantCulture) + "/" +
                          total.ToString(CultureInfo.InvariantCulture) + "\r\n"
                        : "") +
                    "Accept-Ranges: bytes\r\n" +
                    $"Content-Disposition: attachment; filename=\"{AsciiFallback(file.Name)}\"; filename*=UTF-8''{encoded}\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "Connection: close\r\n\r\n";

                await stream.WriteAsync(Encoding.UTF8.GetBytes(head), ct).ConfigureAwait(false);

                if (start > 0) fs.Seek(start, SeekOrigin.Begin);

                BeginTransfer("down", file.Name, length);

                var buffer = new byte[128 * 1024];
                long sent = 0;
                while (sent < length)
                {
                    int toRead = (int)Math.Min(buffer.Length, length - sent);
                    int read = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct).ConfigureAwait(false);
                    if (read <= 0) break;

                    await stream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    sent += read;
                    AdvanceTransfer(read);
                }

                EndTransfer();
                Trace($"Telefona gönderildi: {file.Name} ({FileTransferService.FormatSize(sent)})");
            }
        }

        private enum RangeResult { Full, Partial, Invalid }

        /// <summary>
        /// "Range: bytes=start-end" başlığını çözer. Aralık yoksa tüm dosya döner.
        /// </summary>
        private static (long Start, long Length, RangeResult Kind) ResolveRange(
            Dictionary<string, string> headers, long total)
        {
            if (total <= 0) return (0, 0, RangeResult.Full);

            if (!headers.TryGetValue("Range", out var raw) || string.IsNullOrWhiteSpace(raw))
                return (0, total, RangeResult.Full);

            string value = raw.Trim();
            if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                return (0, total, RangeResult.Full);

            string spec = value[6..].Split(',')[0].Trim();
            int dash = spec.IndexOf('-');
            if (dash < 0) return (0, total, RangeResult.Invalid);

            string startText = spec[..dash].Trim();
            string endText   = spec[(dash + 1)..].Trim();

            // "bytes=-500" → son 500 bayt
            if (startText.Length == 0)
            {
                if (!long.TryParse(endText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var suffix) ||
                    suffix <= 0)
                    return (0, total, RangeResult.Invalid);

                if (suffix > total) suffix = total;
                return (total - suffix, suffix, RangeResult.Partial);
            }

            if (!long.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
                start < 0 || start >= total)
                return (0, total, RangeResult.Invalid);

            long end = total - 1;
            if (endText.Length > 0 &&
                (!long.TryParse(endText, NumberStyles.Integer, CultureInfo.InvariantCulture, out end) || end < start))
                return (0, total, RangeResult.Invalid);

            if (end >= total) end = total - 1;
            return (start, end - start + 1, RangeResult.Partial);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  TRANSFER TELEMETRY
        // ═══════════════════════════════════════════════════════════════════
        private void BeginTransfer(string direction, string name, long total)
        {
            lock (_transfer)
            {
                _transfer.Active    = true;
                _transfer.Direction = direction;
                _transfer.Name      = name;
                _transfer.Bytes     = 0;
                _transfer.Total     = total;
                _transfer.SpeedMBps = 0;
                _speedBytes = 0;
                _speedStamp = DateTime.UtcNow;
            }
            TransferChanged?.Invoke(_transfer);
        }

        private void AdvanceTransfer(long delta)
        {
            lock (_transfer)
            {
                _transfer.Bytes += delta;
                _speedBytes += delta;

                double seconds = (DateTime.UtcNow - _speedStamp).TotalSeconds;
                if (seconds >= 0.5)
                {
                    _transfer.SpeedMBps = _speedBytes / seconds / (1024 * 1024);
                    _speedBytes = 0;
                    _speedStamp = DateTime.UtcNow;
                }
            }
        }

        private void EndTransfer()
        {
            lock (_transfer)
            {
                _transfer.Active = false;
                _transfer.SpeedMBps = 0;
            }
            TransferChanged?.Invoke(_transfer);
        }

        public MobileTransfer Snapshot()
        {
            lock (_transfer)
            {
                return new MobileTransfer
                {
                    Active    = _transfer.Active,
                    Direction = _transfer.Direction,
                    Name      = _transfer.Name,
                    Bytes     = _transfer.Bytes,
                    Total     = _transfer.Total,
                    SpeedMBps = _transfer.SpeedMBps
                };
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  STATE PAYLOAD
        // ═══════════════════════════════════════════════════════════════════
        private object BuildState(bool paired, MobileSession? session)
        {
            MobileTransfer snapshot = Snapshot();
            var files = SharedFiles.Select(ToFileJson).ToArray();

            return new
            {
                app     = "Dosyaktar",
                version = UpdateService.GetCurrentVersion().ToString(),
                device  = DeviceName,
                ip      = IpAddress,
                saveDir = SaveFolder,
                paired,
                session = paired && session != null
                    ? new
                    {
                        name  = session.Name,
                        since = session.Since.ToString("O", CultureInfo.InvariantCulture)
                    }
                    : null,
                transfer = snapshot.Active
                    ? new
                    {
                        active    = true,
                        direction = snapshot.Direction,
                        name      = snapshot.Name,
                        bytes     = snapshot.Bytes,
                        total     = snapshot.Total,
                        speedMBps = Math.Round(snapshot.SpeedMBps, 2),
                        percent   = snapshot.Percent
                    }
                    : null,
                files
            };
        }

        private static object ToFileJson(SharedFile f) => new
        {
            id       = f.Id,
            name     = f.Name,
            size     = f.Size,
            sizeText = f.SizeText
        };

        // ═══════════════════════════════════════════════════════════════════
        //  HELPERS — filesystem
        // ═══════════════════════════════════════════════════════════════════
        private static string SanitizeName(string raw)
        {
            string name = raw.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name[(slash + 1)..];

            foreach (char bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');

            name = name.Trim().Trim('.');
            if (name.Length == 0) name = "dosya";
            if (name.Length > 180) name = name[..180];

            return name;
        }

        private static string SanitizeRelative(string raw)
        {
            var segments = raw.Replace('\\', '/')
                              .Split('/', StringSplitOptions.RemoveEmptyEntries)
                              .Select(SanitizeName)
                              .Where(s => s.Length > 0 && s != "." && s != "..")
                              .Take(12);

            return string.Join(Path.DirectorySeparatorChar, segments);
        }

        private static string UniquePath(string dir, string name)
        {
            string candidate = Path.Combine(dir, name);
            if (!File.Exists(candidate)) return candidate;

            string stem = Path.GetFileNameWithoutExtension(name);
            string ext  = Path.GetExtension(name);

            for (int i = 2; i < 5000; i++)
            {
                candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }

            return Path.Combine(dir, $"{stem}-{Guid.NewGuid():N}{ext}");
        }

        private static string AsciiFallback(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(c < 128 && c != '"' && c != '\\' ? c : '_');
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HELPERS — protocol
        // ═══════════════════════════════════════════════════════════════════
        private static string? ExtractCookie(Dictionary<string, string> headers, string name)
        {
            if (!headers.TryGetValue("Cookie", out var raw)) return null;

            foreach (string part in raw.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (part[..eq].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return part[(eq + 1)..].Trim();
            }
            return null;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return result;

            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    result[Uri.UnescapeDataString(pair)] = "";
                    continue;
                }
                string key = Uri.UnescapeDataString(pair[..eq]);
                string val = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                result[key] = val;
            }
            return result;
        }

        private static string ExtractString(string json, string property)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return "";
                if (doc.RootElement.TryGetProperty(property, out var el))
                {
                    return el.ValueKind switch
                    {
                        JsonValueKind.String => el.GetString() ?? "",
                        JsonValueKind.Number => el.GetRawText(),
                        _ => ""
                    };
                }
            }
            catch { }
            return "";
        }

        private static readonly string PrismSvg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'>" +
            "<stop offset='0' stop-color='%237C5CFF'/><stop offset='1' stop-color='%2310D4E8'/></linearGradient></defs>" +
            "<rect width='64' height='64' rx='16' fill='url(%23g)'/>" +
            "<path d='M32 14l14 8v20l-14 8-14-8V22z' fill='none' stroke='white' stroke-width='3.5' stroke-linejoin='round'/>" +
            "<path d='M32 30l14-8M32 30v20M32 30l-14-8' stroke='white' stroke-width='2.4' opacity='.75'/></svg>";

        // ═══════════════════════════════════════════════════════════════════
        //  HELPERS — HTTP writing
        // ═══════════════════════════════════════════════════════════════════
        private static async Task WriteBytesAsync(
            Stream stream, int status, string contentType, byte[] body,
            CancellationToken ct, string cache = "no-store", string? setCookie = null)
        {
            var head = new StringBuilder(256);
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            head.Append("Cache-Control: ").Append(cache).Append("\r\n");
            head.Append("X-Content-Type-Options: nosniff\r\n");
            if (setCookie != null) head.Append("Set-Cookie: ").Append(setCookie).Append("\r\n");
            head.Append("Connection: close\r\n\r\n");

            await stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()), ct).ConfigureAwait(false);
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        private static Task WriteJsonAsync(Stream stream, int status, object payload, CancellationToken ct, string? setCookie = null)
        {
            string json = JsonSerializer.Serialize(payload);
            return WriteBytesAsync(stream, status, "application/json; charset=utf-8",
                Encoding.UTF8.GetBytes(json), ct, "no-store", setCookie);
        }

        private static Task WriteHtmlAsync(Stream stream, int status, string html, CancellationToken ct)
            => WriteBytesAsync(stream, status, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), ct, "no-store");

        private static Task WriteNotFoundAsync(Stream stream, CancellationToken ct)
            => WriteJsonAsync(stream, 404, new { ok = false, error = "Bulunamadı" }, ct);

        private static string Reason(int status) => status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _   => "OK"
        };

        // ─── pack-URI assets ─────────────────────────────────────────────────
        private static byte[]? ReadAppAsset(string relative)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Assets/{relative}", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                if (info == null) return null;

                using var s = info.Stream;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        private static byte[]? ReadAsset(string name) => ReadAppAsset("mobile/" + name);

        private static string FallbackPage() => """
            <!DOCTYPE html><html lang="tr"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Dosyaktar</title>
            <style>
            body{margin:0;min-height:100vh;display:grid;place-items:center;
            background:linear-gradient(160deg,#fbfaff,#eef3ff);font-family:Segoe UI,system-ui,sans-serif;color:#14112b}
            .card{max-width:380px;padding:32px;border-radius:24px;background:#fff;border:1px solid #fff;
            box-shadow:0 30px 70px -30px rgba(48,20,140,.45);text-align:center}
            h1{font-size:20px;margin:0 0 10px}p{color:#5f5b7c;font-size:14px;line-height:1.6;margin:0}
            </style></head><body><div class="card">
            <h1>Mobil arayüz yüklenemedi</h1>
            <p>Uygulama dosyaları eksik. Masaüstü uygulamasını yeniden yükleyin.</p>
            </div></body></html>
            """;

        // ═══════════════════════════════════════════════════════════════════
        //  NETWORK HELPER
        // ═══════════════════════════════════════════════════════════════════
        public static string GetLocalIPv4()
        {
            // Tek doğruluk kaynağı: NetworkManager. Böylece keşif ekranı ile mobil
            // köprünün gösterdiği adres aynı kartı seçer ve QR erişilebilir olur.
            return NetworkManager.GetLocalIP();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  BUFFERED HTTP READER
        // ═══════════════════════════════════════════════════════════════════
        private sealed class HttpReader
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer = new byte[16 * 1024];
            private int _pos;
            private int _len;

            public HttpReader(Stream stream) => _stream = stream;

            public async Task<string?> ReadLineAsync(CancellationToken ct)
            {
                var sb = new StringBuilder(96);

                while (true)
                {
                    if (_pos >= _len)
                    {
                        _len = await _stream.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
                        _pos = 0;
                        if (_len <= 0) return sb.Length > 0 ? sb.ToString() : null;
                    }

                    byte b = _buffer[_pos++];
                    if (b == (byte)'\n')
                    {
                        if (sb.Length > 0 && sb[^1] == '\r') sb.Length--;
                        return sb.ToString();
                    }

                    if (sb.Length < 8192) sb.Append((char)b);
                }
            }

            public async Task<string> ReadBodyAsync(long contentLength, CancellationToken ct)
            {
                if (contentLength <= 0) return "";
                if (contentLength > 256 * 1024)
                    throw new InvalidOperationException("Gövde çok büyük");

                using var ms = new MemoryStream((int)contentLength);
                await CopyToAsync(ms, contentLength, ct, null).ConfigureAwait(false);
                return Encoding.UTF8.GetString(ms.ToArray());
            }

            public async Task CopyToAsync(Stream destination, long count, CancellationToken ct, Action<long>? onChunk)
            {
                long left = count;

                while (left > 0 && _pos < _len)
                {
                    int take = (int)Math.Min(left, _len - _pos);
                    await destination.WriteAsync(_buffer.AsMemory(_pos, take), ct).ConfigureAwait(false);
                    _pos += take;
                    left -= take;
                    onChunk?.Invoke(take);
                }

                if (left <= 0)
                {
                    await destination.FlushAsync(ct).ConfigureAwait(false);
                    return;
                }

                var chunk = new byte[128 * 1024];

                while (left > 0)
                {
                    int want = (int)Math.Min(chunk.Length, left);
                    int read = await _stream.ReadAsync(chunk.AsMemory(0, want), ct).ConfigureAwait(false);
                    if (read <= 0) throw new IOException("Bağlantı beklenmedik şekilde kapandı.");

                    await destination.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
                    left -= read;
                    onChunk?.Invoke(read);
                }

                await destination.FlushAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
