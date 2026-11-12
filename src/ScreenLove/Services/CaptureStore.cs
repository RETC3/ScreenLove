using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLove.Services
{
    public sealed class CaptureItem : INotifyPropertyChanged
    {
        private ImageSource? _thumbnail;
        private string _info = "";

        public CaptureItem(string path)
        {
            Path = path;
        }

        public string Path { get; }
        public string Name => System.IO.Path.GetFileName(Path);
        public DateTime Created => File.Exists(Path) ? File.GetLastWriteTime(Path) : DateTime.MinValue;

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            private set { _thumbnail = value; OnPropertyChanged(); }
        }

        public string Info
        {
            get => _info;
            private set { _info = value; OnPropertyChanged(); }
        }

        public bool IsVideo => System.IO.Path.GetExtension(Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase);

        public async Task LoadThumbnailAsync()
        {
            if (IsVideo)
            {
                await LoadVideoThumbnailAsync();
                return;
            }

            var path = Path;
            try
            {
                var (thumb, w, h) = await Task.Run(() =>
                {
                    int width, height;
                    using (var fs = File.OpenRead(path))
                    {
                        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                        width = decoder.Frames[0].PixelWidth;
                        height = decoder.Frames[0].PixelHeight;
                    }

                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad; // non tiene bloccato il file
                    bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bi.DecodePixelWidth = Math.Min(360, width);
                    bi.UriSource = new Uri(path);
                    bi.EndInit();
                    bi.Freeze();
                    return (bi, width, height);
                });

                Thumbnail = thumb;
                Info = $"{w} × {h} · {Created:dd/MM/yyyy HH:mm}";
            }
            catch
            {
                Info = "Impossibile leggere l'immagine";
            }
        }

        /// <summary>Anteprima e durata dei video tramite la shell di Windows.</summary>
        private async Task LoadVideoThumbnailAsync()
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path);
                using (var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 360,
                           Windows.Storage.FileProperties.ThumbnailOptions.ResizeThumbnail))
                {
                    if (thumb != null && thumb.Size > 0)
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = thumb.AsStreamForRead();
                        bi.EndInit();
                        bi.Freeze();
                        Thumbnail = bi;
                    }
                }

                var props = await file.Properties.GetVideoPropertiesAsync();
                var d = props.Duration;
                string duration = d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss");
                Info = $"{props.Width} × {props.Height} · {duration} · {Created:dd/MM/yyyy HH:mm}";
            }
            catch
            {
                Info = $"Video · {Created:dd/MM/yyyy HH:mm}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Storico delle catture: tutte le immagini nella cartella delle catture.</summary>
    public sealed class CaptureStore
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".mp4" };

        public CaptureStore(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }
        public ObservableCollection<CaptureItem> Items { get; } = new();

        public void Load()
        {
            Directory.CreateDirectory(Folder);
            Items.Clear();
            var files = new DirectoryInfo(Folder).EnumerateFiles()
                .Where(f => Extensions.Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.LastWriteTimeUtc);
            foreach (var f in files)
            {
                var item = new CaptureItem(f.FullName);
                Items.Add(item);
                _ = item.LoadThumbnailAsync();
            }
        }

        /// <summary>Nuovo nome libero nella cartella delle catture, es. ScreenLove_2026-11-12_15-30-00.mp4.</summary>
        public string NewPath(string extension)
        {
            Directory.CreateDirectory(Folder);
            string baseName = $"ScreenLove_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            string path = System.IO.Path.Combine(Folder, baseName + extension);
            for (int i = 2; File.Exists(path); i++)
                path = System.IO.Path.Combine(Folder, $"{baseName}_{i}{extension}");
            return path;
        }

        public string SaveNew(BitmapSource image)
        {
            string path = NewPath(".png");
            ImageIO.Save(image, path);
            var item = new CaptureItem(path);
            Items.Insert(0, item);
            _ = item.LoadThumbnailAsync();
            return path;
        }

        public CaptureItem? Find(string path) =>
            Items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

        /// <summary>Ricarica la miniatura di un file modificato (o lo aggiunge se nuovo e nella cartella).</summary>
        public void Refresh(string path)
        {
            var item = Find(path);
            if (item != null)
            {
                _ = item.LoadThumbnailAsync();
            }
            else if (string.Equals(System.IO.Path.GetDirectoryName(path), Folder, StringComparison.OrdinalIgnoreCase))
            {
                item = new CaptureItem(path);
                Items.Insert(0, item);
                _ = item.LoadThumbnailAsync();
            }
        }

        public void Delete(CaptureItem item)
        {
            if (File.Exists(item.Path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            Items.Remove(item);
        }
    }
}
