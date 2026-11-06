"""Minimal Vortex-inspired dashboard; no external GUI dependencies."""
from pathlib import Path
import tkinter as tk
from tkinter import ttk, filedialog, messagebox, simpledialog
from manager import Manager

class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Unturned 2 — Modpack Manager (Preview)")
        self.geometry("950x610")
        self.game_var = tk.StringVar()
        self.workshop_var = tk.StringVar()
        self.data_var = tk.StringVar(value=str(Path.home() / "Unturned2-ModpackManager"))
        self.status_var = tk.StringVar(value="Select the Unturned installation to begin.")
        self.mgr = None
        paths = ttk.Frame(self, padding=12)
        paths.pack(fill="x")
        for label, variable, row in (("Game", self.game_var, 0),
                                     ("Workshop (optional)", self.workshop_var, 1),
                                     ("Manager data", self.data_var, 2)):
            ttk.Label(paths, text=label).grid(row=row, column=0, sticky="w")
            ttk.Entry(paths, textvariable=variable).grid(row=row, column=1, sticky="ew", padx=8, pady=4)
            ttk.Button(paths, text="Browse", command=lambda v=variable: self.browse(v)).grid(row=row, column=2)
        paths.columnconfigure(1, weight=1)
        ttk.Button(paths, text="Scan / Refresh", command=self.refresh).grid(row=3, column=1, sticky="e", pady=6)
        self.tabs = ttk.Notebook(self)
        self.tabs.pack(fill="both", expand=True, padx=12, pady=8)
        self.workshop_list = self.make_tab("Steam Workshop", "Workshop downloads are managed by Steam. Selecting an item does not disable Steam subscriptions.")
        self.local_list = self.make_tab("Local Staging", "Imports are copied to a private staging area, not directly into the game.")
        self.installed_list = self.make_tab("Managed Installs", "Only application-created folders can be removed; removals move files to recovery.")
        buttons = ttk.Frame(self, padding=10)
        buttons.pack(fill="x")
        ttk.Button(buttons, text="Import local folder", command=self.import_mod).pack(side="left", padx=4)
        ttk.Button(buttons, text="Install selected local mod", command=self.install).pack(side="left", padx=4)
        ttk.Button(buttons, text="Remove selected managed mod", command=self.remove).pack(side="left", padx=4)
        ttk.Button(buttons, text="Create profile / server export", command=self.profile_export).pack(side="left", padx=4)
        ttk.Label(self, textvariable=self.status_var, anchor="w", padding=9).pack(fill="x")

    def make_tab(self, title, help_text):
        frame = ttk.Frame(self.tabs, padding=8)
        self.tabs.add(frame, text=title)
        ttk.Label(frame, text=help_text, wraplength=850).pack(anchor="w", pady=(0, 8))
        listing = tk.Listbox(frame, selectmode="extended", font=("Consolas", 10))
        listing.pack(fill="both", expand=True)
        return listing

    def browse(self, var):
        folder = filedialog.askdirectory()
        if folder:
            var.set(folder)

    def refresh(self):
        try:
            game = Path(self.game_var.get())
            if not game.is_dir():
                raise ValueError("Choose the real Unturned folder.")
            wk = Path(self.workshop_var.get()) if self.workshop_var.get().strip() else None
            self.mgr = Manager(game, Path(self.data_var.get()), wk)
            self.workshop_list.delete(0, "end")
            for item in self.mgr.workshop_items():
                self.workshop_list.insert("end", item["id"])
            self.local_list.delete(0, "end")
            for item in self.mgr.local_items():
                self.local_list.insert("end", item["name"])
            self.installed_list.delete(0, "end")
            for key in sorted(self.mgr.state()["installed"]):
                self.installed_list.insert("end", key)
            self.status_var.set("Scan completed. Workshop items remain Steam-managed.")
        except Exception as exc:
            messagebox.showerror("Scan error", str(exc))

    def require_manager(self):
        if self.mgr is None:
            raise ValueError("Press Scan / Refresh first.")
        return self.mgr

    def import_mod(self):
        try:
            mgr = self.require_manager()
            source = filedialog.askdirectory(title="Select a mod folder")
            if not source: return
            name = simpledialog.askstring("Mod name", "Staging name (letters, digits, _ - .):",
                                          initialvalue=Path(source).name)
            if not name: return
            mgr.import_local(Path(source), name)
            self.refresh()
        except Exception as exc:
            messagebox.showerror("Import error", str(exc))

    def install(self):
        try:
            mgr = self.require_manager()
            indexes = self.local_list.curselection()
            if len(indexes) != 1:
                raise ValueError("Select one local staging mod.")
            name = self.local_list.get(indexes[0])
            category = simpledialog.askstring("Category", "Choose Contents or Maps:", initialvalue="Contents")
            if category is None: return
            plan = mgr.deploy(name, category)
            if messagebox.askyesno("Confirm install", "Copy into game?\n" + plan["destination"]):
                mgr.deploy(name, category, confirm=True)
                self.refresh()
        except Exception as exc:
            messagebox.showerror("Install error", str(exc))

    def remove(self):
        try:
            mgr = self.require_manager()
            indexes = self.installed_list.curselection()
            if len(indexes) != 1:
                raise ValueError("Select one managed installation.")
            category, name = self.installed_list.get(indexes[0]).split("/", 1)
            mgr.remove(name, category)
            if messagebox.askyesno("Confirm removal", "Move managed installation to recovery?"):
                mgr.remove(name, category, confirm=True)
                self.refresh()
        except Exception as exc:
            messagebox.showerror("Removal error", str(exc))

    def profile_export(self):
        try:
            mgr = self.require_manager()
            name = simpledialog.askstring("Profile", "New profile name:", initialvalue="default")
            if not name: return
            local = [self.local_list.get(i) for i in self.local_list.curselection()]
            workshop = [self.workshop_list.get(i) for i in self.workshop_list.curselection()]
            mgr.save_profile(name, local, workshop, activate=True)
            if messagebox.askyesno("Server export", "Export selected Workshop IDs to WorkshopDownloadConfig.json?"):
                destination = filedialog.asksaveasfilename(defaultextension=".json",
                    initialfile="WorkshopDownloadConfig.json", filetypes=[("JSON", "*.json")])
                if destination:
                    mgr.export_server(name, Path(destination))
            self.status_var.set("Profile saved. Steam subscriptions were not modified.")
        except Exception as exc:
            messagebox.showerror("Profile error", str(exc))

if __name__ == "__main__":
    App().mainloop()
