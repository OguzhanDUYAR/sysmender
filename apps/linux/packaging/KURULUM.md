# Linux Bakım Merkezi v0.001 · Evrensel kurucu

Geliştiren **Oguzhan DUYAR**

Arşivi Linux'ta kullanıcı hesabınızla açın. Masaüstü kurulumunda Python 3.9+ ile aynı Python için Tk kontrol edilir. Eksik sistem paketleri yalnızca onayınızla dağıtımın mevcut paket yöneticisine verilir. Ardından kurucu uygulamayı başlatmayı dener ve başarılıysa kullanıcı hesabınıza yükler.

~~~sh
tar -xzf LinuxBakimMerkezi-v0.001-Evrensel-Kurulum.tar.gz
cd LinuxBakimMerkezi-v0.001-Evrensel-Kurulum
sh install.sh
"$HOME/.local/bin/linuxbakim" gui
~~~

Linux Bakım Merkezi uygulama menüsüne de eklenir. Grafik arayüz X11 veya XWayland ile çalışır. Başsız kullanımda `--cli` seçeneği Tk ve masaüstü kısayolunu atlar.

~~~sh
sh install.sh --check
sh install.sh --cli
sh install.sh --offline
sh install.sh --python /mutlak/yol/python3
sh install.sh --uninstall
~~~

| Seçenek | Davranış |
|---|---|
| `--check` | Mevcut çalışma zamanını veya gereken paket planını gösterir. Kurulum yapmaz. Çalışma zamanı eksikse ve paket planı üretilebildiyse çıkış kodu 3'tür. |
| `--cli` | Tk gerektirmeyen kullanıcı kurulumu. Önceki yönetilen grafik kısayolu kaldırılır. |
| `--offline` | Hazır Python/Tk varsa kullanıcı kurulumu yapar. Paket yöneticisi çalıştırmaz. |
| `--python` | Verilen mutlak yoldaki Python'u kullanır. Paket yöneticisi çalıştırmaz. |
| `--uninstall` | Yalnızca bu kurucuya ait ve değiştirilmemiş uygulama dosyalarını kaldırır. |

## Dağıtım yolları

| Dağıtım ailesi | Bağımlılık yolu |
|---|---|
| Debian, Ubuntu, Mint, Pop!_OS, Kali, Raspberry Pi OS ve uyumlu türevler | APT, `python3`, `python3-tk` |
| Fedora, RHEL, Rocky, AlmaLinux ve uyumlu türevler | DNF, `python3` ve `python3-tkinter`. RHEL 8 ailesinde Python 3.9 yolu. |
| Arch, CachyOS, EndeavourOS, Manjaro, Garuda ve Artix | Pacman, `python` ve `tk` |
| openSUSE ve SLES | Zypper, dağıtım ve yorumlayıcı sürümüne uygun Python/Tk. Eski Leap/SLES için seçilen Python 3.11 paketinin mevcut depoda bulunması gerekir. |
| Alpine | APK, `python3` ve `python3-tkinter`. Paket mevcut dalın etkin deposunda bulunmalıdır. |
| Void | XBPS, `python3` ve `python3-tkinter` |
| Gentoo | Portage, Python ve masaüstü için `tk` USE desteği. Mevcut Portage ayarları kullanılır. |
| NixOS, SteamOS, Fedora Atomic ve diğer değişmez sistemler | Hazır Python/Tk ile kullanıcı kurulumu. Sistem paket katmanına yazılmaz. |
| Diğer Linux dağıtımları | Hazır Python/Tk varsa veya `--python` ile sağlanırsa kullanıcı kurulumu. |

Kurucunun Python kodu mimariye özgü ikili taşımaz. İşletim sistemindeki yorumlayıcı ve yerel araçlar kullanılır. Paket planı desteği, dağıtımın gerçek makinede test edildiği anlamına gelmez. Doğrulama ayrıntıları kaynak arşivindeki TEST_RESULTS.md dosyasındadır.

Kurucuyu `sudo sh install.sh` ile açmayın. Sistem paketleri gerekiyorsa kurucu yalnızca paket yöneticisi için `sudo` veya `doas` kullanır. Üçüncü taraf depo eklemez. Arch üzerinde yalnızca mevcut eşitleme verisiyle paket kurulur. Bu, dağıtımın normal güncelleme sürecinin yerini almaz. Uygulamaya ait olmayan paketler kaldırma sırasında silinmez. Donanım ve yönetim araçları ilgili sistem paketleri mevcutsa kullanılabilir. Kurucu firmware, sürücü ve sunucu paketlerini otomatik eklemez.

Uygulama ve kurulum kaydı `${XDG_DATA_HOME:-$HOME/.local/share}/linuxbakim` altındadır. Kurulum seçeneklerini değiştirmek veya yeni pakete geçmek için kurucuyu yeniden çalıştırın. Mevcut kullanıcı dosyasıyla çakışmada kurulum durur. Ayrı Debian sistem paketi kullanılıyorsa onu APT ile yönetin.

## Paket referansları

[Debian Tk](https://packages.debian.org/stable/python/python3-tk) · [Fedora Tk](https://packages.fedoraproject.org/pkgs/python3.14/python3-tkinter/) · [RHEL 8 paket listesi](https://docs.redhat.com/en/documentation/red_hat_enterprise_linux/8/html-single/package_manifest/index) · [Arch Python](https://archlinux.org/packages/core/x86_64/python/) · [Alpine Tk](https://pkgs.alpinelinux.org/packages?branch=v3.23&name=python3-tkinter) · [Void Tk](https://github.com/void-linux/void-packages/blob/master/srcpkgs/python3-tkinter/template) · [Gentoo tk](https://packages.gentoo.org/useflags/tk)
