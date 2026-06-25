from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
RENDER_DIR = ROOT / "exports" / "docx-render"
OUTPUT = ROOT / "exports" / "docx-render-contact-sheet.png"


def page_number(path: Path) -> int:
    return int(path.stem.split("-")[1])


def main() -> None:
    pages = sorted(RENDER_DIR.glob("page-*.png"), key=page_number)
    if not pages:
        raise SystemExit(f"Nenhuma pagina renderizada em {RENDER_DIR}")

    thumb_width = 360
    margin = 24
    label_height = 28
    columns = 3

    thumbs = []
    for path in pages:
        image = Image.open(path).convert("RGB")
        ratio = thumb_width / image.width
        thumb_height = int(image.height * ratio)
        image = image.resize((thumb_width, thumb_height), Image.LANCZOS)
        thumbs.append((path, image))

    cell_width = thumb_width + margin
    cell_height = max(image.height for _, image in thumbs) + label_height + margin
    rows = (len(thumbs) + columns - 1) // columns

    sheet = Image.new("RGB", (columns * cell_width + margin, rows * cell_height + margin), "white")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()

    for index, (path, image) in enumerate(thumbs):
        row = index // columns
        col = index % columns
        x = margin + col * cell_width
        y = margin + row * cell_height
        draw.text((x, y), path.stem, fill=(20, 20, 20), font=font)
        sheet.paste(image, (x, y + label_height))

    sheet.save(OUTPUT)
    print(f"Contact sheet: {OUTPUT}")


if __name__ == "__main__":
    main()
