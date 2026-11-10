# Windows Bakım Merkezi v0.001

Türkçe ve İngilizce kullanılabilen taşınabilir bir Windows bakım aracı. Windows Bakım 3.2'nin tanılama, onarım ve Store/Xbox işlevlerini WinUtil'den uyarlanan uygulama kataloğu ve kullanıcı tercihleriyle birleştirir.

## İlk çalıştırma

1. `WindowsBakimMerkezi-v0.001-Duzeltme1.exe` dosyasını açın. Windows 10 22H2 veya Windows 11 ve sistemle gelen .NET Framework 4.8/4.8.1 yeterlidir.
2. **Bakım planı** bölümünde **Hızlı tanılama** profilini seçin. İşaretlenen adımların ayrıntılarına bakmak için satırlara tıklayın.
3. **Planı incele** düğmesine basın. Açılan penceredeki sıra ve etkileri kontrol ettikten sonra **Başlat** düğmesini kullanın.
4. **Sonuçlar ve günlük** bölümünde her adımın durumunu ve Windows aracının çıktısını görebilirsiniz. **Sırayı durdur** iptal edilebilen okumaları ve sıradaki adımları durdurur. Devam eden onarımlar, kurulumlar ve kaldırmalar güvenli bitişe kadar sürer.

Uygulama dijital olarak imzalanmamıştır. Windows dosyayı ilk kez açarken yayımcı veya SmartScreen uyarısı gösterebilir. Kurumsal Windows ilkeleri uygulamayı veya bazı yerleşik araçları engelleyebilir.

## İşlevler

**BT araçları** bölümünde 66 işlem bulunur. Kategoriye veya araç adına göre arayın. **Genel BT incelemesi**, **Ağ incelemesi**, **Windows / kararlılık** ve **Yönetici incelemesi** profilleri yalnızca okuma adımlarını seçer. **Planı incele** ile her adımın etkisini kontrol edin. Ağ hedefi, hedef testi veya rota izleme seçildiğinde görünür. TCP portu yalnızca hedef testinde kullanılır.

Dokuz BT bakım eylemi yönetici oturumunda çalışır. Bunlar Defender imza güncelleme ve hızlı tarama, Spooler yeniden başlatma, Winsock sıfırlama, saat eşitleme, PnP taraması, bilgisayar ilkelerini yeniden uygulama, sistem sürücüsü iyileştirme ve üçüncü taraf sürücü yedeğidir. Çalışan bir onarım sırayı durdurma isteğiyle zorla kesilmez. Windows yeniden başlatma gerektirirse sonuçta belirtilir.

**Sonuçlar ve günlük** bölümünde HTML veya JSON raporu kaydedebilirsiniz. Yönetici raporları ve sürücü yedekleri korumalı yönetici günlük klasöründe tutulur. Raporlar bilgisayar, kullanıcı ve ağ bilgileri içerebilir. Test kapsamı TEST_RESULTS.md, araç kapsamı FEATURE_MATRIX.md dosyasındadır.

### BT iş akışları

- **Sorun gider** ile bir belirti seçin. Tanılama planını çalıştırın ve bulguları inceleyin. **Sonraki adım** varsa onarım ve kontrol planını gösterir. Yönetici gerektiren bir senaryoda aynı tanılamayı yönetici penceresinde tekrar çalıştırın. İlk ve sonraki günlüğü **İşlem geçmişi** bölümünden açabilirsiniz.
- **Rapor karşılaştır** ile önceki ve sonraki JSON dosyasını sırayla seçin. Görünüm adım durumlarını ve mesajlarını karşılaştırır. Ham günlük içeriği satır satır karşılaştırılmaz. **Paylaşım özeti** cihaz adını, oturum adını, günlük metnini ve ayrıntılı sonuç mesajlarını içermez.
- **Dosya araçları** bölümünde yerel bir NTFS dosyası için SHA-256 hesaplayın veya beklenen değeri doğrulayın. Klasör taramaları en büyük dosyaları veya aynı boyut ve SHA-256 değerine sahip dosyaları raporlar. Dosyalar silinmez. Tek dosya hash sınırı 2 GiB'dir. Eşleşme taraması en fazla 20.000 girdiyi gezer ve toplam 512 MiB içerik okur. Bağlantılı, çevrimdışı veya erişilemeyen dosyalar atlanır.
- **Ctrl+K** modülleri, BT araçlarını, bakım adımlarını ve WinGet kataloğunu arar. Sonuç araç ekranını açar. **Favoriye ekle** seçilen BT aracını **Sık kullanılanlar** filtresine ekler.
- **Windows araçları → Açık tema / Koyu tema** görünümü değiştirir. Standart kullanıcıda tercih saklanır. Yönetici penceresinde görünüm tercihi oturumla sınırlıdır.

| Bölüm | Kullanım |
| --- | --- |
| Dil | Sol menüdeki **Language · English** veya **Dil · Türkçe** düğmesiyle dili değiştirir ve seçimi kaydeder |
| Canlı izleme | CPU ve fiziksel RAM kullanımını 60 saniyelik grafiklerle, sistem sürücüsünün boş alanını, çalışma süresini ve desteklenen etkin ağ arabirimlerinin hızını gösterir |
| Bakım planı | Sistem ve boş alan özeti, son 7 günün depolama ve donanım olayları, DNS/TCP bağlantı testi, Store/Xbox paket ve hizmet durumu, seçili paketleri yeniden kaydetme, Temp önizlemesi ve temizliği |
| Yönetici bakımı | Fiziksel disk sağlığı, DISM kontrolü ve onarımı, SFC, yerel NTFS sürücülerinde çevrimiçi CHKDSK, Windows Update taraması, sistem geri yükleme noktası, bileşen temizliği ve DNS önbelleği |
| Uygulamalar | 61 paketlik aranabilir WinGet kataloğu, kurulu uygulama ve güncelleme listesi, seçili paketleri kurma ve güncelleme |
| Toplu kaldırma | WinGet ile eşleşen kurulu programları ve izin verilen kaldırılabilir Store paketlerini bulur. Seçilenleri sırayla kaldırır |
| Başlangıç uygulamaları | Bu hesabın Run kayıtlarını ve Başlangıç klasöründeki kısayollarını yedekleyerek devre dışı bırakır ve geri yükler |
| İsteğe bağlı servisler | İzin verilen Windows servislerini başlatır, durdurur veya önceki başlangıç modunu yedekleyip geri yükler |
| Kullanıcı ayarları | Dosya uzantıları, gizli dosyalar, Windows ve uygulama temaları, uygun Windows 11 sürümlerinde görev çubuğunda Görevi Sonlandır |
| Windows araçları | Windows Update, depolama, ağ, kurtarma, güvenlik, uygulama yönetimi ve sürücü iyileştirme ekranlarına kısayollar |

### Canlı izleme ve taşınabilirlik

Siyah ve lime yeşili arayüz, özel nabız simgesi ve **Developed by Oguzhan DUYAR** geliştirici imzası EXE'nin içindedir. Arayüz Türkçeyken imza **Geliştiren Oguzhan DUYAR** olarak görünür. Windows yüksek karşıtlık modu etkinse erişilebilir sistem renkleri kullanılır.

CPU ve fiziksel RAM grafikleri son 60 saniyeyi gösterir. İlk CPU okuması bir başlangıç örneğidir; yüzde için sonraki ölçüm beklenir. Sayfa değiştirilince veya pencere küçültülünce izleme duraklar. Geri dönüldüğünde yeni geçmiş başlar. Ölçülemeyen aralıklar sıfır kullanım olarak çizilmez. Sistem sürücüsü kartı boş kapasiteyi gösterir; disk hızını veya disk sağlığını ölçmez.

CPU ölçümü Windows'un CPU zaman sayaçlarından elde edilir. Birden fazla işlemci grubuna sahip sistemlerde kart başlığı bunun birincil işlemci grubuyla sınırlı olduğunu belirtir. Bu API'nin kapsamı [Microsoft GetSystemTimes belgesinde](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes) açıklanır. RAM, [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex) üzerinden okunur.

Kurulum gerektirmeyen tek EXE taşınabilir. Dil tercihi, işlem günlükleri ve geri alma yedekleri kullanılan bilgisayarda saklanır; bu nedenle taşınabilirlik tüm verilerin USB bellekte tutulduğu anlamına gelmez. WinGet ile kurulum/güncelleme gibi çevrimiçi işlemler internet ve Windows'un ilgili bileşenlerini gerektirir. Canlı izleme için ek paket indirilmez.

### Yönetici işlemleri

**Yönetici araçlarını aç** seçeneği Windows izin penceresini açar. EXE'nin bağlantı içermeyen yerel bir klasörde bulunması gerekir. Bakım listesinde bir yönetici satırına tıklayıp pencereyi açarsanız o adım işaretlenir. İşlemi başlatmak için planı ayrıca onaylayın.

DISM ve SFC birlikte seçildiğinde DISM önce çalışır. DISM onarımı başarılı sonuç vermezse veya yeniden başlatma bekliyorsa SFC atlanır. CHKDSK, planın hazırlandığı sırada seçilen disk birimini işlemden önce yeniden doğrular. Onarımların çıktısı Windows diline göre değişebilir. Yorumlanamayan sonuçlar **İnceleme gerekli** olarak gösterilir. Sistem geri yükleme noktası için Sistem Koruması açık olmalıdır. Windows, nokta oluşturma sıklığını sınırlayabilir.

### Temizlik ve geri alma

**Geçici dosyaları önizle** adımını çalıştırdıktan sonra **Önizlenen geçici dosyaları temizle** adımını ayrı bir planda seçin. Önizleme bir saat geçerlidir. Yalnızca geçerli hesabın yerel NTFS `%LOCALAPPDATA%\Temp` klasöründe, oluşturulma ve son yazma zamanı yedi günden eski olan dosyalar adaydır. Değişen dosyalar, bağlantılar, kilitli ve korumalı dosyalar atlanır. Silme kalıcıdır. Gösterilen boyut dosyaların mantıksal boyutudur.

Kullanıcı ayarlarında **Önceki değere dön** seçeneği ilk değişiklikten önce saklanan kayıt değerini geri getirir. Uygulama başka bir değer algılarsa yedeği saklar ve durumu raporlar. Görünüm değişiklikleri için ilgili pencereyi veya Windows oturumunu yeniden açmanız gerekebilir.

### WinGet

WinGet, Windows'un **Uygulama Yükleyicisi** bileşenine bağlıdır. **Windows araçları** sayfasındaki bağlantıyla bileşene ulaşabilir ve **WinGet'i denetle** seçeneğiyle durumunu kontrol edebilirsiniz. Paketlerin bulunabilirliği, uyumluluğu ve kurucuların davranışı WinGet kaynağına ve Windows sürümüne bağlıdır. Plan, paket kimliklerini ve kabul edilecek koşulları çalıştırmadan önce gösterir.

### Toplu kaldırma

**Toplu kaldırma → Kurulu uygulamaları bul** ile listeyi oluşturun. Kaldırmak istediğiniz satırları işaretleyip **Seçilenleri kaldır** düğmesine basın. Onay ekranında paket kimliklerini, sürümlerini ve kaynaklarını kontrol edin. WinGet işlemleri varsayılan topluluk kaynağını doğrular. Bilinen kurulu sürüm, kaldırıcıya paket kimliğiyle birlikte iletilir. Edge, WebView2, Uygulama Yükleyicisi ve ortak çalışma zamanı paketleri listeden süzülür. Store işlemleri bu hesabın izin verilen paketlerini kapsar ve sürümü kaldırmadan hemen önce kontrol eder. Yerel uygulama verileri kaldırma sırasında silinebilir. Kaldırıcı izin veya kullanıcı onayı isteyebilir.

### Başlangıç ve servisler

**Başlangıç uygulamaları** bu hesabın Run kayıtlarını ve yerel Başlangıç klasöründeki `.lnk` kısayollarını gösterir. **Başlangıçtan çıkar** orijinal kaydı veya kısayolu saklar. **Önceki kaydı geri yükle** aynı adla sonradan eklenmiş farklı bir girdinin üzerine yazmaz. Windows'un kendi başlangıç listesine **Windows başlangıç ayarları** bağlantısından ulaşabilirsiniz. Zamanlanmış görevler, tüm kullanıcıların Run kayıtları ve Store başlangıç izinleri uygulamanın düzenleme kapsamı dışındadır.

**Servisleri aç** yönetici izni ister. **Servisleri listele** yüklü isteğe bağlı servisleri gösterir. Faks, perakende tanıtımı ve çevrimdışı harita hizmetleri örneklerdir. Başlangıcı devre dışı bırakmak ile çalışan servisi durdurmak ayrı işlemlerdir. Başlangıç modu otomatik, gecikmeli otomatik veya el ile olacak şekilde saklanıp geri yüklenebilir. Bağımlılık denetimi izin vermezse işlem atlanır.

Dil değişimi pencereyi aynı bölümde yeniden açar ve geçici plan seçimlerini sıfırlar. Tercih normal kullanıcı penceresinden kaydedilir. Yönetici penceresi açılırken bu dili kullanır ve kendi dil değişikliklerini açık oturumla sınırlar. Windows iletişim kutuları ve yerleşik komut çıktısı Windows'un kendi dilini kullanabilir.

### Günlükler ve veriler

Normal kullanıcı günlükleri `%LOCALAPPDATA%\WindowsBakimMerkezi\Gunlukler`, ayar yedekleri `%LOCALAPPDATA%\WindowsBakimMerkezi\AyarYedekleri` altında tutulur. Yönetici günlükleri, erişimi yöneticiler ve Windows sistem hesabıyla sınırlı olan `%SystemRoot%\Logs\WindowsBakimMerkezi` klasörüne yazılır. Eski klasörde ek yazma yetkisi varsa yeni günlükler Windows Logs altındaki WindowsBakimMerkezi-Korumali klasörüne yazılır. Eski günlükler eski klasörde kalır. Yönetici penceresi dosya yolunu veya görüntülenen çıktıyı kopyalayabilir. Çok uzun araç çıktıları sınıra ulaştığında adım sonuçları kaydedilmeye devam eder. Günlükler sürücü, uygulama, olay ve bağlantı ayrıntıları içerebilir.

## Kaynak ve doğrulama

4.4 sürümünde canlı CPU/RAM okumaları iki dilde, normal ve en küçük pencere boyutunda denetlendi. Sayfa değiştirme, küçültme/geri açma ve pencereyi doğrudan kapatma test edildi. CPU sayaçlarının geri gitmesi, sıfır aralık, geçersiz fark ve taşma senaryoları sınandı. 10.000 örnekle grafik geçmişinin 31 kayıt sınırında kaldığı doğrulandı. Bu doğrulamalar sistemde uygulama kaldırmaz veya servis kapatmaz.

Arşivi bir klasöre çıkarıp o klasörde PowerShell açın. `.\Derle.ps1` komutu Windows ile gelen C# derleyicisini kullanır ve `WindowsBakimMerkezi.exe` üretir. Ek .NET SDK ya da paket indirme gerekmez. `.\Derle.ps1 -Dogrula` komutu örnek dosyalar, örnek ayar değerleri, komut çıktısı ve arayüz kontrolleriyle doğrulama çalıştırır. Bu komutun çıktıları ayrı bir `Dogrulama-…` klasörüne yazılır.

Windows 11 x64 ortamında iki dilde açılış, iptal ve zaman aşımı, günlük sınırı, WinGet kaynak denetimi, deneme Store paketleriyle eski sürümün reddedilmesi, başlangıç kısayolu çakışması, servis izin listesi ve örnek Temp temizliği kontrol edildi. 6.000 dosyalık örnekte iki sürüm aynı 120 adayı buldu. Ölçülen ortanca süre 4.1 için yaklaşık 590 ms, 4.2 için 22 ms idi. Canlı kayıt defteri yazımı, yükseltilmiş günlük klasörünün oluşturulması, yönetici onarımları, servis değişiklikleri, gerçek WinGet/Store işlemleri, NTFS sabit bağlantı senaryosu ve diğer Windows sürümleri bu ortamda uçtan uca denenmedi.

Windows Bakım 3.2, Hugo ZED / Oğuzhan D. WinUtil kataloğundan ve seçili tercih tanımlarından uyarlanan kaynak, Chris Titus Tech / CT Tech Group LLC'nin MIT lisansına tabidir. Lisans `WinUtil-MIT.txt` dosyasında ve uygulamanın içinde bulunur.

## English quick start

1. Open `WindowsBakimMerkezi-v0.001-Duzeltme1.exe` on Windows 10 22H2 or Windows 11. Select **Language · English** in the sidebar.
2. Use **Maintenance plan → Quick diagnostics** for a first scan. Review the selected steps before starting. The **Results and log** page shows each result.
3. In **Bulk uninstall**, select **Find installed apps**. Choose individual apps, review the exact package IDs and sources, then confirm. Local app data may be removed.
4. In **Startup apps**, disable or restore a Run entry or a shortcut for your Windows account. **Open services** opens a separate administrator window. Select **List services** to manage supported optional services.
5. In **IT tools**, choose a read-only profile or individual tools, then review the plan. **Troubleshoot** runs diagnostics before proposing a separate repair. **Results and log** supports local history, HTML/JSON export and JSON comparison. **Ctrl+K** searches the workspace.

The executable is unsigned. Windows tools and native messages can use the operating system's language. Rebuilding and testing require the Windows .NET Framework compiler. Run `.\Derle.ps1` to build or `.\Derle.ps1 -Dogrula` to run the verification suite.

Developed by Oguzhan DUYAR.




