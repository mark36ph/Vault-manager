class ChatGPTImportParser:
    """
    Parses text copied from ChatGPT into Fact Vault fields.

    Supported labels:
    Title:
    Script:
    Description:
    Pinned Comment:
    Tags:
    Notes:
    """

    FIELDS = {
        "title": ["title"],
        "script": ["script", "narration"],
        "description": ["description"],
        "pinned_comment": ["pinned comment", "comment"],
        "notes": ["notes", "tags", "hashtags"],
        "category": ["category"],
        "template": ["template", "project template"],
    }

    @staticmethod
    def parse(text):

        result = {
            "title": "",
            "script": "",
            "description": "",
            "pinned_comment": "",
            "notes": "",
            "category": "",
            "template": "",
        }

        current_field = None

        for line in text.splitlines():

            clean = line.strip()
            lower = clean.lower().replace(":", "")

            matched_field = None

            for field, labels in ChatGPTImportParser.FIELDS.items():

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

        return result