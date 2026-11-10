"""Shared tool catalog, real capability gates, and normalized outcomes."""
from __future__ import annotations

import ipaddress
import json
import os
import re
import socket
import threading
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable, Dict, Optional, Tuple

from .adapters import PACKAGES, SERVICES
from .host import Host, find_tool, read_text
from .metrics import Sampler
from .runner import CommandResult, clean, execute

OK, REVIEW, FAILED, UNAVAILABLE, CANCELLED = "ok", "review", "failed", "unavailable", "cancelled"
GROUPS = {"system": ("Sistem", "System"), "packages": ("Paketler", "Packages"),
          "services": ("Servisler", "Services"), "network": ("Ağ", "Network"),
          "storage": ("Depolama", "Storage"), "hardware": ("Donanım", "Hardware"),
          "security": ("Güvenlik", "Security"), "logs": ("Günlükler", "Logs"),
          "desktop": ("Masaüstü", "Desktop"), "gaming": ("Oyun", "Gaming"),
          "recovery": ("Kurtarma", "Recovery")}


@dataclass(frozen=True)
class Result:
    tool: str
    status: str
    detail: str
    code: Optional[int] = None
    truncated: bool = False

    def as_dict(self) -> dict:
        return asdict(self)


@dataclass(frozen=True)
class Context:
    host: Host
    lang: str = "tr"
    find: Callable = find_tool
    execute: Callable = execute

    def text(self, tr: str, en: str) -> str:
        return en if self.lang == "en" else tr


@dataclass(frozen=True)
class Tool:
    id: str
    group: str
    tr: str
    en: str
    work: Callable
    risk: str = "read"

    def name(self, context: Context) -> str:
        return context.text(self.tr, self.en)


TOOLS: Dict[str, Tool] = {}


def unavailable(tool: str, ctx: Context, message: str = "") -> Result:
    return Result(tool, UNAVAILABLE, message or ctx.text("Bu ortam için uygun araç bulunamadı.", "No matching capability on this host."))


def run(tool: str, ctx: Context, stop: Optional[threading.Event] = None) -> Result:
    if tool not in TOOLS:
        return unavailable(tool, ctx, ctx.text("Bilinmeyen araç.", "Unknown tool."))
    if not ctx.host.linux:
        return unavailable(tool, ctx, ctx.text("Bu işlev Linux çekirdeği gerektirir.", "This tool requires a Linux kernel."))
    if stop and stop.is_set():
        return Result(tool, CANCELLED, ctx.text("İptal edildi.", "Cancelled."))
    try:
        result = TOOLS[tool].work(tool, ctx, stop)
        return Result(result.tool, result.status, clean(result.detail), result.code, result.truncated)
    except (OSError, ValueError, OverflowError) as error:
        return Result(tool, FAILED, clean(str(error)))


def run_command(tool: str, ctx: Context, stop, args: Tuple[str, ...], timeout=15,
                ok_codes=(0,), inspect: Optional[Callable[[CommandResult], bool]] = None) -> Result:
    if not args:
        return unavailable(tool, ctx)
    binary = ctx.find(args[0])
    if not binary:
        return unavailable(tool, ctx, ctx.text("Bulunamadı: ", "Missing executable: ") + args[0])
    outcome = ctx.execute((binary, *args[1:]), timeout=timeout, stop=stop)
    if outcome.reason:
        status = CANCELLED if outcome.reason == "cancelled" else FAILED
        reasons = {"cancelled": ctx.text("İptal edildi.", "Cancelled."), "timeout": ctx.text("Zaman aşımı.", "Timed out."),
                   "start": ctx.text("İşlem başlatılamadı.", "Could not start command.")}
        return Result(tool, status, reasons.get(outcome.reason, outcome.reason) + "\n" + outcome.output,
                      outcome.code, outcome.truncated)
    status = OK if outcome.code in ok_codes else FAILED
    if status == OK and (outcome.truncated or (inspect and inspect(outcome))):
        status = REVIEW
    return Result(tool, status, outcome.output.strip() or ctx.text("Komut çıktı döndürmedi.", "The command returned no output."),
                  outcome.code, outcome.truncated)


def add(identifier, group, tr, en, action):
    if identifier in TOOLS:
        raise ValueError("Duplicate tool ID")
    TOOLS[identifier] = Tool(identifier, group, tr, en, action)


def command(identifier, group, tr, en, args, timeout=15, inspect=None):
    add(identifier, group, tr, en, lambda tool, ctx, stop: run_command(tool, ctx, stop, args, timeout, inspect=inspect))


def system_info(tool, ctx, stop):
    return Result(tool, OK, json.dumps(ctx.host.as_dict(), indent=2, ensure_ascii=False))


def resource_snapshot(tool, ctx, stop):
    values = Sampler().sample()
    used = values.get("memory_used")
    return Result(tool, OK if used is not None else REVIEW, json.dumps(values, indent=2))


def processes(tool, ctx, stop):
    rows = []
    for path in sorted(Path("/proc").glob("[0-9]*"), key=lambda item: item.name)[:4096]:
        if stop and stop.is_set():
            return Result(tool, CANCELLED, ctx.text("İptal edildi.", "Cancelled."))
        if not path.name.isdigit():
            continue
        text = read_text(path / "status", 8192)
        if text is None:
            continue
        values = {key: value.strip() for key, sep, value in (line.partition(":") for line in text.splitlines()) if sep}
        mem = (values.get("VmRSS", "").split() or ["0"])[0]
        rows.append((int(mem) if mem.isdigit() else 0, path.name, values.get("PPid", "?"),
                     values.get("State", "?"), values.get("Name", "?")))
    rows.sort(reverse=True)
    if not rows:
        return unavailable(tool, ctx, "/proc/<pid>/status")
    header = "PID\tPPID\tRSS KiB\tSTATE\tNAME"
    return Result(tool, OK, header + "\n" + "\n".join(f"{pid}\t{ppid}\t{rss}\t{state}\t{name}" for rss, pid, ppid, state, name in rows[:60]))


def package_view(kind: str):
    def action(tool, ctx, stop):
        adapter = PACKAGES.get(ctx.host.package)
        args = getattr(adapter, kind, ()) if adapter else ()
        if not args:
            return unavailable(tool, ctx, ctx.text("Bu paket yöneticisi için denetim tanımlı değil: ",
                                                   "No adapter for this package operation: ") + ctx.host.package)
        if kind == "updates" and ctx.host.package == "pacman":
            out = run_command(tool, ctx, stop, args, 30, (0, 1), lambda result: result.code == 1 and bool(result.output.strip()))
            return out
        allowed = (0, 100) if kind == "updates" and ctx.host.package == "dnf" else (0,)
        inspect = None
        if kind == "verify" and ctx.host.package in {"apt", "rpm", "dnf", "zypper"}:
            if ctx.host.package != "apt":
                allowed = (0, 1)
            inspect = lambda result: result.code != 0 or bool(result.output.strip())
        elif kind == "audit" and ctx.host.package == "apt":
            inspect = lambda result: bool(result.output.strip())
        return run_command(tool, ctx, stop, args, 60 if kind == "verify" else 35, allowed, inspect)
    return action


def services(tool, ctx, stop):
    init = ctx.host.init
    if init == "runit":
        names = tuple(str(path) for path in sorted(Path("/var/service").glob("*"))[:100]
                      if path.is_dir() and re.fullmatch(r"[A-Za-z0-9_.@-]+", path.name))
        return run_command(tool, ctx, stop, ("sv", "status", *names) if names else ())
    return run_command(tool, ctx, stop, SERVICES.get(init, ()))


def systemd(args, inspect=None):
    def action(tool, ctx, stop):
        if ctx.host.init != "systemd":
            return unavailable(tool, ctx, ctx.text("Aktif systemd gerekli.", "An active systemd instance is required."))
        return run_command(tool, ctx, stop, args, 20, inspect=inspect)
    return action


def journal(tool, ctx, stop):
    if ctx.host.init == "systemd":
        args = ("journalctl", "-b", "-p", "warning", "-n", "100", "--no-pager", "-o", "short-iso")
        return run_command(tool, ctx, stop, args, 20, inspect=lambda item: "permission" in item.output.lower())
    for path in (Path("/var/log/syslog"), Path("/var/log/messages")):
        try:
            with path.open("rb") as stream:
                stream.seek(max(0, stream.seek(0, os.SEEK_END) - 65536))
                lines = stream.read(65536).decode("utf-8", "replace").splitlines()
                return Result(tool, OK, str(path) + "\n" + "\n".join(lines[-100:]))
        except OSError:
            continue
    return unavailable(tool, ctx, ctx.text("Okunabilir journald/syslog bulunamadı.", "No readable journald or syslog source."))


def firewall(tool, ctx, stop):
    if ctx.find("firewall-cmd"):
        state = run_command(tool, ctx, stop, ("firewall-cmd", "--state"))
        if state.status == OK and state.detail == "running":
            return run_command(tool, ctx, stop, ("firewall-cmd", "--get-active-zones"))
    for args in (("nft", "list", "ruleset"), ("ufw", "status", "verbose")):
        if ctx.find(args[0]):
            return run_command(tool, ctx, stop, args)
    return unavailable(tool, ctx)


def sysfiles(paths: Tuple[str, ...]):
    def action(tool, ctx, stop):
        rows = [(path, read_text(Path(path), 32768)) for path in paths]
        available = [(path, value.strip()) for path, value in rows if value is not None]
        if not available:
            return unavailable(tool, ctx)
        return Result(tool, OK if len(available) == len(rows) else REVIEW,
                      "\n".join(path + "\n" + value for path, value in available))
    return action


def sensors(tool, ctx, stop):
    rows = []
    for folder in list(Path("/sys/class/hwmon").glob("hwmon*"))[:32]:
        label = (read_text(folder / "name", 128) or folder.name).strip()
        for node in [*folder.glob("temp*_input"), *folder.glob("fan*_input")][:40]:
            value = read_text(node, 128)
            if value and re.fullmatch(r"-?\d+", value.strip()):
                number = int(value.strip())
                rows.append(f"{label} / {node.name}: {number / 1000:.1f} °C" if node.name.startswith("temp")
                            else f"{label} / {node.name}: {number} rpm")
    return Result(tool, OK, "\n".join(rows)) if rows else unavailable(tool, ctx, ctx.text("Okunabilir sensör yok.", "No readable sensors."))


def power(tool, ctx, stop):
    rows = []
    for item in list(Path("/sys/class/power_supply").glob("*"))[:24]:
        for key in ("type", "status", "capacity", "energy_full", "energy_full_design", "charge_full", "charge_full_design", "cycle_count", "online"):
            value = read_text(item / key, 256)
            if value is not None:
                rows.append(f"{item.name}/{key}: {value.strip()}")
    return Result(tool, OK, "\n".join(rows)) if rows else unavailable(tool, ctx)


def desktop(tool, ctx, stop):
    session = ctx.host.session.lower()
    desk = ctx.host.desktop.lower()
    if session == "wayland" and ("hyprland" in desk or os.environ.get("HYPRLAND_INSTANCE_SIGNATURE")):
        return run_command(tool, ctx, stop, ("hyprctl", "-j", "monitors"))
    if session == "wayland" and ("sway" in desk or os.environ.get("SWAYSOCK")):
        return run_command(tool, ctx, stop, ("swaymsg", "-t", "get_outputs", "-r"))
    if "kde" in desk and ctx.find("kscreen-doctor"):
        return run_command(tool, ctx, stop, ("kscreen-doctor", "-o"))
    if session == "x11" and os.environ.get("DISPLAY"):
        return run_command(tool, ctx, stop, ("xrandr", "--current"))
    return unavailable(tool, ctx, ctx.text("Bu grafik oturumu için ekran adaptörü yok: ", "No display adapter for this session: ") + session)


def validate_target(value: str, port: int) -> str:
    host = value.strip()
    if "%" in host or not isinstance(port, int) or not 1 <= port <= 65535:
        raise ValueError("Invalid target or TCP port")
    try:
        return str(ipaddress.ip_address(host))
    except ValueError:
        pass
    if len(host) > 253 or not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\.?", host):
        raise ValueError("Invalid DNS name or IP address")
    return host


def endpoint(ctx: Context, host: str, port: int, stop=None) -> Result:
    host = validate_target(host, port)
    if not ctx.host.linux:
        return unavailable("endpoint", ctx)
    if stop and stop.is_set():
        return Result("endpoint", CANCELLED, ctx.text("İptal edildi.", "Cancelled."))
    try:
        address = ipaddress.ip_address(host)
        values = [address]
    except ValueError:
        result = run_command("endpoint", ctx, stop, ("getent", "ahosts", host), 6)
        if result.status != OK:
            return result
        values = []
        for line in result.detail.splitlines():
            try:
                address = ipaddress.ip_address(line.split()[0])
                if address not in values:
                    values.append(address)
            except (ValueError, IndexError):
                continue
    rows = []
    for address in values[:4]:
        if stop and stop.is_set():
            return Result("endpoint", CANCELLED, "\n".join(rows))
        family = socket.AF_INET6 if address.version == 6 else socket.AF_INET
        try:
            with socket.socket(family, socket.SOCK_STREAM) as sock:
                sock.settimeout(2.5)
                sock.connect((str(address), port))
            rows.append(f"{address}:{port} TCP OK")
            return Result("endpoint", OK, "\n".join(rows))
        except OSError as error:
            rows.append(f"{address}:{port} {error}")
    return Result("endpoint", REVIEW, "\n".join(rows) or ctx.text("Adres bulunamadı.", "No address resolved."))


add("info", "system", "Sistem ve uyumluluk", "System and compatibility", system_info)
add("resources", "system", "Kaynak anlık görüntüsü", "Resource snapshot", resource_snapshot)
add("processes", "system", "Bellek kullanan süreçler", "Processes by memory", processes)
add("pressure", "system", "CPU/RAM/I/O baskısı", "CPU/memory/I/O pressure", sysfiles(("/proc/pressure/cpu", "/proc/pressure/memory", "/proc/pressure/io")))
add("packages", "packages", "Kurulu paketler", "Installed packages", package_view("installed"))
add("package-audit", "packages", "Paket veritabanı denetimi", "Package database audit", package_view("audit"))
add("package-verify", "packages", "Paket dosyası bütünlüğü", "Package file integrity", package_view("verify"))
add("updates", "packages", "Önbellekten güncelleme kontrolü", "Cached update check", package_view("updates"))
add("services", "services", "Servis envanteri", "Service inventory", services)
add("failed-services", "services", "Başarısız servisler", "Failed services",
    systemd(("systemctl", "--no-pager", "--no-legend", "--plain", "list-units", "--type=service", "--state=failed"),
            lambda result: bool(result.output.strip())))
add("timers", "services", "systemd zamanlayıcıları", "systemd timers", systemd(("systemctl", "--no-pager", "list-timers", "--all")))
add("boot-time", "services", "Önyükleme süresi", "Boot time", systemd(("systemd-analyze", "time")))
add("logs", "logs", "Son sistem uyarıları", "Recent system warnings", journal)
add("kernel-logs", "logs", "Kernel uyarıları", "Kernel warnings",
    systemd(("journalctl", "-k", "-b", "-p", "warning", "-n", "100", "--no-pager")))
add("previous-boot", "logs", "Önceki açılış günlüğü", "Previous boot log",
    systemd(("journalctl", "-b", "-1", "-p", "warning", "-n", "100", "--no-pager")))
add("firewall", "security", "Güvenlik duvarı", "Firewall", firewall)
add("hardening", "security", "Çekirdek güvenlik göstergeleri", "Kernel security indicators",
    sysfiles(("/proc/sys/kernel/randomize_va_space", "/proc/sys/kernel/kptr_restrict",
              "/proc/sys/kernel/dmesg_restrict", "/sys/kernel/security/lockdown")))
add("sensors", "hardware", "Isı ve fan sensörleri", "Thermal and fan sensors", sensors)
add("power", "hardware", "Pil ve güç durumu", "Battery and power", power)
add("display", "desktop", "Monitör ve grafik oturumu", "Display and session", desktop)
command("addresses", "network", "IPv4/IPv6 arabirimleri", "IPv4/IPv6 interfaces", ("ip", "-j", "address", "show"))
command("routes", "network", "IPv4 yönlendirme", "IPv4 routes", ("ip", "-j", "route", "show"))
command("routes-v6", "network", "IPv6 yönlendirme", "IPv6 routes", ("ip", "-j", "-6", "route", "show"))
command("neighbors", "network", "ARP ve komşu tablosu", "ARP and neighbor table", ("ip", "-j", "neigh", "show"))
command("sockets", "network", "TCP/UDP soketleri", "TCP/UDP sockets", ("ss", "-H", "-tuna"))
command("networkmanager", "network", "NetworkManager aygıtları", "NetworkManager devices", ("nmcli", "-t", "-f", "DEVICE,TYPE,STATE", "device", "status"))
command("dns", "network", "DNS durumu", "DNS status", ("resolvectl", "status"))
command("block", "storage", "Disk ve bölüm envanteri", "Block device inventory", ("lsblk", "-J", "-o", "NAME,TYPE,SIZE,FSTYPE,MOUNTPOINT,MODEL,ROTA"))
command("mounts", "storage", "Bağlı dosya sistemleri", "Mounted filesystems", ("findmnt", "-J", "-o", "TARGET,SOURCE,FSTYPE"))
command("space", "storage", "Yerel disk boş alanı", "Local free space", ("df", "-l", "-h", "-T"), 12)
command("fstab", "storage", "fstab doğrulama", "fstab verification", ("findmnt", "--verify", "--verbose"))
command("smart-scan", "storage", "SMART aygıtlarını bul", "Discover SMART devices", ("smartctl", "--scan"), 12)
command("nvme", "storage", "NVMe envanteri", "NVMe inventory", ("nvme", "list", "-o", "json"), 12)
command("btrfs", "storage", "Btrfs dosya sistemleri", "Btrfs filesystems", ("btrfs", "filesystem", "show"))
command("zfs", "storage", "ZFS havuz durumu", "ZFS pool status", ("zpool", "status", "-x"))
add("raid", "storage", "Linux yazılım RAID", "Linux software RAID", sysfiles(("/proc/mdstat",)))
command("pci", "hardware", "PCI ve sürücü bilgileri", "PCI and drivers", ("lspci", "-nnk"))
command("usb", "hardware", "USB aygıtları", "USB devices", ("lsusb",))
command("modules", "hardware", "Kernel modülleri", "Kernel modules", ("lsmod",))
command("nvidia", "hardware", "NVIDIA ölçümleri", "NVIDIA metrics",
        ("nvidia-smi", "--query-gpu=name,driver_version,temperature.gpu,utilization.gpu,memory.used,memory.total", "--format=csv,noheader,nounits"))
command("firmware", "hardware", "Firmware aygıtları", "Firmware devices", ("fwupdmgr", "get-devices", "--json"))
command("selinux", "security", "SELinux durumu", "SELinux state", ("getenforce",))
command("apparmor", "security", "AppArmor durumu", "AppArmor state", ("aa-status",))
command("secure-boot", "security", "Secure Boot durumu", "Secure Boot state", ("mokutil", "--sb-state"))
command("boot", "system", "UEFI önyükleme durumu", "UEFI boot status", ("bootctl", "--no-pager", "status"))
command("flatpak", "packages", "Flatpak uygulamaları", "Flatpak applications", ("flatpak", "list", "--app", "--columns=application,version,installation"))
command("snap", "packages", "Snap paketleri", "Snap packages", ("snap", "list"))
command("audio", "desktop", "Ses sunucusu durumu", "Audio server status", ("pactl", "info"))
command("pipewire", "desktop", "PipeWire ses yönlendirmesi", "PipeWire audio routing", ("wpctl", "status"), 10)
command("audio-devices", "desktop", "ALSA ses aygıtları", "ALSA playback devices", ("aplay", "-l"), 10)
command("snapper", "recovery", "Snapper yapılandırmaları", "Snapper configurations", ("snapper", "list-configs"))
command("zfs-snapshots", "recovery", "ZFS snapshot envanteri", "ZFS snapshot inventory",
        ("zfs", "list", "-t", "snapshot", "-o", "name,creation,used,referenced"))
command("coredumps", "logs", "Son açılıştaki çökme kayıtları", "Current boot crash metadata",
        ("coredumpctl", "--no-pager", "-b", "list"), 12)
command("gamemode", "gaming", "GameMode durumu", "GameMode status", ("gamemoded", "-s"), 10)
command("vulkan", "gaming", "Vulkan sürücü özeti", "Vulkan driver summary", ("vulkaninfo", "--summary"), 20)
command("opengl", "gaming", "OpenGL sürücü özeti", "OpenGL driver summary", ("glxinfo", "-B"), 12)

