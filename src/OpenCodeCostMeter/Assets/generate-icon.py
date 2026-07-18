# /// script
# dependencies = [
#   "resvg-python",
#   "Pillow",
# ]
# ///
#
# Generate icon.ico from icon.svg.
# Run with: uv run --python 3.12 src/Assets/generate-icon.py

import io
import struct

import resvg_python
from PIL import Image


def save_ico(images, path):
    """Save multiple PIL images as a multi-size ICO file using PNG encoding."""
    png_data = []
    for img in images:
        buf = io.BytesIO()
        img.save(buf, format="PNG")
        png_data.append(buf.getvalue())

    count = len(images)
    with open(path, "wb") as f:
        # ICONDIR
        f.write(struct.pack("<HHH", 0, 1, count))

        header_size = 6
        entry_size = 16
        data_offset = header_size + entry_size * count

        for img, data in zip(images, png_data):
            width = img.width if img.width < 256 else 0
            height = img.height if img.height < 256 else 0
            f.write(struct.pack(
                "<BBBBHHII",
                width,
                height,
                0,  # bColorCount (0 for >256)
                0,  # bReserved
                1,  # wPlanes
                32,  # wBitCount
                len(data),
                data_offset,
            ))
            data_offset += len(data)

        for data in png_data:
            f.write(data)


svg_path = "src/Assets/icon.svg"
ico_path = "src/Assets/icon.ico"
sizes = [16, 32, 48, 64, 128, 256]

with open(svg_path, "r", encoding="utf-8") as f:
    svg_data = f.read()

png_list = resvg_python.svg_to_png(svg_data)
png_bytes = bytes(png_list)
base = Image.open(io.BytesIO(png_bytes)).convert("RGBA")

images = [base.resize((size, size), Image.Resampling.LANCZOS) for size in sizes]
save_ico(images, ico_path)

print(f"Created {ico_path} with sizes: {sizes}")
