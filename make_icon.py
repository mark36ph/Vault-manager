from PIL import Image
from pathlib import Path

input_file = Path("assets") / "icons" / "app.png"
output_file = Path("assets") / "icons" / "app.ico"

if not input_file.exists():
    raise FileNotFoundError(f"Could not find: {input_file}")

image = Image.open(input_file).convert("RGBA")

image.save(
    output_file,
    format="ICO",
    sizes=[
        (16, 16),
        (32, 32),
        (48, 48),
        (64, 64),
        (128, 128),
        (256, 256)
    ]
)

print(f"Icon created: {output_file}")