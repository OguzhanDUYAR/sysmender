# Açık kaynak araştırması

Bu tarama, mimari ve araç davranışı seçimi için yapıldı. Üçüncü taraf depo kodu, görselleri ve dağıtım dosyaları alınmadı. Bu nedenle adaylar için kaynak ithalatına yönelik tam güvenlik, CI veya commit denetimi yapılmadı. Aşağıdaki sürümler görülen etiketlerdir.

| Aday | İncelenen kanıt ve karar |
|---|---|
| Cockpit | 371 etiketi, commit `81e54f105922348261530429fcab2f4faac0a47e`. LGPL-2.1-or-later ve GPL-3.0-or-later bileşenleri. Başsız yönetim yaklaşımı incelendi, web servisi eklenmedi. [Sürüm](https://github.com/cockpit-project/cockpit/releases/tag/371) |
| btop | v1.4.8, Apache-2.0. Sürüm notlarında örnekleme ve eşzamanlılık düzeltmeleri var. Ölçüm mantığı Linux dokümanlarından bağımsız yazıldı. [Depo](https://github.com/aristocratos/btop) · [Sürüm](https://github.com/aristocratos/btop/releases/tag/v1.4.8) |
| BleachBit | v6.0.6, GPL-3.0. Temizleyici kuralları ve silme işlevi eklenmedi. [Depo](https://github.com/bleachbit/bleachbit) · [Sürüm](https://github.com/bleachbit/bleachbit/releases/tag/v6.0.6) |
| Snapper | v0.13.2, GPL-2.0 etiketi. Yalnızca kullanıcının sisteminde bulunan CLI ile yapılandırma sorgusu. [Depo](https://github.com/openSUSE/snapper) · [Sürüm](https://github.com/openSUSE/snapper/releases/tag/v0.13.2) |
| Timeshift | Linux Mint deposu, GPL-2.0-or-later lisans dosyası ve `8958d97` commit sayfası görüldü. Mount ve geri alma kapsamı nedeniyle entegrasyon bekliyor. [Depo](https://github.com/linuxmint/timeshift) · [Lisans](https://github.com/linuxmint/timeshift/tree/master/LICENSES) |
| Lynis | 3.1.7, GPL-3.0 etiketi. Tam denetim paketi veya kural seti eklenmedi. [Depo](https://github.com/CISOfy/lynis) · [Sürüm](https://github.com/CISOfy/lynis/releases/tag/3.1.7) |
| Netdata | Depoda GPL-3.0 gösteriliyor. Sürekli çalışan ajan yerine uygulama içi örnekleme. [Depo](https://github.com/netdata/netdata) |
| Stacer | GPL-3.0 etiketi. Masaüstü araç gruplaması kıyaslandı. [Depo](https://github.com/oguzhaninan/Stacer) |
| Mission Center | Upstream GitLab, GitHub aynası kendisini gayriresmî olarak işaretliyor. Aynadaki README GPLv3 bildiriyor. [Upstream](https://gitlab.com/mission-center-devs/mission-center) · [Ayna](https://github.com/Slimbook-Team/mission-center) |
| Resources | GPL-3.0 etiketi, masaüstü kaynak görünümü. [Depo](https://github.com/nokyan/resources) |
| Webmin | Resmî sayfada BSD-3-Clause. Web yönetim servisi eklenmedi. [Proje](https://webmin.com/about/) |
| YaST | yast-yast2 deposunda GPL-2.0 etiketi. openSUSE’ye özgü yönetim katmanı taşınmadı. [Depo](https://github.com/yast/yast-yast2) |
| KDE System Monitor, GNOME Disks | Resmî proje sayfaları bulundu. Masaüstü görünümü ve disk araçlarının kapsamı karşılaştırıldı. Depo dosyaları ve lisans bileşenleri doğrulanmadı. [KDE](https://apps.kde.org/plasma-systemmonitor/) · [GNOME](https://apps.gnome.org/DiskUtility/) |
| GSmartControl | GPL-3.0 etiketi. SMART arayüzü alınmadı, yerel smartctl keşfi kullanıldı. [Depo](https://github.com/ashaduri/gsmartcontrol) |
| OpenSnitch | GPL-3.0 etiketi. Güvenlik duvarı ajanı veya politikası eklenmedi. [Depo](https://github.com/evilsocket/opensnitch) |
| Nmap | Nmap Public Source License. Port tarayıcısı eklenmedi, açıkça girilen tek hedef/port için Python soketi kullanıldı. [Lisans](https://nmap.org/npsl/) · [Depo](https://github.com/nmap/nmap) |

Uygulamadaki ayrıştırma ve komut sözleşmeleri için [Linux procfs](https://docs.kernel.org/filesystems/proc.html), [Debian APT](https://manpages.debian.org/bookworm/apt/apt-get.8.en.html), [DNF](https://dnf.readthedocs.io/en/latest/command_ref.html), [pacman](https://pacman.archlinux.page/pacman.8.html) ve [lsblk](https://manpages.debian.org/bookworm/util-linux/lsblk.8.en.html) referans alındı.
