using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Dosyaktar.Services
{
    public enum HistoryDirection { Sent, Received }

    public sealed class HistoryEntry
    {
        public string          Title     { get; set; } = "";
        public string          Detail    { get; set; } = "";
        public string          Path      { get; set; } = "";
        public HistoryDirection Direction { get; set; } = HistoryDirection.Sent;
        public DateTime        When      { get; set; } = DateTime.Now;
        public bool            Success   { get; set; } = true;

        public string Icon => Direction == HistoryDirection.Sent ? "\uE898" : "\uE896";
        public string WhenText => When.Date == DateTime.Today
            ? When.ToString("HH:mm")
            : When.ToString("dd MMM · HH:mm");
    }

    public static class HistoryService
    {
        private const int MaxEntries = 250;

        private static readonly string _dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dosyaktar");

        private static readonly string _path = Path.Combine(_dir, "history.json");

        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        private static List<HistoryEntry>? _cache;

        public static IReadOnlyList<HistoryEntry> Load()
        {
            if (_cache != null) return _cache;

            try
            {
                if (!File.Exists(_path))
                {
                    _cache = new List<HistoryEntry>();
                    return _cache;
                }

                _cache = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path), _opts)
                         ?? new List<HistoryEntry>();
            }
            catch
            {
                _cache = new List<HistoryEntry>();
            }

            return _cache;
        }

        public static void Add(HistoryEntry entry)
        {
            var list = Load().ToList();
            list.Insert(0, entry);

            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);

            _cache = list;
            Save();
        }

        public static void Clear()
        {
            _cache = new List<HistoryEntry>();
            Save();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(_cache ?? new List<HistoryEntry>(), _opts));
            }
            catch { }
        }
    }
}
