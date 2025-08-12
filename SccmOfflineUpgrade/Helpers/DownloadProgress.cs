using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace SccmOfflineUpgrade
{
    internal sealed class DownloadProgress
    {
        private sealed class StabilityInfo
        {
            public long LastSize;
            public int StableCount;
        }

        // Tahmini toplam byte (stdout'tan parse edersek dolar)
        public long? ExpectedTotalBytes { get; set; }

        // Şu ana kadar tespit edilen toplam byte (klasör taramasından)
        public long CurrentBytes { get; private set; }

        // Tamamlanan dosyalar (loglandı)
        private readonly HashSet<string> _completedLogged = new(StringComparer.OrdinalIgnoreCase);

        // Stabilite takibi: path -> StabilityInfo
        private readonly ConcurrentDictionary<string, StabilityInfo> _stability =
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

                    var info = _stability.GetOrAdd(fi.FullName, _ => new StabilityInfo { LastSize = fi.Length, StableCount = 0 });

                    if (info.LastSize == fi.Length)
                    {
                        // boyut değişmedi: stabil sayaç ++
                        info.StableCount = Math.Min(info.StableCount + 1, 10);

                        // 2 ardışık ölçüm stabil ise ve daha önce loglanmadıysa -> tamamlandı
                        if (info.StableCount >= 2 && !_completedLogged.Contains(fi.FullName))
                        {
                            _completedLogged.Add(fi.FullName);
                            onFileCompleted?.Invoke(fi.FullName, fi.Length);
                        }
                    }
                    else
                    {
                        // boyut değişti: sayaç sıfırla
                        info.LastSize = fi.Length;
                        info.StableCount = 0;
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
