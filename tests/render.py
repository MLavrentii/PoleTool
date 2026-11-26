"""Renders a sheet JSON exported by the core tests to PNG (checks only, not part of the plugin)."""
import json, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon, Circle
from matplotlib import font_manager as fm

for f in fm.findSystemFonts():
    if "NotoSansCJK-Regular" in f:
        fm.fontManager.addfont(f)
plt.rcParams["font.family"] = ["Noto Sans CJK JP", "DejaVu Sans"]

COL = {"Pole": "#222", "Equip": "#111", "Cable": "#c00", "Earth": "#0a0", "Guy": "#333", "Ground": "#555",
       "Text": "#00a", "Dim": "#a0a", "Bom": "#000", "Center": "#0aa", "Hidden": "#888"}
STY = {"Solid": "-", "Hidden": "--", "Center": "-.", "Phantom": ":"}

def main(src, dst, dpi=150, win=None):
    E = json.load(open(src, encoding="utf-8"))
    xs, ys = [], []
    for e in E:
        for k in ("p", "a", "b"):
            v = e.get(k)
            if isinstance(v, list) and v and isinstance(v[0], list):
                xs += [q[0] for q in v]; ys += [q[1] for q in v]
            elif isinstance(v, list) and len(v) == 2:
                xs.append(v[0]); ys.append(v[1])
    if win: xs, ys = [win[0], win[1]], [win[2], win[3]]
    w = max(xs) - min(xs); h = max(ys) - min(ys)
    W = 16; fig = plt.figure(figsize=(W, W * h / w + 0.5)); ax = fig.add_axes([0, 0, 1, 1]); ax.set_aspect("equal"); ax.axis("off")
    pt = W * 72 / w  # points per model unit
    z = 0
    for e in E:
        z += 1
        t = e["t"]
        if t == "M":
            ax.add_patch(Polygon(e["p"], closed=True, fc="white", ec="none", zorder=z))
        elif t == "L":
            p = e["p"] + ([e["p"][0]] if e["c"] else [])
            ax.plot([q[0] for q in p], [q[1] for q in p], STY.get(e["k"], "-"), color=COL.get(e["l"], "k"), lw=0.6, zorder=z)
        elif t == "T":
            ha = {"Left": "left", "Right": "right", "Center": "center"}[e["a"]]
            ax.text(e["p"][0], e["p"][1], e["s"], ha=ha, va="baseline", fontsize=e["h"] * pt * 0.95, color=COL.get(e["l"], "k"), zorder=z + 10000)
        elif t == "C":
            ax.add_patch(Circle(e["p"], e["r"], fc="#00a" if e["f"] else "white", ec="#00a", lw=0.6, zorder=z + 10000))
        elif t == "D":
            a, b, at = e["a"], e["b"], e["at"]
            if e["v"]:
                ax.plot([at, at], [a[1], b[1]], color="#a0a", lw=0.5, zorder=z + 10000)
                for y in (a[1], b[1]): ax.plot([at - 60, a[0]], [y, y], color="#a0a", lw=0.4, zorder=z + 10000)
                ax.text(at - 40, (a[1] + b[1]) / 2, f"{abs(a[1]-b[1]):.0f}", rotation=90, ha="right", va="center", fontsize=125 * pt, color="#a0a", zorder=z + 10000)
        elif t == "B":
            x0, y0 = e["p"]; rh = e["rh"]; ws = e["w"]; th = e["th"]
            ax.text(x0, y0 + rh * 0.4, e["title"], fontsize=th * 1.3 * pt, zorder=z + 10000)
            rows = e["rows"]; tw = sum(ws)
            for i in range(len(rows) + 1):
                ax.plot([x0, x0 + tw], [y0 - i * rh] * 2, color="k", lw=0.5)
            cx = x0
            for wv in ws + [0]:
                ax.plot([cx, cx], [y0, y0 - len(rows) * rh], color="k", lw=0.5); cx += wv
            for i, r in enumerate(rows):
                cx = x0
                for j, s in enumerate(r):
                    ax.text(cx + th * 0.4, y0 - (i + 0.7) * rh, s, fontsize=th * 0.9 * pt, zorder=z + 10000)
                    cx += ws[j]
    ax.set_xlim(min(xs) - 300, max(xs) + 300); ax.set_ylim(min(ys) - 300, max(ys) + 300)
    ax.set_clip_on(True)
    fig.savefig(dst, dpi=dpi, facecolor="white")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 150, [float(v) for v in sys.argv[4:8]] if len(sys.argv) > 7 else None)
