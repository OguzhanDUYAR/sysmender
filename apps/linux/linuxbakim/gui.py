"""Tk front end for the same bounded diagnostics used by the CLI."""
from __future__ import annotations

import queue
import threading
import tkinter as tk
from collections import deque
from dataclasses import replace
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from . import AUTHOR, __version__
from .catalog import FAILED, GROUPS, TOOLS, Result, endpoint, run, validate_target
from .cli import status_label
from .metrics import Sampler
from .reports import report, save_new
from .runner import clean
from .workflows import REPAIR, SCENARIOS, diagnose, repair


SCENARIO_NAMES = {
    "health": ("Genel sağlık", "General health"), "slow": ("Yavaşlık", "Slow computer"),
    "network": ("Ağ bağlantısı", "Network connectivity"), "storage": ("Disk ve bölümler", "Disks and partitions"),
    "services": ("Servisler", "Services"), "packages": ("Paket sorunları", "Package issues"),
    "security": ("Güvenlik", "Security"), "graphics": ("Grafik ve oyun", "Graphics and gaming"),
    "audio": ("Ses sorunu", "Audio issue"), "boot": ("Açılış sorunu", "Boot issue"),
}
THEMES = {
    "dark": {"bg": "#0b0e0a", "panel": "#161e14", "fg": "#ebf3e7", "mute": "#afc2a9",
             "accent": "#b4ff40", "onaccent": "#152100", "line": "#31412d"},
    "light": {"bg": "#f3f7ef", "panel": "#e3ecdf", "fg": "#182016", "mute": "#40523a",
              "accent": "#346400", "onaccent": "#ffffff", "line": "#afc1a6"},
}


def bytes_text(value):
    if value is None:
        return "—"
    for unit in ("B", "KiB", "MiB", "GiB"):
        if value < 1024:
            return f"{value:.1f} {unit}"
        value /= 1024
    return f"{value:.1f} TiB"


class Dashboard:
    def __init__(self, root, context):
        self.root, self.ctx = root, context
        self.theme = "dark"
        self.group, self.query = "all", tk.StringVar()
        self.only_favorites = tk.BooleanVar()
        self.favorites, self.results = set(), []
        self.events = queue.Queue()
        self.stop = threading.Event()
        self.worker = None
        self.closing = False
        self.sampler = Sampler()
        self.samples = deque(maxlen=40)
        self.tool_ids, self.result_ids = {}, {}
        root.geometry("1130x780")
        root.minsize(780, 590)
        root.protocol("WM_DELETE_WINDOW", self.close)
        root.bind("<Control-k>", lambda event: self.search.focus_set())
        self.query.trace_add("write", lambda *_: self.populate())
        self.build()
        self.set_status(self.t("Hazır", "Ready"))
        self.refresh()
        self.poll()

    def t(self, tr, en):
        return self.ctx.text(tr, en)

    def configure_style(self):
        colors = THEMES[self.theme]
        style = ttk.Style(self.root)
        style.theme_use("clam")
        self.root.configure(bg=colors["bg"])
        style.configure(".", background=colors["bg"], foreground=colors["fg"], font=("Sans", 10))
        style.configure("TEntry", fieldbackground=colors["panel"], foreground=colors["fg"], insertcolor=colors["fg"])
        style.configure("TCombobox", fieldbackground=colors["panel"], foreground=colors["fg"], arrowcolor=colors["fg"])
        style.map("TCombobox", fieldbackground=[("readonly", colors["panel"])])
        style.configure("TButton", padding=(9, 5), background=colors["panel"], bordercolor=colors["line"])
        style.map("TButton", background=[("active", colors["line"])])
        style.configure("Accent.TButton", background=colors["accent"], foreground=colors["onaccent"])
        style.map("Accent.TButton", background=[("active", colors["accent"])],
                  foreground=[("disabled", colors["mute"])])
        style.configure("Heading.TLabel", font=("Sans", 20, "bold"))
        style.configure("Muted.TLabel", foreground=colors["mute"])
        style.configure("Horizontal.TProgressbar", background=colors["accent"], troughcolor=colors["panel"])
        style.configure("Treeview", background=colors["panel"], fieldbackground=colors["panel"], rowheight=25)
        style.configure("Treeview.Heading", background=colors["line"], foreground=colors["fg"])
        style.map("Treeview", background=[("selected", colors["accent"])],
                  foreground=[("selected", colors["onaccent"])])
        return colors

    def build(self):
        if hasattr(self, "progress"):
            self.progress.stop()
        for child in self.root.winfo_children():
            child.destroy()
        colors = self.configure_style()
        product = self.t("Linux Bakım Merkezi", "Linux Maintenance Center")
        self.root.title(f"{product} · v{__version__}")
        wrap = ttk.Frame(self.root, padding=14)
        wrap.pack(fill="both", expand=True)
        title = ttk.Frame(wrap)
        title.pack(fill="x")
        mark = tk.Canvas(title, width=40, height=40, bg=colors["bg"], highlightthickness=0)
        mark.create_oval(3, 3, 37, 37, outline=colors["accent"], width=2)
        mark.create_line(7, 21, 14, 21, 17, 12, 21, 29, 25, 17, 28, 21, 33, 21, fill=colors["accent"], width=2)
        mark.pack(side="left", padx=(0, 8))
        ttk.Label(title, text=product, style="Heading.TLabel").pack(side="left")
        ttk.Button(title, text="Türkçe" if self.ctx.lang == "en" else "English", command=self.switch_language).pack(side="right", padx=3)
        ttk.Button(title, text=self.t("Tema", "Theme"), command=self.switch_theme).pack(side="right", padx=3)
        signature = self.t(f"Geliştiren {AUTHOR}", f"Developed by {AUTHOR}")
        ttk.Label(wrap, text=f"{signature}   ·   v{__version__}   ·   {self.ctx.host.pretty_name}",
                  style="Muted.TLabel").pack(anchor="w", pady=(0, 5))
        monitor = ttk.Frame(wrap)
        monitor.pack(fill="x")
        self.live = ttk.Label(monitor, text="", padding=(8, 8))
        self.live.pack(side="left", fill="x", expand=True)
        self.graph = tk.Canvas(monitor, width=180, height=52, bg=colors["panel"], highlightthickness=1,
                               highlightbackground=colors["line"])
        if self.ctx.host.linux:
            self.graph.pack(side="right")
        layout = ttk.Panedwindow(wrap, orient="horizontal")
        layout.pack(fill="both", expand=True, pady=6)
        left, right = ttk.Frame(layout, padding=(0, 0, 8, 0)), ttk.Frame(layout, padding=(8, 0, 0, 0))
        layout.add(left, weight=2)
        layout.add(right, weight=3)

        browse = ttk.Frame(left)
        browse.pack(fill="x")
        self.group_keys = ("all", *GROUPS)
        values = [self.t("Tüm araçlar", "All tools"), *(self.t(*group) for group in GROUPS.values())]
        self.groups = ttk.Combobox(browse, values=values, state="readonly", width=17)
        self.groups.current(self.group_keys.index(self.group))
        self.groups.bind("<<ComboboxSelected>>", self.change_group)
        self.groups.pack(side="left", padx=(0, 5))
        self.search = ttk.Entry(browse, textvariable=self.query, width=13)
        self.search.pack(side="left", fill="x", expand=True)
        ttk.Label(left, text=self.t("Ara · Ctrl+K", "Search · Ctrl+K"), style="Muted.TLabel").pack(anchor="w", pady=(3, 0))
        ttk.Checkbutton(left, text=self.t("Yalnızca favoriler", "Favorites only"), variable=self.only_favorites,
                        command=self.populate).pack(anchor="w")
        self.tools = self.tree(left, ("tool",), (self.t("Tanılama aracı", "Diagnostic tool"),), (285,), 9)
        self.tools.bind("<Double-1>", lambda event: self.run_tool())
        self.tools.bind("<Return>", lambda event: self.run_tool())
        actions = ttk.Frame(left)
        actions.pack(fill="x", pady=5)
        self.start = ttk.Button(actions, text=self.t("Aracı çalıştır", "Run tool"), command=self.run_tool, style="Accent.TButton")
        self.start.pack(side="left", padx=(0, 5))
        ttk.Button(actions, text=self.t("Favoriye ekle / çıkar", "Toggle favorite"), command=self.favorite).pack(side="left")
        self.scenario = ttk.Combobox(left, values=[self.t(*SCENARIO_NAMES[key]) for key in SCENARIOS], state="readonly")
        self.scenario.current(0)
        self.scenario.pack(fill="x", pady=(4, 3))
        self.batch = ttk.Button(left, text=self.t("Tanılama planını çalıştır", "Run diagnostic workflow"),
                                command=self.run_scenario)
        self.batch.pack(fill="x")
        self.target = ttk.Button(left, text=self.t("DNS ve TCP hedefi…", "DNS and TCP target…"), command=self.run_target)
        self.target.pack(fill="x", pady=(4, 0))
        self.plan = ttk.Button(left, text=self.t("APT onarımını önizle", "Preview APT repair"), command=self.preview)
        self.plan.pack(fill="x", pady=(4, 0))

        self.history = self.tree(right, ("tool", "status"), (self.t("Sonuçlar", "Results"), self.t("Durum", "Status")), (220, 128), 6)
        self.history.bind("<<TreeviewSelect>>", self.show_result)
        detail_frame = ttk.Frame(right)
        detail_frame.pack(fill="both", expand=True, pady=6)
        self.detail = tk.Text(detail_frame, wrap="word", state="disabled", font=("Monospace", 10), height=8,
                              bg=colors["panel"], fg=colors["fg"], relief="flat", padx=8, pady=7,
                              highlightthickness=1, highlightbackground=colors["line"])
        detail_scroll = ttk.Scrollbar(detail_frame, orient="vertical", command=self.detail.yview)
        self.detail.configure(yscrollcommand=detail_scroll.set)
        detail_scroll.pack(side="right", fill="y")
        self.detail.pack(side="left", fill="both", expand=True)
        controls = ttk.Frame(right)
        controls.pack(fill="x")
        ttk.Button(controls, text=self.t("Özet JSON kaydet", "Save summary JSON"), command=self.export).pack(side="left")
        self.cancel = ttk.Button(controls, text=self.t("Durdur", "Stop"), command=self.cancel_job)
        self.cancel.pack(side="right")
        footer = ttk.Frame(wrap)
        footer.pack(fill="x")
        self.status = ttk.Label(footer, style="Muted.TLabel")
        self.status.pack(side="left")
        self.progress = ttk.Progressbar(footer, mode="indeterminate", length=100)
        self.progress.pack(side="right")
        self.populate()
        self.update_buttons()
        self.result_ids.clear()
        for result in self.results:
            self.add_result(result)
        if not self.results:
            self.write_detail(self.t("Bir araç veya tanılama planı seçin. APT önizlemesi paketleri değiştirmez.",
                                      "Select a tool or diagnostic workflow. APT preview does not modify packages."))
        self.set_status(self.t("İşlem sürüyor", "Working") if self.worker else self.t("Hazır", "Ready"))

    def tree(self, parent, columns, titles, widths, height):
        frame = ttk.Frame(parent)
        frame.pack(fill="both", expand=True)
        tree = ttk.Treeview(frame, show="headings", columns=columns, selectmode="browse", height=height)
        scroll = ttk.Scrollbar(frame, orient="vertical", command=tree.yview)
        tree.configure(yscrollcommand=scroll.set)
        scroll.pack(side="right", fill="y")
        tree.pack(side="left", fill="both", expand=True)
        for column, title, width in zip(columns, titles, widths):
            tree.heading(column, text=title)
            tree.column(column, width=width, minwidth=75)
        return tree

    def change_group(self, event):
        self.group = self.group_keys[self.groups.current()]
        self.populate()

    def populate(self):
        self.tools.delete(*self.tools.get_children())
        term = self.query.get().strip().casefold()
        self.tool_ids.clear()
        for tool in TOOLS.values():
            if self.group != "all" and tool.group != self.group:
                continue
            if self.only_favorites.get() and tool.id not in self.favorites:
                continue
            name = tool.name(self.ctx)
            if term and term not in f"{tool.id} {tool.tr} {tool.en}".casefold():
                continue
            iid = self.tools.insert("", "end", values=(("★  " if tool.id in self.favorites else "") + name,))
            self.tool_ids[iid] = tool.id

    def selected_tool(self):
        selection = self.tools.selection()
        return self.tool_ids.get(selection[0]) if selection else None

    def favorite(self):
        tool = self.selected_tool()
        if tool:
            self.favorites.symmetric_difference_update({tool})
            self.populate()

    def switch_language(self):
        self.ctx = replace(self.ctx, lang="tr" if self.ctx.lang == "en" else "en")
        self.build()

    def switch_theme(self):
        self.theme = "dark" if self.theme == "light" else "light"
        self.build()

    def run_tool(self):
        tool = self.selected_tool()
        if tool:
            ctx = self.ctx
            self.start_job(lambda: [run(tool, ctx, self.stop)])

    def run_scenario(self):
        index = self.scenario.current()
        key, ctx = tuple(SCENARIOS)[index], self.ctx
        self.start_job(lambda: diagnose(key, ctx, self.stop))

    def preview(self):
        ctx = self.ctx
        self.start_job(lambda: [repair(ctx, stop=self.stop)])

    def run_target(self):
        from tkinter.simpledialog import askinteger, askstring
        title = self.t("Ağ hedefi", "Network target")
        host = askstring(title, self.t("Bir DNS adı veya IP adresi", "One DNS name or IP address"), parent=self.root)
        if not host:
            return
        port = askinteger(title, self.t("TCP portu", "TCP port"), minvalue=1, maxvalue=65535, parent=self.root)
        if port is None:
            return
        try:
            host = validate_target(host, port)
        except ValueError as error:
            messagebox.showerror(title, str(error), parent=self.root)
            return
        ctx = self.ctx
        self.start_job(lambda: [endpoint(ctx, host, port, self.stop)])

    def start_job(self, operation):
        if self.worker:
            return
        self.stop.clear()

        def work():
            try:
                for result in operation():
                    self.events.put(("result", result))
            except Exception as error:
                self.events.put(("result", Result("internal", FAILED, clean(str(error)))))
            finally:
                self.events.put(("done", None))

        self.worker = threading.Thread(target=work, daemon=True)
        self.worker.start()
        self.set_status(self.t("İşlem sürüyor", "Working"))
        self.update_buttons()

    def update_buttons(self):
        state = "disabled" if self.worker else "normal"
        for button in (self.start, self.batch, self.target, self.plan):
            button.configure(state=state)
        self.cancel.configure(state="normal" if self.worker else "disabled")
        if self.worker:
            self.progress.start(65)
        else:
            self.progress.stop()

    def cancel_job(self):
        self.stop.set()
        self.set_status(self.t("Durdurma istendi", "Stop requested"))

    def poll(self):
        while True:
            try:
                event, value = self.events.get_nowait()
            except queue.Empty:
                break
            if event == "result":
                if len(self.results) == 100:
                    self.results.pop(0)
                    oldest = self.history.get_children()[0]
                    self.result_ids.pop(oldest, None)
                    self.history.delete(oldest)
                self.results.append(value)
                self.add_result(value)
            else:
                self.worker = None
                self.update_buttons()
                if self.closing:
                    self.root.destroy()
                    return
                self.set_status(self.t("Hazır", "Ready"))
        self.root.after(80, self.poll)

    def add_result(self, result):
        tool = TOOLS.get(result.tool)
        name = tool.name(self.ctx) if tool else result.tool
        iid = self.history.insert("", "end", values=(name, status_label(self.ctx, result.status)))
        self.result_ids[iid] = result
        self.history.selection_set(iid)
        self.history.see(iid)
        self.show_result()

    def show_result(self, event=None):
        selected = self.history.selection()
        result = self.result_ids.get(selected[0]) if selected else None
        if result:
            detail = clean(result.detail)
            if result.truncated:
                detail += self.t("\n\nÇıktı boyut sınırına ulaştı.", "\n\nOutput reached its size limit.")
            if result.tool == REPAIR and result.status == "review":
                detail += self.t("\n\nUygulanabilir bir plan için etkileşimli terminal komutu:",
                                 "\n\nInteractive terminal command for an eligible plan:")
                detail += "\nlinuxbakim repair apt-dependencies --apply"
            self.write_detail(detail)

    def write_detail(self, text):
        self.detail.configure(state="normal")
        self.detail.delete("1.0", "end")
        self.detail.insert("1.0", text)
        self.detail.configure(state="disabled")

    def set_status(self, text):
        self.status.configure(text=text)

    def refresh(self):
        if self.ctx.host.linux:
            sample = self.sampler.sample()
            cpu = sample["cpu_percent"]
            ram = f"{bytes_text(sample['memory_used'])} / {bytes_text(sample['memory_total'])}"
            swap = f"{bytes_text(sample['swap_used'])} / {bytes_text(sample['swap_total'])}"
            network = sample["network_bytes_per_second"]
            disk = sample["disk_bytes_per_second"]
            cpu_text = f"{cpu:.1f}%" if cpu is not None else "—"
            io_text = lambda values: " / ".join(bytes_text(value) + "/s" for value in values) if values else "—"
            self.live.configure(text=f"CPU {cpu_text}    RAM {ram}    SWAP {swap}\n"
                                     f"{self.t('Ağ RX/TX', 'Network RX/TX')} {io_text(network)}    "
                                     f"{self.t('Disk R/W', 'Disk R/W')} {io_text(disk)}")
            self.samples.append(cpu)
            self.draw_cpu()
        else:
            self.live.configure(text=self.t("Canlı ölçüm için Linux çekirdeği gerekli.", "Live monitoring requires a Linux kernel."))
        self.root.after(1500, self.refresh)

    def draw_cpu(self):
        self.graph.delete("all")
        colors = THEMES[self.theme]
        self.graph.create_text(6, 7, anchor="nw", text="CPU · 60 s", fill=colors["mute"], font=("Sans", 8))
        for index, (old, new) in enumerate(zip(self.samples, list(self.samples)[1:]), start=40 - len(self.samples)):
            if old is not None and new is not None:
                x = 7 + index * 4.2
                self.graph.create_line(x, 46 - old * 0.28, x + 4.2, 46 - new * 0.28,
                                       fill=colors["accent"], width=2)

    def export(self):
        if not self.results:
            self.set_status(self.t("Önce bir araç çalıştırın.", "Run a tool first."))
            return
        path = filedialog.asksaveasfilename(parent=self.root, defaultextension=".json", initialfile="linuxbakim-summary.json",
                                            filetypes=[("JSON", "*.json")], confirmoverwrite=False)
        if path:
            try:
                save_new(Path(path), report(self.results))
                self.set_status(self.t("Özet kaydedildi.", "Summary saved."))
            except OSError as error:
                messagebox.showerror(self.t("Rapor kaydedilemedi", "Could not save report"), str(error), parent=self.root)

    def close(self):
        if self.worker:
            self.closing = True
            self.cancel_job()
        else:
            self.root.destroy()


def open_gui(context):
    if context.host.root:
        print(context.text("Arayüzü normal kullanıcıyla açın.", "Start the interface as an unprivileged user."))
        return 3
    try:
        root = tk.Tk()
    except tk.TclError as error:
        print(context.text("Grafik oturumu açılamadı. Komut satırını kullanabilirsiniz.",
                           "Could not open a graphical session. Use the CLI.") + "\n" + clean(str(error)))
        return 3
    Dashboard(root, context)
    root.mainloop()
    return 0
