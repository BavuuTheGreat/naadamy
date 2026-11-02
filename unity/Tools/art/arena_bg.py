# The three stages of the online arena, wide like a fighting game's (720×300, the wrestlers' ground at y 262): the Naadam
# stadium at noon, an ovoo on the steppe at sunset, and a winter camp under the snow. The camera shows a 400×225 part of
# a stage and follows the wrestlers along it.
#   python3 unity/Tools/art/arena_bg.py [preview_dir]
import math
import os
import random
import sys

from pix import Canvas, hexc, mix, save, shade
from story_bg import cloud, disc, ger, grass_band, horse_sil, range_, ridge, sky, stars, tug
from naadam_bg import crowd, mn_flag

W, H, FLOOR = 720, 300, 262
ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Resources', 'Art')


def ring(c, base, light, dark, seed, top=FLOOR - 26):
    """The wrestling ground in front: packed earth and grass, a little lighter where the wrestlers stand."""
    grass_band(c, top, H, base, light, dark, seed=seed, density=0.03)
    for y in range(FLOOR - 4, H):
        t = (y - FLOOR + 4) / (H - FLOOR + 4)
        for x in range(160, 560):
            d = abs(x - 360) / 200
            if d < 1 and random.Random(x * 31 + y).random() < (1 - d) * 0.35 * (1 - t * 0.5):
                c.px(x, y, shade(mix(base, '#b99a64', 0.5), -0.05 * t))


def naadam():
    c = Canvas(W, H)
    sky(c, [(0, '#2f78d8'), (90, '#7fb8f0'), (150, '#d6ecf9')])
    for i, (x, y, w) in enumerate([(90, 30, 90), (330, 22, 70), (560, 40, 110), (690, 18, 50)]):
        cloud(c, x, y, w, 9, '#ffffff', '#ffffff', '#cfdcee', i + 10)
    range_(c, ridge(W, 150, 26, 0.5, 11, peaks=[(520, 16, 110)]), '#7f95b8', '#a7b9d6', '#62789c', haze=('#b4c4dc', 0.3))
    # the stands, packed and roaring, in three tiers round the field
    for tier, (top, h, col) in enumerate([(140, 26, '#7a4a26'), (164, 24, '#8a5a33'), (186, 22, '#9a6a3e')]):
        c.rect(0, top, W, h, col); c.rect(0, top + h - 2, W, 2, shade(col, -0.3))
        crowd(c, 2, W - 2, top + 2, 3, 40 + tier, 6, 4)
    # the officials' pavilion and the nine white standards
    c.poly([(300, 140), (360, 106), (420, 140)], '#2b5bd8'); c.poly([(300, 140), (360, 106), (360, 140)], '#3f76db')
    c.rect(310, 140, 100, 26, '#f2eee4'); c.rect(310, 140, 100, 3, '#d8342b'); c.rect(352, 148, 16, 18, '#d8342b')
    c.ellipse(360, 106, 5, 3, '#ffd34a')
    for i in range(9):
        tug(c, 318 + i * 11, 206, 34)
    for x in (40, 160, 560, 680):
        c.line(x, 100, x, 140, '#4a3a2a'); mn_flag(c, x + 1, 100, 18, 12)
    c.rect(0, 206, W, 4, '#2e5a20')
    for x in range(0, W, 10):   # the painted fence
        c.rect(x, 208, 8, 4, '#2b5bd8' if (x // 10) % 2 else '#d8342b')
    ring(c, '#5fa83f', '#8fcf63', '#3e7a2a', 7, top=211)
    for x in range(0, W, 10):   # the painted fence, over the grass's edge
        c.rect(x, 208, 8, 4, '#2b5bd8' if (x // 10) % 2 else '#d8342b')
    return c


def steppe():
    c = Canvas(W, H)
    sky(c, [(0, '#2a2a6a'), (60, '#8a3f7a'), (130, '#f0884a'), (175, '#ffd27a')])
    stars(c, 40, 0, 50, 4)
    disc(c, 470, 170, 30, '#ffe29a', halo='#ffb35a', halo_r=60)
    for i, (x, y, w) in enumerate([(120, 70, 120), (600, 56, 140), (330, 90, 90)]):
        cloud(c, x, y, w, 8, '#f4a07a', '#ffd0a0', '#9a4a6a', i + 30)
    range_(c, ridge(W, 186, 40, 0.55, 23, peaks=[(160, 30, 120), (560, 20, 90)]), '#5a3a6a', '#8a5a7a', '#3a2550', haze=('#c06a7a', 0.35))
    range_(c, ridge(W, 214, 18, 0.5, 31), '#4a3a4a', '#7a5a50', '#2a1c2a')
    # an ovoo on the hill with its blue scarves, a camp and a herd far off
    for k in range(7):
        c.ellipse(118, 206 - k * 5, 20 - k * 2.6, 4, shade('#8a7a6a', -0.1 * (k % 2)))
    c.line(118, 168, 118, 206, '#6b4a2b')
    for k in range(6):
        c.line(118, 172 + k, 96 + k * 9, 196 + (k % 3) * 3, ['#3f76db', '#ffffff', '#f7c646', '#2ea35f', '#d8342b', '#3f76db'][k])
    for x, s in ((560, 26), (600, 20), (632, 16)):
        ger(c, x, 214, s, felt='#e6d2c0', shade_='#b89a90', smoke='#e8c8c0')
    for i in range(7):
        horse_sil(c, 230 + i * 14 + (i % 2) * 5, 222 - (i % 3), '#2a1c2a', 1.4, grazing=i % 2 == 0)
    ring(c, '#8a8a4a', '#c0a860', '#4a4a2a', 9, top=226)
    return c


def winter():
    c = Canvas(W, H)
    sky(c, [(0, '#7aa0c8'), (100, '#bcd2e6'), (170, '#e8f0f6')])
    for i, (x, y, w) in enumerate([(160, 40, 140), (480, 30, 160)]):
        cloud(c, x, y, w, 10, '#f4f8fc', '#ffffff', '#b8c8d8', i + 50)
    range_(c, ridge(W, 168, 46, 0.55, 41, peaks=[(300, 34, 140)]), '#8aa0b8', '#c8d8e8', '#6a80a0', snow='#f4f8fc', snow_line=150, haze=('#d8e4f0', 0.3))
    range_(c, ridge(W, 206, 16, 0.5, 47), '#c8d4e0', '#ffffff', '#9aaec4')
    for x, s in ((90, 40), (150, 30), (620, 36), (670, 26)):
        ger(c, x, 226, s, felt='#f4f0e8', shade_='#c8c4bc', smoke='#ffffff')
    for x in (210, 520):
        tug(c, x, 226, 30)
    r = random.Random(5)
    for _ in range(14):   # people of the camp watching, wrapped in deels
        x = r.choice([r.randrange(190, 260), r.randrange(450, 560)]); col = r.choice(['#d8342b', '#2b5bd8', '#2ea35f', '#8a5a33', '#9466e0'])
        c.rect(x, 214, 5, 12, col); c.ellipse(x + 2.5, 211, 2.5, 2.5, '#e8b48a'); c.rect(x, 207, 5, 3, shade(col, -0.3))
    grass_band(c, FLOOR - 36, H, '#e4ecf4', '#ffffff', '#b8c8d8', seed=13, density=0.02)
    for y in range(FLOOR - 4, H):   # the trampled snow where they wrestle
        for x in range(170, 550):
            d = abs(x - 360) / 190
            if d < 1 and random.Random(x * 17 + y).random() < (1 - d) * 0.3:
                c.px(x, y, '#cad6e2')
    for _ in range(260):   # falling snow
        x, y = r.randrange(W), r.randrange(H)
        c.px(x, y, '#ffffff')
    return c


def main():
    prev = sys.argv[1] if len(sys.argv) > 1 else None
    for name, fn in (('naadam', naadam), ('steppe', steppe), ('winter', winter)):
        c = fn()
        save(c, os.path.join(ROOT, f'arena_stage_{name}.png.bytes'), prev and os.path.join(prev, f'arena_stage_{name}.png'), 2)


if __name__ == '__main__':
    main()
