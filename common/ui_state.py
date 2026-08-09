class KeyboardScrollBinding:
    """Install page-scoped keyboard scrolling for a CTkScrollableFrame."""

    def __init__(self, owner, scrollable, *, units=3):
        self.owner = owner
        self.scrollable = scrollable
        self.units = units
        self.bindings = []
        self.toplevel = owner.winfo_toplevel()

        for sequence, callback in (
            ("<Up>", lambda event: self._scroll_units(event, -self.units)),
            ("<Down>", lambda event: self._scroll_units(event, self.units)),
            ("<Prior>", lambda event: self._scroll_pages(event, -1)),
            ("<Next>", lambda event: self._scroll_pages(event, 1)),
            ("<Home>", lambda event: self._scroll_to(event, 0.0)),
            ("<End>", lambda event: self._scroll_to(event, 1.0)),
        ):
            funcid = self.toplevel.bind(sequence, callback, add="+")
            if funcid:
                self.bindings.append((sequence, funcid))

        owner.bind("<Destroy>", self._on_destroy, add="+")

    @staticmethod
    def _is_text_input(event):
        widget = getattr(event, "widget", None)
        if widget is None:
            return False
        try:
            widget_class = str(widget.winfo_class()).lower()
        except Exception:
            return False
        return "entry" in widget_class or "text" in widget_class or "spinbox" in widget_class

    def _canvas(self):
        return getattr(self.scrollable, "_parent_canvas", None)

    def _scroll_units(self, event, amount):
        if self._is_text_input(event):
            return None
        canvas = self._canvas()
        if canvas is not None:
            canvas.yview_scroll(amount, "units")
        return "break"

    def _scroll_pages(self, event, amount):
        if self._is_text_input(event):
            return None
        canvas = self._canvas()
        if canvas is not None:
            canvas.yview_scroll(amount, "pages")
        return "break"

    def _scroll_to(self, event, position):
        if self._is_text_input(event):
            return None
        canvas = self._canvas()
        if canvas is not None:
            canvas.yview_moveto(position)
        return "break"

    def _on_destroy(self, event):
        if event.widget is not self.owner:
            return
        for sequence, funcid in self.bindings:
            try:
                self.toplevel.unbind(sequence, funcid)
            except Exception:
                pass
        self.bindings.clear()


class DirtyStateTracker:
    """Poll a settings snapshot and show when values differ from the last save."""

    def __init__(self, owner, snapshot_func, label, *, interval_ms=250):
        self.owner = owner
        self.snapshot_func = snapshot_func
        self.label = label
        self.interval_ms = interval_ms
        self.clean_snapshot = snapshot_func()
        self.after_id = None
        owner.bind("<Destroy>", self._on_destroy, add="+")
        self._schedule()

    def mark_clean(self):
        try:
            self.clean_snapshot = self.snapshot_func()
            self.label.configure(text="")
        except Exception:
            pass

    def _schedule(self):
        try:
            self.after_id = self.owner.after(self.interval_ms, self._poll)
        except Exception:
            self.after_id = None

    def _poll(self):
        self.after_id = None
        try:
            dirty = self.snapshot_func() != self.clean_snapshot
            self.label.configure(
                text="Unsaved changes" if dirty else "",
                text_color=("#B54708", "#FEC84B"),
            )
        except Exception:
            return
        self._schedule()

    def _on_destroy(self, event):
        if event.widget is not self.owner:
            return
        if self.after_id is not None:
            try:
                self.owner.after_cancel(self.after_id)
            except Exception:
                pass
            self.after_id = None
