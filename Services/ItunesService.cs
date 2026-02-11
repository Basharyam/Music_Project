using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    // service calling ituners search API
    public class ItunesService
    {

        // init http client with prefix base domain
        private static readonly HttpClient _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://itunes.apple.com/")
        };


        public async Task<ItunesTrackInfo?> SearchOneAsync(
            string songTitle,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(songTitle))
                return null;

            string encodedTerm = Uri.EscapeDataString(songTitle);
            string url = $"search?term={encodedTerm}&media=music&limit=1";

            using HttpResponseMessage response =
                await _httpClient.GetAsync(url, cancellationToken);

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            var data = JsonSerializer.Deserialize<ItunesSearchResponse>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            var item = data?.Results?.FirstOrDefault();
            if (item == null)
                return null;

            return new ItunesTrackInfo
            {
                TrackName = item.TrackName,
                ArtistName = item.ArtistName,
                AlbumName = item.CollectionName,
                ArtworkUrl = item.ArtworkUrl100
            };
        }
    }
}
