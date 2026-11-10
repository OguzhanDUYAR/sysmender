"""UI smoke for a host with Tk and a display. Execute with python -m tests.gui_smoke."""
import time
import tkinter as tk
from dataclasses import replace

from linuxbakim.catalog import Context
from linuxbakim.gui import Dashboard
from linuxbakim.host import Host


def check():
    root = tk.Tk()
    try:
        root.withdraw()
        host = Host.detect()
        app = Dashboard(root, Context(replace(host, linux=False)))
        root.update()
        app.query.set("CPU")
        assert len(app.tools.get_children()) >= 1
        app.switch_language()
        assert app.ctx.lang == "en"
        app.switch_theme()
        assert app.theme == "light"
        app.run_scenario()
        deadline = time.monotonic() + 5
        while app.worker and time.monotonic() < deadline:
            root.update()
            time.sleep(0.03)
        assert app.worker is None and app.results
        assert all(item.status == "unavailable" for item in app.results)
        assert "requires a Linux kernel" in app.detail.get("1.0", "end")
        if host.linux:
            app.ctx = replace(app.ctx, host=host)
            root.after(0, app.refresh)
            root.update()
            assert "RAM" in app.live.cget("text")
    finally:
        root.destroy()
    print("PASS Tk navigation, search, language, theme, workflow, result detail")


if __name__ == "__main__":
    check()
