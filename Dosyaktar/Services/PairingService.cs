using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dosyaktar.Services
{
    public sealed class PairingService : IDisposable
    {
        public const int PairingPort = 5003;

        public event EventHandler<(string IP, string Hostname, string Pin)>? PairingRequested;
        public event EventHandler<(string IP, bool Accepted)>? PairingResponseReceived;
        public event EventHandler<string>? LogMessage;

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public void StartListening()
        {
            if (_listener != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                _listener = new TcpListener(IPAddress.Any, PairingPort);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                Task.Run(() => ListenLoopAsync(_cts.Token));
                Log("Eşleştirme servisi dinliyor...");
            }
            catch (Exception ex)
            {
                Log($"Eşleştirme servisi başlatılamadı: {ex.Message}");
            }
        }

        public void StopListening()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _listener!.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client, token));
                }
            }
            catch (Exception ex) when (!(ex is ObjectDisposedException || ex is OperationCanceledException))
            {
                Log($"Dinleme hatası: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    
                    string ip = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                    string? line = await reader.ReadLineAsync();
                    
                    if (string.IsNullOrEmpty(line)) return;

                    var parts = line.Split('|');
                    if (parts[0] == "REQUEST" && parts.Length >= 3)
                    {
                        string pin = parts[1];
                        string host = parts[2];
                        PairingRequested?.Invoke(this, (ip, host, pin));
                    }
                    else if (parts[0] == "ACCEPT")
                    {
                        PairingResponseReceived?.Invoke(this, (ip, true));
                    }
                    else if (parts[0] == "REJECT")
                    {
                        PairingResponseReceived?.Invoke(this, (ip, false));
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"İstemci işlenirken hata: {ex.Message}");
            }
        }

        public async Task<bool> SendRequestAsync(string targetIP, string pin, string hostname)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(targetIP), PairingPort);
                await using var stream = client.GetStream();
                byte[] data = Encoding.UTF8.GetBytes($"REQUEST|{pin}|{hostname}\n");
                await stream.WriteAsync(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log($"İstek gönderme hatası ({targetIP}): {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendResponseAsync(string targetIP, bool accept)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Parse(targetIP), PairingPort);
                await using var stream = client.GetStream();
                string msg = accept ? "ACCEPT\n" : "REJECT\n";
                byte[] data = Encoding.UTF8.GetBytes(msg);
                await stream.WriteAsync(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Yanıt gönderme hatası ({targetIP}): {ex.Message}");
                return false;
            }
        }

        private void Log(string msg) => LogMessage?.Invoke(this, msg);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopListening();
        }
    }
}
