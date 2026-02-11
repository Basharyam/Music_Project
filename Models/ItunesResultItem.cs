using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Telhai.DotNet.PlayerProject.Models
{
    public class ItunesResultItem
    {
        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("collectionName")]
        public string? CollectionName { get; set; }

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; set; }
    }
}
