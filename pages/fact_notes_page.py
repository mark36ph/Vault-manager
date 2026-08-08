from datetime import datetime

import customtkinter as ctk

from pages.base_page import BasePage
from common.ui_fonts import TABLE_FONT


class FactNotesPage(BasePage):
    """Capture and manage reusable fact ideas and research notes."""

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Fact Notes")
        self.app = app
        self.editing_note_id = None

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))

        self.subtitle = ctk.CTkLabel(
            self,
            text="Keep fact ideas, research notes, and checklists ready for future projects.",
            font=("Segoe UI", 13),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        )
        self.subtitle.pack(fill="x", padx=24, pady=(0, 14), before=self.content)
        self.content.pack_configure(padx=24, pady=(0, 20))

        self.build()

    def build(self):
        toolbar = ctk.CTkFrame(self.content, fg_color="transparent")
        toolbar.pack(fill="x", pady=(0, 10))

        self.count_label = ctk.CTkLabel(
            toolbar,
            text="0 notes",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        )
        self.count_label.pack(side="left")

        ctk.CTkButton(
            toolbar,
            text="Refresh",
            width=82,
            height=36,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404A"),
            text_color=("#344054", "#D0D5DD"),
            hover_color=("#F2F4F7", "#252A33"),
            command=self.load_notes,
        ).pack(side="right", padx=(8, 0))

        ctk.CTkButton(
            toolbar,
            text="+ Add Note",
            width=108,
            height=36,
            corner_radius=7,
            command=self.open_note_editor_dialog,
        ).pack(side="right")

        self.notes_list = ctk.CTkScrollableFrame(
            self.content,
            fg_color="transparent",
        )
        self.notes_list.pack(fill="both", expand=True)
        self.load_notes()

    def load_notes(self):
        for widget in self.notes_list.winfo_children():
            widget.destroy()

        notes = self.pm.db.get_fact_notes()
        count = len(notes)
        self.count_label.configure(text=f"{count} note{'s' if count != 1 else ''}")

        if not notes:
            empty = ctk.CTkFrame(
                self.notes_list,
                corner_radius=8,
                fg_color=("#FFFFFF", "#181B21"),
                border_width=1,
                border_color=("#E4E7EC", "#2A2F38"),
            )
            empty.pack(fill="x", pady=4)
            ctk.CTkLabel(
                empty,
                text="No fact notes yet",
                font=("Segoe UI", 15, "bold"),
            ).pack(anchor="w", padx=16, pady=(16, 3))
            ctk.CTkLabel(
                empty,
                text="Add an idea, research lead, or checklist to keep for later.",
                font=("Segoe UI", 12),
                text_color=("#667085", "#8F96A3"),
            ).pack(anchor="w", padx=16, pady=(0, 16))
            return

        for note in notes:
            self.create_note_card(note)

    def create_note_card(self, note):
        card = ctk.CTkFrame(
            self.notes_list,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        card.pack(fill="x", pady=4)

        is_pinned = self.is_note_pinned(note)
        header = ctk.CTkFrame(card, fg_color="transparent")
        header.pack(fill="x", padx=14, pady=(12, 3))

        ctk.CTkLabel(
            header,
            text=("📌 " if is_pinned else "") + note["title"],
            font=("Segoe UI Emoji", 15, "bold"),
            anchor="w",
        ).pack(side="left", fill="x", expand=True)

        ctk.CTkLabel(
            header,
            text=note["status"],
            font=("Segoe UI", 11, "bold"),
            fg_color=("#EEF4FF", "#24344D"),
            text_color=("#175CD3", "#AFCBFF"),
            corner_radius=6,
            padx=8,
            pady=2,
        ).pack(side="right")

        ctk.CTkLabel(
            card,
            text=f"Created {note['created']}",
            font=("Segoe UI", 11),
            text_color=("#667085", "#8F96A3"),
            anchor="w",
        ).pack(fill="x", padx=14, pady=(0, 7))

        preview = (note["notes"] or "").strip()
        if len(preview) > 150:
            preview = preview[:150].rstrip() + "…"
        if not preview:
            preview = "No notes added yet."

        ctk.CTkLabel(
            card,
            text=preview,
            font=("Segoe UI Emoji", 12),
            wraplength=820,
            justify="left",
            anchor="w",
            text_color=("#475467", "#AAB1BC"),
        ).pack(fill="x", padx=14, pady=(0, 10))

        buttons = ctk.CTkFrame(card, fg_color="transparent")
        buttons.pack(fill="x", padx=14, pady=(0, 12))

        secondary = {
            "height": 32,
            "corner_radius": 6,
            "fg_color": "transparent",
            "border_width": 1,
            "border_color": ("#D0D5DD", "#3A404A"),
            "text_color": ("#344054", "#D0D5DD"),
            "hover_color": ("#F2F4F7", "#252A33"),
        }

        ctk.CTkButton(
            buttons,
            text="Open",
            width=68,
            command=lambda n=note: self.open_note_window(n),
            **secondary,
        ).pack(side="left", padx=(0, 5))
        ctk.CTkButton(
            buttons,
            text="Edit",
            width=64,
            command=lambda n=note: self.open_note_editor_dialog(n),
            **secondary,
        ).pack(side="left", padx=(0, 5))
        ctk.CTkButton(
            buttons,
            text="Unpin" if is_pinned else "Pin",
            width=66,
            command=lambda n=note: self.toggle_pin(n),
            **secondary,
        ).pack(side="left")
        ctk.CTkButton(
            buttons,
            text="Delete",
            width=72,
            height=32,
            corner_radius=6,
            fg_color="transparent",
            border_width=1,
            border_color=("#FDA29B", "#7A3030"),
            text_color=("#B42318", "#FDA29B"),
            hover_color=("#FEF3F2", "#3A2222"),
            command=lambda n=note: self.open_delete_dialog(n),
        ).pack(side="right")

    def open_note_window(self, note):
        window = ctk.CTkToplevel(self)
        window.title(note["title"])
        window.geometry("900x700")
        window.transient(self)
        window.grab_set()
        window.lift()
        window.focus_force()

        header = ctk.CTkFrame(window, fg_color="transparent")
        header.pack(fill="x", padx=22, pady=(20, 10))
        ctk.CTkLabel(
            header,
            text=("📌 " if self.is_note_pinned(note) else "") + note["title"],
            font=("Segoe UI Emoji", 22, "bold"),
        ).pack(side="left")

        ctk.CTkButton(
            header,
            text="Edit",
            width=78,
            height=34,
            command=lambda: self.open_window_edit(note, window),
        ).pack(side="right")

        info = ctk.CTkFrame(
            window,
            corner_radius=8,
            fg_color=("#FFFFFF", "#181B21"),
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        info.pack(fill="x", padx=22, pady=(0, 10))
        ctk.CTkLabel(
            info,
            text=f"{note['status']}   •   Created {note['created']}",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=14, pady=10)

        notes_box = ctk.CTkTextbox(
            window,
            font=TABLE_FONT,
            wrap="none",
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        notes_box.pack(fill="both", expand=True, padx=22, pady=(0, 10))
        notes_box.insert("1.0", note["notes"])
        self.setup_clickable_note_checkboxes(notes_box, note)
        notes_box.configure(state="disabled")
        self.setup_checkbox_viewer(notes_box)

        ctk.CTkButton(
            window,
            text="Close",
            width=88,
            height=34,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404A"),
            text_color=("#344054", "#D0D5DD"),
            command=window.destroy,
        ).pack(anchor="e", padx=22, pady=(0, 20))

    def open_window_edit(self, note, window):
        window.destroy()
        self.open_note_editor_dialog(note)

    def toggle_pin(self, note):
        self.pm.db.toggle_fact_note_pinned(note["id"])
        self.load_notes()

    def open_delete_dialog(self, note):
        dialog = ctk.CTkToplevel(self)
        dialog.title("Delete Note")
        dialog.geometry("420x220")
        dialog.transient(self)
        dialog.grab_set()
        dialog.lift()
        dialog.focus_force()

        ctk.CTkLabel(
            dialog,
            text="Delete this note?",
            font=("Segoe UI", 20, "bold"),
        ).pack(anchor="w", padx=24, pady=(24, 7))
        ctk.CTkLabel(
            dialog,
            text=note["title"],
            font=("Segoe UI Emoji", 13),
            wraplength=360,
            justify="left",
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=24, pady=(0, 5))
        ctk.CTkLabel(
            dialog,
            text="This cannot be undone.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=24)

        buttons = ctk.CTkFrame(dialog, fg_color="transparent")
        buttons.pack(side="bottom", fill="x", padx=24, pady=24)
        ctk.CTkButton(
            buttons,
            text="Delete",
            width=82,
            height=34,
            fg_color="#B42318",
            hover_color="#912018",
            command=lambda: self.confirm_delete_note(note, dialog),
        ).pack(side="right")
        ctk.CTkButton(
            buttons,
            text="Cancel",
            width=82,
            height=34,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404A"),
            text_color=("#344054", "#D0D5DD"),
            command=dialog.destroy,
        ).pack(side="right", padx=(0, 8))

    def confirm_delete_note(self, note, dialog):
        self.pm.db.delete_fact_note(note["id"])
        dialog.destroy()
        self.load_notes()

    def is_note_pinned(self, note):
        try:
            return note["pinned"] == 1
        except Exception:
            return False

    def is_note_checked(self, note):
        try:
            return note["checked"] == 1
        except Exception:
            return False

    def toggle_checked(self, note):
        self.pm.db.toggle_fact_note_checked(note["id"])
        self.load_notes()

    def open_note_editor_dialog(self, note=None):
        is_editing = note is not None
        window = ctk.CTkToplevel(self)
        window.title("Edit Note" if is_editing else "Add Note")
        window.geometry("850x700")
        window.transient(self)
        window.grab_set()
        window.lift()
        window.focus_force()

        ctk.CTkLabel(
            window,
            text="Edit Fact Note" if is_editing else "Add Fact Note",
            font=("Segoe UI", 22, "bold"),
        ).pack(anchor="w", padx=24, pady=(22, 4))
        ctk.CTkLabel(
            window,
            text="Capture an idea, source notes, or a reusable research checklist.",
            font=("Segoe UI", 12),
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=24, pady=(0, 14))

        fields = ctk.CTkFrame(window, fg_color="transparent")
        fields.pack(fill="x", padx=24, pady=(0, 10))
        fields.grid_columnconfigure(0, weight=1)

        title_entry = ctk.CTkEntry(fields, placeholder_text="Fact idea title...", height=36)
        title_entry.grid(row=0, column=0, sticky="ew", padx=(0, 8))
        status = ctk.CTkOptionMenu(fields, values=["Idea", "Researching"], width=140, height=36)
        status.grid(row=0, column=1)

        if is_editing:
            title_entry.insert(0, note["title"])
            status.set(note["status"])
        else:
            status.set("Idea")

        notes_box = ctk.CTkTextbox(
            window,
            font=TABLE_FONT,
            wrap="none",
            corner_radius=8,
            border_width=1,
            border_color=("#E4E7EC", "#2A2F38"),
        )
        notes_box.pack(fill="both", expand=True, padx=24, pady=(0, 10))
        if is_editing:
            notes_box.insert("1.0", note["notes"])
        self.setup_checkbox_textbox(notes_box)

        buttons = ctk.CTkFrame(window, fg_color="transparent")
        buttons.pack(fill="x", padx=24, pady=(0, 22))

        secondary = {
            "height": 34,
            "corner_radius": 6,
            "fg_color": "transparent",
            "border_width": 1,
            "border_color": ("#D0D5DD", "#3A404A"),
            "text_color": ("#344054", "#D0D5DD"),
            "hover_color": ("#F2F4F7", "#252A33"),
        }
        ctk.CTkButton(
            buttons,
            text="Format Table",
            width=104,
            command=lambda: self.format_textbox_table(notes_box),
            **secondary,
        ).pack(side="left", padx=(0, 5))
        ctk.CTkButton(
            buttons,
            text="☐",
            width=42,
            command=lambda: self.insert_checkbox(notes_box),
            **secondary,
        ).pack(side="left", padx=(0, 5))
        ctk.CTkButton(
            buttons,
            text="☑",
            width=42,
            command=lambda: self.toggle_checkbox_line(notes_box),
            **secondary,
        ).pack(side="left")

        def save_dialog_note():
            title = title_entry.get().strip()
            notes = notes_box.get("1.0", "end").strip()
            if not title:
                self.open_missing_title_dialog()
                return

            if is_editing:
                self.pm.db.update_fact_note(note["id"], title, "", notes, status.get())
            else:
                created = datetime.now().strftime("%Y-%m-%d %H:%M")
                self.pm.db.add_fact_note(title, "", notes, status.get(), created)
            self.load_notes()

        ctk.CTkButton(
            buttons,
            text="Save Note" if is_editing else "Add Note",
            width=96,
            height=34,
            command=save_dialog_note,
        ).pack(side="right")
        ctk.CTkButton(
            buttons,
            text="Close",
            width=76,
            command=window.destroy,
            **secondary,
        ).pack(side="right", padx=(0, 8))

    def setup_clickable_note_checkboxes(self, textbox, note):
        try:
            real_textbox = textbox._textbox
            real_textbox.tag_configure(
                "checked_line",
                background="#2f3f52",
                foreground="#d6eaff",
            )
            real_textbox.bind(
                "<Button-1>",
                lambda event: self.toggle_view_note_checkbox(event, textbox, note),
            )
            real_textbox.bind(
                "<Motion>",
                lambda event: self.update_checkbox_cursor(event, textbox),
            )
            self.refresh_checkbox_highlights(textbox)
        except Exception as error:
            print(f"Clickable checkbox setup failed: {error}")

    def toggle_view_note_checkbox(self, event, textbox, note):
        try:
            index = textbox.index(f"@{event.x},{event.y}")
            clicked_char = textbox.get(index, f"{index}+1c")
            if clicked_char not in ["☐", "☑"]:
                return None

            new_char = "☑" if clicked_char == "☐" else "☐"
            textbox.configure(state="normal")
            textbox.delete(index, f"{index}+1c")
            textbox.insert(index, new_char)
            new_notes = textbox.get("1.0", "end").strip()
            self.pm.db.update_fact_note(
                note["id"],
                note["title"],
                note["category"] if "category" in note.keys() else "",
                new_notes,
                note["status"],
            )
            self.refresh_checkbox_highlights(textbox)
            textbox.configure(state="disabled")
            self.load_notes()
            return "break"
        except Exception as error:
            print(f"View checkbox toggle failed: {error}")
            try:
                textbox.configure(state="disabled")
            except Exception:
                pass
            return None

    def open_missing_title_dialog(self):
        dialog = ctk.CTkToplevel(self)
        dialog.title("Missing Title")
        dialog.geometry("420x190")
        dialog.transient(self)
        dialog.grab_set()
        dialog.lift()
        dialog.focus_force()
        ctk.CTkLabel(
            dialog,
            text="Missing title",
            font=("Segoe UI", 20, "bold"),
        ).pack(anchor="w", padx=24, pady=(24, 7))
        ctk.CTkLabel(
            dialog,
            text="Please enter a fact idea title before saving this note.",
            font=("Segoe UI", 12),
            wraplength=360,
            justify="left",
            text_color=("#667085", "#8F96A3"),
        ).pack(anchor="w", padx=24)
        ctk.CTkButton(
            dialog,
            text="OK",
            width=80,
            height=34,
            command=dialog.destroy,
        ).pack(anchor="e", padx=24, pady=24)

    def format_textbox_table(self, textbox):
        text = textbox.get("1.0", "end").strip()
        if not text:
            return

        lines = [line.strip() for line in text.splitlines() if line.strip()]
        rows = []
        for line in lines:
            if not line.startswith("|") or "---" in line:
                continue
            parts = [part.strip() for part in line.strip("|").split("|")]
            if len(parts) >= 2:
                rows.append([
                    parts[0].replace("**", "").strip(),
                    parts[1].replace("**", "").strip(),
                ])

        if not rows:
            return

        fact_width = max(len(row[0]) for row in rows)
        total_width = max(len(row[1]) for row in rows)
        formatted = []
        for index, row in enumerate(rows):
            fact, total = row
            if index == 0:
                formatted.append(f"{fact.ljust(fact_width)}   {total.rjust(total_width)}")
                formatted.append(f"{'-' * fact_width}   {'-' * total_width}")
            else:
                formatted.append(f"{fact.ljust(fact_width)}   {total.rjust(total_width)}")

        textbox.delete("1.0", "end")
        textbox.insert("1.0", "\n".join(formatted))

    def insert_checkbox(self, textbox):
        textbox.insert("insert", "☐ ")
        textbox.focus_set()

    def toggle_checkbox_line(self, textbox):
        try:
            line_start = textbox.index("insert linestart")
            line_end = textbox.index("insert lineend")
            line_text = textbox.get(line_start, line_end)
            if "☐" in line_text:
                line_text = line_text.replace("☐", "☑", 1)
            elif "☑" in line_text:
                line_text = line_text.replace("☑", "☐", 1)
            else:
                line_text = "☐ " + line_text
            textbox.delete(line_start, line_end)
            textbox.insert(line_start, line_text)
            self.refresh_checkbox_highlights(textbox)
            textbox.focus_set()
        except Exception as error:
            print(f"Checkbox toggle failed: {error}")

    def setup_checkbox_textbox(self, textbox):
        try:
            real_textbox = textbox._textbox
            real_textbox.tag_configure("checked_line", background="#2f4f2f")
            real_textbox.bind(
                "<Button-1>",
                lambda event: self.handle_checkbox_click(event, textbox),
            )
            self.refresh_checkbox_highlights(textbox)
        except Exception as error:
            print(f"Checkbox setup failed: {error}")

    def handle_checkbox_click(self, event, textbox):
        try:
            index = textbox.index(f"@{event.x},{event.y}")
            clicked_char = textbox.get(index, f"{index}+1c")
            if clicked_char not in ["☐", "☑"]:
                return None
            new_char = "☑" if clicked_char == "☐" else "☐"
            textbox.delete(index, f"{index}+1c")
            textbox.insert(index, new_char)
            self.refresh_checkbox_highlights(textbox)
            return "break"
        except Exception as error:
            print(f"Checkbox click failed: {error}")
            return None

    def refresh_checkbox_highlights(self, textbox):
        try:
            real_textbox = textbox._textbox
            real_textbox.tag_remove("checked_line", "1.0", "end")
            line_count = int(textbox.index("end-1c").split(".")[0])
            for line_number in range(1, line_count + 1):
                line_start = f"{line_number}.0"
                line_end = f"{line_number}.end"
                if "☑" in textbox.get(line_start, line_end):
                    real_textbox.tag_add("checked_line", line_start, line_end)
        except Exception as error:
            print(f"Checkbox highlight failed: {error}")

    def update_checkbox_cursor(self, event, textbox):
        try:
            index = textbox.index(f"@{event.x},{event.y}")
            hovered_char = textbox.get(index, f"{index}+1c")
            textbox._textbox.configure(cursor="hand2" if hovered_char in ["☐", "☑"] else "xterm")
        except Exception as error:
            print(f"Checkbox cursor update failed: {error}")

    def setup_checkbox_viewer(self, textbox):
        try:
            real_textbox = textbox._textbox
            real_textbox.tag_configure(
                "checked_line",
                background="#2A3A2E",
                foreground="#D7F5DF",
            )
            self.refresh_checkbox_highlights(textbox)
        except Exception as error:
            print(f"Checkbox viewer setup failed: {error}")
