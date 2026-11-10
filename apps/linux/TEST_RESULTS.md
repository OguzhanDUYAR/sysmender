# Test sonuçları

## Tamamlanan kontroller

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

WSL kurulu değildi. VirtualBox, çalışma oturumunda E_ACCESSDENIED döndürdü. Yerel bir Linux çekirdeğinde çalıştırma ve DEB kurulum/kaldırma yapılmadı. Gerçek APT onarımı, Wayland/X11, uzun süreli kaynak kullanımı ve dağıtım adaptörleri için VM veya fiziksel konuk testi gerekiyor. Arch/RPM tarifleri derlenmedi. Polkit bu sürümün kapsamı dışındadır.
