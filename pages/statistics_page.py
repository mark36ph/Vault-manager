from collections import Counter
from datetime import datetime

import customtkinter as ctk

from pages.base_page import BasePage


MUTED_TEXT = ("#667085", "#8F96A3")
CARD_BG = ("#FFFFFF", "#181B21")
CARD_BORDER = ("#E4E7EC", "#2A2F38")
WARNING_TEXT = ("#B54708", "#FEC84B")


class StatisticsPage(BasePage):
    """Project library and publishing statistics dashboard."""

    STATUSES = ("In Progress", "Scheduled", "Completed", "Published")

    def __init__(self, parent, pm, app):
        super().__init__(parent, pm, "Statistics")
        self.app = app
        self.body = None
        self._keyboard_bindings = []

        self.header.configure(font=("Segoe UI", 24, "bold"))
        self.header.pack_configure(padx=24, pady=(20, 4))

        self.subtitle = ctk.CTkLabel(
            self,
            text="Project volume, workflow status, categories, and recent activity.",
            font=("Segoe UI", 13),
            text_color=MUTED_TEXT,
            anchor="w",
        )
        self.subtitle.pack(fill="x", padx=24, pady=(0, 14), before=self.content)
        self.content.pack_configure(padx=24, pady=(0, 20))

        self.build()
        self._install_keyboard_scrolling()

    def build(self):
        projects = [dict(project) for project in self.pm.get_all_projects()]
        total = len(projects)
        counts = Counter(str(project.get("status") or "") for project in projects)

        in_progress = counts["In Progress"]
        scheduled = counts["Scheduled"]
        completed = counts["Completed"]
        published = counts["Published"]
        active = in_progress + scheduled
        finished = completed + published
        completion_ratio = finished / total if total else 0

        now = datetime.now()
        created_this_month = sum(
            1 for project in projects if self._is_same_month(project.get("created"), now)
        )
        completed_this_month = sum(
            1
            for project in projects
            if str(project.get("status") or "") == "Completed"
            and self._is_same_month(project.get("updated") or project.get("created"), now)
        )
        published_this_month = sum(
            1
            for project in projects
            if str(project.get("status") or "") == "Published"
            and self._is_same_month(project.get("updated") or project.get("created"), now)
        )
        overdue_scheduled = sum(
            1 for project in projects if self._is_overdue_scheduled(project, now)
        )

        body = ctk.CTkScrollableFrame(
            self.content,
            fg_color="transparent",
            scrollbar_button_color=("#D0D5DD", "#3A404B"),
            scrollbar_button_hover_color=("#98A2B3", "#596170"),
        )
        body.pack(fill="both", expand=True)
        self.body = body

        actions = ctk.CTkFrame(body, fg_color="transparent")
        actions.pack(fill="x", pady=(0, 8))
        ctk.CTkButton(
            actions,
            text="Refresh",
            width=82,
            height=32,
            corner_radius=7,
            fg_color="transparent",
            border_width=1,
            border_color=("#D0D5DD", "#3A404B"),
            text_color=("#344054", "#D0D5DD"),
            hover_color=("#F2F4F7", "#252A33"),
            command=self.refresh,
        ).pack(side="right")

        overview = ctk.CTkFrame(body, fg_color="transparent")
        overview.pack(fill="x")
        for column in range(4):
            overview.grid_columnconfigure(column, weight=1)

        overview_stats = [
            ("Total projects", total, "All projects in the library"),
            ("Active", active, "In progress or scheduled"),
            ("Finished", finished, "Completed or published"),
            ("Completion rate", f"{completion_ratio * 100:.0f}%", f"{finished} of {total} finished"),
        ]

        for column, (title, value, detail) in enumerate(overview_stats):
            self._metric_card(overview, column, title, value, detail)

        month_card = self._card(body)
        month_card.pack(fill="x", pady=(0, 10))
        self._section_heading(
            month_card,
            "This month",
            now.strftime("Activity for %B %Y."),
        )
        month_grid = ctk.CTkFrame(month_card, fg_color="transparent")
        month_grid.pack(fill="x", padx=16, pady=(0, 14))
        month_grid.grid_columnconfigure((0, 1, 2, 3), weight=1)
        self._mini_stat(month_grid, 0, "Created", created_this_month)
        self._mini_stat(month_grid, 1, "Completed", completed_this_month)
        self._mini_stat(month_grid, 2, "Published", published_this_month)
        self._mini_stat(month_grid, 3, "Overdue scheduled", overdue_scheduled)

        if overdue_scheduled:
            ctk.CTkLabel(
                month_card,
                text=f"{overdue_scheduled} scheduled project(s) have a publish time in the past.",
                font=("Segoe UI", 11, "bold"),
                text_color=WARNING_TEXT,
                anchor="w",
            ).pack(fill="x", padx=16, pady=(0, 14))

        main = ctk.CTkFrame(body, fg_color="transparent")
        main.pack(fill="x", pady=(2, 10))
        main.grid_columnconfigure(0, weight=3)
        main.grid_columnconfigure(1, weight=2)

        status_card = self._card(main)
        status_card.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self._section_heading(
            status_card,
            "Status distribution",
            "Where projects currently sit in the workflow.",
        )

        for status in self.STATUSES:
            count = counts[status]
            ratio = count / total if total else 0
            row = ctk.CTkFrame(status_card, fg_color="transparent")
            row.pack(fill="x", padx=16, pady=(2, 9))

            top = ctk.CTkFrame(row, fg_color="transparent")
            top.pack(fill="x")
            ctk.CTkLabel(
                top,
                text=status,
                font=("Segoe UI", 12, "bold"),
                anchor="w",
            ).pack(side="left")
            ctk.CTkLabel(
                top,
                text=f"{count}  •  {ratio * 100:.0f}%",
                font=("Segoe UI", 11),
                text_color=MUTED_TEXT,
            ).pack(side="right")

            bar = ctk.CTkProgressBar(row, height=7)
            bar.set(ratio)
            bar.pack(fill="x", pady=(5, 0))

        completion_card = self._card(main)
        completion_card.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self._section_heading(
            completion_card,
            "Publishing progress",
            "Finished projects compared with the full library.",
        )

        ctk.CTkLabel(
            completion_card,
            text=f"{completion_ratio * 100:.0f}%",
            font=("Segoe UI", 34, "bold"),
        ).pack(anchor="w", padx=16, pady=(3, 0))
        ctk.CTkLabel(
            completion_card,
            text=f"{finished} of {total} projects are completed or published.",
            font=("Segoe UI", 12),
            text_color=MUTED_TEXT,
            anchor="w",
        ).pack(fill="x", padx=16, pady=(2, 10))

        progress = ctk.CTkProgressBar(completion_card, height=9)
        progress.set(completion_ratio)
        progress.pack(fill="x", padx=16, pady=(0, 14))

        snapshot = ctk.CTkFrame(completion_card, fg_color="transparent")
        snapshot.pack(fill="x", padx=16, pady=(0, 14))
        snapshot.grid_columnconfigure((0, 1), weight=1)
        self._mini_stat(snapshot, 0, "Scheduled", scheduled)
        self._mini_stat(snapshot, 1, "Published", published)

        lower = ctk.CTkFrame(body, fg_color="transparent")
        lower.pack(fill="x", pady=(0, 10))
        lower.grid_columnconfigure(0, weight=1)
        lower.grid_columnconfigure(1, weight=1)

        categories_card = self._card(lower)
        categories_card.grid(row=0, column=0, sticky="nsew", padx=(0, 5))
        self._section_heading(
            categories_card,
            "Category mix",
            "Most-used project categories in the library.",
        )
        self._build_categories(categories_card, projects, total)

        schedule_card = self._card(lower)
        schedule_card.grid(row=0, column=1, sticky="nsew", padx=(5, 0))
        self._section_heading(
            schedule_card,
            "Upcoming schedule",
            "The next projects currently waiting to publish.",
        )
        self._build_schedule(schedule_card, projects, now)

        recent_card = self._card(body)
        recent_card.pack(fill="x", pady=(0, 4))
        self._section_heading(
            recent_card,
            "Recent activity",
            "Most recently created or updated projects.",
        )
        self._build_recent(recent_card, projects)

    def refresh(self):
        if self.body is not None:
            self.body.destroy()
            self.body = None
        self.build()

    def _install_keyboard_scrolling(self):
        """Allow the active Statistics page to scroll with the keyboard."""
        toplevel = self.winfo_toplevel()
        bindings = (
            ("<Up>", lambda _event: self._scroll_units(-3)),
            ("<Down>", lambda _event: self._scroll_units(3)),
            ("<Prior>", lambda _event: self._scroll_pages(-1)),
            ("<Next>", lambda _event: self._scroll_pages(1)),
            ("<Home>", lambda _event: self._scroll_to(0.0)),
            ("<End>", lambda _event: self._scroll_to(1.0)),
        )
        for sequence, callback in bindings:
            funcid = toplevel.bind(sequence, callback, add="+")
            if funcid:
                self._keyboard_bindings.append((sequence, funcid))

        self.bind("<Destroy>", self._remove_keyboard_scrolling, add="+")

    def _scroll_units(self, amount):
        canvas = getattr(self.body, "_parent_canvas", None)
        if canvas is not None:
            canvas.yview_scroll(amount, "units")
        return "break"

    def _scroll_pages(self, amount):
        canvas = getattr(self.body, "_parent_canvas", None)
        if canvas is not None:
            canvas.yview_scroll(amount, "pages")
        return "break"

    def _scroll_to(self, position):
        canvas = getattr(self.body, "_parent_canvas", None)
        if canvas is not None:
            canvas.yview_moveto(position)
        return "break"

    def _remove_keyboard_scrolling(self, event=None):
        if event is not None and event.widget is not self:
            return
        if not self._keyboard_bindings:
            return
        toplevel = self.winfo_toplevel()
        for sequence, funcid in self._keyboard_bindings:
            try:
                toplevel.unbind(sequence, funcid)
            except Exception:
                pass
        self._keyboard_bindings.clear()

    def _metric_card(self, parent, column, title, value, detail):
        card = self._card(parent)
        card.grid(
            row=0,
            column=column,
            sticky="nsew",
            padx=(0 if column == 0 else 5, 0 if column == 3 else 5),
            pady=(0, 10),
        )
        ctk.CTkLabel(
            card,
            text=str(value),
            font=("Segoe UI", 28, "bold"),
        ).pack(anchor="w", padx=16, pady=(14, 1))
        ctk.CTkLabel(
            card,
            text=title,
            font=("Segoe UI", 12, "bold"),
        ).pack(anchor="w", padx=16)
        ctk.CTkLabel(
            card,
            text=detail,
            font=("Segoe UI", 10),
            text_color=MUTED_TEXT,
            anchor="w",
        ).pack(fill="x", padx=16, pady=(2, 14))

    def _card(self, parent):
        return ctk.CTkFrame(
            parent,
            corner_radius=8,
            fg_color=CARD_BG,
            border_width=1,
            border_color=CARD_BORDER,
        )

    def _section_heading(self, parent, title, subtitle):
        ctk.CTkLabel(
            parent,
            text=title,
            font=("Segoe UI", 15, "bold"),
            anchor="w",
        ).pack(fill="x", padx=16, pady=(14, 2))
        ctk.CTkLabel(
            parent,
            text=subtitle,
            font=("Segoe UI", 11),
            text_color=MUTED_TEXT,
            anchor="w",
        ).pack(fill="x", padx=16, pady=(0, 10))

    def _mini_stat(self, parent, column, title, value):
        frame = ctk.CTkFrame(parent, fg_color="transparent")
        frame.grid(row=0, column=column, sticky="ew")
        ctk.CTkLabel(
            frame,
            text=str(value),
            font=("Segoe UI", 18, "bold"),
        ).pack(anchor="w")
        ctk.CTkLabel(
            frame,
            text=title,
            font=("Segoe UI", 10),
            text_color=MUTED_TEXT,
        ).pack(anchor="w")

    def _build_categories(self, parent, projects, total):
        category_counts = Counter(
            str(project.get("category") or "Uncategorised") for project in projects
        )
        ranked = category_counts.most_common(5)

        if not ranked:
            self._empty(parent, "No category data yet.")
            return

        for category, count in ranked:
            ratio = count / total if total else 0
            row = ctk.CTkFrame(parent, fg_color="transparent")
            row.pack(fill="x", padx=16, pady=(1, 8))

            line = ctk.CTkFrame(row, fg_color="transparent")
            line.pack(fill="x")
            ctk.CTkLabel(
                line,
                text=category,
                font=("Segoe UI", 11, "bold"),
                anchor="w",
            ).pack(side="left")
            ctk.CTkLabel(
                line,
                text=f"{count}  •  {ratio * 100:.0f}%",
                font=("Segoe UI", 10),
                text_color=MUTED_TEXT,
            ).pack(side="right")

            bar = ctk.CTkProgressBar(row, height=6)
            bar.set(ratio)
            bar.pack(fill="x", pady=(4, 0))

        ctk.CTkFrame(parent, height=5, fg_color="transparent").pack()

    def _build_schedule(self, parent, projects, now):
        scheduled = [
            project
            for project in projects
            if str(project.get("status") or "") == "Scheduled"
            and str(project.get("scheduled_for") or "").strip()
        ]
        scheduled.sort(key=lambda project: str(project.get("scheduled_for") or ""))

        if not scheduled:
            self._empty(parent, "No projects are currently scheduled.")
            return

        for project in scheduled[:4]:
            row = ctk.CTkFrame(parent, fg_color="transparent")
            row.pack(fill="x", padx=16, pady=(1, 8))
            ctk.CTkLabel(
                row,
                text=str(project.get("title") or "Untitled project"),
                font=("Segoe UI", 11, "bold"),
                anchor="w",
            ).pack(fill="x")

            scheduled_for = str(project.get("scheduled_for") or "")
            is_overdue = self._is_overdue_scheduled(project, now)
            ctk.CTkLabel(
                row,
                text=(
                    f"Overdue • {self._pretty_date(scheduled_for)}"
                    if is_overdue
                    else self._pretty_date(scheduled_for)
                ),
                font=("Segoe UI", 10, "bold" if is_overdue else "normal"),
                text_color=WARNING_TEXT if is_overdue else MUTED_TEXT,
                anchor="w",
            ).pack(fill="x", pady=(1, 0))

        ctk.CTkFrame(parent, height=5, fg_color="transparent").pack()

    def _build_recent(self, parent, projects):
        ranked = sorted(
            projects,
            key=lambda project: str(
                project.get("updated") or project.get("created") or ""
            ),
            reverse=True,
        )[:5]

        if not ranked:
            self._empty(parent, "No project activity yet.")
            return

        for index, project in enumerate(ranked):
            row = ctk.CTkFrame(parent, fg_color="transparent")
            row.pack(fill="x", padx=16, pady=(2, 6))

            ctk.CTkLabel(
                row,
                text=str(project.get("title") or "Untitled project"),
                font=("Segoe UI", 11, "bold"),
                anchor="w",
            ).pack(side="left", fill="x", expand=True)

            status = str(project.get("status") or "Unknown")
            stamp = str(project.get("updated") or project.get("created") or "")
            ctk.CTkLabel(
                row,
                text=f"{status}  •  {self._pretty_date(stamp)}",
                font=("Segoe UI", 10),
                text_color=MUTED_TEXT,
                anchor="e",
            ).pack(side="right", padx=(12, 0))

            if index < len(ranked) - 1:
                ctk.CTkFrame(
                    parent,
                    height=1,
                    fg_color=("#EAECF0", "#2A3039"),
                ).pack(fill="x", padx=16, pady=(0, 3))

        ctk.CTkFrame(parent, height=6, fg_color="transparent").pack()

    def _empty(self, parent, text):
        ctk.CTkLabel(
            parent,
            text=text,
            font=("Segoe UI", 11),
            text_color=MUTED_TEXT,
            anchor="w",
        ).pack(fill="x", padx=16, pady=(2, 16))

    @staticmethod
    def _parse_date(value):
        value = str(value or "").strip()
        if not value:
            return None
        for fmt in ("%Y-%m-%d %H:%M", "%Y-%m-%d %H:%M:%S"):
            try:
                return datetime.strptime(value, fmt)
            except ValueError:
                continue
        return None

    @classmethod
    def _is_same_month(cls, value, now):
        parsed = cls._parse_date(value)
        return bool(parsed and parsed.year == now.year and parsed.month == now.month)

    @classmethod
    def _is_overdue_scheduled(cls, project, now):
        if str(project.get("status") or "") != "Scheduled":
            return False
        parsed = cls._parse_date(project.get("scheduled_for"))
        return bool(parsed and parsed < now)

    @classmethod
    def _pretty_date(cls, value):
        parsed = cls._parse_date(value)
        if parsed is None:
            return str(value or "No date")
        return parsed.strftime("%d %b %Y, %H:%M")
