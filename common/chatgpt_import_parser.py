class ChatGPTImportParser:
    """
    Parses Fact Vault Manager import formats.

    Supported formats:

    1. Standard format:
       Title:
       Category:
       Template:
       Script:
       Description:
       Pinned Comment:
       Tags:
       Sources:

    2. Visual Timeline format:
       Visual Timeline:

       0–3 sec

       Narration:
       ...

       Visual:
       ...

       Search:
       ...

       Free Sources:
       ...

       On Screen:
       ...
    """

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
        "visual_plan": ["visual", "visual plan"]
    }

    @staticmethod
    def parse(text):

        if "Visual Timeline:" in text or "Narration:" in text:

            return ChatGPTImportParser.parse_visual_timeline(
                text
            )

        return ChatGPTImportParser.parse_standard_format(
            text
        )

    @staticmethod
    def parse_standard_format(text):

        result = ChatGPTImportParser.empty_result()

        current_field = None

        for line in text.splitlines():

            clean = line.strip()
            lower = clean.lower().replace(":", "")

            matched_field = None

            for field, labels in ChatGPTImportParser.FIELD_LABELS.items():

                if lower in labels:

                    matched_field = field
                    break

            if matched_field:

                current_field = matched_field
                continue

            if current_field:

                result[current_field] += line + "\n"

        for key in result:

            result[key] = result[key].strip()

        notes_parts = []

        if result["tags"]:

            notes_parts.append(
                "Tags:\n" + result["tags"]
            )

        if result["sources"]:

            notes_parts.append(
                "Sources:\n" + result["sources"]
            )

        if notes_parts:

            result["notes"] = "\n\n".join(
                notes_parts
            ).strip()

        return result

    @staticmethod
    def parse_visual_timeline(text):

        result = ChatGPTImportParser.empty_result()

        result["template"] = "Shorts"

        blocks = ChatGPTImportParser.split_timeline_blocks(
            text
        )

        script_parts = []
        on_screen_parts = []
        visual_parts = []
        notes_parts = []

        for block in blocks:

            time_label = ChatGPTImportParser.extract_time_label(
                block
            )

            narration = ChatGPTImportParser.extract_section(
                block,
                "Narration"
            )

            visual = ChatGPTImportParser.extract_section(
                block,
                "Visual"
            )

            search = ChatGPTImportParser.extract_section(
                block,
                "Search"
            )

            free_sources = ChatGPTImportParser.extract_section(
                block,
                "Free Sources"
            )

            on_screen = ChatGPTImportParser.extract_section(
                block,
                "On Screen"
            )

            if narration:

                script_parts.append(
                    narration
                )

            if on_screen:

                if time_label:

                    on_screen_parts.append(
                        f"{time_label}\n{on_screen}"
                    )

                else:

                    on_screen_parts.append(
                        on_screen
                    )

            if visual:

                visual_text = ""

                if time_label:

                    visual_text += time_label + "\n"

                visual_text += visual

                visual_parts.append(
                    visual_text.strip()
                )

            note_text = ""

            if time_label:

                note_text += time_label + "\n"

            if search:

                note_text += "Search:\n" + search + "\n\n"

            if free_sources:

                note_text += "Free Sources:\n" + free_sources

            if note_text.strip():

                notes_parts.append(
                    note_text.strip()
                )

        result["script"] = "\n\n".join(
            script_parts
        ).strip()

        result["on_screen_text"] = "\n\n".join(
            on_screen_parts
        ).strip()

        result["visual_plan"] = "\n\n".join(
            visual_parts
        ).strip()

        result["notes"] = "\n\n".join(
            notes_parts
        ).strip()

        return result

    @staticmethod
    def split_timeline_blocks(text):

        cleaned = text.replace(
            "────────────────────────",
            "-----BLOCK-----"
        )

        raw_blocks = cleaned.split(
            "-----BLOCK-----"
        )

        blocks = []

        for block in raw_blocks:

            block = block.strip()

            if not block:
                continue

            if "Narration:" not in block:
                continue

            blocks.append(
                block
            )

        return blocks

    @staticmethod
    def extract_time_label(block):

        for line in block.splitlines():

            clean = line.strip()

            if not clean:
                continue

            if "sec" in clean.lower():

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
            "on screen:"
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

                section_lines.append(
                    line
                )

        return "\n".join(
            section_lines
        ).strip()

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
            "visual_plan": ""
        }