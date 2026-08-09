from common.chatgpt_import_parser import ChatGPTImportParser


def test_hybrid_visual_timeline_imports_metadata_and_production_content():
    text = """Title:
Octopus Blood Is Blue
Category:
Nature
Template:
Shorts
Description:
A short fact about octopus blood chemistry.
Pinned Comment:
Would you swim with an octopus?
Tags:
octopus, ocean, biology
Sources:
Smithsonian Ocean

Visual Timeline:

0–3 sec

Narration:
Octopus blood is blue, not red.

Visual:
Close-up of an octopus underwater.

Search:
octopus underwater
blue ocean octopus

Free Sources:
Pexels
Pixabay

On Screen:
BLUE BLOOD?

────────────────────────

3–7 sec

Narration:
That is because it uses copper-rich hemocyanin to carry oxygen.

Visual:
Macro science illustration of copper and blood flow.

Search:
copper science
blood cells animation

Free Sources:
Pexels
Pixabay

On Screen:
COPPER CARRIES OXYGEN
"""

    result = ChatGPTImportParser.parse(text)

    assert result["title"] == "Octopus Blood Is Blue"
    assert result["category"] == "Nature"
    assert result["template"] == "Shorts"
    assert result["description"] == "A short fact about octopus blood chemistry."
    assert result["pinned_comment"] == "Would you swim with an octopus?"
    assert result["script"] == (
        "Octopus blood is blue, not red.\n\n"
        "That is because it uses copper-rich hemocyanin to carry oxygen."
    )
    assert "0–3 sec\nClose-up of an octopus underwater." in result["visual_plan"]
    assert "0–3 sec\nBLUE BLOOD?" in result["on_screen_text"]
    assert "Tags:\noctopus, ocean, biology" in result["notes"]
    assert "Sources:\nSmithsonian Ocean" in result["notes"]
    assert "Search:\noctopus underwater\nblue ocean octopus" in result["notes"]
    assert "Free Sources:\nPexels\nPixabay" in result["notes"]


def test_standard_format_accepts_inline_values():
    result = ChatGPTImportParser.parse(
        "Title: Moon Dust Smells Strange\n"
        "Category: Space\n"
        "Template: Shorts\n"
        "Description: Apollo astronauts described a distinctive smell.\n"
        "Pinned Comment: What would you compare it to?\n"
        "Tags: moon, Apollo\n"
        "Sources: NASA"
    )

    assert result["title"] == "Moon Dust Smells Strange"
    assert result["category"] == "Space"
    assert result["template"] == "Shorts"
    assert result["description"] == "Apollo astronauts described a distinctive smell."
    assert result["pinned_comment"] == "What would you compare it to?"
    assert result["notes"] == "Tags:\nmoon, Apollo\n\nSources:\nNASA"


def test_visual_timeline_without_metadata_keeps_shorts_default():
    result = ChatGPTImportParser.parse(
        "Visual Timeline:\n\n"
        "0–3 sec\n\n"
        "Narration:\nA surprising fact.\n\n"
        "Visual:\nA dramatic landscape.\n\n"
        "Search:\nmountain landscape\n\n"
        "Free Sources:\nPexels\n\n"
        "On Screen:\nDID YOU KNOW?"
    )

    assert result["template"] == "Shorts"
    assert result["script"] == "A surprising fact."


def test_metadata_template_can_override_visual_timeline_default():
    result = ChatGPTImportParser.parse(
        "Title: Example\n"
        "Template: My Custom Template\n\n"
        "Visual Timeline:\n\n"
        "0–3 sec\n\n"
        "Narration:\nExample narration.\n\n"
        "Visual:\nExample visual.\n\n"
        "Search:\nexample\n\n"
        "Free Sources:\nPixabay\n\n"
        "On Screen:\nEXAMPLE"
    )

    assert result["template"] == "My Custom Template"
