using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Telhai.DotNet.PlayerProject.Models
{
    public class ItunesSearchResponse
    {
        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }

        [JsonPropertyName("results")]
        public List<ItunesResultItem>? Results { get; set; }
    }
}
