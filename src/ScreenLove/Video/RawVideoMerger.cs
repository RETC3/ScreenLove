using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace ScreenLove.Video
{
    /// <summary>
    /// Unione di video senza il motore di montaggio di Windows (che non funziona su alcune schede
    /// video, es. macchine virtuali). Ogni video viene decodificato dal MediaTranscoder in un AVI
    /// temporaneo non compresso (NV12 + PCM) alla dimensione del primo; fotogrammi e audio vengono
    /// poi riletti in sequenza e codificati in un unico MP4. I file temporanei vengono eliminati.
    /// </summary>
    internal static class RawVideoMerger
    {
        private const int SampleRate = 48000;
        private const int AudioBytesPerSecond = SampleRate * 4; // stereo, 16 bit

        private sealed record Sample(byte[] Data, TimeSpan Time, TimeSpan Duration);

        public static async Task MergeAsync(IReadOnlyList<string> inputs, string output, Action<double>? progress)
        {
            var first = await StorageFile.GetFileFromPathAsync(inputs[0]);
            var firstProfile = await MediaEncodingProfile.CreateFromFileAsync(first);
            uint width = firstProfile.Video.Width & ~1u, height = firstProfile.Video.Height & ~1u;
            uint fpsNum = firstProfile.Video.FrameRate.Numerator, fpsDen = Math.Max(1, firstProfile.Video.FrameRate.Denominator);
            if (fpsNum == 0) { fpsNum = 30; fpsDen = 1; }
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * fpsDen / fpsNum);

            bool anyAudio = false;
            foreach (var path in inputs)
            {
                var p = await MediaEncodingProfile.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
                if (p.Audio != null) anyAudio = true;
            }

            var video = new BlockingCollection<Sample>(boundedCapacity: 30);
            var audio = new BlockingCollection<Sample>(boundedCapacity: 400);

            var videoDesc = new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Nv12, width, height));
            AudioStreamDescriptor? audioDesc = anyAudio ? new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(SampleRate, 2, 16)) : null;
            var source = audioDesc != null ? new MediaStreamSource(videoDesc, audioDesc) : new MediaStreamSource(videoDesc);
            source.BufferTime = TimeSpan.Zero;
            source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
            source.SampleRequested += (_, args) =>
            {
                var request = args.Request;
                var deferral = request.GetDeferral();
                var queue = request.StreamDescriptor is VideoStreamDescriptor ? video : audio;
                Task.Run(() =>
                {
                    try
                    {
                        if (queue.TryTake(out var s, Timeout.Infinite))
                        {
                            var sample = MediaStreamSample.CreateFromBuffer(s.Data.AsBuffer(), s.Time);
                            sample.Duration = s.Duration;
                            request.Sample = sample;
                        }
                        else
                        {
                            request.Sample = null;
                        }
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                });
            };

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = width;
            profile.Video.Height = height;
            profile.Video.FrameRate.Numerator = fpsNum;
            profile.Video.FrameRate.Denominator = fpsDen;
            profile.Video.PixelAspectRatio.Numerator = 1;
            profile.Video.PixelAspectRatio.Denominator = 1;
            profile.Video.Bitrate = Math.Max(firstProfile.Video.Bitrate, 2_000_000);
            profile.Audio = anyAudio ? AudioEncodingProperties.CreateAac(SampleRate, 2, 192_000) : null;

            string temp = Path.Combine(Path.GetTempPath(), "ScreenLove-unione-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                using var outFile = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

                // Il produttore legge i video uno dopo l'altro; il transcoder li consuma in parallelo.
                var producer = Task.Run(async () =>
                {
                    try
                    {
                        var offset = TimeSpan.Zero;
                        for (int i = 0; i < inputs.Count; i++)
                        {
                            int index = i;
                            string avi = Path.Combine(temp, $"{i}.avi");
                            await DecodeToAviAsync(inputs[i], avi, width, height, fpsNum, fpsDen,
                                p => progress?.Invoke((index + p * 0.5) / inputs.Count));
                            offset += Feed(avi, offset, frameDuration, width, height, anyAudio, video, audio,
                                p => progress?.Invoke((index + 0.5 + p * 0.5) / inputs.Count));
                            File.Delete(avi);
                        }
                    }
                    finally
                    {
                        video.CompleteAdding();
                        audio.CompleteAdding();
                    }
                });

                var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
                var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, outFile.AsRandomAccessStream(), profile);
                if (!prepared.CanTranscode)
                {
                    // sblocca il produttore prima di uscire
                    while (!producer.IsCompleted) { video.TryTake(out _, 50); audio.TryTake(out _, 0); }
                    throw new InvalidOperationException("Codifica non disponibile (" + prepared.FailureReason + ").");
                }
                var encode = prepared.TranscodeAsync().AsTask();
                await Task.WhenAll(producer, encode);
            }
            catch
            {
                try { File.Delete(output); } catch { /* ignorato */ }
                throw;
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { /* ignorato */ }
            }
        }

        private static async Task DecodeToAviAsync(string input, string avi, uint width, uint height, uint fpsNum, uint fpsDen, Action<double> progress)
        {
            var src = await StorageFile.GetFileFromPathAsync(input);
            var srcProfile = await MediaEncodingProfile.CreateFromFileAsync(src);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(avi)!);
            var dst = await folder.CreateFileAsync(Path.GetFileName(avi), CreationCollisionOption.ReplaceExisting);

            var profile = MediaEncodingProfile.CreateAvi(VideoEncodingQuality.HD720p);
            profile.Video = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Nv12, width, height);
            profile.Video.FrameRate.Numerator = fpsNum;
            profile.Video.FrameRate.Denominator = fpsDen;
            profile.Audio = srcProfile.Audio != null ? AudioEncodingProperties.CreatePcm(SampleRate, 2, 16) : null;

            var prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(src, dst, profile);
            if (!prepared.CanTranscode)
                throw new InvalidOperationException("Impossibile decodificare " + Path.GetFileName(input) + " (" + prepared.FailureReason + ").");
            var op = prepared.TranscodeAsync();
            op.Progress = (_, p) => progress(p / 100.0);
            await op;
        }

        /// <summary>Passa fotogrammi e audio di un AVI alle code; restituisce la durata del segmento.</summary>
        private static TimeSpan Feed(string avi, TimeSpan offset, TimeSpan frameDuration, uint width, uint height, bool withAudio,
            BlockingCollection<Sample> video, BlockingCollection<Sample> audio, Action<double> progress)
        {
            int frameSize = (int)(width * height * 3 / 2);
            long frames = 0, audioBytes = 0;
            using var fs = new FileStream(avi, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            long total = fs.Length;

            foreach (var (id, pos, size) in Chunks(fs, 0, fs.Length))
            {
                if (id.EndsWith("dc") || id.EndsWith("db"))
                {
                    var data = new byte[frameSize];
                    fs.Position = pos;
                    if (size == frameSize)
                        fs.ReadExactly(data, 0, frameSize);
                    else
                        ReadPadded(fs, size, data, (int)width, (int)height);
                    video.Add(new Sample(data, offset + TimeSpan.FromTicks(frameDuration.Ticks * frames), frameDuration));
                    frames++;
                    if (frames % 15 == 0) progress((double)pos / total);
                }
                else if (withAudio && id.EndsWith("wb") && size > 0)
                {
                    var data = new byte[size - size % 4];
                    fs.Position = pos;
                    fs.ReadExactly(data, 0, data.Length);
                    var t = TimeSpan.FromTicks(audioBytes * TimeSpan.TicksPerSecond / AudioBytesPerSecond);
                    audio.Add(new Sample(data, offset + t, TimeSpan.FromTicks((long)data.Length * TimeSpan.TicksPerSecond / AudioBytesPerSecond)));
                    audioBytes += data.Length;
                }
            }

            var duration = TimeSpan.FromTicks(frameDuration.Ticks * frames);
            if (withAudio)
            {
                // video senza audio (o audio più corto): si completa con silenzio fino alla fine del segmento
                long needed = (long)(duration.TotalSeconds * SampleRate) * 4 - audioBytes;
                while (needed > 0)
                {
                    int n = (int)Math.Min(needed, AudioBytesPerSecond);
                    var t = TimeSpan.FromTicks(audioBytes * TimeSpan.TicksPerSecond / AudioBytesPerSecond);
                    audio.Add(new Sample(new byte[n], offset + t, TimeSpan.FromTicks((long)n * TimeSpan.TicksPerSecond / AudioBytesPerSecond)));
                    audioBytes += n;
                    needed -= n;
                }
            }
            progress(1);
            return duration;
        }

        /// <summary>Fotogramma NV12 con righe allineate (stride più largo della larghezza): si tolgono i byte in più.</summary>
        private static void ReadPadded(FileStream fs, int size, byte[] dest, int width, int height)
        {
            var raw = new byte[size];
            fs.ReadExactly(raw, 0, size);
            int stride = size * 2 / (height * 3);
            if (stride < width) { Buffer.BlockCopy(raw, 0, dest, 0, Math.Min(size, dest.Length)); return; }
            for (int y = 0; y < height * 3 / 2; y++)
                Buffer.BlockCopy(raw, y * stride, dest, y * width, width);
        }

        /// <summary>Elenca i blocchi di un file RIFF/AVI (anche OpenDML oltre 1 GB) senza caricarlo in memoria.</summary>
        private static IEnumerable<(string id, long pos, int size)> Chunks(FileStream fs, long start, long end)
        {
            var header = new byte[8];
            long position = start;
            while (position + 8 <= end)
            {
                fs.Position = position;
                if (fs.Read(header, 0, 8) < 8) yield break;
                string id = Encoding.ASCII.GetString(header, 0, 4);
                int size = BitConverter.ToInt32(header, 4);
                if (size < 0) yield break;
                if (id is "RIFF" or "LIST")
                {
                    foreach (var c in Chunks(fs, position + 12, Math.Min(end, position + 8 + size)))
                        yield return c;
                }
                else
                {
                    yield return (id, position + 8, size);
                }
                position += 8 + size + (size & 1);
            }
        }
    }
}
