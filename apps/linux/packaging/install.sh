#!/bin/sh
# Linux Bakım Merkezi v0.001 · Oguzhan DUYAR
set -fu
PATH=/usr/sbin:/usr/bin:/sbin:/bin:/run/current-system/sw/bin:/nix/var/nix/profiles/default/bin
export PATH
profile=desktop
mode=install
offline=no
custom=

die() { printf '%s\n' "$*" >&2; exit 1; }

while [ "$#" -gt 0 ]; do
    case "$1" in
        --cli) profile=cli ;;
        --check) [ "$mode" = install ] || die 'Bir işlem seçin.'; mode=check ;;
        --uninstall) [ "$mode" = install ] || die 'Bir işlem seçin.'; mode=uninstall ;;
        --offline) offline=yes ;;
        --python)
            [ "$#" -ge 2 ] || die '--python için mutlak bir yol girin.'
            shift
            case "$1" in /*) custom=$1 ;; *) die 'Python yolu / ile başlamalı.' ;; esac
            offline=yes ;;
        --help|-h)
            printf '%s\n' 'Linux Bakım Merkezi v0.001 · Geliştiren Oguzhan DUYAR' \
                'sh install.sh [--cli] [--offline | --python /mutlak/yol]' \
                'sh install.sh --check [--cli] [--offline | --python /mutlak/yol]' \
                'sh install.sh --uninstall [--python /mutlak/yol]'
            exit 0 ;;
        *) die "Bilinmeyen seçenek: $1" ;;
    esac
    shift
done

[ "$(uname -s)" = Linux ] || die 'Bu kurucu Linux çekirdeği gerektirir.'
if [ "$mode" != check ] && [ "$(id -u)" -eq 0 ]; then
    die 'Kurucuyu normal kullanıcı olarak açın. Gerekirse paketler için sudo/doas çağrılır.'
fi

case "$0" in */*) here=${0%/*} ;; *) here=. ;; esac
bundle=$(CDPATH='' cd -- "$here" && pwd -P) || die 'Kurulum klasörü okunamadı.'
[ -f "$bundle/setup.py" ] || die 'Arşivin tamamını çıkartın. setup.py bulunamadı.'
if [ "$mode" != uninstall ]; then
    [ -f "$bundle/linuxbakim.pyz" ] && [ -f "$bundle/bundle.sha256" ] &&
        [ -f "$bundle/org.oguzhanduyar.LinuxBakim.svg" ] || die 'Arşivin tamamını aynı klasöre çıkartın.'
    if command -v sha256sum >/dev/null 2>&1; then
        (cd "$bundle" && sha256sum -c bundle.sha256 >/dev/null) ||
            die 'Arşivdeki dosyalar bütünlük kontrolünden geçmedi. Arşivi tekrar çıkartın.'
    elif [ "$offline" = no ]; then
        die 'Bütünlük kontrolü için sha256sum gerekli. Python hazırsa --offline ile deneyin.'
    fi
fi

distro=unknown
like=
variant=
version=
release=/etc/os-release
[ -r "$release" ] || release=/usr/lib/os-release
if [ -r "$release" ]; then
    while IFS= read -r line || [ -n "$line" ]; do
        case "$line" in ID=*|ID_LIKE=*|VARIANT_ID=*|VERSION_ID=*) ;; *) continue ;; esac
        value=${line#*=}
        case "$value" in
            \"*\") value=${value#\"}; value=${value%\"} ;;
            \'*\') value=${value#\'}; value=${value%\'} ;;
        esac
        case "$value" in *[!a-zA-Z0-9._' '-]*) continue ;; esac
        case "$line" in
            ID=*) distro=$value ;; ID_LIKE=*) like=$value ;;
            VARIANT_ID=*) variant=$value ;; VERSION_ID=*) version=$value ;;
        esac
    done < "$release"
fi

mutable=yes
case "$distro:$variant" in
    steamos:*|nixos:*|bazzite:*|bluefin:*|aurora:*|*microos*|*aeon*|*kalpa*|*silverblue*|*kinoite*|*sericea*|*onyx*|*atomic*|*coreos*|vanilla:*|blendos:*|flatcar:*|talos:*)
        mutable=no ;;
esac
[ ! -e /run/ostree-booted ] || mutable=no

manager=
base_pkg=
tk_pkg=
preferred=/usr/bin/python3
for hint in "$distro" $like; do
    case "$hint" in
        ubuntu|debian|linuxmint|kali|raspbian|pop|zorin|neon|elementary|devuan|mx)
            manager=apt-get; base_pkg=python3; tk_pkg=python3-tk ;;
        arch|archlinux|archarm|cachyos|endeavouros|manjaro|garuda|artix)
            manager=pacman; base_pkg=python; tk_pkg=tk ;;
        fedora|rhel|centos|rocky|almalinux|ol|amzn|nobara)
            manager=dnf; base_pkg=python3; tk_pkg=python3-tkinter
            case "$hint:$version" in
                rhel:8|rhel:8.*|centos:8|centos:8.*|rocky:8|rocky:8.*|almalinux:8|almalinux:8.*|ol:8|ol:8.*)
                    preferred=/usr/bin/python3.9; base_pkg=python39; tk_pkg=python39-tkinter ;;
            esac ;;
        opensuse|opensuse-leap|opensuse-tumbleweed|opensuse-slowroll|suse|sles)
            manager=zypper; base_pkg=python3; tk_pkg=python3-tk
            case "$version" in
                15|15.*) preferred=/usr/bin/python3.11; base_pkg=python311; tk_pkg=python311-tk ;;
                16|16.*) ;;
                *) tk_pkg=versioned ;;
            esac ;;
        alpine) manager=apk; base_pkg=python3; tk_pkg=python3-tkinter ;;
        void) manager=xbps-install; base_pkg=python3; tk_pkg=python3-tkinter ;;
        gentoo) manager=emerge; base_pkg='>=dev-lang/python-3.9'; tk_pkg='>=dev-lang/python-3.9[tk]' ;;
        *) continue ;;
    esac
    break
done

python_ok() {
    [ -x "$1" ] && "$1" -I -c 'import sys, zlib; sys.exit(sys.version_info < (3, 9))' >/dev/null 2>&1
}

tk_ok() {
    "$1" -I -c 'import tkinter; tkinter.Tcl().eval("info patchlevel")' >/dev/null 2>&1
}

select_python() {
    py=
    if [ -n "$custom" ]; then
        set -- "$custom"
    else
        set -- "$preferred" /usr/bin/python3 /bin/python3 /usr/local/bin/python3 \
            /run/current-system/sw/bin/python3 "${HOME:-/nonexistent}/.nix-profile/bin/python3" \
            /nix/var/nix/profiles/default/bin/python3 /usr/bin/python3.11 /usr/bin/python3.12 \
            /usr/bin/python3.13 /usr/bin/python3.14 /usr/bin/python3.10 /usr/bin/python3.9
    fi
    for candidate in "$@"; do
        if python_ok "$candidate" && { [ "$profile" = cli ] || tk_ok "$candidate"; }; then
            py=$candidate
            return 0
        fi
    done
    return 1
}

if [ "$mode" = uninstall ]; then profile=cli; fi

if ! select_python; then
    if [ "$mode" = uninstall ]; then
        die 'Kaldırma için Python 3.9+ gerekli. Kurulu yorumlayıcının yolunu --python ile verin.'
    fi
    if [ "$profile" = desktop ]; then
        required='Python 3.9+ ve Tk (aynı Python yorumlayıcısı)'
    else
        required='Python 3.9+'
    fi
    printf '%s\n' "Dağıtım: $distro $version | Gereksinim: $required"
    [ "$offline" = no ] || die 'Çevrimdışı kurulum için çalışma zamanı eksik. Python/Tk yolunu veya --cli seçeneğini kontrol edin.'
    [ "$mutable" = yes ] || die 'Bu sistemde otomatik paket değişikliği kapalı. Dağıtımın desteklediği Python ortamını --python ile verin.'
    [ -n "$manager" ] || die 'Otomatik paket kurulumu bu dağıtımda tanımlı değil. Python 3.9+ ve gerekirse Tk kurun.'
    pkg_bin=$(command -v "$manager") || die "Paket yöneticisi bulunamadı: $manager"
    case "$pkg_bin" in /*) ;; *) die 'Paket yöneticisinin mutlak yolu bulunamadı.' ;; esac

    set --
    if ! python_ok "$preferred"; then
        if [ -x "$preferred" ] && "$preferred" -I -c 'import sys; sys.exit(sys.version_info >= (3, 9))' >/dev/null 2>&1; then
            die "Sistemdeki $preferred çok eski. Python 3.9+ sağlayıp --python ile seçin."
        fi
        set -- "$base_pkg"
    fi
    if [ "$profile" = desktop ]; then
        if [ "$manager" = emerge ]; then
            set -- "$tk_pkg"
        elif [ "$tk_pkg" != versioned ]; then
            set -- "$@" "$tk_pkg"
        fi
    fi
    printf 'Eksik çalışma zamanı için %s paket planı:' "$manager"
    if [ "$#" -gt 0 ]; then printf ' %s' "$@"; fi
    if [ "$profile" = desktop ] && [ "$tk_pkg" = versioned ]; then printf ' python3XX-tk (yorumlayıcı sürümüne göre)'; fi
    printf '\n'
    [ "$mode" != check ] || exit 3
    [ -t 0 ] && [ -t 1 ] || die 'Sistem bağımlılığı kurmak için etkileşimli bir terminalden çalıştırın.'
    if [ -x /usr/bin/sudo ]; then
        admin() { /usr/bin/sudo -- "$@"; }
    elif [ -x /usr/bin/doas ]; then
        admin() { /usr/bin/doas "$@"; }
    else
        die 'Eksik sistem paketleri için sudo veya doas gerekli.'
    fi
    printf 'Yerel paket yöneticisiyle devam edilsin mi? [e/H] '
    IFS= read -r answer || die 'Kurulum iptal edildi.'
    case "$answer" in e|E|evet|Evet|y|Y|yes|Yes) ;; *) die 'Kurulum iptal edildi.' ;; esac

    if [ "$#" -gt 0 ]; then
        case "$manager" in
            apt-get)
                admin "$pkg_bin" -o APT::Update::Error-Mode=any \
                    -o Acquire::AllowInsecureRepositories=false update &&
                    admin "$pkg_bin" --no-remove --no-install-recommends \
                        -o APT::Get::Assume-Yes=false -o APT::Get::AllowUnauthenticated=false \
                        -o APT::Get::Allow-Downgrades=false -o APT::Get::Allow-Change-Held-Packages=false \
                        -o Acquire::AllowInsecureRepositories=false install "$@" ||
                    die 'APT bağımlılıkları kuramadı. Uygulama dosyaları yazılmadı.' ;;
            pacman)
                admin "$pkg_bin" -S --needed "$@" ||
                    die 'Pacman bağımlılıkları kuramadı. Dağıtımınızın normal güncelleme sürecini kontrol edin.' ;;
            dnf) admin "$pkg_bin" install "$@" || die 'DNF bağımlılıkları kuramadı.' ;;
            zypper) admin "$pkg_bin" install --no-recommends "$@" || die 'Zypper bağımlılıkları kuramadı.' ;;
            apk) admin "$pkg_bin" add --update-cache "$@" || die 'APK bağımlılıkları kuramadı.' ;;
            xbps-install) admin "$pkg_bin" -S "$@" || die 'XBPS bağımlılıkları kuramadı.' ;;
            emerge) admin "$pkg_bin" --ask --autounmask=n "$@" ||
                die 'Portage bağımlılıkları kuramadı. Python/Tk USE ayarlarını kontrol edin veya --cli kullanın.' ;;
        esac
    fi
    if [ "$profile" = desktop ] && [ "$tk_pkg" = versioned ] && ! select_python; then
        python_ok "$preferred" || die 'Sistemdeki openSUSE Python yorumlayıcısı çalışmadı.'
        tk_pkg=$("$preferred" -I -c 'import sys; print("python%d%d-tk" % sys.version_info[:2])') ||
            die 'Tk paket sürümü belirlenemedi.'
        case "$tk_pkg" in python3[0-9]-tk|python3[0-9][0-9]-tk) ;; *) die 'Tk paket adı doğrulanamadı.' ;; esac
        admin "$pkg_bin" install --no-recommends "$tk_pkg" || die 'Zypper Tk paketini kuramadı.'
    fi
    select_python || die 'Paket işlemi tamamlandı ancak uygun Python/Tk çalışmadı. Uygulama dosyaları yazılmadı.'
fi

printf '%s\n' "Dağıtım: $distro $version | Mod: $profile | Python: $py"
[ "$mode" != check ] || exit 0
if [ "$profile" = desktop ] && [ -z "${DISPLAY:-}" ]; then
    printf '%s\n' 'Masaüstü oturumu algılanmadı. GUI için X11 veya Wayland altında XWayland gerekir.'
fi
exec "$py" -I "$bundle/setup.py" "$mode" "$profile" "$py"
