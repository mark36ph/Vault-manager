import customtkinter as ctk
from tkinter import filedialog, messagebox
from pathlib import Path

from common.project_integrity_repair import SAFE_REPAIR_TYPES, repair_safe_project_integrity
from common.project_orphan_recovery import recover_orphan_project
from common.settings_manager import SettingsManager
from widgets.project_card import ScheduleDialog


MUTED_TEXT = ("#667085", "#8F96A3")
READY_TEXT = ("#027A48", "#75E0A7")
WARNING_TEXT = ("#B54708", "#FEC84B")
ERROR_TEXT = ("#B42318", "#FDA29B")


def split_integrity_issues(issues):
    """Separate safe-repair candidates from issues that require manual review."""
    safe = []
    manual = []
    for issue in issues:
        if str(issue.get("type") or "") in SAFE_REPAIR_TYPES:
            safe.append(issue)
        else:
            manual.append(issue)
    return safe, manual


def orphan_integrity_issues(issues):
    """Return integrity findings that represent recoverable orphan folders."""
    return [
        issue
        for issue in issues
        if str(issue.get("type") or "") == "orphan_folder" and issue.get("folder")
    ]


def integrity_report_text(issues):
    """Create a concise, user-facing report for project integrity findings."""
    issues = list(issues)
    if not issues:
        return "No project integrity issues found."

    safe, manual = split_integrity_issues(issues)
    lines = [
        f"Found {len(issues)} issue(s): {len(safe)} safe repair candidate(s), "
        f"{len(manual)} manual review issue(s).",
        "",
    ]
    for issue in issues:
        issue_type = str(issue.get("type") or "unknown")
        title = str(issue.get("title") or issue.get("folder") or "Project")
        message = str(issue.get("message") or issue_type)
        label = "SAFE REPAIR" if issue_type in SAFE_REPAIR_TYPES else "MANUAL REVIEW"
        lines.append(f"[{label}] {title}: {message}")
    return "\n".join(lines)


class OrphanRecoveryDialog(ctk.CTkToplevel):
    """Choose one orphan folder and explicitly restore its database record."""

    def __init__(self, parent, orphan_issues, categories):
        super().__init__(parent)
        self.result = None
        self.orphan_issues = list(orphan_issues)
        self.folder_by_label = {}

        self.title("Recover Orphan Project")
        self.geometry("680x410")
        self.minsize(620, 390)
        self.resizable(True, False)
        self.transient(parent)
        self.grab_set()

        ctk.CTkLabel(
            self,
            text="Recover Orphan Project",
            font=("Segoe UI", 20, "bold"),
            anchor="w",
        ).pack(fill="x", padx=28, pady=(24, 4))

        ctk.CTkLabel(
            self,
            text=(
                "This creates a database record for an existing orphan folder. "
                "It does not move, rename, or delete any files."
            ),
            font=("Segoe UI", 12),
            text_color=MUTED_TEXT,
            justify="left",
            anchor="w",
            wraplength=620,
        ).pack(fill="x", padx=28, pady=(0, 20))

        ctk.CTkLabel(
            self,
            text="Orphan folder",
            font=("Segoe UI", 11, "bold"),
            anchor="w",
        ).pack(fill="x", padx=28, pady=(0, 6))

        labels = []
        for index, issue in enumerate(self.orphan_issues, start=1):
            folder = str(issue.get("folder") or "")
            path = Path(folder)
            label = f"{index}. {path.parent.name} / {path.name}"
            labels.append(label)
            self.folder_by_label[label] = folder

        self.folder_menu = ctk.CTkOptionMenu(
            self,
            values=labels,
            height=40,
            command=self._folder_changed,
        )
        self.folder_menu.pack(fill="x", padx=28)
        if labels:
            self.folder_menu.set(labels[0])

        self.path_label = ctk.CTkLabel(
            self,
            text=self.folder_by_label.get(labels[0], "") if labels else "",
            font=("Consolas", 10),
            text_color=MUTED_TEXT,
            anchor="w",
            justify="left",
            wraplength=620,
        )
        self.path_label.pack(fill="x", padx=28, pady=(7, 18))

        ctk.CTkLabel(
            self,
            text="Category",
            font=("Segoe UI", 11, "bold"),
            anchor="w",
        ).pack(fill="x", padx=28, pady=(0, 6))

        values = list(categories) or ["Misc"]
        self.category_menu = ctk.CTkOptionMenu(self, values=values, height=40)
        self.category_menu.pack(fill="x", padx=28)
        self.category_menu.set("Misc" if "Misc" in values else values[0])

        buttons = ctk.CTkFrame(self, fg_color="transparent")
        buttons.pack(side="bottom", fill="x", padx=28, pady=(18, 24))

        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=112,
            height=38,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self._cancel,
        ).pack(side="right", padx=(10, 0))

        ctk.CTkButton(
            buttons,
            text="Recover project",
            width=142,
            height=38,
            command=self._confirm,
        ).pack(side="right")

        self.protocol("WM_DELETE_WINDOW", self._cancel)
        self.bind("<Escape>", lambda _event: self._cancel())
        self.bind("<Return>", lambda _event: self._confirm())

    def _folder_changed(self, label):
        self.path_label.configure(text=self.folder_by_label.get(label, ""))

    def _confirm(self):
        label = self.folder_menu.get()
        folder = self.folder_by_label.get(label, "")
        if not folder:
            return
        self.result = {
            "folder": folder,
            "category": self.category_menu.get().strip() or "Misc",
        }
        self.destroy()

    def _cancel(self):
        self.result = None
        self.destroy()


class GeneralPage(ctk.CTkScrollableFrame):
    def __init__(self, parent, pm, app):
        super().__init__(
            parent,
            fg_color="transparent",
            scrollbar_button_color=("#D0D5DD", "#3A404B"),
            scrollbar_button_hover_color=("#98A2B3", "#596170"),
        )
        self.pm = pm
        self.app = app
        self.settings = SettingsManager()
        self.build()

    def build(self):
        ctk.CTkLabel(
            self,
            text="General",
            font=("Segoe UI", 23, "bold"),
        ).pack(anchor="w", padx=4, pady=(2, 2))

        ctk.CTkLabel(
            self,
            text="Choose where projects are stored and how the app behaves at startup.",
            font=("Segoe UI", 13),
            text_color=MUTED_TEXT,
        ).pack(anchor="w", padx=4, pady=(0, 16))

        storage = self._section("Project storage")
        ctk.CTkLabel(
            storage,
            text="Projects folder",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 6))

        folder_row = ctk.CTkFrame(storage, fg_color="transparent")
        folder_row.pack(fill="x", padx=14, pady=(0, 14))

        self.projects_folder = ctk.CTkEntry(folder_row, height=36)
        self.projects_folder.pack(side="left", fill="x", expand=True)
        self.projects_folder.insert(
            0,
            self.settings.get("general", "projects_folder", ""),
        )

        ctk.CTkButton(
            folder_row,
            text="Browse",
            width=88,
            height=36,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.browse_projects_folder,
        ).pack(side="left", padx=(8, 0))

        startup = self._section("Startup")

        self.start_maximized = ctk.BooleanVar(
            value=self.settings.get("general", "start_maximized", True)
        )
        self.remember_project = ctk.BooleanVar(
            value=self.settings.get("general", "remember_last_project", True)
        )
        self.check_updates = ctk.BooleanVar(
            value=self.settings.get("general", "check_updates", True)
        )

        for text, variable in (
            ("Open maximized", self.start_maximized),
            ("Remember last opened project", self.remember_project),
            ("Check for updates on startup", self.check_updates),
        ):
            ctk.CTkCheckBox(
                startup,
                text=text,
                variable=variable,
                font=("Segoe UI", 13),
            ).pack(anchor="w", padx=14, pady=6)

        appearance = self._section("Appearance")
        ctk.CTkLabel(
            appearance,
            text="Theme",
            font=("Segoe UI", 13, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 6))

        self.theme = ctk.CTkOptionMenu(
            appearance,
            values=["Dark", "Light", "System"],
            width=180,
            height=34,
        )
        self.theme.pack(anchor="w", padx=14, pady=(0, 14))
        self.theme.set(
            self.settings.get("general", "theme", "dark").title()
        )

        integrity = self._section("Project integrity")
        ctk.CTkLabel(
            integrity,
            text=(
                "Check database records against project folders. Safe repair never moves "
                "or deletes project files. Orphan recovery only restores a database link."
            ),
            font=("Segoe UI", 12),
            text_color=MUTED_TEXT,
            justify="left",
            anchor="w",
            wraplength=680,
        ).pack(fill="x", padx=14, pady=(8, 8))

        integrity_actions = ctk.CTkFrame(integrity, fg_color="transparent")
        integrity_actions.pack(fill="x", padx=14, pady=(0, 8))

        ctk.CTkButton(
            integrity_actions,
            text="Check integrity",
            width=118,
            height=34,
            corner_radius=7,
            command=self.check_project_integrity,
        ).pack(side="left")

        self.repair_integrity_button = ctk.CTkButton(
            integrity_actions,
            text="Repair safe issues",
            width=132,
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.repair_safe_integrity,
        )
        self.repair_integrity_button.pack(side="left", padx=(8, 0))

        self.recover_orphan_button = ctk.CTkButton(
            integrity_actions,
            text="Recover orphan",
            width=118,
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            text_color=("#344054", "#D0D5DD"),
            command=self.recover_orphan_project,
        )
        self.recover_orphan_button.pack(side="left", padx=(8, 0))

        self.integrity_status = ctk.CTkLabel(
            integrity,
            text="Not checked yet.",
            font=("Segoe UI", 11),
            text_color=MUTED_TEXT,
            anchor="w",
        )
        self.integrity_status.pack(fill="x", padx=14, pady=(0, 6))

        self.integrity_report = ctk.CTkTextbox(
            integrity,
            height=125,
            corner_radius=7,
            border_width=1,
            font=("Consolas", 10),
            wrap="word",
        )
        self.integrity_report.pack(fill="x", padx=14, pady=(0, 14))
        self._set_integrity_report("Run Check integrity to scan project records and folders.")

        ctk.CTkButton(
            self,
            text="Save changes",
            height=36,
            width=130,
            corner_radius=7,
            command=self.save_settings,
        ).pack(anchor="e", padx=4, pady=(2, 4))

    def _section(self, title):
        frame = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        frame.pack(fill="x", padx=4, pady=(0, 10))
        ctk.CTkLabel(
            frame,
            text=title,
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 2))
        return frame

    def _set_integrity_report(self, text):
        self.integrity_report.configure(state="normal")
        self.integrity_report.delete("1.0", "end")
        self.integrity_report.insert("1.0", text)
        self.integrity_report.configure(state="disabled")

    def _render_integrity_issues(self, issues):
        issues = list(issues)
        safe, manual = split_integrity_issues(issues)
        self._set_integrity_report(integrity_report_text(issues))

        if not issues:
            self.integrity_status.configure(
                text="Integrity check passed — no issues found.",
                text_color=READY_TEXT,
            )
        else:
            self.integrity_status.configure(
                text=(
                    f"{len(issues)} issue(s): {len(safe)} safe repair candidate(s), "
                    f"{len(manual)} manual review."
                ),
                text_color=WARNING_TEXT if not manual else ERROR_TEXT,
            )
        return safe, manual

    def check_project_integrity(self):
        try:
            issues = self.pm.check_project_integrity()
        except Exception as error:
            self.integrity_status.configure(
                text="Integrity check failed.",
                text_color=ERROR_TEXT,
            )
            self._set_integrity_report(str(error))
            messagebox.showerror("Project Integrity", str(error), parent=self)
            return

        self._render_integrity_issues(issues)

    def repair_safe_integrity(self):
        try:
            issues = list(self.pm.check_project_integrity())
        except Exception as error:
            messagebox.showerror("Project Integrity", str(error), parent=self)
            return

        safe, _manual = self._render_integrity_issues(issues)
        if not safe:
            messagebox.showinfo(
                "Project Integrity",
                "There are no safe repair candidates. Manual-review issues were not changed.",
                parent=self,
            )
            return

        confirmed = messagebox.askyesno(
            "Repair Safe Issues",
            (
                f"Repair {len(safe)} safe candidate(s)?\n\n"
                "This can clear stale schedules and correct safe stored folder paths. "
                "It will not move or delete project files."
            ),
            parent=self,
        )
        if not confirmed:
            return

        self.repair_integrity_button.configure(state="disabled", text="Repairing...")
        try:
            result = repair_safe_project_integrity(self.pm)
            remaining = list(self.pm.check_project_integrity())
            repaired = result.get("repaired", [])
            skipped = result.get("skipped", [])
            errors = result.get("errors", [])
            self._render_integrity_issues(remaining)

            summary = (
                f"Repaired {len(repaired)} issue(s). "
                f"Skipped {len(skipped)} issue(s). "
                f"Errors: {len(errors)}."
            )
            if errors:
                messagebox.showwarning("Project Integrity", summary, parent=self)
            else:
                messagebox.showinfo("Project Integrity", summary, parent=self)
        except Exception as error:
            self.integrity_status.configure(
                text="Safe repair failed.",
                text_color=ERROR_TEXT,
            )
            messagebox.showerror("Project Integrity", str(error), parent=self)
        finally:
            self.repair_integrity_button.configure(
                state="normal",
                text="Repair safe issues",
            )

    def recover_orphan_project(self):
        try:
            issues = list(self.pm.check_project_integrity())
        except Exception as error:
            messagebox.showerror("Project Integrity", str(error), parent=self)
            return

        orphans = orphan_integrity_issues(issues)
        self._render_integrity_issues(issues)
        if not orphans:
            messagebox.showinfo(
                "Recover Orphan Project",
                "No orphan project folders were found.",
                parent=self,
            )
            return

        categories = self.pm.db.get_categories()
        dialog = OrphanRecoveryDialog(self, orphans, categories)
        self.wait_window(dialog)
        if dialog.result is None:
            return

        folder = dialog.result["folder"]
        category = dialog.result["category"]
        path = Path(folder)
        scheduled_for = ""

        if path.parent.name == "Scheduled":
            schedule_dialog = ScheduleDialog(self)
            self.wait_window(schedule_dialog)
            if schedule_dialog.result is None:
                return
            scheduled_for = schedule_dialog.result.strftime("%Y-%m-%d %H:%M")

        schedule_line = f"\nScheduled for: {scheduled_for}" if scheduled_for else ""
        confirmed = messagebox.askyesno(
            "Recover Orphan Project",
            (
                f"Recover this existing folder as a project?\n\n"
                f"Title: {path.name}\n"
                f"Status: {path.parent.name}\n"
                f"Category: {category}"
                f"{schedule_line}\n\n"
                "No files will be moved, renamed, or deleted."
            ),
            parent=self,
        )
        if not confirmed:
            return

        self.recover_orphan_button.configure(state="disabled", text="Recovering...")
        try:
            recovered = recover_orphan_project(
                self.pm,
                folder,
                category=category,
                scheduled_for=scheduled_for,
            )
            remaining = list(self.pm.check_project_integrity())
            self._render_integrity_issues(remaining)
            messagebox.showinfo(
                "Recover Orphan Project",
                (
                    f"Recovered '{recovered['title']}' as {recovered['status']}.\n\n"
                    "The existing project folder was left unchanged."
                ),
                parent=self,
            )
        except Exception as error:
            messagebox.showerror("Recover Orphan Project", str(error), parent=self)
        finally:
            self.recover_orphan_button.configure(state="normal", text="Recover orphan")

    def browse_projects_folder(self):
        folder = filedialog.askdirectory()
        if folder:
            self.projects_folder.delete(0, "end")
            self.projects_folder.insert(0, folder)

    def save_settings(self):
        self.settings.set(
            "general",
            "projects_folder",
            self.projects_folder.get().strip(),
        )
        self.settings.set(
            "general",
            "start_maximized",
            self.start_maximized.get(),
        )
        self.settings.set(
            "general",
            "remember_last_project",
            self.remember_project.get(),
        )
        self.settings.set(
            "general",
            "check_updates",
            self.check_updates.get(),
        )
        self.settings.set(
            "general",
            "theme",
            self.theme.get().lower(),
        )
        ctk.set_appearance_mode(self.theme.get())
        messagebox.showinfo("Settings", "Settings saved successfully.")