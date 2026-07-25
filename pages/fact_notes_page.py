from tkinter import messagebox
from datetime import datetime
import customtkinter as ctk

from pages.base_page import BasePage
from common.ui_fonts import (
    EMOJI_FONT,
    EMOJI_FONT_BOLD,
    EMOJI_TITLE_FONT,
    EMOJI_BUTTON_FONT,
    TABLE_FONT
)

class FactNotesPage(BasePage):

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Fact Notes")

        self.app = app
        self.editing_note_id = None

        self.build()

    def build(self):

        # ======================================
        # Main list-only layout
        # ======================================

        main = ctk.CTkFrame(
            self.content,
            corner_radius=12
        )

        main.pack(
            fill="both",
            expand=True,
            padx=10,
            pady=10
        )

        # ======================================
        # Header
        # ======================================

        header = ctk.CTkFrame(
            main,
            fg_color="transparent"
        )

        header.pack(
            fill="x",
            padx=20,
            pady=(20, 10)
        )

        ctk.CTkLabel(
            header,
            text="Saved Fact Ideas",
            font=EMOJI_TITLE_FONT
        ).pack(
            side="left"
        )

        ctk.CTkButton(
            header,
            text="➕ Add Note",
            width=130,
            height=38,
            font=EMOJI_BUTTON_FONT,
            command=self.open_note_editor_dialog
        ).pack(
            side="right",
            padx=(10, 0)
        )

        ctk.CTkButton(
            header,
            text="Refresh",
            width=100,
            height=38,
            command=self.load_notes
        ).pack(
            side="right"
        )

        # ======================================
        # Notes list
        # ======================================

        self.notes_list = ctk.CTkScrollableFrame(
            main,
            fg_color="transparent"
        )

        self.notes_list.pack(
            fill="both",
            expand=True,
            padx=15,
            pady=(0, 15)
        )

        self.load_notes()
        

    # ======================================
    # Load Notes
    # ======================================

    def load_notes(self):

        for widget in self.notes_list.winfo_children():
            widget.destroy()

        notes = self.pm.db.get_fact_notes()

        if not notes:

            ctk.CTkLabel(
                self.notes_list,
                text="No fact notes yet. Add ideas above.",
                text_color="gray"
            ).pack(
                pady=30
            )

            return

        for note in notes:

            self.create_note_card(note)

    # ======================================
    # Note Card
    # ======================================

    def create_note_card(self, note):

        card = ctk.CTkFrame(
            self.notes_list,
            corner_radius=10,
            border_width=1
        )

        card.pack(
            fill="x",
            padx=5,
            pady=7
        )

        is_pinned = self.is_note_pinned(note)
        icon = "📌" if is_pinned else "📝"

        # Header row

        header = ctk.CTkFrame(
            card,
            fg_color="transparent"
        )

        header.pack(
            fill="x",
            padx=15,
            pady=(12, 4)
        )

        title_text = note["title"]

        ctk.CTkLabel(
            header,
            text=f"{icon} {title_text}",
            font=EMOJI_FONT_BOLD,
            anchor="w"
        ).pack(
            side="left",
            fill="x",
            expand=True
        )

        if is_pinned:

            ctk.CTkLabel(
                header,
                text="Pinned",
                text_color="gray",
                font=EMOJI_FONT
            ).pack(
                side="right",
                padx=(10, 0)
            )

        # Meta row

        meta_text = (
            f"📌 {note['status']}    "
            f"📅 {note['created']}"
        )

        ctk.CTkLabel(
            card,
            text=meta_text,
            font=EMOJI_FONT,
            text_color="gray",
            anchor="w"
        ).pack(
            fill="x",
            padx=15,
            pady=(0, 8)
        )

        # Short preview

        preview = note["notes"].strip()

        if len(preview) > 80:
            preview = preview[:80].rstrip() + "...\nOpen to view more"

        if not preview:
            preview = "No notes added yet."

        ctk.CTkLabel(
            card,
            text=preview,
            font=EMOJI_FONT,
            wraplength=650,
            justify="left",
            anchor="w",
            text_color="gray"
        ).pack(
            fill="x",
            padx=15,
            pady=(0, 12)
        )

        # Buttons

        buttons = ctk.CTkFrame(
            card,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=15,
            pady=(0, 12)
        )

        ctk.CTkButton(
            buttons,
            text="🔎 Open",
            width=95,
            font=EMOJI_BUTTON_FONT,
            command=lambda n=note: self.open_note_window(n)
        ).pack(
            side="left",
            padx=(0, 5)
        )

        pin_text = "📌 Unpin" if is_pinned else "📌 Pin"

        ctk.CTkButton(
            buttons,
            text=pin_text,
            width=95,
            font=EMOJI_BUTTON_FONT,
            command=lambda n=note: self.toggle_pin(n)
        ).pack(
            side="left",
            padx=5
        )

        ctk.CTkButton(
            buttons,
            text="✏ Edit",
            width=95,
            font=EMOJI_BUTTON_FONT,
            command=lambda n=note: self.open_note_editor_dialog(n)
        ).pack(
            side="left",
            padx=5
        )

        ctk.CTkButton(
            buttons,
            text="🗑 Delete",
            width=95,
            fg_color="#B22222",
            hover_color="#8B0000",
            font=EMOJI_BUTTON_FONT,
            command=lambda n=note: self.open_delete_dialog(n)
        ).pack(
            side="right"
        )

    # ======================================
    # Large Open Window
    # ======================================

    def open_note_window(self, note):

        window = ctk.CTkToplevel(self)
        window.title(note["title"])
        window.geometry("900x700")
        window.transient(self)
        window.grab_set()

        window.lift()
        window.focus_force()

        header = ctk.CTkFrame(
            window,
            fg_color="transparent"
        )

        header.pack(
            fill="x",
            padx=20,
            pady=(20, 10)
        )

        is_pinned = self.is_note_pinned(note)
        icon = "📌" if is_pinned else "📝"

        ctk.CTkLabel(
            header,
            text=f"{icon} {note['title']}",
            font=("Segoe UI", 28, "bold")
        ).pack(
            anchor="w"
        )

        info = ctk.CTkFrame(
            window
        )

        info.pack(
            fill="x",
            padx=20,
            pady=(0, 15)
        )

        ctk.CTkLabel(
            info,
            text=f"📌 Status: {note['status']}",
            font=EMOJI_FONT
        ).pack(
            anchor="w",
            padx=15,
            pady=(12, 2)
        )

        ctk.CTkLabel(
            info,
            text=f"📅 Created: {note['created']}",
            font=EMOJI_FONT
        ).pack(
            anchor="w",
            padx=15,
            pady=(2, 12)
        )

        notes_box = ctk.CTkTextbox(
            window,
            font=TABLE_FONT,
            wrap="none"
        )

        notes_box.pack(
            fill="both",
            expand=True,
            padx=20,
            pady=(0, 15)
        )

        notes_box.insert(
            "1.0",
            note["notes"]
        )

        self.setup_clickable_note_checkboxes(
            notes_box,
            note
        )

        notes_box.configure(
            state="disabled"
        )
        
        self.setup_checkbox_viewer(
            notes_box
        )

        buttons = ctk.CTkFrame(
            window,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=20,
            pady=(0, 20)
        )

        ctk.CTkButton(
            buttons,
            text="✏ Edit This Note",
            width=150,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.open_window_edit(note, window)
        ).pack(
            side="left",
            padx=5
        )

        ctk.CTkButton(
            buttons,
            text="Close",
            width=120,
            command=window.destroy
        ).pack(
            side="right",
            padx=5
        )

    def open_window_edit(self, note, window):

        window.destroy()

        self.open_note_editor_dialog(note)

    # ======================================
    # Actions
    # ======================================

    def toggle_pin(self, note):

        self.pm.db.toggle_fact_note_pinned(
            note["id"]
        )

        self.load_notes()

    def open_delete_dialog(self, note):

        dialog = ctk.CTkToplevel(self)
        dialog.title("Delete Note")
        dialog.geometry("420x230")
        dialog.transient(self)
        dialog.grab_set()

        dialog.lift()
        dialog.focus_force()

        ctk.CTkLabel(
            dialog,
            text="Delete this note?",
            font=("Segoe UI", 24, "bold")
        ).pack(
            anchor="w",
            padx=25,
            pady=(25, 10)
        )

        ctk.CTkLabel(
            dialog,
            text=note["title"],
            font=EMOJI_FONT,
            wraplength=360,
            justify="left",
            text_color="gray"
        ).pack(
            anchor="w",
            padx=25,
            pady=(0, 15)
        )

        ctk.CTkLabel(
            dialog,
            text="This cannot be undone.",
            font=EMOJI_FONT,
            text_color="gray"
        ).pack(
            anchor="w",
            padx=25,
            pady=(0, 20)
        )

        buttons = ctk.CTkFrame(
            dialog,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=25,
            pady=(0, 25)
        )

        ctk.CTkButton(
            buttons,
            text="Cancel",
            height=38,
            command=dialog.destroy
        ).pack(
            side="right",
            padx=(8, 0)
        )

        ctk.CTkButton(
            buttons,
            text="🗑 Delete",
            height=38,
            fg_color="#B22222",
            hover_color="#8B0000",
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.confirm_delete_note(note, dialog)
        ).pack(
            side="right"
        )

    def confirm_delete_note(self, note, dialog):

        self.pm.db.delete_fact_note(
            note["id"]
        )

        dialog.destroy()

        self.load_notes()

    # ======================================
    # Helpers
    # ======================================

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

        self.pm.db.toggle_fact_note_checked(
            note["id"]
        )

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
            text="✏ Edit Fact Note" if is_editing else "➕ Add Fact Note",
            font=EMOJI_TITLE_FONT
        ).pack(
            anchor="w",
            padx=25,
            pady=(25, 15)
        )

        title_entry = ctk.CTkEntry(
            window,
            placeholder_text="Fact idea title...",
            font=EMOJI_FONT
        )

        title_entry.pack(
            fill="x",
            padx=25,
            pady=(0, 12)
        )

        if is_editing:

            title_entry.insert(
                0,
                note["title"]
            )

        status = ctk.CTkOptionMenu(
            window,
            values=[
                "Idea",
                "Researching"
            ]
        )

        status.pack(
            fill="x",
            padx=25,
            pady=(0, 15)
        )

        if is_editing:
            status.set(note["status"])
        else:
            status.set("Idea")

        notes_box = ctk.CTkTextbox(
            window,
            font=TABLE_FONT,
            wrap="none"
        )

        notes_box.pack(
            fill="both",
            expand=True,
            padx=25,
            pady=(0, 15)
        )

        if is_editing:

            notes_box.insert(
                "1.0",
                note["notes"]
            )
        self.setup_checkbox_textbox(notes_box)

        buttons = ctk.CTkFrame(
            window,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=25,
            pady=(0, 25)
        )

        ctk.CTkButton(
            buttons,
            text="↔ Format Table",
            height=38,
            width=130,
            command=lambda: self.format_textbox_table(notes_box)
        ).pack(
            side="left",
            padx=(0, 8)
        )

        ctk.CTkButton(
            buttons,
            text="☐",
            height=38,
            width=45,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.insert_checkbox(notes_box)
        ).pack(
            side="left",
            padx=(0, 5)
        )

        ctk.CTkButton(
            buttons,
            text="☑",
            height=38,
            width=45,
            font=EMOJI_BUTTON_FONT,
            command=lambda: self.toggle_checkbox_line(notes_box)
        ).pack(
            side="left",
            padx=(0, 8)
        )

        def save_dialog_note():

            title = title_entry.get().strip()
            notes = notes_box.get("1.0", "end").strip()

            if not title:

                self.open_missing_title_dialog()

                return

            if is_editing:

                self.pm.db.update_fact_note(
                    note["id"],
                    title,
                    "",
                    notes,
                    status.get()
                )

            else:

                created = datetime.now().strftime("%Y-%m-%d %H:%M")

                self.pm.db.add_fact_note(
                    title,
                    "",
                    notes,
                    status.get(),
                    created
                )

            #window.destroy()

            self.load_notes()

        ctk.CTkButton(
            buttons,
            text="💾 Save Note" if is_editing else "➕ Add Note",
            height=38,
            font=EMOJI_BUTTON_FONT,
            command=save_dialog_note
        ).pack(
            side="left",
            padx=(0, 8)
        )

        ctk.CTkButton(
            buttons,
            text="Close",
            height=38,
            command=window.destroy
        ).pack(
            side="right"
        )

    def setup_clickable_note_checkboxes(self, textbox, note):

        try:

            real_textbox = textbox._textbox

            real_textbox.tag_configure(
                "checked_line",
                background="#2f3f52",
                foreground="#d6eaff"
            )

            real_textbox.bind(
                "<Button-1>",
                lambda event: self.toggle_view_note_checkbox(
                    event,
                    textbox,
                    note
                )
            )

            real_textbox.bind(
                "<Motion>",
                lambda event: self.update_checkbox_cursor(
                    event,
                    textbox
                )
            )

            self.refresh_checkbox_highlights(
                textbox
            )

        except Exception as e:

            print(
                f"Clickable checkbox setup failed: {e}"
            )

    def toggle_view_note_checkbox(self, event, textbox, note):

        try:

            index = textbox.index(
                f"@{event.x},{event.y}"
            )

            clicked_char = textbox.get(
                index,
                f"{index}+1c"
            )

            if clicked_char not in [
                "☐",
                "☑"
            ]:

                return None

            new_char = "☑" if clicked_char == "☐" else "☐"

            textbox.configure(
                state="normal"
            )

            textbox.delete(
                index,
                f"{index}+1c"
            )

            textbox.insert(
                index,
                new_char
            )

            new_notes = textbox.get(
                "1.0",
                "end"
            ).strip()

            self.pm.db.update_fact_note(
                note["id"],
                note["title"],
                note["category"] if "category" in note.keys() else "",
                new_notes,
                note["status"]
            )

            self.refresh_checkbox_highlights(
                textbox
            )

            textbox.configure(
                state="disabled"
            )

            self.load_notes()

            return "break"

        except Exception as e:

            print(
                f"View checkbox toggle failed: {e}"
            )

            try:

                textbox.configure(
                    state="disabled"
                )

            except Exception:

                pass

            return None

    def open_missing_title_dialog(self):

        dialog = ctk.CTkToplevel(self)
        dialog.title("Missing Title")
        dialog.geometry("420x200")
        dialog.transient(self)
        dialog.grab_set()

        dialog.lift()
        dialog.focus_force()

        ctk.CTkLabel(
            dialog,
            text="Missing title",
            font=("Segoe UI", 24, "bold")
        ).pack(
            anchor="w",
            padx=25,
            pady=(25, 10)
        )

        ctk.CTkLabel(
            dialog,
            text="Please enter a fact idea title before saving this note.",
            font=EMOJI_FONT,
            wraplength=360,
            justify="left",
            text_color="gray"
        ).pack(
            anchor="w",
            padx=25,
            pady=(0, 20)
        )

        buttons = ctk.CTkFrame(
            dialog,
            fg_color="transparent"
        )

        buttons.pack(
            fill="x",
            padx=25,
            pady=(0, 25)
        )

        ctk.CTkButton(
            buttons,
            text="OK",
            height=38,
            width=100,
            command=dialog.destroy
        ).pack(
            side="right"
        )
        
    def format_textbox_table(self, textbox):

        text = textbox.get(
            "1.0",
            "end"
        ).strip()

        if not text:
            return

        lines = [
            line.strip()
            for line in text.splitlines()
            if line.strip()
        ]

        rows = []

        for line in lines:

            if not line.startswith("|"):
                continue

            if "---" in line:
                continue

            parts = [
                part.strip()
                for part in line.strip("|").split("|")
            ]

            if len(parts) >= 2:

                fact = parts[0].replace("**", "").strip()
                total = parts[1].replace("**", "").strip()

                rows.append(
                    [
                        fact,
                        total
                    ]
                )

        if not rows:
            return

        fact_width = max(
            len(row[0])
            for row in rows
        )

        total_width = max(
            len(row[1])
            for row in rows
        )

        formatted = []

        for index, row in enumerate(rows):

            fact = row[0]
            total = row[1]

            if index == 0:

                formatted.append(
                    f"{fact.ljust(fact_width)}   {total.rjust(total_width)}"
                )

                formatted.append(
                    f"{'-' * fact_width}   {'-' * total_width}"
                )

            else:

                formatted.append(
                    f"{fact.ljust(fact_width)}   {total.rjust(total_width)}"
                )

        textbox.delete(
            "1.0",
            "end"
        )

        textbox.insert(
            "1.0",
            "\n".join(formatted)
        )

    def insert_checkbox(self, textbox):

        textbox.insert(
            "insert",
            "☐ "
        )

        textbox.focus_set()

    def toggle_checkbox_line(self, textbox):

        try:

            line_start = textbox.index(
                "insert linestart"
            )

            line_end = textbox.index(
                "insert lineend"
            )

            line_text = textbox.get(
                line_start,
                line_end
            )

            if "☐" in line_text:

                line_text = line_text.replace(
                    "☐",
                    "☑",
                    1
                )

            elif "☑" in line_text:

                line_text = line_text.replace(
                    "☑",
                    "☐",
                    1
                )

            else:

                line_text = "☐ " + line_text

            textbox.delete(
                line_start,
                line_end
            )

            textbox.insert(
                line_start,
                line_text
            )

            self.refresh_checkbox_highlights(textbox)

            textbox.focus_set()

        except Exception as e:

            print(
                f"Checkbox toggle failed: {e}"
            )

    def setup_checkbox_textbox(self, textbox):

        try:

            real_textbox = textbox._textbox

            real_textbox.tag_configure(
                "checked_line",
                background="#2f4f2f"
            )

            real_textbox.bind(
                "<Button-1>",
                lambda event: self.handle_checkbox_click(
                    event,
                    textbox
                )
            )

            self.refresh_checkbox_highlights(textbox)

        except Exception as e:

            print(
                f"Checkbox setup failed: {e}"
            )

    def handle_checkbox_click(self, event, textbox):

        try:

            index = textbox.index(
                f"@{event.x},{event.y}"
            )

            clicked_char = textbox.get(
                index,
                f"{index}+1c"
            )

            if clicked_char not in [
                "☐",
                "☑"
            ]:

                return None

            new_char = "☑" if clicked_char == "☐" else "☐"

            textbox.delete(
                index,
                f"{index}+1c"
            )

            textbox.insert(
                index,
                new_char
            )

            self.refresh_checkbox_highlights(textbox)

            return "break"

        except Exception as e:

            print(
                f"Checkbox click failed: {e}"
            )

            return None

    def refresh_checkbox_highlights(self, textbox):

        try:

            real_textbox = textbox._textbox

            real_textbox.tag_remove(
                "checked_line",
                "1.0",
                "end"
            )

            line_count = int(
                textbox.index("end-1c").split(".")[0]
            )

            for line_number in range(1, line_count + 1):

                line_start = f"{line_number}.0"
                line_end = f"{line_number}.end"

                line_text = textbox.get(
                    line_start,
                    line_end
                )

                if "☑" in line_text:

                    real_textbox.tag_add(
                        "checked_line",
                        line_start,
                        line_end
                    )

        except Exception as e:

            print(
                f"Checkbox highlight failed: {e}"
            )

    def update_checkbox_cursor(self, event, textbox):

        try:

            index = textbox.index(
                f"@{event.x},{event.y}"
            )

            hovered_char = textbox.get(
                index,
                f"{index}+1c"
            )

            real_textbox = textbox._textbox

            if hovered_char in [
                "☐",
                "☑"
            ]:

                real_textbox.configure(
                    cursor="hand2"
                )

            else:

                real_textbox.configure(
                    cursor="xterm"
                )

        except Exception as e:

            print(
                f"Checkbox cursor update failed: {e}"
            )
           
    def setup_checkbox_viewer(self, textbox):

        try:

            real_textbox = textbox._textbox

            real_textbox.tag_configure(
                "checked_line",
                background="#66FF00",
                foreground="#ff0000"
            )

            self.refresh_checkbox_highlights(
                textbox
            )

        except Exception as e:

            print(
                f"Checkbox viewer setup failed: {e}"
            )