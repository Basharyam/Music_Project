using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    public class SongDataStore
    {
        private const string FileName = "songdata.json";
        private readonly string _filePath;
        private readonly object _lock = new object();

        // In-memory cache
        private List<SongData> _items = new List<SongData>();

        public string StorePath => _filePath;

        public SongDataStore()
        {
            // Stable location (requirement-friendly):
            // C:\Users\<you>\AppData\Local\Telhai.DotNet.PlayerProject\
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Telhai.DotNet.PlayerProject");

            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, FileName);

            LoadOrCreate();
        }

        private void LoadOrCreate()
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                {
                    _items = new List<SongData>();
                    SaveLocked(); // create empty file immediately
                    return;
                }

                try
                {
                    string json = File.ReadAllText(_filePath);
                    _items = JsonSerializer.Deserialize<List<SongData>>(json) ?? new List<SongData>();
                }
                catch
                {
                    // If file corrupted: reset (still meets requirements)
                    _items = new List<SongData>();
                    SaveLocked();
                }
            }
        }

        public SongData? GetByFilePath(string filePath)
        {
            lock (_lock)
            {
                return _items.FirstOrDefault(x =>
                    string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void Upsert(SongData data)
        {
            lock (_lock)
            {
                int idx = _items.FindIndex(x =>
                    string.Equals(x.FilePath, data.FilePath, StringComparison.OrdinalIgnoreCase));

                data.LastUpdatedUtc = DateTime.UtcNow;

                if (idx >= 0)
                    _items[idx] = data;
                else
                    _items.Add(data);

                SaveLocked();
            }
        }

        private void SaveLocked()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_items, options);
            File.WriteAllText(_filePath, json);
        }
    }
}
