using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;

namespace Telhai.DotNet.PlayerProject.ViewModels
{
    public class SongEditorViewModel : INotifyPropertyChanged
    {
        private readonly SongDataStore _store;
        private readonly MusicTrack _track;

        public SongData Song { get; }

        public string FilePath => _track.FilePath;

        // --- UI display values (read-only metadata from cache only) ---
        public string TrackName => Song.TrackName ?? "-";
        public string ArtistName => Song.ArtistName ?? "-";
        public string AlbumName => Song.AlbumName ?? "-";

        // --- Cover shown in editor ---
        private ImageSource? _coverImage;
        public ImageSource? CoverImage
        {
            get => _coverImage;
            private set { _coverImage = value; OnPropertyChanged(); }
        }

        private string _coverHint = "Cover shown from cached data (no API here).";
        public string CoverHint
        {
            get => _coverHint;
            private set { _coverHint = value; OnPropertyChanged(); }
        }

        // --- Editable title ---
        private string _customTitle;
        public string CustomTitle
        {
            get => _customTitle;
            set
            {
                if (_customTitle != value)
                {
                    _customTitle = value;
                    Song.CustomTitle = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    OnPropertyChanged();
                }
            }
        }

        // --- User images management ---
        public ObservableCollection<string> UserImages { get; }

        private string? _selectedUserImage;
        public string? SelectedUserImage
        {
            get => _selectedUserImage;
            set
            {
                if (_selectedUserImage != value)
                {
                    _selectedUserImage = value;
                    OnPropertyChanged();
                    (RemoveImageCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        // Commands
        public ICommand AddImageCommand { get; }
        public ICommand RemoveImageCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? RequestClose;

        public SongEditorViewModel(SongDataStore store, MusicTrack track)
        {
            _store = store;
            _track = track;

            // Load from JSON store only (NO API)
            Song = _store.GetByFilePath(track.FilePath) ?? new SongData { FilePath = track.FilePath };

            _customTitle = Song.CustomTitle ?? "";
            UserImages = new ObservableCollection<string>(Song.UserImages ?? new System.Collections.Generic.List<string>());

            AddImageCommand = new RelayCommand(_ => AddImage());
            RemoveImageCommand = new RelayCommand(_ => RemoveImage(), _ => SelectedUserImage != null);
            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke());

            LoadCoverFromCachedData();
        }

        private void LoadCoverFromCachedData()
        {
            // Priority:
            // 1) first user image (if exists & file exists)
            // 2) cached ArtworkUrl (if exists)
            // 3) default local resource (spotify.jpg)

            // 1) user images
            foreach (var p in UserImages)
            {
                if (File.Exists(p))
                {
                    CoverImage = LoadBitmapFromFile(p);
                    CoverHint = "Cover from user images.";
                    return;
                }
            }

            // 2) cached ArtworkUrl (no API!)
            if (!string.IsNullOrWhiteSpace(Song.ArtworkUrl))
            {
                var img = LoadBitmapFromUrl(Song.ArtworkUrl!);
                if (img != null)
                {
                    CoverImage = img;
                    CoverHint = "Cover from cached iTunes URL (stored in JSON).";
                    return;
                }
            }

            // 3) default resource
            CoverImage = LoadBitmapFromPack("pack://application:,,,/Assets/spotify.jpg");
            CoverHint = "Default cover (Assets/spotify.jpg).";
        }

        private void AddImage()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select image(s)",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (var path in ofd.FileNames)
                {
                    if (!File.Exists(path)) continue;
                    if (!UserImages.Contains(path))
                        UserImages.Add(path);
                }

                // refresh cover after adding
                LoadCoverFromCachedData();
            }
        }

        private void RemoveImage()
        {
            if (SelectedUserImage == null) return;

            UserImages.Remove(SelectedUserImage);
            SelectedUserImage = null;

            LoadCoverFromCachedData();
        }

        private void Save()
        {
            Song.FilePath = _track.FilePath;
            Song.UserImages = new System.Collections.Generic.List<string>(UserImages);

            _store.Upsert(Song);

            // Refresh cover hint if needed
            LoadCoverFromCachedData();

            RequestClose?.Invoke();
        }

        // ---------- Image loaders (safe, no Freeze needed here) ----------
        private ImageSource? LoadBitmapFromFile(string filePath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        private ImageSource? LoadBitmapFromUrl(string url)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        private ImageSource? LoadBitmapFromPack(string packUri)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(packUri, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
