# Özellik matrisi · v0.001

v4.4 temel kaynak olarak incelendi. v5.0 çalışma ağacındaki 52 BT aracı, mevcut WinUtil atıfları ve işletim akışları korundu. v0.001 kataloğu 66 araç içerir. Bunların 57'si okuma veya tanılama, 9'u yönetici bakım eylemidir. Dosya çalışma alanı ve asistan bu sayıya dahil edilmedi.

| Alan | Önceki durum | v0.001 | Öncelik ve açık kapsam |
| --- | --- | --- | --- |
| Canlı gösterge paneli | CPU/RAM ve disk boş alanı | Çalışma süresi, etkin Ethernet/Wi-Fi toplam alım/gönderim hızı | P0. GPU ve disk I/O sayaçları sırada |
| Cihaz ve Windows | CIM donanım, BIOS, batarya, sürücü ve sürüm | USB sınıfı aygıtlar, kurulum tarihi, lisans durumu, kayıt defteri geri dönüşü | P0. Batarya kapasite geçmişi ve üretici sensörleri sırada |
| Olay ve BSOD | Sistem/uygulama/güvenilirlik ve çökme olayları | Döküm tarihi ve boyutu, CBS/DISM son kayıtları | P0. Döküm içeriği ve sembol tabanlı analiz sırada |
| Windows onarımı | DISM, SFC, CHKDSK, WUA, geri yükleme noktası | Güncelleme servis ve ilke okuması, BCD okuma, asistanla DISM/SFC ve kontrol | P0. Doğrudan BCD değişikliği ve Update önbellek sıfırlaması yok |
| Ağ | IP/DHCP/DNS, TCP, bağlantı testi, VPN, SMB, RDP, Winsock | Arabirim ve bağlantı hızı, sayısal TCP/UDP ve sınırlı rota izleme | P0. PathPing, DNS sunucusu geri alma, DHCP yenileme ve ağ içe aktarma sırada |
| Güvenlik | Defender, Firewall profilleri, BitLocker, TPM/Secure Boot, sertifika | UAC göstergeleri, yerel yönetici üyeleri ve etkin gelen Firewall kuralları | P0/P1. Uyum politikası puanlaması ve TLS denetimi sırada |
| Yazılım ve sürücü | WinGet, Store, kaldırma, sürücü yedeği ve yeniden tarama | WinGet'ten bağımsız 32/64 bit masaüstü envanteri | P1. Sürücü geri dönüş otomasyonu ve kurum paket profili sırada |
| Depolama ve dosya | Disk analizi/iyileştirme, Temp önizleme | Seçilen NTFS dizininde büyük dosya ve eşleşme incelemesi, SHA-256 doğrulama | P1. Genel yedekleme/geri yükleme ve profil taşıma sırada |
| Servis ve görevler | İsteğe bağlı servis denetimi, durum, görev sonucu | Kritik servis durum/mod kontrolü ve senaryo bazlı Spooler adımı | P1. Servis yapılandırma yedeği ve süreç ağacı sırada |
| Asistan | Hazır okuma profilleri | Yedi belirti, tanılama, ayrı onarım onayı ve takip adımları | P0/P1. Bozuk önyüklemede WinPE çalışma ve ses/Bluetooth sihirbazı sırada |
| Raporlama | HTML/JSON ve güncel günlük | Kalıcı metin geçmişi, JSON karşılaştırma, azaltılmış paylaşım özeti | P0. Ham günlük satırı farkı ve merkezi denetim sırada |
| Arayüz | Koyu lime, TR/EN ve kategori | Açık tema, Ctrl+K genel arama, favoriler | P0. Diğer DPI/çoklu monitör laboratuvar testi sırada |
| Eklentiler ve kurumsal | Yerel sabit katalog | Aynı doğrulanan gömülü kayıt sistemi genişletildi | P2. İmzalı dış eklenti, AD/Entra/Intune ve yetkili uzaktan yönetim sırada |

