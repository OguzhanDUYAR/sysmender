# Mimari

| Katman | Dosyalar | Sorumluluk |
|---|---|---|
| Sunum | `gui.py`, `cli.py` | Masaüstü, terminal, yerelleştirme |
| Uygulama | `catalog.py`, `workflows.py` | Araç tablosu, tanılama planları, APT onayı ve doğrulama |
| Sistem | `host.py`, `adapters.py`, `metrics.py`, `runner.py` | Algılama, sabit komut dizileri, Linux sayaçları, sınırlı süreçler |
| Veri | `reports.py` | Şemalı JSON ve özel onarım kayıtları |
| Paketleme | `build_release.py`, `packaging/` | Zipapp, Debian arşivi, masaüstü girdisi ve ikon |

Windows v4.4, C# WinForms ile Windows yönetim araçları ve PowerShell kullanıyordu. Plan, sonuç geçmişi ve canlı ekran yaklaşımı Linux’a taşındı. Registry, Windows servis API’leri ve DISM/SFC bağımlılıkları Linux platformuna aktarılmadı.

Python standart kütüphanesi ve isteğe bağlı Tk, başsız kullanımda üçüncü taraf Python bağımlılıklarını kaldırır. PySide6 daha zengin Wayland ve erişilebilirlik seçenekleriyle birlikte Qt çalışma zamanı getirir. Rust/GTK4 ve Rust/Qt mimariye özgü derleme ve toolkit bağları gerektirir. Tauri web görünümüne bağımlıdır. Tk seçimi GUI’yi X11/XWayland ile sınırlar.

Arayüzde tek arka plan işçisi sonuçları kuyrukla ana döngüye yollar. Araç çıktısı 192 KiB, geçmiş 100 sonuçla sınırlıdır. Ölçüm 1,5 saniye aralıklıdır. İlk hız örneği, kayıp veya sıfırlanmış sayaçlar boş değer verir. Ağ toplamı loopback’i dışlar ve sanal arabirimleri kapsayabilir. Disk toplamı tanınan fiziksel aygıt adlandırmalarıyla sınırlıdır.
