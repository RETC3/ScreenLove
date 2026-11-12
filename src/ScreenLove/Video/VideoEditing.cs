using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace ScreenLove.Video
{
    /// <summary>Taglio e unione di video MP4 con le API multimediali di Windows (codifica hardware se disponibile).</summary>
    public static class VideoEditing
    {
        public static async Task<TimeSpan> GetDurationAsync(string path)
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            return (await file.Properties.GetVideoPropertiesAsync()).Duration;
        }

        /// <summary>Crea un nuovo video con la sola parte tra start ed end.</summary>
        public static async Task TrimAsync(string input, TimeSpan start, TimeSpan end, string output, Action<double>? progress = null)
        {
            var source = await StorageFile.GetFileFromPathAsync(input);
            var duration = (await source.Properties.GetVideoPropertiesAsync()).Duration;
            if (end <= start) throw new ArgumentException("La fine deve venire dopo l'inizio.");

            var destination = await CreateFileAsync(output);
            try
            {
                var profile = await MediaEncodingProfile.CreateFromFileAsync(source);
                var transcoder = new MediaTranscoder
                {
                    HardwareAccelerationEnabled = true,
                    // Nonostante la documentazione, TrimStopTime è l'istante di fine (verificato coi controlli).
                    TrimStartTime = start,
                    TrimStopTime = end < duration ? end : TimeSpan.Zero, // zero = fino alla fine
                };
                var prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile);
                if (!prepared.CanTranscode)
                    throw new InvalidOperationException("Taglio non disponibile (" + prepared.FailureReason + ").");
                await WithProgress(prepared.TranscodeAsync(), progress);
            }
            catch
            {
                TryDelete(output);
                throw;
            }
        }

        /// <summary>Il motore di montaggio di Windows non funziona su questo PC (es. schede video virtuali).</summary>
        private static bool _compositionUnavailable;

        /// <summary>
        /// Unisce i video nell'ordine dato. Dimensioni e qualità sono quelle del primo.
        /// Usa il motore di montaggio di Windows; se non è disponibile, il metodo di riserva <see cref="RawVideoMerger"/>.
        /// </summary>
        public static async Task MergeAsync(IReadOnlyList<string> inputs, string output, Action<double>? progress = null)
        {
            if (!_compositionUnavailable)
            {
                try
                {
                    await MergeWithCompositionAsync(inputs, output, progress);
                    return;
                }
                catch
                {
                    _compositionUnavailable = true;
                    TryDelete(output);
                }
            }
            await RawVideoMerger.MergeAsync(inputs, output, progress);
        }

        private static async Task MergeWithCompositionAsync(IReadOnlyList<string> inputs, string output, Action<double>? progress)
        {
            var composition = new MediaComposition();
            StorageFile? first = null;
            foreach (var path in inputs)
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                first ??= file;
                composition.Clips.Add(await MediaClip.CreateFromFileAsync(file));
            }

            var destination = await CreateFileAsync(output);
            try
            {
                // RenderToFileAsync fallisce con alcuni MP4 (0xC00DA7FC, "stream not in a state…"):
                // si fa generare al montaggio un flusso e lo si codifica con il MediaTranscoder.
                var source = await MediaEncodingProfile.CreateFromFileAsync(first!);
                var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                profile.Video.Width = source.Video.Width;
                profile.Video.Height = source.Video.Height;
                profile.Video.FrameRate.Numerator = source.Video.FrameRate.Numerator;
                profile.Video.FrameRate.Denominator = Math.Max(1, source.Video.FrameRate.Denominator);
                profile.Video.Bitrate = Math.Max(source.Video.Bitrate, 2_000_000);
                profile.Audio = source.Audio != null ? AudioEncodingProperties.CreateAac(48000, 2, 192_000) : null;

                var stream = composition.GenerateMediaStreamSource(profile);
                using var outStream = await destination.OpenAsync(FileAccessMode.ReadWrite);
                var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
                var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(stream, outStream, profile);
                if (!prepared.CanTranscode)
                    throw new InvalidOperationException("Unione non disponibile (" + prepared.FailureReason + ").");
                await WithProgress(prepared.TranscodeAsync(), progress);
            }
            catch
            {
                TryDelete(output);
                throw;
            }
        }

        private static async Task WithProgress(IAsyncActionWithProgress<double> op, Action<double>? progress)
        {
            if (progress != null)
                op.Progress = (_, p) => progress(p / 100.0);
            await op;
        }

        private static async Task<StorageFile> CreateFileAsync(string path)
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
            return await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignorato */ }
        }
    }
}
