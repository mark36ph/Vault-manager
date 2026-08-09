class ChatGPTImportParser:
    """Parse Fact Vault Manager ChatGPT import formats."""

    FIELD_LABELS = {
        "title": ["title"],
        "category": ["category"],
        "template": ["template"],
        "script": ["script"],
        "description": ["description"],
        "pinned_comment": ["pinned comment"],
        "tags": ["tags"],
        "sources": ["sources"],
        "on_screen_text": ["on screen", "on-screen text", "onscreen text"],
        "visual_plan": ["visual", "visual plan"],
    }

    @staticmethod
    def parse(text):
        text = str(text or "")
        timeline_marker = ChatGPTImportParser.find_visual_timeline_marker(text)

        if timeline_marker is not None:
            prefix = text[:timeline_marker].strip()
            timeline_text = text[timeline_marker:].strip()
            result = ChatGPTImportParser.parse_visual_timeline(timeline_text)

            if prefix:
                metadata = ChatGPTImportParser.parse_standard_format(prefix)
                ChatGPTImportParser.merge_metadata(result, metadata)

            return result

        if "Narration:" in text:
            return ChatGPTImportParser.parse_visual_timeline(text)

        return ChatGPTImportParser.parse_standard_format(text)

    @staticmethod
    def find_visual_timeline_marker(text):
        for index, line in enumerate(text.splitlines(keepends=True)):
            if line.strip().lower() == "visual timeline:":
                return sum(len(item) for item in text.splitlines(keepends=True)[:index])
        return None

    @staticmethod
    def parse_standard_format(text):
        result = ChatGPTImportParser.empty_result()
        current_field = None

        for line in str(text or "").splitlines():
            clean = line.strip()
            lower = clean.lower()
            matched_field = None
            inline_value = ""

            for field, labels in ChatGPTImportParser.FIELD_LABELS.items():
                for label in labels:
                    if lower == f"{label}:":
                        matched_field = field
                        break
                    prefix = f"{label}:"
                    if lower.startswith(prefix):
                        matched_field = field
                        inline_value = clean[len(prefix):].strip()
                        break
                if matched_field:
                    break

            if matched_field:
                current_field = matched_field
                if inline_value:
                    result[current_field] += inline_value + "\n"
                continue

            if current_field:
                result[current_field] += line + "\n"

        for key in result:
            result[key] = result[key].strip()

        result["notes"] = ChatGPTImportParser.metadata_notes(result)
        return result

    @staticmethod
    def metadata_notes(result):
        notes_parts = []
        if result.get("tags"):
            notes_parts.append("Tags:\n" + result["tags"])
        if result.get("sources"):
            notes_parts.append("Sources:\n" + result["sources"])
        return "\n\n".join(notes_parts).strip()

    @staticmethod
    def merge_metadata(result, metadata):
        for key in (
            "title",
            "category",
            "template",
            "description",
            "pinned_comment",
            "tags",
            "sources",
        ):
            value = str(metadata.get(key) or "").strip()
            if value:
                result[key] = value

        metadata_notes = str(metadata.get("notes") or "").strip()
        timeline_notes = str(result.get("notes") or "").strip()
        result["notes"] = "\n\n".join(
            part for part in (metadata_notes, timeline_notes) if part
        ).strip()

    @staticmethod
    def parse_visual_timeline(text):
        result = ChatGPTImportParser.empty_result()
        result["template"] = "Shorts"
        blocks = ChatGPTImportParser.split_timeline_blocks(text)

        script_parts = []
        on_screen_parts = []
        visual_parts = []
        notes_parts = []

        for block in blocks:
            time_label = ChatGPTImportParser.extract_time_label(block)
            narration = ChatGPTImportParser.extract_section(block, "Narration")
            visual = ChatGPTImportParser.extract_section(block, "Visual")
            search = ChatGPTImportParser.extract_section(block, "Search")
            free_sources = ChatGPTImportParser.extract_section(block, "Free Sources")
            on_screen = ChatGPTImportParser.extract_section(block, "On Screen")

            if narration:
                script_parts.append(narration)

            if on_screen:
                on_screen_parts.append(
                    f"{time_label}\n{on_screen}".strip() if time_label else on_screen
                )

            if visual:
                visual_parts.append(
                    f"{time_label}\n{visual}".strip() if time_label else visual
                )

            note_parts = []
            if time_label:
                note_parts.append(time_label)
            if search:
                note_parts.append("Search:\n" + search)
            if free_sources:
                note_parts.append("Free Sources:\n" + free_sources)
            if note_parts:
                notes_parts.append("\n".join(note_parts).strip())

        result["script"] = "\n\n".join(script_parts).strip()
        result["on_screen_text"] = "\n\n".join(on_screen_parts).strip()
        result["visual_plan"] = "\n\n".join(visual_parts).strip()
        result["notes"] = "\n\n".join(notes_parts).strip()
        return result

    @staticmethod
    def split_timeline_blocks(text):
        cleaned = str(text or "").replace(
            "────────────────────────",
            "-----BLOCK-----",
        )
        raw_blocks = cleaned.split("-----BLOCK-----")

        blocks = []
        for block in raw_blocks:
            block = block.strip()
            if block and "Narration:" in block:
                blocks.append(block)
        return blocks

    @staticmethod
    def extract_time_label(block):
        for line in block.splitlines():
            clean = line.strip()
            if clean and "sec" in clean.lower():
                return clean
        return ""

    @staticmethod
    def extract_section(block, section_name):
        lines = block.splitlines()
        wanted = section_name.lower() + ":"
        section_lines = []
        collecting = False
        known_sections = [
            "narration:",
            "visual:",
            "search:",
            "free sources:",
            "on screen:",
        ]

        for line in lines:
            clean = line.strip()
            lower = clean.lower()

            if lower == wanted:
                collecting = True
                continue

            if collecting and lower in known_sections:
                break

            if collecting:
                section_lines.append(line)

        return "\n".join(section_lines).strip()

    @staticmethod
    def empty_result():
        return {
            "title": "",
            "category": "",
            "template": "",
            "script": "",
            "description": "",
            "pinned_comment": "",
            "tags": "",
            "sources": "",
            "notes": "",
            "on_screen_text": "",
            "visual_plan": "",
        }
