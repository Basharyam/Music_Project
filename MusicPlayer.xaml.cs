using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;

namespace Telhai.DotNet.PlayerProject
{
    public partial class MusicPlayer : Window
    {
        private readonly MediaPlayer mediaPlayer = new MediaPlayer();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private List<MusicTrack> library = new List<MusicTrack>();
        private bool isDragging = false;

        private const string FILE_NAME = "library.json";

        // iTunes + Cancellation
        private readonly ItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _metadataCts;

        // default cover (loaded safely after InitializeComponent)
        private BitmapImage? _defaultCover;

        public MusicPlayer()
        {
            InitializeComponent();

            // Load default cover safely (NO CRASH)
            _defaultCover = TryLoadResourceImage("pack://application:,,,/Assets/spotify.jpg");
            imgCover.Source = _defaultCover; // can be null if not found

            // Initial volume
            mediaPlayer.Volume = sliderVolume.Value;

            mediaPlayer.MediaOpened += (s, e) =>
            {
                bool hasAudio = mediaPlayer.HasAudio;
                string duration = mediaPlayer.NaturalDuration.HasTimeSpan
                    ? mediaPlayer.NaturalDuration.TimeSpan.ToString(@"mm\:ss")
                    : "unknown";
                txtStatus.Text = $"Media opened | HasAudio={hasAudio} | Duration={duration}";
            };

            mediaPlayer.MediaFailed += (s, e) =>
            {
                txtStatus.Text = "Media failed: " + e.ErrorException.Message;
            };

            mediaPlayer.MediaEnded += (s, e) =>
            {
                timer.Stop();
                sliderProgress.Value = 0;
                txtStatus.Text = "Ended";
            };

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += Timer_Tick;

            // Default UI state
            txtTrackName.Text = "-";
            txtArtistName.Text = "-";
            txtAlbumName.Text = "-";
            txtFilePath.Text = "-";
            txtStatus.Text = "Ready";

            LoadLibrary();
        }

        private BitmapImage? TryLoadResourceImage(string packUri)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(packUri, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                // If BuildAction/Path is wrong => do not crash
                return null;
            }
        }

        // --------------------------
        // UI BUTTONS
        // --------------------------
        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack)
            {
                PlaySelectedTrack();
                return;
            }

            if (mediaPlayer.Source != null)
            {
                mediaPlayer.Volume = sliderVolume.Value;
                mediaPlayer.Play();
                timer.Start();
                txtStatus.Text = "Playing";
            }
            else
            {
                txtStatus.Text = "Choose a song first";
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Pause();
            txtStatus.Text = "Paused";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Stop();
            timer.Stop();
            sliderProgress.Value = 0;
            txtStatus.Text = "Stopped";
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            mediaPlayer.Volume = sliderVolume.Value;
        }

        // --------------------------
        // LIBRARY MANAGEMENT
        // --------------------------
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "MP3 Files|*.mp3"
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    MusicTrack track = new MusicTrack
                    {
                        Title = Path.GetFileNameWithoutExtension(file),
                        FilePath = file
                    };
                    library.Add(track);
                }

                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                library.Remove(track);
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        // Single click: show name + path + default cover (no playback)
        private void LstLibrary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                txtCurrentSong.Text = track.Title;
                txtFilePath.Text = track.FilePath;

                imgCover.Source = _defaultCover;

                txtTrackName.Text = track.Title;
                txtArtistName.Text = "-";
                txtAlbumName.Text = "-";
                txtStatus.Text = "Selected";
            }
        }

        // Double click: play + metadata
        private void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            PlaySelectedTrack();
        }

        private void PlaySelectedTrack()
        {
            if (lstLibrary.SelectedItem is not MusicTrack track)
            {
                txtStatus.Text = "Choose a song first";
                return;
            }

            if (!File.Exists(track.FilePath))
            {
                txtStatus.Text = "File not found: " + track.FilePath;
                return;
            }

            // Cancel previous metadata request
            _metadataCts?.Cancel();
            _metadataCts = new CancellationTokenSource();
            var token = _metadataCts.Token;

            // Clean start
            mediaPlayer.Stop();

            // show default immediately
            imgCover.Source = _defaultCover;

            // Play immediately
            mediaPlayer.Volume = sliderVolume.Value;
            mediaPlayer.Open(new Uri(track.FilePath, UriKind.Absolute));
            mediaPlayer.Play();
            timer.Start();

            // Base UI update
            txtCurrentSong.Text = track.Title;
            txtFilePath.Text = track.FilePath;
            txtTrackName.Text = track.Title;
            txtArtistName.Text = "-";
            txtAlbumName.Text = "-";
            txtStatus.Text = "Playing + searching iTunes...";

            _ = LoadMetadataAsync(track, token);
        }

        private async Task LoadMetadataAsync(MusicTrack track, CancellationToken token)
        {
            try
            {
                string term = BuildSearchTerm(track);

                ItunesTrackInfo? info = await _itunesService.SearchOneAsync(term, token);

                if (token.IsCancellationRequested) return;

                if (info == null)
                {
                    txtStatus.Text = "No iTunes results. Showing file info only.";
                    txtTrackName.Text = Path.GetFileNameWithoutExtension(track.FilePath);
                    txtArtistName.Text = "-";
                    txtAlbumName.Text = "-";
                    imgCover.Source = _defaultCover;
                    return;
                }

                txtTrackName.Text = string.IsNullOrWhiteSpace(info.TrackName) ? track.Title : info.TrackName;
                txtArtistName.Text = string.IsNullOrWhiteSpace(info.ArtistName) ? "-" : info.ArtistName;
                txtAlbumName.Text = string.IsNullOrWhiteSpace(info.AlbumName) ? "-" : info.AlbumName;

                // cover
                if (!string.IsNullOrWhiteSpace(info.ArtworkUrl))
                {
                    var bmp = await LoadImageFromUrlAsync(info.ArtworkUrl, token);
                    if (!token.IsCancellationRequested && bmp != null)
                        imgCover.Source = bmp;
                    else
                        imgCover.Source = _defaultCover;
                }
                else
                {
                    imgCover.Source = _defaultCover;
                }


                if (!txtStatus.Text.StartsWith("Media opened"))
                    txtStatus.Text = "Playing";
            }
            catch (OperationCanceledException)
            {
                // expected when changing songs
            }
            catch (Exception ex)
            {
                txtStatus.Text = "iTunes error: " + ex.Message + " | Showing file info only.";
                txtTrackName.Text = Path.GetFileNameWithoutExtension(track.FilePath);
                txtArtistName.Text = "-";
                txtAlbumName.Text = "-";
                imgCover.Source = _defaultCover;
            }
        }

        private string BuildSearchTerm(MusicTrack track)
        {
            string name = Path.GetFileNameWithoutExtension(track.FilePath);

            name = name.Replace("-", " ").Replace("_", " ");
            name = Regex.Replace(name, @"\[(.*?)\]", " ");
            name = Regex.Replace(name, @"\((.*?)\)", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim();

            return name;
        }

        private Task<BitmapImage?> LoadImageFromUrlAsync(string url, CancellationToken token)
        {
            // must run on UI thread; this method is awaited from UI context
            token.ThrowIfCancellationRequested();

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                return Task.FromResult<BitmapImage?>(bmp);
            }
            catch
            {
                return Task.FromResult<BitmapImage?>(null);
            }
        }


        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (mediaPlayer.Source != null && mediaPlayer.NaturalDuration.HasTimeSpan && !isDragging)
            {
                sliderProgress.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                sliderProgress.Value = mediaPlayer.Position.TotalSeconds;
            }
        }

        private void Slider_DragStarted(object sender, MouseButtonEventArgs e) => isDragging = true;

        private void Slider_DragCompleted(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            mediaPlayer.Position = TimeSpan.FromSeconds(sliderProgress.Value);
        }

        private void UpdateLibraryUI()
        {
            lstLibrary.ItemsSource = null;
            lstLibrary.ItemsSource = library;
        }

        private void SaveLibrary()
        {
            string json = JsonSerializer.Serialize(library);
            File.WriteAllText(FILE_NAME, json);
        }

        private void LoadLibrary()
        {
            if (File.Exists(FILE_NAME))
            {
                string json = File.ReadAllText(FILE_NAME);
                library = JsonSerializer.Deserialize<List<MusicTrack>>(json) ?? new List<MusicTrack>();
                UpdateLibraryUI();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _metadataCts?.Cancel();
            base.OnClosed(e);
        }
    }
}
