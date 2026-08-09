from pathlib import Path

import customtkinter as ctk

from common.app_info import AppInfo
from common.project_integrity_repair import SAFE_REPAIR_TYPES, repair_safe_project_integrity
from common.project_orphan_deletion import delete_orphan_project
from common.project_orphan_recovery import recover_orphan_project
from common.settings_manager import SettingsManager
from common.ui_state import KeyboardScrollBinding
from dialogs.orphan_delete_dialog import OrphanDeleteDialog
from widgets.message_dialog import ask_confirmation, show_message


MUTED_TEXT = ("#667085", "#8F96A3")
READY_TEXT = ("#027A48", "#75E0A7")
WARNING_TEXT = ("#B54708", "#FEC84B")
ERROR_TEXT = ("#B42318", "#FDA29B")
RECOVERY_STATUSES = ["In Progress", "Completed", "Published"]


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
        self.geometry("680x500")
        self.minsize(620, 470)
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
                "This restores an existing orphan folder as a project. Recovery never "
                "puts the project back into Scheduled."
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

        ctk.CTkLabel(
            self,
            text="Recover as",
            font=("Segoe UI", 11, "bold"),
            anchor="w",
        ).pack(fill="x", padx=28, pady=(16, 6))

        self.status_menu = ctk.CTkOptionMenu(
            self,
            values=RECOVERY_STATUSES,
            height=40,
        )
        self.status_menu.pack(fill="x", padx=28)
        if labels:
            self.status_menu.set(
                self._default_status_for_folder(self.folder_by_label.get(labels[0], ""))
            )

        ctk.CTkLabel(
            self,
            text=(
                "If the selected status differs from the current folder, the whole project "
                "folder is moved intact to that status folder."
            ),
            font=("Segoe UI", 10),
            text_color=MUTED_TEXT,
            anchor="w",
            justify="left",
            wraplength=620,
        ).pack(fill="x", padx=28, pady=(7, 0))

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

    @staticmethod
    def _default_status_for_folder(folder):
        current = Path(folder).parent.name if folder else ""
        if current in RECOVERY_STATUSES:
            return current
        return "In Progress"

    def _folder_changed(self, label):
        folder = self.folder_by_label.get(label, "")
        self.path_label.configure(text=folder)
        self.status_menu.set(self._default_status_for_folder(folder))

    def _confirm(self):
        label = self.folder_menu.get()
        folder = self.folder_by_label.get(label, "")
        if not folder:
            return
        self.result = {
            "folder": folder,
            "category": self.category_menu.get().strip() or "Misc",
            "target_status": self.status_menu.get().strip() or "In Progress",
        }
        self.destroy()

    def _cancel(self):
        self.result = None
        self.destroy()


class IntegrityPage(ctk.CTkScrollableFrame):
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
        self.app_info = AppInfo()
        self.build()
        self.keyboard_scroll = KeyboardScrollBinding(self, self)

    def build(self):
        ctk.CTkLabel(
            self,
            text="Project Integrity",
            font=("Segoe UI", 23, "bold"),
        ).pack(anchor="w", padx=4, pady=(2, 2))

        ctk.CTkLabel(
            self,
            text="Check database records against project folders and safely resolve integrity issues.",
            font=("Segoe UI", 13),
            text_color=MUTED_TEXT,
        ).pack(anchor="w", padx=4, pady=(0, 16))

        integrity = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        integrity.pack(fill="x", padx=4, pady=(0, 10))

        ctk.CTkLabel(
            integrity,
            text="Integrity tools",
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 2))

        ctk.CTkLabel(
            integrity,
            text=(
                "Safe repair never moves or deletes project files. Orphan recovery never "
                "restores Scheduled status."
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

        self.delete_orphan_button = ctk.CTkButton(
            integrity_actions,
            text="Delete orphan",
            width=110,
            height=34,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#FDA29B", "#7A3434"),
            text_color=("#B42318", "#FDA29B"),
            hover_color=("#FEF3F2", "#3A2020"),
            command=self.delete_orphan_folder,
        )
        self.delete_orphan_button.pack(side="left", padx=(8, 0))

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
            height=180,
            corner_radius=7,
            border_width=1,
            font=("Consolas", 10),
            wrap="word",
        )
        self.integrity_report.pack(fill="x", padx=14, pady=(0, 14))
        self._set_integrity_report("Run Check integrity to scan project records and folders.")

        self._build_diagnostics()

    def _build_diagnostics(self):
        card = ctk.CTkFrame(
            self,
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A3039"),
            fg_color=("#FFFFFF", "#181B21"),
        )
        card.pack(fill="x", padx=4, pady=(0, 10))

        ctk.CTkLabel(
            card,
            text="App diagnostics",
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w", padx=14, pady=(12, 2))
        ctk.CTkLabel(
            card,
            text="Configuration summary only — API keys are never displayed.",
            font=("Segoe UI", 11),
            text_color=MUTED_TEXT,
        ).pack(anchor="w", padx=14, pady=(2, 10))

        version = self.app_info.get("version", "Unknown")
        build = self.app_info.get("build", "")
        version_text = f"v{version}" + (f" • Build {build}" if build not in (None, "") else "")

        db_path = Path(getattr(self.pm.db, "db_path", "data/factvault.db")).resolve()
        db_state = "Ready" if db_path.exists() else "Not found"

        try:
            projects_root = self.pm.get_projects_root()
            projects_text = str(projects_root)
            projects_state = "Ready" if projects_root.exists() else "Folder not found"
        except Exception:
            projects_text = "Not configured"
            projects_state = "Needs setup"

        ai_ready = bool(str(self.settings.get("ai", "api_key", "") or "").strip())
        provider = str(self.settings.get("images", "provider", "Pixabay") or "Pixabay")
        image_key_name = "pexels_api_key" if provider == "Pexels" else "pixabay_api_key"
        images_ready = bool(str(self.settings.get("images", image_key_name, "") or "").strip())

        width = self.settings.get("resolve", "timeline_width", 1080)
        height = self.settings.get("resolve", "timeline_height", 1920)
        frame_rate = self.settings.get("resolve", "frame_rate", 30)

        self._diagnostic_row(card, "App version", version_text, "Ready", True)
        self._diagnostic_row(card, "Database", str(db_path), db_state, db_path.exists())
        self._diagnostic_row(card, "Projects folder", projects_text, projects_state, projects_state == "Ready")
        self._diagnostic_row(card, "OpenAI", "API key configured" if ai_ready else "API key missing", "Ready" if ai_ready else "Needs setup", ai_ready)
        self._diagnostic_row(card, "Images", f"{provider} • " + ("API key configured" if images_ready else "API key missing"), "Ready" if images_ready else "Needs setup", images_ready)
        self._diagnostic_row(card, "Resolve export", f"{width} × {height} @ {frame_rate} fps", "Ready", True, last=True)

    def _diagnostic_row(self, parent, label, value, state, ready, *, last=False):
        row = ctk.CTkFrame(parent, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=(2, 8 if not last else 12))
        ctk.CTkLabel(
            row,
            text=label,
            width=112,
            font=("Segoe UI", 11, "bold"),
            anchor="w",
        ).pack(side="left")
        ctk.CTkLabel(
            row,
            text=value,
            font=("Segoe UI", 10),
            text_color=MUTED_TEXT,
            anchor="w",
        ).pack(side="left", fill="x", expand=True, padx=(8, 12))
        ctk.CTkLabel(
            row,
            text=state,
            font=("Segoe UI", 10, "bold"),
            text_color=READY_TEXT if ready else WARNING_TEXT,
            anchor="e",
        ).pack(side="right")

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
            show_message(self, "Project Integrity", str(error), kind="error")
            return
        self._render_integrity_issues(issues)

    def repair_safe_integrity(self):
        try:
            issues = list(self.pm.check_project_integrity())
        except Exception as error:
            show_message(self, "Project Integrity", str(error), kind="error")
            return

        safe, _manual = self._render_integrity_issues(issues)
        if not safe:
            show_message(
                self,
                "Project Integrity",
                "There are no safe repair candidates. Manual-review issues were not changed.",
            )
            return

        confirmed = ask_confirmation(
            self,
            "Repair Safe Issues",
            (
                f"Repair {len(safe)} safe candidate(s)?\n\n"
                "This can clear stale schedules and correct safe stored folder paths. "
                "It will not move or delete project files."
            ),
            confirm_text="Repair issues",
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
            show_message(
                self,
                "Project Integrity",
                summary,
                kind="warning" if errors else "success",
            )
        except Exception as error:
            self.integrity_status.configure(
                text="Safe repair failed.",
                text_color=ERROR_TEXT,
            )
            show_message(self, "Project Integrity", str(error), kind="error")
        finally:
            self.repair_integrity_button.configure(state="normal", text="Repair safe issues")

    def recover_orphan_project(self):
        try:
            issues = list(self.pm.check_project_integrity())
        except Exception as error:
            show_message(self, "Project Integrity", str(error), kind="error")
            return

        orphans = orphan_integrity_issues(issues)
        self._render_integrity_issues(issues)
        if not orphans:
            show_message(
                self,
                "Recover Orphan Project",
                "No orphan project folders were found.",
            )
            return

        categories = self.pm.db.get_categories()
        dialog = OrphanRecoveryDialog(self, orphans, categories)
        self.wait_window(dialog)
        if dialog.result is None:
            return

        folder = dialog.result["folder"]
        category = dialog.result["category"]
        target_status = dialog.result["target_status"]
        path = Path(folder)
        source_status = path.parent.name
        move_note = ""
        if source_status != target_status:
            move_note = (
                f"\n\nThe folder will move from '{source_status}' to "
                f"'{target_status}' intact."
            )

        confirmed = ask_confirmation(
            self,
            "Recover Orphan Project",
            (
                f"Recover this existing folder as a project?\n\n"
                f"Title: {path.name}\n"
                f"Recover as: {target_status}\n"
                f"Category: {category}"
                f"{move_note}"
            ),
            confirm_text="Recover project",
        )
        if not confirmed:
            return

        self.recover_orphan_button.configure(state="disabled", text="Recovering...")
        try:
            recovered = recover_orphan_project(
                self.pm,
                folder,
                category=category,
                target_status=target_status,
            )
            remaining = list(self.pm.check_project_integrity())
            self._render_integrity_issues(remaining)
            show_message(
                self,
                "Recover Orphan Project",
                (
                    f"Recovered '{recovered['title']}' as {recovered['status']}.\n\n"
                    "The project files were kept intact."
                ),
                kind="success",
            )
        except Exception as error:
            show_message(self, "Recover Orphan Project", str(error), kind="error")
        finally:
            self.recover_orphan_button.configure(state="normal", text="Recover orphan")

    def _confirm_orphan_delete(self, path):
        dialog = ctk.CTkToplevel(self)
        dialog.title("Delete Orphan Folder")
        dialog.geometry("520x300")
        dialog.resizable(False, False)
        dialog.transient(self)
        dialog.grab_set()
        dialog.lift()
        dialog.focus_force()
        dialog.result = False

        ctk.CTkLabel(
            dialog,
            text="Delete orphan folder?",
            font=("Segoe UI", 21, "bold"),
            anchor="w",
        ).pack(fill="x", padx=24, pady=(24, 6))
        ctk.CTkLabel(
            dialog,
            text=path.name,
            font=("Segoe UI Emoji", 13, "bold"),
            anchor="w",
            justify="left",
            wraplength=460,
        ).pack(fill="x", padx=24, pady=(0, 10))
        ctk.CTkLabel(
            dialog,
            text=(
                "This folder is not linked to a database project. Deleting it will "
                "permanently remove the folder and everything inside it."
            ),
            font=("Segoe UI", 12),
            text_color=MUTED_TEXT,
            anchor="w",
            justify="left",
            wraplength=460,
        ).pack(fill="x", padx=24, pady=(0, 12))
        ctk.CTkLabel(
            dialog,
            text="Deleting the folder cannot be undone.",
            font=("Segoe UI", 12, "bold"),
            text_color=ERROR_TEXT,
            anchor="w",
        ).pack(fill="x", padx=24)

        buttons = ctk.CTkFrame(dialog, fg_color="transparent")
        buttons.pack(side="bottom", fill="x", padx=24, pady=24)

        def finish(result):
            dialog.result = result
            dialog.destroy()

        dialog.protocol("WM_DELETE_WINDOW", lambda: finish(False))
        dialog.bind("<Escape>", lambda _event: finish(False))
        ctk.CTkButton(
            buttons,
            text="Delete Folder",
            width=132,
            height=36,
            fg_color="#B42318",
            hover_color="#912018",
            command=lambda: finish(True),
        ).pack(side="right")
        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=88,
            height=36,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404A"),
            text_color=("#344054", "#D0D5DD"),
            hover_color=("#F2F4F7", "#252A33"),
            command=lambda: finish(False),
        ).pack(side="left")

        self.wait_window(dialog)
        return dialog.result

    def delete_orphan_folder(self):
        try:
            issues = list(self.pm.check_project_integrity())
        except Exception as error:
            show_message(self, "Project Integrity", str(error), kind="error")
            return

        orphans = orphan_integrity_issues(issues)
        self._render_integrity_issues(issues)
        if not orphans:
            show_message(
                self,
                "Delete Orphan Folder",
                "No orphan project folders were found.",
            )
            return

        dialog = OrphanDeleteDialog(self, orphans)
        self.wait_window(dialog)
        if dialog.result is None:
            return

        folder = dialog.result
        path = Path(folder)
        if not self._confirm_orphan_delete(path):
            return

        self.delete_orphan_button.configure(state="disabled", text="Deleting...")
        try:
            delete_orphan_project(self.pm, folder)
            remaining = list(self.pm.check_project_integrity())
            self._render_integrity_issues(remaining)
            self.integrity_status.configure(
                text=f"Deleted orphan folder: {path.name}",
                text_color=READY_TEXT,
            )
        except Exception as error:
            show_message(self, "Delete Orphan Folder", str(error), kind="error")
        finally:
            self.delete_orphan_button.configure(state="normal", text="Delete orphan")
