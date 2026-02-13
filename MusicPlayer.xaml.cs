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
using Telhai.DotNet.PlayerProject.ViewModels;

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

        // Song metadata cache store (songdata.json)
        private readonly SongDataStore _songStore = new SongDataStore();

        // Default cover (resource)
        private BitmapImage? _defaultCover;

        // Slideshow timer for user images
        private readonly DispatcherTimer _slideshowTimer = new DispatcherTimer();
        private int _slideshowIndex = 0;
        private List<string> _slideshowImages = new List<string>();
        private CancellationTokenSource? _coverCts;

        public MusicPlayer()
        {
            InitializeComponent();

            // Load default cover safely (NO CRASH)
            _defaultCover = TryLoadResourceImage("pack://application:,,,/Assets/spotify.jpg");
            imgCover.Source = _defaultCover;

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
                StopSlideshow();
                txtStatus.Text = "Ended";
            };

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += Timer_Tick;

            // Slideshow timer (every 3 seconds)
            _slideshowTimer.Interval = TimeSpan.FromSeconds(3);
            _slideshowTimer.Tick += SlideshowTimer_Tick;

            // Default UI state
            txtTrackName.Text = "-";
            txtArtistName.Text = "-";
            txtAlbumName.Text = "-";
            txtFilePath.Text = "-";
            txtStatus.Text = "Ready";

            LoadLibrary();
        }

        // ------------------------------------
        // Helpers
        // ------------------------------------
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
                return null;
            }
        }

        private bool HasAnyMetadata(SongData d)
        {
            return !string.IsNullOrWhiteSpace(d.CustomTitle)
                || !string.IsNullOrWhiteSpace(d.TrackName)
                || !string.IsNullOrWhiteSpace(d.ArtistName)
                || !string.IsNullOrWhiteSpace(d.AlbumName)
                || !string.IsNullOrWhiteSpace(d.ArtworkUrl);
        }

        private void ApplySongDataToUI(MusicTrack track, SongData data)
        {
            // title priority: CustomTitle -> TrackName -> local title
            txtTrackName.Text =
                !string.IsNullOrWhiteSpace(data.CustomTitle) ? data.CustomTitle :
                !string.IsNullOrWhiteSpace(data.TrackName) ? data.TrackName :
                track.Title;

            txtArtistName.Text = string.IsNullOrWhiteSpace(data.ArtistName) ? "-" : data.ArtistName;
            txtAlbumName.Text = string.IsNullOrWhiteSpace(data.AlbumName) ? "-" : data.AlbumName;

            // local path always shown
            txtFilePath.Text = track.FilePath;
        }

        private void StopSlideshow()
        {
            _slideshowTimer.Stop();
            _slideshowImages = new List<string>();
            _slideshowIndex = 0;
        }

        private void StartSlideshowIfAny(SongData data)
        {
            StopSlideshow();

            if (data.UserImages == null || data.UserImages.Count == 0)
                return;

            // Keep only existing files
            var list = new List<string>();
            foreach (var p in data.UserImages)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                    list.Add(p);
            }

            if (list.Count == 0)
                return;

            _slideshowImages = list;
            _slideshowIndex = 0;

            // Show first image immediately
            try
            {
                imgCover.Source = new BitmapImage(new Uri(_slideshowImages[_slideshowIndex], UriKind.Absolute));
            }
            catch
            {
                imgCover.Source = _defaultCover;
            }

            _slideshowTimer.Start();
        }

        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            if (_slideshowImages.Count == 0)
            {
                _slideshowTimer.Stop();
                return;
            }

            _slideshowIndex = (_slideshowIndex + 1) % _slideshowImages.Count;

            try
            {
                imgCover.Source = new BitmapImage(new Uri(_slideshowImages[_slideshowIndex], UriKind.Absolute));
            }
            catch
            {
                imgCover.Source = _defaultCover;
            }
        }

        private async Task ShowCoverFromSongDataAsync(SongData data, CancellationToken token)
        {
            // If user images exist -> slideshow (requirements 3.2)
            if (data.UserImages != null && data.UserImages.Count > 0)
            {
                StartSlideshowIfAny(data);
                return;
            }

            // Otherwise show saved API cover (requirements 3.1)
            StopSlideshow();

            if (!string.IsNullOrWhiteSpace(data.ArtworkUrl))
            {
                var bmp = await LoadImageFromUrlAsync(data.ArtworkUrl, token);
                if (!token.IsCancellationRequested && bmp != null)
                {
                    imgCover.Source = bmp;
                    return;
                }
            }

            imgCover.Source = _defaultCover;
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

        // ------------------------------------
        // UI BUTTONS
        // ------------------------------------
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
            StopSlideshow();
            txtStatus.Text = "Stopped";
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            mediaPlayer.Volume = sliderVolume.Value;
        }

        // EDIT WINDOW (3.2) - no API here
        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track)
            {
                MessageBox.Show("Select a song first.", "Edit Song", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var vm = new SongEditorViewModel(_songStore, track);
            var win = new SongEditorWindow(vm)
            {
                Owner = this
            };
            win.ShowDialog();

            // Refresh current selection UI from cache (no API)
            var cached = _songStore.GetByFilePath(track.FilePath);
            if (cached != null)
            {
                ApplySongDataToUI(track, cached);
                _ = ShowCoverFromSongDataAsync(cached, CancellationToken.None);
                txtStatus.Text = "Updated from editor (cache)";
            }
        }

        // ------------------------------------
        // LIBRARY MANAGEMENT
        // ------------------------------------
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
                    library.Add(new MusicTrack
                    {
                        Title = Path.GetFileNameWithoutExtension(file),
                        FilePath = file
                    });
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

        // ------------------------------------
        // LIST EVENTS (Single click shows info)
        // ------------------------------------
        private async void LstLibrary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track)
                return;

            txtCurrentSong.Text = track.Title;
            txtFilePath.Text = track.FilePath;

            // Cancel any pending cover download / slideshow start
            _coverCts?.Cancel();
            _coverCts = new CancellationTokenSource();
            var token = _coverCts.Token;

            // If already saved => show without API
            var cached = _songStore.GetByFilePath(track.FilePath);
            if (cached != null && HasAnyMetadata(cached))
            {
                ApplySongDataToUI(track, cached);
                await ShowCoverFromSongDataAsync(cached, token);
                txtStatus.Text = "Selected (cache)";
                return;
            }

            // No cache => show basic local info
            StopSlideshow();
            imgCover.Source = _defaultCover;
            txtTrackName.Text = track.Title;
            txtArtistName.Text = "-";
            txtAlbumName.Text = "-";
            txtStatus.Text = "Selected";
        }

        // double click: play
        private void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            PlaySelectedTrack();
        }

        // ------------------------------------
        // PLAY + METADATA (Cache first, cancel previous)
        // ------------------------------------
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

                txtTrackName.Text = Path.GetFileNameWithoutExtension(track.FilePath);
                txtArtistName.Text = "-";
                txtAlbumName.Text = "-";
                txtFilePath.Text = track.FilePath;
                imgCover.Source = _defaultCover;

                SaveBasicSongData(track);
                return;
            }

            // Cancel previous metadata request
            _metadataCts?.Cancel();
            _metadataCts = new CancellationTokenSource();
            var token = _metadataCts.Token;

            // Cancel cover CTS too
            _coverCts?.Cancel();
            _coverCts = new CancellationTokenSource();

            // Play immediately
            mediaPlayer.Stop();
            mediaPlayer.Volume = sliderVolume.Value;
            mediaPlayer.Open(new Uri(track.FilePath, UriKind.Absolute));
            mediaPlayer.Play();
            timer.Start();

            // Update base UI
            txtCurrentSong.Text = track.Title;
            txtFilePath.Text = track.FilePath;

            // Cache exists => no API call
            var cached = _songStore.GetByFilePath(track.FilePath);
            if (cached != null && HasAnyMetadata(cached))
            {
                ApplySongDataToUI(track, cached);
                _ = ShowCoverFromSongDataAsync(cached, token);
                txtStatus.Text = "Playing (cache)";
                return;
            }

            // No cache => show default until API
            StopSlideshow();
            imgCover.Source = _defaultCover;
            txtTrackName.Text = track.Title;
            txtArtistName.Text = "-";
            txtAlbumName.Text = "-";
            txtStatus.Text = "Playing + searching iTunes...";

            _ = LoadMetadataFromApiAndCacheAsync(track, token);
        }

        private void SaveBasicSongData(MusicTrack track)
        {
            var basic = _songStore.GetByFilePath(track.FilePath) ?? new SongData { FilePath = track.FilePath };
            basic.TrackName = Path.GetFileNameWithoutExtension(track.FilePath);
            basic.ArtistName = null;
            basic.AlbumName = null;
            basic.ArtworkUrl = null;

            _songStore.Upsert(basic);
        }

        private async Task LoadMetadataFromApiAndCacheAsync(MusicTrack track, CancellationToken token)
        {
            try
            {
                string term = BuildSearchTerm(track);
                ItunesTrackInfo? info = await _itunesService.SearchOneAsync(term, token);

                if (token.IsCancellationRequested) return;

                if (info == null)
                {
                    txtStatus.Text = "No iTunes results. Showing file info only (saved).";

                    var basic = _songStore.GetByFilePath(track.FilePath) ?? new SongData { FilePath = track.FilePath };
                    basic.TrackName = Path.GetFileNameWithoutExtension(track.FilePath);
                    basic.ArtistName = null;
                    basic.AlbumName = null;
                    basic.ArtworkUrl = null;

                    _songStore.Upsert(basic);

                    txtTrackName.Text = basic.TrackName;
                    txtArtistName.Text = "-";
                    txtAlbumName.Text = "-";
                    StopSlideshow();
                    imgCover.Source = _defaultCover;

                    return;
                }

                var existing = _songStore.GetByFilePath(track.FilePath) ?? new SongData { FilePath = track.FilePath };

                existing.TrackName = info.TrackName;
                existing.ArtistName = info.ArtistName;
                existing.AlbumName = info.AlbumName;
                existing.ArtworkUrl = info.ArtworkUrl;
                existing.LastUpdatedUtc = DateTime.UtcNow;

                _songStore.Upsert(existing);

                ApplySongDataToUI(track, existing);
                await ShowCoverFromSongDataAsync(existing, token);

                if (!txtStatus.Text.StartsWith("Media opened"))
                    txtStatus.Text = "Playing (API saved)";
            }
            catch (OperationCanceledException)
            {
                // expected when changing songs
            }
            catch
            {
                txtStatus.Text = "iTunes error. Showing file info only (saved).";

                var basic = _songStore.GetByFilePath(track.FilePath) ?? new SongData { FilePath = track.FilePath };
                basic.TrackName = Path.GetFileNameWithoutExtension(track.FilePath);
                basic.ArtistName = null;
                basic.AlbumName = null;
                basic.ArtworkUrl = null;

                _songStore.Upsert(basic);

                txtTrackName.Text = basic.TrackName;
                txtArtistName.Text = "-";
                txtAlbumName.Text = "-";
                StopSlideshow();
                imgCover.Source = _defaultCover;
            }
        }

        // ------------------------------------
        // TIMER + SLIDER
        // ------------------------------------
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

        // ------------------------------------
        // SAVE / LOAD LIBRARY
        // ------------------------------------
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
            _coverCts?.Cancel();
            StopSlideshow();
            base.OnClosed(e);
        }
    }
}
