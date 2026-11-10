# Dağıtım uyumluluğu

Her Linux satırı hedef adaptör durumudur ve gerçek konuk testi bekler. Araçlar yerel paketler, oturum ve izinlere bağlıdır.

Dağıtımlar arası kurucunun Python/Tk paket adları ve çevrimdışı çalışma seçenekleri [kurulum kılavuzundadır](packaging/KURULUM.md).

| Hedef | Paket ve init yolu |
|---|---|
| Ubuntu, Debian, Linux Mint, Pop!_OS, Zorin, Kali, Raspberry Pi OS | APT/dpkg, systemd algılama, yerel APT onarım adayı |
| Arch, CachyOS, EndeavourOS, Manjaro, Garuda | Pacman ve systemd algılama |
| Fedora, Rocky, AlmaLinux, RHEL | DNF/RPM, systemd ve SELinux |
| openSUSE, SLES | Zypper/RPM, Snapper envanteri |
| Alpine | APK, musl ve OpenRC |
| Gentoo | Portage, qlist varsa envanter, OpenRC/systemd |
| Void | XBPS ve runit |
| NixOS | nix-env sorgusu, deklaratif değişiklik yok |
| Fedora Atomic | rpm-ostree durum sorgusu |
| SteamOS | Pacman envanteri ve isteğe bağlı oyun araçları |
| WSL, container, SSH | Erişilebilir tanılama, APT onarımı kapalı |
| Windows | Geliştirme ve GUI testi, Linux araçları kullanılamıyor |

x86_64, ARM64, gerçek GNOME/KDE/Hyprland, native Wayland ve X11 konuk oturumları doğrulanmadı. Zipapp ve Debian arşivi mimariden bağımsız Python taşır. glibc/musl algılama, Python ve yerel araç sürümleri için ayrıca çalıştırma testi gerekir.
