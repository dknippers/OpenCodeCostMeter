# /// script
# dependencies = [
#   "resvg-python",
#   "Pillow",
# ]
# ///
#
# Generate icon.ico from icon.svg.
# Run with: uv run --python 3.12 src/Assets/generate-icon.py

import resvg_python
from PIL import Image
import io

svg_path = "src/Assets/icon.svg"
ico_path = "src/Assets/icon.ico"

with open(svg_path, "r", encoding="utf-8") as f:
    svg_data = f.read()

png_list = resvg_python.svg_to_png(svg_data)
png_bytes = bytes(png_list)
base = Image.open(io.BytesIO(png_bytes)).convert("RGBA")

sizes = [16, 32, 48, 64, 128, 256]
images = []
for size in sizes:
    img = base.resize((size, size), Image.Resampling.LANCZOS)
    images.append(img)

images[0].save(
    ico_path,
    format="ICO",
    sizes=[(img.width, img.height) for img in images],
    append_images=images[1:]
)

print(f"Created {ico_path} with sizes: {sizes}")
