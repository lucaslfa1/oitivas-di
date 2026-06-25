from pathlib import Path

import pypdfium2 as pdfium
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
PDF_PATH = ROOT / "exports" / "Sentinel_Documentacao_ISO9001.pdf"
OUTPUT = ROOT / "exports" / "pdf-render-contact-sheet.png"


def main() -> None:
    if not PDF_PATH.exists():
        raise SystemExit(f"PDF ausente: {PDF_PATH}")

    pdf = pdfium.PdfDocument(str(PDF_PATH))
    thumb_width = 320
    margin = 24
    label_height = 28
    columns = 3

    thumbs = []
    for index in range(len(pdf)):
        page = pdf[index]
        bitmap = page.render(scale=1.0)
        image = bitmap.to_pil().convert("RGB")
        ratio = thumb_width / image.width
        thumb_height = int(image.height * ratio)
        image = image.resize((thumb_width, thumb_height), Image.LANCZOS)
        thumbs.append((f"page-{index + 1}", image))

    cell_width = thumb_width + margin
    cell_height = max(image.height for _, image in thumbs) + label_height + margin
    rows = (len(thumbs) + columns - 1) // columns

    sheet = Image.new("RGB", (columns * cell_width + margin, rows * cell_height + margin), "white")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()

    for index, (label, image) in enumerate(thumbs):
        row = index // columns
        col = index % columns
        x = margin + col * cell_width
        y = margin + row * cell_height
        draw.text((x, y), label, fill=(20, 20, 20), font=font)
        sheet.paste(image, (x, y + label_height))

    sheet.save(OUTPUT)
    print(f"PDF contact sheet: {OUTPUT}")


if __name__ == "__main__":
    main()
