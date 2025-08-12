using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SccmOfflineUpgrade
{
    internal sealed class DownloadProgress
    {
        private sealed class StabilityInfo
        {
            public long LastSize;
            public int StableCount;
        }

        // stdout'tan "A MB of B MB" gibi kalıpları yakalamak için
        private static readonly Regex RxPercent = new(@"(?<!\d)(?<p>\d{1,3})\s?%(?!\d)", RegexOptions.Compiled);
        private static readonly Regex RxBytesOfTotal =
            new(@"(?<done>\d+(?:\.\d+)?)\s*(MB|GB)\s+of\s+(?<tot>\d+(?:\.\d+)?)\s*(MB|GB)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public long? ExpectedTotalBytes { get; set; }
        public long CurrentBytes { get; private set; }

        private readonly HashSet<string> _completedLogged = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, StabilityInfo> _stability =
            new(StringComparer.OrdinalIgnoreCase);

        public int CompletedFilesCount => _completedLogged.Count;

        public void ScanFolderAndUpdate(string rootFolder, Action<string, long> onFileCompleted)
        {
            long sum = 0;

            foreach (var file in Directory.EnumerateFiles(rootFolder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new FileInfo(file);
                    sum += fi.Length;

                    var info = _stability.GetOrAdd(fi.FullName, _ => new StabilityInfo { LastSize = fi.Length, StableCount = 0 });

                    if (info.LastSize == fi.Length)
                    {
                        info.StableCount = Math.Min(info.StableCount + 1, 10);
                        if (info.StableCount >= 2 && !_completedLogged.Contains(fi.FullName))
                        {
                            _completedLogged.Add(fi.FullName);
                            onFileCompleted?.Invoke(fi.FullName, fi.Length);
                        }
                    }
                    else
                    {
                        info.LastSize = fi.Length;
                        info.StableCount = 0;
                    }
                }
                catch { /* ignore */ }
            }

            CurrentBytes = sum;
        }

        public int GetPercent()
        {
            if (ExpectedTotalBytes.HasValue && ExpectedTotalBytes.Value > 0)
            {
                var p = (int)Math.Floor((CurrentBytes * 100.0) / ExpectedTotalBytes.Value);
                return Math.Max(0, Math.Min(99, p)); // süreç bitene kadar 100 yapmayalım
            }
            return -1; // bilinmiyor
        }

        /// <summary>
        /// Bir log satırından yüzde tahmini çıkarır. Bulursa 0..100 arası değer döndürür; bulamazsa null.
        /// "NN%" kalıbını ya da "A MB of B MB|GB" kalıbını destekler.
        /// </summary>
        public int? ParsePercent(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            // Doğrudan yüzde
            var m = RxPercent.Match(line);
            if (m.Success && int.TryParse(m.Groups["p"].Value, out var pct))
            {
                return Math.Max(0, Math.Min(100, pct));
            }

            // A MB of B MB/GB -> ExpectedTotalBytes set et
            var b = RxBytesOfTotal.Match(line);
            if (b.Success)
            {
                double tot = double.Parse(b.Groups["tot"].Value.Replace(',', '.'));
                var unit = b.Groups[2].Value.ToUpperInvariant(); // MB|GB (done için ilk grup, tot için ikinci grup; ikisi de aynı birim kabul)
                long totalBytes = unit == "GB" ? (long)(tot * 1024 * 1024 * 1024) : (long)(tot * 1024 * 1024);
                if (totalBytes > 0) ExpectedTotalBytes = totalBytes;

                // Yüzdeyi CurrentBytes üzerinden hesaplayacağız; burada direkt döndürmeyelim.
            }

            return null;
        }
    }
}
