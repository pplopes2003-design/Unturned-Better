"""Minimal Vortex-inspired dashboard; no external GUI dependencies."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
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
        self._scan_executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="u2m-scan")
        self._scan_future = None
        self._scan_generation = 0
        self.protocol("WM_DELETE_WINDOW", self._close)
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
        buttons = ttk.Frame(self, padding=(10, 4))
        buttons.pack(fill="x")
        basic = ttk.Frame(buttons)
        basic.pack(fill="x", pady=2)
        weapons = ttk.Frame(buttons)
        weapons.pack(fill="x", pady=2)
        advanced = ttk.Frame(buttons)
        advanced.pack(fill="x", pady=2)
        ttk.Button(basic, text="Import local folder", command=self.import_mod).pack(side="left", padx=4)
        ttk.Button(basic, text="Install local mod", command=self.install).pack(side="left", padx=4)
        ttk.Button(basic, text="Remove managed mod", command=self.remove).pack(side="left", padx=4)
        ttk.Button(basic, text="Save profile / server export", command=self.profile_export).pack(side="left", padx=4)
        ttk.Button(weapons, text="Generate Auto 24", command=self.generate_auto32).pack(side="left", padx=4)
        ttk.Button(weapons, text="Upgrade Auto32 to 24", command=self.upgrade_auto24).pack(side="left", padx=4)
        ttk.Button(weapons, text="Tune Auto recoil", command=self.tune_auto32).pack(side="left", padx=4)
        ttk.Button(weapons, text="Generate Legendary Pack", command=self.generate_legendary_pack).pack(side="left", padx=4)
        ttk.Button(advanced, text="Check conflicts", command=self.show_conflicts).pack(side="left", padx=4)
        ttk.Button(advanced, text="Apply profile", command=self.apply_profile).pack(side="left", padx=4)
        ttk.Button(advanced, text="Install C# module", command=self.install_module).pack(side="left", padx=4)
        ttk.Button(advanced, text="Check AI service", command=self.check_ai_service).pack(side="left", padx=4)
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

    def _close(self):
        self._scan_generation += 1
        self._scan_executor.shutdown(wait=False, cancel_futures=True)
        self.destroy()

    @staticmethod
    def _scan_snapshot(game, data, workshop):
        mgr = Manager(game, data, workshop)
        return (mgr,
                [item["id"] for item in mgr.workshop_items(include_size=False)],
                [item["name"] for item in mgr.local_items(include_size=False)],
                sorted(mgr.state()["installed"]))

    def refresh(self):
        if self._scan_future is not None and not self._scan_future.done():
            self.status_var.set("Scan already running; please wait.")
            return
        game = Path(self.game_var.get())
        if not game.is_dir():
            messagebox.showerror("Scan error", "Choose the real Unturned folder.")
            return
        wk = Path(self.workshop_var.get()) if self.workshop_var.get().strip() else None
        data = Path(self.data_var.get())
        self._scan_generation += 1
        generation = self._scan_generation
        self.status_var.set("Scanning package names (no content hashing)...")
        self._scan_future = self._scan_executor.submit(
            self._scan_snapshot, game, data, wk)
        self.after(80, lambda: self._finish_refresh(generation))

    def _finish_refresh(self, generation):
        if generation != self._scan_generation:
            return
        future = self._scan_future
        if future is None:
            return
        if not future.done():
            self.after(80, lambda: self._finish_refresh(generation))
            return
        self._scan_future = None
        try:
            mgr, workshop, local, installed = future.result()
            self.mgr = mgr
            self.workshop_list.delete(0, "end")
            for item_id in workshop:
                self.workshop_list.insert("end", item_id)
            self.local_list.delete(0, "end")
            for name in local:
                self.local_list.insert("end", name)
            self.installed_list.delete(0, "end")
            for key in installed:
                self.installed_list.insert("end", key)
            self.status_var.set(
                f"Scan completed: {len(workshop)} Workshop, "
                f"{len(local)} staged, {len(installed)} managed installs.")
        except Exception as exc:
            self.status_var.set("Scan failed.")
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
            category = simpledialog.askstring("Category", "Choose Content or Maps:", initialvalue="Content")
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
            existing = mgr.profile_state()["profiles"].get(name, {})
            maps = [item for item in existing.get("local_maps", []) if item not in local]
            modules = [item for item in existing.get("modules", []) if item not in local]
            # When saving from the generic list, ask for per-mod type:
            contents = []
            for item in local:
                kind = simpledialog.askstring("Local mod type: " + item,
                    "Content, Maps or Modules:", initialvalue="Content")
                if kind is None: return
                kind = kind.strip().lower()
                if kind == "content": contents.append(item)
                elif kind == "maps": maps.append(item)
                elif kind == "modules": modules.append(item)
                else: raise ValueError("Unknown category: " + kind)
            mgr.save_profile(name, contents, workshop, activate=False, local_maps=maps, modules=modules)
            if messagebox.askyesno("Server export", "Export selected Workshop IDs to WorkshopDownloadConfig.json?"):
                destination = filedialog.asksaveasfilename(defaultextension=".json",
                    initialfile="WorkshopDownloadConfig.json", filetypes=[("JSON", "*.json")])
                if destination:
                    mgr.export_server(name, Path(destination))
            self.status_var.set("Profile saved (not applied). Use Apply profile to deploy selected mods.")
        except Exception as exc:
            messagebox.showerror("Profile error", str(exc))

    def generate_auto32(self):
        try:
            from build_auto32 import create_auto32
            mgr = self.require_manager()
            gun_id = simpledialog.askinteger("Auto 32 (experimental)",
                "Gun legacy ID (must not conflict):", initialvalue=32610,
                minvalue=1, maxvalue=65535)
            if gun_id is None: return
            magazine_id = simpledialog.askinteger("Auto 32 (experimental)",
                "Magazine legacy ID:", initialvalue=32611,
                minvalue=1, maxvalue=65535)
            if magazine_id is None: return
            target = mgr.staging / "Auto32"
            extra = [mgr.workshop] if mgr.workshop else []
            if not messagebox.askyesno("Generate experimental mod",
                "Generate a staging-only Auto 32 gun and magazine using your installed Bluntforce models?\n\n"
                "This does not install the mod or modify vanilla files. "
                "The 4-round incremental reload is NOT implemented."):
                return
            result = create_auto32(mgr.game, target, gun_id=gun_id,
                                   magazine_id=magazine_id, other_roots=extra)
            self.refresh()
            messagebox.showinfo("Auto 32 generated",
                "Staging only. IDs: " + str(result["gun_id"]) + ", " +
                str(result["magazine_id"]) +
                "\nReview BUILD_NOTES.txt and scan for conflicts before installation.")
        except Exception as exc:
            messagebox.showerror("Auto 32 generation failed", str(exc))

    def tune_auto32(self):
        try:
            from auto32_recoil import tune_staged_auto32, PRESETS
            mgr = self.require_manager()
            # Protect registry snapshots: never modify staged content while it
            # has a managed copy deployed to the game.
            if "Content/Auto32" in mgr.state()["installed"]:
                raise ValueError(
                    "Close Unturned, then remove Content/Auto32 under Managed Installs "
                    "before tuning staging. The manager keeps the removed mod in recovery. "
                    "Reinstall it after tuning.")
            preset = simpledialog.askstring("Auto 32 recoil",
                "Preset: soft / balanced / strong", initialvalue="balanced")
            if preset is None: return
            preset = preset.strip().lower()
            if preset not in PRESETS:
                raise ValueError("Select soft, balanced or strong.")
            result = tune_staged_auto32(mgr.staging / "Auto32", preset)
            messagebox.showinfo("Recoil preset applied",
                "Adjusted Auto 32 in staging only.\nPreset: " + preset +
                "\nItem ID: " + result["id"] +
                "\nNext: Install local mod (Content), then restart the game.")
        except Exception as exc:
            messagebox.showerror("Auto 32 recoil adjustment failed", str(exc))

    def generate_legendary_pack(self):
        try:
            from build_legendary_items import build_legendaries
            mgr = self.require_manager()
            eagle_id = simpledialog.askinteger("Legendary Eaglefire",
                "Eaglefire legacy item ID:", initialvalue=32612, minvalue=1, maxvalue=65535)
            if eagle_id is None: return
            fire_id = simpledialog.askinteger("Legendary 12 Gauge Incendiary",
                "Incendiary magazine legacy ID:", initialvalue=32613,
                minvalue=1, maxvalue=65535)
            if fire_id is None: return
            if not messagebox.askyesno("Experimental Legendary Pack",
                "Generate two items into staging only?\n"
                "Eaglefire: semi, 3-round burst and full-auto\n"
                "12 Gauge: small-area explosive shot, NOT verified Burner ignition.\n"
                "Natural spawns need map loot-table integration."):
                return
            extras = [mgr.workshop] if mgr.workshop else []
            result = build_legendaries(mgr.game, mgr.staging / "LegendaryPack",
                                      eagle_id=eagle_id, fire_id=fire_id,
                                      extra_roots=extras)
            self.refresh()
            messagebox.showinfo("Legendary pack generated",
                "Staging: " + result["path"] + "\nEaglefire ID " + str(eagle_id) +
                "\nIncendiary 12G ID " + str(fire_id) +
                "\nVisual burner effect: " + str(result["effect_id"] or "not found") +
                "\nWashington loot root: " + ("attached" if result["washington_root_attached"] else "not found") +
                "\nReview README_TESTING.txt before deployment.")
        except Exception as exc:
            messagebox.showerror("Legendary pack failed", str(exc))

    def upgrade_auto24(self):
        try:
            from upgrade_auto24 import upgrade_staging_auto24
            mgr = self.require_manager()
            if "Content/Auto32" in mgr.state()["installed"]:
                raise ValueError("Unturned must be closed. Remove managed Content/Auto32 "
                                 "in Managed Installs first, then upgrade staging and reinstall.")
            if not messagebox.askyesno("Upgrade Auto 32 to Legendary Auto 24",
                    "Keep the original item IDs and GUIDs but change magazine "
                    "capacity to 24 and mark gun/magazine Legendary?\n"
                    "Only staging files will be edited."):
                return
            result = upgrade_staging_auto24(mgr.staging / "Auto32")
            self.refresh()
            messagebox.showinfo("Upgrade complete",
                "Auto 24 staging updated. Gun ID " + result["gun_id"] +
                ", magazine " + result["magazine_id"] +
                ".\nInstall local mod (Content), then restart Unturned.")
        except Exception as exc:
            messagebox.showerror("Auto 24 upgrade failed", str(exc))

    def install_module(self):
        try:
            from module_support import install_module
            mgr = self.require_manager()
            indexes = self.local_list.curselection()
            if len(indexes) != 1:
                raise ValueError("Select exactly one staging folder containing a .module file.")
            name = self.local_list.get(indexes[0])
            plan = install_module(mgr, name)
            if plan["issues"]:
                raise ValueError("Dependencies or duplicate modules: " + str(plan["issues"]))
            if messagebox.askyesno("Install C# module", "Executable code can run inside Unturned. Only install trusted modules.\n\n" +
                                   str(plan["module"]) + "\n\nInstall?"):
                install_module(mgr, name, confirm=True)
                self.refresh()
        except Exception as exc:
            messagebox.showerror("Module installation failed", str(exc))

    def check_ai_service(self):
        from ai_service_status import check_ai_service
        result = check_ai_service()
        self.status_var.set(result["message"])
        if result["online"]:
            messagebox.showinfo("AI service online",
                "Local AI Service responds on 127.0.0.1:8765.\n"
                "Protocol: " + str(result["protocol"]) +
                "\nThis does NOT mean NPCs are being controlled.")
        else:
            messagebox.showwarning("AI service unavailable",
                result["message"] + "\n\nOpen AIService in PowerShell and run python server.py.")

    def show_conflicts(self):
        try:
            mgr = self.require_manager()
            name = simpledialog.askstring("Conflicts", "Profile name:", initialvalue=mgr.profile_state()["active"])
            if not name: return
            result = mgr.analyze_conflicts(name)
            lines = ["Scanned assets: " + str(result["assets_scanned"]),
                     "Conflicts: " + str(len(result["conflicts"])),
                     "Invalid GUIDs: " + str(len(result["invalid_guids"]))]
            for conflict in result["conflicts"][:12]:
                lines.append(str(conflict["kind"]) + " " + str(conflict["value"]) + ": " +
                             ", ".join(a["owner"] for a in conflict["assets"]))
            messagebox.showinfo("Conflict report", "\n".join(lines))
        except Exception as exc:
            messagebox.showerror("Conflict scan failed", str(exc))

    def apply_profile(self):
        try:
            mgr = self.require_manager()
            name = simpledialog.askstring("Apply profile", "Existing profile name:", initialvalue=mgr.profile_state()["active"])
            if not name: return
            plan = mgr.apply_profile(name)
            summary = ("Profile: " + name + "\nInstall: " + ", ".join(plan["add"]) +
                       "\nRemove to recovery: " + ", ".join(plan["remove"]) +
                       "\nSteam Workshop subscriptions are unchanged.\n\nApply changes?")
            if messagebox.askyesno("Confirm profile deployment", summary):
                mgr.apply_profile(name, confirm=True)
                self.refresh()
        except Exception as exc:
            messagebox.showerror("Profile activation failed", str(exc))

if __name__ == "__main__":
    App().mainloop()
