"""Create README GIFs from the released v2 APNG, not from source GIF guesses."""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "pets" / "wisadel-rest" / "spritesheet.png"
FALLBACK = ROOT / "pets" / "wisadel-rest" / "spritesheet.webp"
OUT = ROOT / "docs" / "previews"
CELL = (192, 208)
STATES = (
    "idle", "running-right", "running-left", "waving", "jumping",
    "failed", "waiting", "running", "review",
)
BACKGROUND = (239, 243, 248, 255)


def preview_frame(cell):
    background = Image.new("RGBA", CELL, BACKGROUND)
    background.alpha_composite(cell.convert("RGBA"))
    return background.convert("RGB")


def save_gif(name, frames, durations):
    path = OUT / f"{name}.gif"
    frames[0].save(
        path, format="GIF", save_all=True, append_images=frames[1:],
        duration=durations, loop=0, optimize=True, disposal=2,
    )
    with Image.open(path) as check:
        if check.n_frames < 2 or check.size != CELL:
            raise ValueError(f"Invalid preview: {path}")
        print(f"{path.relative_to(ROOT)}: {check.n_frames} frames, {path.stat().st_size} bytes")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rows = {state: [] for state in STATES}
    durations = []
    elapsed = rounded_elapsed = 0.0
    with Image.open(ATLAS) as atlas:
        if atlas.size != (1536, 2288) or atlas.n_frames < 2:
            raise ValueError("Expected the released animated 8x11 v2 atlas")
        for index in range(atlas.n_frames):
            atlas.seek(index)
            elapsed += float(atlas.info.get("duration", 35))
            next_rounded = round(elapsed / 10) * 10
            durations.append(max(20, int(next_rounded - rounded_elapsed)))
            rounded_elapsed = next_rounded
            for row, state in enumerate(STATES):
                cell = atlas.crop((0, row * CELL[1], CELL[0], (row + 1) * CELL[1]))
                rows[state].append(preview_frame(cell))
    for state in STATES:
        save_gif(state, rows[state], durations)

    directions = []
    with Image.open(FALLBACK) as atlas:
        if atlas.size != (1536, 2288):
            raise ValueError("Expected the released v2 fallback atlas")
        for index in range(16):
            column, row_offset = index % 8, index // 8
            top = (9 + row_offset) * CELL[1]
            cell = atlas.crop((column * CELL[0], top, (column + 1) * CELL[0], top + CELL[1]))
            directions.append(preview_frame(cell))
    save_gif("look-directions", directions, [220] * 16)


if __name__ == "__main__":
    main()
