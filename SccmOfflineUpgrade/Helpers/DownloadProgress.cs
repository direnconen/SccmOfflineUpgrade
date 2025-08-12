using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SccmOfflineUpgrade
{
    internal sealed class DownloadProgress
    {
        // Tahmini toplam byte (stdout'tan parse edersek dolar)
        public long? ExpectedTotalBytes { get; set; }

        // Şu ana kadar tespit edilen toplam byte (klasör taramasından)
        public long CurrentBytes { get; private set; }

        // Tamamlanan dosyalar (loglandı)
        private readonly HashSet<string> _completedLogged = new(StringComparer.OrdinalIgnoreCase);

        // Stabilite takibi: path -> (lastSize, stableCount)
        private readonly ConcurrentDictionary<string, (long lastSize, int stableCount)> _stability =
            new(StringComparer.OrdinalIgnoreCase);

        // Tamamlandı sayacı
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

                    var key = fi.FullName;
                    var tuple = _stability.GetOrAdd(key, _ => (fi.Length, 0));

                    if (tuple.lastSize == fi.Length)
                    {
                        // boyut değişmedi: stabil sayaç ++
                        var next = (fi.Length, Math.Min(tuple.stableCount + 1, 10));
                        _stability[key] = next;

                        // 2 ardışık ölçüm stabil ise ve daha önce loglanmadıysa -> tamamlandı
                        if (next.stableCount >= 2 && !_completedLogged.Contains(key))
                        {
                            _completedLogged.Add(key);
                            onFileCompleted?.Invoke(key, fi.Length);
                        }
                    }
                    else
                    {
                        // boyut değişti: sayaç sıfırla
                        _stability[key] = (fi.Length, 0);
                    }
                }
                catch
                {
                    // erişim hataları vs. yok say
                }
            }

            CurrentBytes = sum;
        }

        public int GetPercent()
        {
            if (ExpectedTotalBytes.HasValue && ExpectedTotalBytes.Value > 0)
            {
                var p = (int)Math.Floor((CurrentBytes * 100.0) / ExpectedTotalBytes.Value);
                return Math.Max(0, Math.Min(99, p)); // işlem bitene kadar 99'u geçme
            }
            // Beklenen toplam bilinmiyorsa yüzdesiz; 0 döndür (indeterminate gibi davranacağız)
            return -1;
        }
    }
}
