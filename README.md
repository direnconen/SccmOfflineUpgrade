SccmOfflineUpgrade v1.0.0
================================

Yazar            : Arksoft Bilisim
Modül            : SccmOfflineUpgrade.psm1
Açıklama         : Kapalı ağ (offline) SCCM/ConfigMgr ortamları için uçtan uca "Prepare → Connect → Import" akışıyla 
                   yükseltme (in-console update) paketlerini hazırlama, indirme ve içeri alma işlemlerini otomatikleştirir.
Hedef Kitle      : SCCM yöneticileri ve DevOps ekipleri
Lisans           : Internal

--------------------------------------------------------------------------------
İÇİNDEKİLER
--------------------------------------------------------------------------------
1) Genel Bakış
2) Önkoşullar
3) Kurulum ve Modülün Yüklenmesi
4) Komutlar (Cmdlet’ler) ve Parametreleri
   4.1) Install-SccmServiceConnectionPoint
   4.2) Export-SccmOfflinePackage
   4.3) Invoke-SccmOnlineDownload
   4.4) Import-SccmOfflineUpdates
5) Kullanım Senaryoları (Uçtan Uca Akış)
   5.1) Kapalı Ağ: Prepare & Paket Üret
   5.2) Açık Ağ: Connect & İndir & Paketle
   5.3) Kapalı Ağ: Import & (Opsiyonel) Prereq Check & (Opsiyonel) Upgrade Başlat
6) Loglama ve Çıktılar
7) Hata Yönetimi ve Sorun Giderme
8) SSS (Sık Sorulan Sorular)
9) Sürüm Notları

--------------------------------------------------------------------------------
1) GENEL BAKIŞ
--------------------------------------------------------------------------------
Bu modül, Microsoft Configuration Manager (SCCM/ConfigMgr) ortamlarında internet erişimi olmayan (kapalı ağ) 
site’leri güncel sürümlere yükseltmek için kullanılan offline yöntemi otomatikleştirir. Akış:
  - PREPARE  : Kapalı ağdaki SCCM sunucusunda usage data üretilir ve transfer paketi hazırlanır.
  - CONNECT  : Açık ağda usage data Microsoft’a gönderilir ve update paketleri indirilir.
  - IMPORT   : Kapalı ağda indirilen paketler SCCM’e import edilir; istenirse prereq check ve upgrade tetiklenir.

Modül ayrıca:
  - Service Connection Point (SCP) rolünü kurabilir ve Offline/Online moda alabilir.
  - Tüm adımları ayrıntılı loglar.
  - İndirme sırasında hataya düşen dosyaları ayrı bir log dosyasına yazar.
  - Transfer/indirme paketlerini otomatik ZIP’ler.

--------------------------------------------------------------------------------
2) ÖNKOŞULLAR
--------------------------------------------------------------------------------
- SCCM Konsolu ve/veya ConfigMgr PowerShell modülü (ConfigMgr cmdlet’leri için).
- ServiceConnectionTool.exe (aynı sürümün CD.Latest dizininden). Modül otomatik bulmaya çalışır.
- Kapalı ağ tarafında yönetici yetkileri.
- Açık ağ tarafında internet erişimi; gerekiyorsa proxy bilgisi.
- .NET Framework ve gerekli bağımlılıklar (ServiceConnectionTool’un gerektirdikleri).
- Yeterli disk alanı (indirilecek güncellemeler GB’larca olabilir).

--------------------------------------------------------------------------------
3) KURULUM VE MODÜLÜN YÜKLENMESİ
--------------------------------------------------------------------------------
1. Modül dosyalarını bir klasöre kopyalayın (örn. C:\Modules\SccmOfflineUpgrade\).
2. (İsteğe bağlı) Modül manifest dosyası (SccmOfflineUpgrade.psd1) kullanıyorsanız aynı klasöre koyun.
3. PowerShell’de:
   Import-Module "C:\Modules\SccmOfflineUpgrade\SccmOfflineUpgrade.psd1" -Force

Not: ConfigMgr cmdlet’lerini kullanacaksanız SCCM konsolunun “Connect via Windows PowerShell” kısayolunu 
kullanmanız veya ConfigurationManager modülünün yüklü olması gerekir.

--------------------------------------------------------------------------------
4) KOMUTLAR (CMDLET’LER) VE PARAMETRELERİ
--------------------------------------------------------------------------------

4.1) Install-SccmServiceConnectionPoint
---------------------------------------
Açıklama: Service Connection Point (SCP) rolünü kurar veya mevcut ise modunu değiştirir (Offline/Online).

Sözdizimi:
  Install-SccmServiceConnectionPoint -SiteCode <String> -SiteSystemServerName <String> [-Mode <Offline|Online>] 
                                     [-LogPath <String>] [-WhatIf] [-Confirm]

Parametreler:
  -SiteCode (Zorunlu)              : SCCM site kodu (örn. ABC).
  -SiteSystemServerName (Zorunlu)  : SCP rolünün kurulacağı site system sunucu adı (FQDN önerilir).
  -Mode                            : 'Offline' veya 'Online'. Varsayılan: Offline.
  -LogPath                         : İşlem log dosyası. Varsayılan: C:\ProgramData\SccmOfflineUpgrade\logs\Install-SCP.log
  -WhatIf / -Confirm               : Standart güvenlik parametreleri.

Çıktı:
  Yok. Başarı/hata detayları log’a yazılır.

Örnek:
  Install-SccmServiceConnectionPoint -SiteCode "ABC" -SiteSystemServerName "SCCM-PRI.contoso.local" -Mode Offline -Verbose


4.2) Export-SccmOfflinePackage
------------------------------
Açıklama: Kapalı ağdaki SCCM sunucusunda PREPARE adımını çalıştırır, ServiceConnectionTool klasörü ve UsageData.cab’i 
          içeren bir aktarım ZIP’i üretir.

Sözdizimi:
  Export-SccmOfflinePackage -OutputZip <String> [-ServiceConnectionToolPath <String>] 
                            [-StagingRoot <String>] [-UsageDataCabName <String>] [-LogPath <String>]

Parametreler:
  -OutputZip (Zorunlu)             : Dışarı taşınacak ZIP dosyasının tam yolu.
  -ServiceConnectionToolPath       : ServiceConnectionTool klasörü. Boş kalırsa CD.Latest’ten otomatik bulunur.
  -StagingRoot                     : Geçici çalışma klasörü. Varsayılan: %ProgramData%\SccmOfflineUpgrade\staging
  -UsageDataCabName                : Usage data CAB dosyası adı. Varsayılan: UsageData.cab
  -LogPath                         : İşlem log dosyası. Varsayılan: %ProgramData%\SccmOfflineUpgrade\logs\Export-SccmOfflinePackage.log

Çıktı:
  -OutputZip yolunu string olarak döndürür.

Örnek:
  $zip1 = Export-SccmOfflinePackage -OutputZip "D:\XFER\CM-UsageData-And-Tool.zip" -Verbose


4.3) Invoke-SccmOnlineDownload
------------------------------
Açıklama: Açık ağda CONNECT adımını çalıştırır. Kapalı ağdan gelen ZIP’i açar, usage data’yı yükler, güncellemeleri indirir,
          hatalı indirmeleri ayrı log’a yazar ve tüm indirmeleri tek bir ZIP haline getirir.

Sözdizimi:
  Invoke-SccmOnlineDownload -InputZip <String> -DownloadOutput <String> -OutputZip <String> 
                            [-Proxy <String>] [-LogPath <String>] [-ErrorLogPath <String>]

Parametreler:
  -InputZip (Zorunlu)              : Kapalı ağdan taşınan ilk paket (Export-SccmOfflinePackage çıktısı).
  -DownloadOutput (Zorunlu)        : Güncellemelerin indirileceği klasör.
  -OutputZip (Zorunlu)             : Geri kapalı ağa taşınacak ZIP dosyası.
  -Proxy                           : Gerekirse proxy (örn. http://user:pass@proxy:8080).
  -LogPath                         : İşlem log dosyası. Varsayılan: %ProgramData%\SccmOfflineUpgrade\logs\Invoke-SccmOnlineDownload.log
  -ErrorLogPath                    : Hatalı indirmeler için ayrı log. Varsayılan: %ProgramData%\SccmOfflineUpgrade\logs\OnlineDownloadErrors.log

Çıktı:
  [pscustomobject] @{ OutputZip = <String>; ErrorLog = <String|null> }

Örnek:
  $result = Invoke-SccmOnlineDownload `
              -InputZip "E:\CarryIn\CM-UsageData-And-Tool.zip" `
              -DownloadOutput "E:\CM\UpdatePacks" `
              -OutputZip "E:\CarryBack\CM-UpdatePacks.zip" `
              -Verbose
  if ($result.ErrorLog) { Write-Host "Hata detayları: $($result.ErrorLog)" }


4.4) Import-SccmOfflineUpdates
------------------------------
Açıklama: Kapalı ağda IMPORT adımını çalıştırır. İndirilen paketleri SCCM’e import eder; istenirse prereq check (LOCAL) ve 
          (destek dışı) upgrade tetikleme denemesi yapar.

Sözdizimi:
  Import-SccmOfflineUpdates -InputZip <String> [-ServiceConnectionToolPath <String>] [-ExtractTo <String>] 
                            [-RunPrereqCheck] [-CdLatestRoot <String>] [-TryStartUpgrade] [-SiteCode <String>] 
                            [-LogPath <String>]

Parametreler:
  -InputZip (Zorunlu)              : Açık ağdan dönen güncellemeleri içeren ZIP (Invoke-SccmOnlineDownload çıktısı).
  -ServiceConnectionToolPath       : ServiceConnectionTool klasörü. Boş kalırsa CD.Latest’ten otomatik bulunur.
  -ExtractTo                       : ZIP’in açılacağı klasör. Varsayılan: %ProgramData%\SccmOfflineUpgrade\import
  -RunPrereqCheck                  : CD.Latest\...\prereqchk.exe /LOCAL çalıştırır (bulunursa).
  -CdLatestRoot                    : prereqchk.exe aramak için CD.Latest kökü. Boşsa Setup’tan türetilir.
  -TryStartUpgrade                 : “Available” durumdaki en yeni in-console update’i WMI tabanlı *best-effort* tetikler.
  -SiteCode                        : TryStartUpgrade için gerekli (örn. ABC).
  -LogPath                         : İşlem log dosyası. Varsayılan: %ProgramData%\SccmOfflineUpgrade\logs\Import-SccmOfflineUpdates.log

Çıktı:
  Yok. Başarı/hata detayları log’a yazılır.

Örnek:
  Import-SccmOfflineUpdates `
    -InputZip "D:\CarryBack\CM-UpdatePacks.zip" `
    -RunPrereqCheck `
    -TryStartUpgrade `
    -SiteCode "ABC" `
    -Verbose

--------------------------------------------------------------------------------
5) KULLANIM SENARYOLARI (UÇTAN UCA AKIŞ)
--------------------------------------------------------------------------------

5.1) Kapalı Ağ: Prepare & Paket Üret
------------------------------------
1) (Opsiyonel) SCP rolünü kur ve Offline moda al:
   Install-SccmServiceConnectionPoint -SiteCode "ABC" -SiteSystemServerName "SCCM-PRI.contoso.local" -Mode Offline -Verbose

2) PREPARE ve paket oluştur:
   $zip1 = Export-SccmOfflinePackage -OutputZip "D:\XFER\CM-UsageData-And-Tool.zip" -Verbose

3) Oluşan ZIP’i açık ağa taşıyın.

5.2) Açık Ağ: Connect & İndir & Paketle
---------------------------------------
1) İndirme ve paketleme:
   $result = Invoke-SccmOnlineDownload `
               -InputZip "E:\CarryIn\CM-UsageData-And-Tool.zip" `
               -DownloadOutput "E:\CM\UpdatePacks" `
               -OutputZip "E:\CarryBack\CM-UpdatePacks.zip" `
               -Verbose

2) $result.ErrorLog doluysa hataları inceleyin.
3) Oluşan ZIP’i tekrar kapalı ağa taşıyın.

5.3) Kapalı Ağ: Import & (Opsiyonel) Prereq & (Opsiyonel) Upgrade
-----------------------------------------------------------------
1) IMPORT + prereq check + (best-effort) upgrade tetikle:
   Import-SccmOfflineUpdates `
     -InputZip "D:\CarryBack\CM-UpdatePacks.zip" `
     -RunPrereqCheck `
     -TryStartUpgrade `
     -SiteCode "ABC" `
     -Verbose

2) Monitoring > Updates and Servicing Status ve aşağıdaki logları takip edin:
   - CMUpdate.log
   - ConfigMgrPrereq.log

--------------------------------------------------------------------------------
6) LOGLAMA VE ÇIKTILAR
--------------------------------------------------------------------------------
Varsayılan log klasörü: C:\ProgramData\SccmOfflineUpgrade\logs\

- Install-SCP.log                    : SCP rol + mod işlemleri
- Export-SccmOfflinePackage.log      : PREPARE + paket üretim
- Invoke-SccmOnlineDownload.log      : CONNECT + indirme + paketleme
- OnlineDownloadErrors.log           : Hatalı indirmeler (ayrı dosya)
- Import-SccmOfflineUpdates.log      : IMPORT + (opsiyonel) prereq + (opsiyonel) upgrade tetik

ZIP Çıktıları:
- CM-UsageData-And-Tool.zip          : Kapalı ağdan açık ağa taşınacak ilk paket
- CM-UpdatePacks.zip                 : Açık ağdan kapalı ağa geri taşınacak indirme paketi

--------------------------------------------------------------------------------
7) HATA YÖNETİMİ VE SORUN GİDERME
--------------------------------------------------------------------------------
- ServiceConnectionTool.exe bulunamadı:
  * CD.Latest dizini doğru mu? Gerekirse -ServiceConnectionToolPath parametresi ile tam yolu verin.
- Indirme başarısız/hatalı dosyalar:
  * OnlineDownloadErrors.log dosyasını kontrol edin. Proxy gereksinimini değerlendirin (-Proxy).
  * Disk alanını ve antivirüs/IDS engellemelerini kontrol edin.
- Prereq check uyarıları:
  * ConfigMgrPrereq.log ve prereqchk çıktısını inceleyin. Eksik roller/özellikler için düzeltme yapın.
- Upgrade tetiklenmiyor:
  * TryStartUpgrade best-effort bir yaklaşımdır (resmi cmdlet değildir). GUI’den “Install Update Pack” ile deneyin.
  * CMUpdate.log ve Updates and Servicing Status’ı inceleyin.

--------------------------------------------------------------------------------
8) SSS (Sık Sorulan Sorular)
--------------------------------------------------------------------------------
S: Modül ConfigMgr cmdlet’lerine muhtaç mı?
C: SCP rol yönetimi ve otomatik upgrade denemesi için evet. PREPARE/CONNECT/IMPORT ise ServiceConnectionTool ile çalışır.

S: Proxy arkasındayım, nasıl ayarlayacağım?
C: Invoke-SccmOnlineDownload içinde -Proxy parametresine uygun formatta verin (örn. http://user:pass@proxy:8080).

S: Paket boyutları çok büyük, ne yapabilirim?
C: DownloadOutput diskini büyük seçin; ZIP aşamasında da yeterli alan bırakın.

S: Hangi logları takip etmeliyim?
C: Modül loglarına ek olarak SCCM logları: CMUpdate.log, ConfigMgrPrereq.log, dmpdownloader.log, hman.log vb.

--------------------------------------------------------------------------------
9) SÜRÜM NOTLARI
--------------------------------------------------------------------------------
v1.0.0
- İlk sürüm: PREPARE/CONNECT/IMPORT akışları; SCP rol yönetimi; indirme hatalarını ayrı loglama;
  ZIP paketleme; prereq check ve best-effort upgrade tetikleme.
