# Test sonuçları

## Evrensel kurucu

| Kontrol | Sonuç |
|---|---|
| Python test paketi, Windows Python 3.14.10 | 45 test. 43 geçti, Linux loopback ve dosya kilidi testleri atlandı. |
| Kullanıcı kurulumu | Dosya yerleştirme, tekrar kurulum, CLI profiline geçiş, özel veri dizini ve kaldırma geçişleri geçildi. |
| Hata yolları | Arşiv bozulması, geçersiz kayıt, dosya çakışması, kullanıcı düzenlemesi, dosya yazma hatası ve yazdıktan hemen sonra kesinti sınandı. |
| Arşivler | Tar/Gzip içeriği, çalıştırma izinleri, SHA-256 ve bağımsız arşiv açma kontrol edildi. |
| Alpine Linux 3.24.4, i686 emülasyonu | Python ve Tk olmadan başlatıldı. Kurucu gerçek APK ile eksikleri yükledi. Normal kullanıcıyla CLI, masaüstü girdisi ve kaldırma kontrol edildi. |
| Debian 12, i386 emülasyonu | Python ve Tk olmadan başlatıldı. Kurucu gerçek APT ile eksikleri yükledi. Normal kullanıcıyla CLI, masaüstü girdisi ve kaldırma kontrol edildi. Debian paketi ayrıca APT ile kuruldu ve kaldırıldı. |
| Dağıtım planları | BusyBox kabuğunda dağıtım ve paket yöneticisi test girdileriyle 10 paket planı, üç değişmez sistem ve root koruması kontrol edildi. |

Konuk sistemlerde dağıtımların resmî paketleri yerel test deposundan kullanıldı. DNF, Pacman, Zypper, XBPS ve Portage paket yazma işlemleri gerçek dağıtımda henüz sınanmadı. Grafik çalışma zamanı ve masaüstü girdisi doğrulandı. Linux X11/Wayland oturumunda görsel test yapılmadı.

Linux üzerinde kullanıcı hesabıyla `python3 -m tests.installer_smoke <arşiv>` komutu izole bir kullanıcı dizininde kurulum ve kaldırma sınaması yapar. Paket yöneticisiyle bağımlılık yüklemesi, Python/Tk eksik ayrı bir sanal makinede etkileşimli olarak denenmelidir.

## Uygulama çekirdeğinde tamamlanan kontroller

| Kontrol | Sonuç |
|---|---|
| Windows, Python 3.14.10, unittest | 33 test, 32 başarılı, gerçek Linux loopback testi atlandı |
| Tk arayüzü | Arama, dil, tema, arka plan iş akışı ve sonuç görünümü geçti |
| Alt süreçler | Gerçek süreçle süre sınırı, iptal ve çıktı sınırı geçti |
| APT akışı | İzin, onay, plan değişikliği, log hatası, iptal ve sonuç senaryoları sahte alt süreçlerle geçti |
| Portable | ZIP CRC, sürüm, CLI ve Windows’ta Linux işlem kapısı kontrol edildi |
| Debian arşivi | Bağımsız tar aracıyla ar üyeleri, kontrol, izin ve paket içerik özetleri kontrol edildi |
| Temiz kaynak | Kaynak ZIP açıldı, testler tekrarlandı ve yeniden üretilen paketlerin SHA-256 değerleri eşleşti |

## Bekleyen ortam testleri

Yukarıdaki kurulum testleri Linux çekirdeği üzerinde yapıldı. APT sistem onarımı, gerçek masaüstü oturumları, uzun süreli kaynak kullanımı ve diğer dağıtım adaptörleri için ek konuk testi gerekiyor. Arch/RPM tarifleri derlenmedi.
