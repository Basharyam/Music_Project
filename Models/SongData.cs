using System;
using System.Collections.Generic;

namespace Telhai.DotNet.PlayerProject.Models
{
    public class SongData
    {
        // Key (unique per song)
        public string FilePath { get; set; } = "";

        // Saved metadata (from iTunes)
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public string? AlbumName { get; set; }
        public string? ArtworkUrl { get; set; }

        // Edited by user (from editor window)
        public string? CustomTitle { get; set; }

        // User-managed images (local file paths)
        public List<string> UserImages { get; set; } = new List<string>();

        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
