# Özellik kapsamı

| Alan | Uygulanan | Bekleyen kapsam |
|---|---|---|
| Sistem | Canlı CPU/RAM/takas/ağ/disk, basınç, RSS, distro/kernel/init | Per-core grafik, GPU zaman serisi, performans profilleri |
| Paket | Envanter, seçili adaptörlerde önbellekten güncelleme, dosya ve DB denetimi, Flatpak/Snap sorgusu | Toplu kaldırma, yükseltme, depo düzenleme |
| Onarım | APT bağımlılık planı, etkileşimli terminal, kaldırma reddi, sonuç denetimi | RPM/Pacman, kernel/boot, otomatik geri alma |
| Servis | systemd/OpenRC/runit envanteri, başarısız birimler ve zamanlayıcılar | Servis ve başlangıç öğesi değişiklikleri |
| Ağ | IP/yönlendirme, DNS, komşular, soketler, tek TCP hedefi | Ağ ayarı ve uzak bakım |
| Disk | Blok/mount/alan/fstab, SMART keşfi, NVMe, RAID, Btrfs/ZFS | SMART sağlık testi, fsck, bölümleme, biçimlendirme |
| Donanım | PCI/USB/modüller, sensör, pil, NVIDIA, fwupd envanteri | Firmware ve sürücü yükleme |
| Güvenlik | Firewall, kernel, Secure Boot, SELinux/AppArmor bilgisi | Politika değişikliği ve CVE taraması |
| Günlükler | journald, önceki boot, syslog, çökme meta verileri | Zaman aralığı filtreleme ve otomatik kök neden |
| Masaüstü/oyun | XRandR/KDE/Sway/Hyprland, ses, Vulkan/OpenGL, GameMode | Native Wayland arayüz, masaüstü ve oyun optimizasyonu |
| Kurtarma | Snapper ve ZFS snapshot envanteri | Oluşturma/geri yükleme, Timeshift, Restic/Borg |
| GUI | TR/EN, koyu/açık, arama, oturum favorileri, iptal, JSON özet | Kalıcı ayarlar, bildirimler, GUI yetkilendirme |
| Paketleme | Portable, Debian, Arch/RPM tarifleri | Derlenmiş RPM, AppImage, Flatpak |
