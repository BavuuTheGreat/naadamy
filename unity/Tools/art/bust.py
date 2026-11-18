# Dialogue portraits in a JRPG pixel-art style: a head-and-shoulders bust with bold black linework, flat cel shading and a
# clear, anime-realistic face. Each bust is drawn smooth at a high resolution (on a 112×144 grid of units), then reduced
# to the dialogue frame size with a limited palette, and the linework is put back as crisp black pixels.
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

UW, UH = 112, 144          # drawing units
PW, PH = 112, 144          # dialogue portrait pixels, rendered at the displayed size
K = 8                      # hi-res pixels per unit
INK = (26, 16, 20)
REFERENCE = os.path.join(os.path.dirname(__file__), 'reference', 'bolormaa-narration.png')


def rgb(h): h = h.lstrip('#'); return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


class Canvas2:
    """Two pictures drawn together: the colour shapes, smooth at a high resolution, and the lines, drawn crisp at the
    finished pixel size (1 or 2 pixels wide, like hand-placed pixel art). A shape drawn later covers the lines under it."""
    def __init__(self):
        self.col = Image.new('RGBA', (UW * K, UH * K), (0, 0, 0, 0)); self.c = ImageDraw.Draw(self.col)
        self.sm = Image.new('RGBA', (PW, PH), (0, 0, 0, 0)); self.s = ImageDraw.Draw(self.sm)

    def P(self, pts): return [(x * K, y * K) for x, y in pts]

    def S(self, pts): return [(x * PW / UW - 0.5, y * PH / UH - 0.5) for x, y in pts]

    def box(self, cx, cy, rx, ry):   # an ellipse's box in pixels, at least one pixel
        f = PW / UW; x0, y0 = round((cx - rx) * f), round((cy - ry) * f)
        return [x0, y0, max(x0, round((cx + rx) * f) - 1), max(y0, round((cy + ry) * f) - 1)]

    def cover(self, draw):
        """Lines under a new shape are hidden where the shape covers most of a pixel."""
        m = Image.new('L', (PW * 4, PH * 4), 0); draw(ImageDraw.Draw(m), 4 * PW / UW)
        a = np.asarray(self.sm).copy(); a[np.asarray(m.resize((PW, PH), Image.BOX)) > 150] = 0
        self.sm = Image.fromarray(a, 'RGBA'); self.s = ImageDraw.Draw(self.sm)

    def poly(self, pts, fill, form=None):
        self.c.polygon(self.P(pts), fill=fill)
        self.cover(lambda d, f: d.polygon([(x * f, y * f) for x, y in pts], fill=255))
        if form: self.form(lambda d: d.polygon(self.P(pts), fill=255), fill, *form)

    def ell(self, cx, cy, rx, ry, fill, form=None):
        if min(rx, ry) < 2.6:   # small dots and beads: crisp pixels
            b = self.box(cx, cy, rx, ry)   # PIL leaves out ellipses this small, so they are drawn as little blocks
            (self.s.rectangle if b[2] - b[0] < 2 or b[3] - b[1] < 2 else self.s.ellipse)(b, fill=tuple(fill) + (255,)); return
        self.c.ellipse([(cx - rx) * K, (cy - ry) * K, (cx + rx) * K, (cy + ry) * K], fill=fill)
        self.cover(lambda d, f: d.ellipse([(cx - rx) * f, (cy - ry) * f, (cx + rx) * f, (cy + ry) * f], fill=255))
        if form: self.form(lambda d: d.ellipse([(cx - rx) * K, (cy - ry) * K, (cx + rx) * K, (cy + ry) * K], fill=255), fill, *form)

    def form(self, draw, base, shadow=(4, 4), light=(2, 2)):
        """Cel shading like Bolormaa's: the light comes from the upper left, so a shape gets a cool shadow band along
        its lower-right edge (two tones) and a warm lit band along its upper-left edge. Offsets are in drawing units."""
        m = Image.new('L', self.col.size, 0); draw(ImageDraw.Draw(m)); m = np.asarray(m) > 127
        def edge(dx, dy):   # the part of the shape that leaves it when moved by (dx, dy) units
            dx, dy = round(dx * K), round(dy * K); sh = np.zeros_like(m)
            H, W = m.shape
            sh[max(0, -dy):H - max(0, dy), max(0, -dx):W - max(0, dx)] = m[max(0, dy):H - max(0, -dy), max(0, dx):W - max(0, -dx)]
            return m & ~sh
        a = np.asarray(self.col).copy(); b = np.array(base, float)
        mix = lambda t, w: tuple(int(round(b[i] * (1 - w) + t[i] * w)) for i in range(3)) + (255,)
        if light: a[edge(-light[0], -light[1])] = mix((255, 244, 222), 0.22)
        if shadow:
            a[edge(*shadow)] = mix((70, 36, 96), 0.26)
            a[edge(shadow[0] * 0.45, shadow[1] * 0.45)] = mix((40, 16, 60), 0.42)
        self.col = Image.fromarray(a, 'RGBA'); self.c = ImageDraw.Draw(self.col)

    def strands(self, pts, lines, col, w=0.7):
        """Hair texture: thin strands in a darker or lighter tone, kept inside the shape pts (drawn in colour, not ink)."""
        layer = Image.new('RGBA', self.col.size, (0, 0, 0, 0)); d = ImageDraw.Draw(layer)
        for ln in lines: d.line(self.P(ln), fill=tuple(col) + (255,), width=max(1, round(w * K)))
        m = Image.new('L', self.col.size, 0); ImageDraw.Draw(m).polygon(self.P(pts), fill=255)
        layer.putalpha(Image.fromarray(np.minimum(np.asarray(layer)[..., 3], np.asarray(m)), 'L'))
        self.col.alpha_composite(layer); self.c = ImageDraw.Draw(self.col)

    def line(self, pts, w=2.0, col=INK):
        self.s.line(self.S(pts), fill=tuple(col) + (255,), width=1 if w < 2.1 else 2)   # outlines (2.2 and up) are bold like Bolormaa's ink

    def oline(self, pts, w=2.0):   # an outline round a closed shape
        self.line(pts + [pts[0]], w)

    def oell(self, cx, cy, rx, ry, w=2.0):
        self.s.ellipse(self.box(cx, cy, rx, ry), outline=INK + (255,), width=1)


def bez(p0, p1, p2, n=20):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0], (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in [i / n for i in range(n + 1)]]


def hair_lines(seed, x0, x1, y0, y1, n, sway=8):
    """n strands falling from the top of a box, each a gentle curve; the same seed gives the same strands."""
    import random
    r = random.Random(seed); out = []
    for i in range(n):
        x = x0 + (x1 - x0) * (i + r.random()) / n; top = y0 + r.random() * 6; bot = y1 - r.random() * (y1 - y0) * 0.4
        mid = (x + (x - (x0 + x1) / 2) * 0.25 + r.uniform(-sway, sway) * 0.3, (top + bot) / 2)
        out.append(bez((x, top), mid, (x + (x - (x0 + x1) / 2) * 0.35, bot), 12))
    return out


def finish(cv, colours=64):
    """The reduction to pixel art: average the shapes down to a small palette, then lay the crisp lines on top."""
    small = cv.col.resize((PW, PH), Image.BOX)
    rgbim = small.convert('RGB').quantize(colors=colours, method=Image.MEDIANCUT, dither=Image.NONE).convert('RGBA')
    src, out = np.asarray(small), np.asarray(rgbim).copy(); white = src[..., :3].min(axis=2) > 225   # eye whites and teeth are
    out[white] = src[white]; rgbim = Image.fromarray(out, 'RGBA')                                      # too small to win a palette slot
    rgbim.alpha_composite(cv.sm)
    return rgbim


# ---------------------------------------------------------------- the face
EYE = {-1: (51, 66, 1.12), 1: (83, 64, 0.88)}   # each eye's centre and width: the far eye is a little narrower (the face is turned to our right)


def eyes(cv, c, kind, look=(0, 0), only=None):
    iris = rgb(c['iris']); lx, ly = look
    for s in (-1, 1):
        if only and s != only: continue
        cx, cy, wf = EYE[s]
        if kind in ('open', 'wide', 'look', 'down'):
            h = 5.8 if kind != 'wide' else 7.4   # almond eyes, the size of Bolormaa's
            if kind == 'down': h = 4.4
            cv.poly([(cx - 10 * wf, cy + 1), (cx - 5 * wf, cy - h), (cx + 5 * wf, cy - h), (cx + 11 * wf, cy - 1), (cx + 6 * wf, cy + 4), (cx - 6 * wf, cy + 4)], (250, 246, 240))
            cv.ell(cx + lx, cy - 0.5 + ly, 4.6 * wf, min(h, 5.4) if kind != 'down' else 4.0, iris)
            cv.ell(cx + lx, cy - 2.2 + ly, 4.2 * wf, 2.2, tuple(round(v * 0.55) for v in iris))   # the lid's shadow on the iris
            cv.ell(cx + lx, cy - 0.5 + ly, 2.4 * wf, 2.8, (24, 12, 10))
            cv.ell(cx - 2 * wf + lx, cy - 3 + ly, 1.9, 1.9, (255, 255, 255))
            cv.line([(cx - 11 * wf, cy + 2), (cx - 5 * wf, cy - h - 1), (cx + 5 * wf, cy - h - 1), (cx + 12 * wf, cy - 1)], 3.4)   # heavy black lashes
            cv.line([(cx + s * 11 * wf, cy - 1), (cx + s * 14 * wf, cy - 3)], 2.2)
        elif kind == 'happy':
            cv.line(bez((cx - 10 * wf, cy + 1), (cx, cy - 8), (cx + 10 * wf, cy + 1)), 3.2)
        elif kind == 'shut':
            cv.line(bez((cx - 10 * wf, cy - 1), (cx, cy + 4), (cx + 10 * wf, cy - 1)), 2.8)
        elif kind == 'half':
            cv.poly([(cx - 10 * wf, cy + 1), (cx + 11 * wf, cy - 1), (cx + 6 * wf, cy + 4), (cx - 6 * wf, cy + 4)], (250, 246, 240))
            cv.ell(cx, cy + 0.5, 4.8 * wf, 3.4, iris); cv.ell(cx, cy + 0.5, 2.2 * wf, 2.0, (24, 12, 10))
            cv.line([(cx - 11 * wf, cy + 1), (cx + 12 * wf, cy - 1.5)], 3.4)


def brows(cv, c, inner, outer):
    """Thick brows: the near one long and nearly level, the far one shorter and rising outwards."""
    col = rgb(c['brow']) if 'brow' in c else INK
    cv.line([(37, 57 + outer), (48, 55 + (inner + outer) / 2), (62, 55 + inner)], 3.6, col)
    cv.line([(74, 55 + inner), (82, 52 + (inner + outer) / 2), (91, 49 + outer)], 3.6, col)


def mouth(cv, c, kind, talk):
    lip, lipl = rgb(c.get('lips', '#d77a68')), rgb(c.get('lipsl', '#f2aa98'))
    cx, cy = 69, 88
    if kind == 'grin' or talk == 2:
        cv.poly(bez((cx - 11, cy - 1), (cx, cy + 12), (cx + 11, cy - 2)), (130, 40, 50)); cv.poly([(cx - 9, cy - 1), (cx + 9, cy - 2), (cx + 8, cy + 1), (cx - 8, cy + 2)], (255, 255, 255))
        cv.line(bez((cx - 11, cy - 1), (cx, cy + 12), (cx + 11, cy - 2)) + [(cx - 11, cy - 1)], 1.8)
    elif talk == 1:
        cv.ell(cx, cy + 1.5, 5.4, 3.8, (130, 40, 50)); cv.line([(cx - 6, cy), (cx + 6, cy - 0.5)], 1.6); cv.ell(cx, cy + 3, 2.8, 1.2, (230, 120, 130))
    elif kind in ('smile', 'smug'):   # closed lips, pink with a light middle, the right corner turning up
        cv.poly([(cx - 9, cy), (cx + 9, cy - 1), (cx + 6, cy + 6), (cx - 6, cy + 6)], lip); cv.poly([(cx - 3, cy + 2.5), (cx + 4, cy + 2.5), (cx + 3, cy + 5), (cx - 2, cy + 5)], lipl)
        cv.line([(cx - 11, cy - 1.5), (cx - 4, cy + 0.5), (cx + 4, cy + 0.5), (cx + 10, cy - 1.5), (cx + 12, cy - (4.5 if kind == 'smug' else 3))], 1.8)
    elif kind == 'o':
        cv.ell(cx, cy + 1, 3.8, 4.8, (130, 40, 50)); cv.oell(cx, cy + 1, 3.8, 4.8, 1.6)
    elif kind == 'sad':
        cv.poly([(cx - 7, cy + 2), (cx + 7, cy + 2), (cx + 4, cy + 6), (cx - 4, cy + 6)], lip)
        cv.line(bez((cx - 8, cy + 3), (cx, cy - 1), (cx + 8, cy + 3)), 1.8)
    else:
        cv.poly([(cx - 8, cy), (cx + 8, cy - 0.5), (cx + 5, cy + 5), (cx - 5, cy + 5)], lip); cv.poly([(cx - 3, cy + 2), (cx + 3, cy + 2), (cx + 2, cy + 4), (cx - 2, cy + 4)], lipl); cv.line([(cx - 10, cy), (cx + 10, cy - 0.5), (cx + 11, cy - 2)], 1.8)


FACE = {'neutral': ('open', (0, 0), 'flat'), 'happy': ('open', (1, -1), 'smile'), 'laugh': ('happy', (-1, -2), 'grin'), 'wink': ('wink', (0, -2), 'smile'),
        'surprised': ('wide', (-4, -4), 'o'), 'think': ('look', (-2, 1), 'flat'), 'sad': ('down', (-3, 3), 'sad'), 'proud': ('half', (1, -3), 'smug'),
        'tease': ('half', (2, -3), 'smug'), 'excited': ('wide', (-3, -3), 'grin'), 'worried': ('open', (-4, 3), 'sad'), 'shy': ('down', (-1, 1), 'smile'),
        'curious': ('wide', (-1, -4), 'o'), 'stern': ('half', (3, -1), 'flat')}


def bust(c, expr='happy', talk=0, blink=False):
    """One portrait, the face turned a little to our right. c: skin, iris, hair (dark, mid, light), deel, panel, trim;
    hairdo braids | bun | bob | short; hat mongol | loovuuz | cap | flower; beard, mustache, glasses, jewels, elder, fem."""
    cv = Canvas2(); fem = c.get('fem', False)
    skin, skin_d = rgb(c['skin']), rgb(c['skin_d'])
    hair, hair_d, hair_l = rgb(c['hair'][1]), rgb(c['hair'][0]), rgb(c['hair'][2])
    deel, deel_d, panel, gold = rgb(c['deel']), rgb(c['deel_d']), rgb(c['panel']), rgb(c['trim'])
    do = c.get('hairdo', 'short'); hat = c.get('hat'); long_ = fem and do in ('braids', 'bob', 'long')
    cv.poly([(0, 0), (UW, 0), (UW, UH), (0, UH)], rgb(c.get('bg', '#3c2d7a')))     # the indigo background
    # hair behind the head: a full mass framing the face, down to the shoulders for the women
    if fem:
        back = [(14, 116 if long_ else 90), (12, 60), (18, 34), (30, 16), (50, 5), (72, 4), (90, 12), (102, 30), (106, 56), (106, 116 if long_ else 90)]
        cv.poly(back, hair, form=((5, 3), (2, 2))); cv.oline(back, 2.2)
        cv.strands(back, hair_lines(c['hair'][1], 12, 106, 8, 118 if long_ else 92, 22), hair_d)
        cv.strands(back, hair_lines(c['hair'][2], 14, 104, 8, 100 if long_ else 80, 9), hair_l, 0.5)
    if do == 'bun':
        cv.ell(64, 9, 16, 10, hair, form=((3, 3), (1.5, 1.5))); cv.oell(64, 9, 16, 10, 2.2); cv.line([(52, 6), (66, 2)], 1.4, hair_l)
    # the deel: a dark blue body with teal shoulders, a high mandarin collar edged in gold, a gold knot clasp
    bw = 4 if c.get('broad') else 0
    body = [(0, 144), (0, 120), (30 - bw, 108), (92 + bw, 108), (112, 118), (112, 144)]
    cv.poly(body, deel, form=((4, 6), (0, 2))); cv.line(body[1:-1], 2.2)
    for sh in ([(0, 144), (0, 122), (28 - bw, 112), (44, 126), (40, 144)], [(76, 144), (82, 128), (100 + bw, 116), (112, 122), (112, 144)]):
        cv.poly(sh, panel, form=((5, 6), (2, 2))); cv.line(sh[1:-1], 2.0)
        cv.strands(sh, [bez((sh[2][0] + 4, sh[2][1] + 4), (sh[2][0] + 8, sh[2][1] + 14), (sh[2][0] + 6, 144), 10)], tuple(round(v * 0.82) for v in panel), 0.9)   # a fold
    cv.poly([(44, 126), (66, 128), (66, 144), (40, 144)], deel_d)
    if c.get('medal'): cv.ell(26, 132, 4.5, 4.5, rgb('#ffcf3a')); cv.oell(26, 132, 4.5, 4.5, 1.6)
    cv.poly([(44, 92), (72, 92), (72, 112), (44, 112)], skin); cv.poly([(44, 92), (72, 92), (72, 101), (44, 98)], skin_d)     # the neck
    cv.line([(45, 90), (46, 108)], 2.0); cv.line([(72, 96), (72, 110)], 2.0)
    if c.get('fur'):
        fur = rgb(c['fur']); fc = [(30, 108), (94, 106), (98, 118), (66, 126), (28, 118)]; cv.poly(fc, fur, form=((3, 4), (1.5, 1.5))); cv.oline(fc, 2.0)
        cv.strands(fc, [[(x, 107), (x + 2, 124)] for x in range(32, 96, 4)], tuple(round(v * 0.8) for v in fur), 0.6)
    else:
        collar = [(36, 100), (68, 110), (86, 104), (88, 118), (68, 128), (38, 116)]
        cv.poly(collar, deel, form=((3, 3), (1.5, 1.5))); cv.poly([(68, 110), (86, 104), (88, 118), (68, 128)], deel_d); cv.oline(collar, 2.2)
        cv.line([(37, 101), (68, 111), (86, 105)], 2.2, gold); cv.line([(68, 112), (68, 127)], 2.2, gold)
    cv.line([(61, 126), (75, 126)], 2.2, gold); cv.line([(68, 122), (68, 138)], 2.2, gold)
    # the face, turned a little to our right: the near cheek and the ear on the left, the far cheek in shade on the right
    jaw = 0 if fem else 2
    face = [(36, 50), (44, 36), (62, 28), (82, 32), (92, 44), (94, 58), (93 + jaw, 70), (90 + jaw, 84), (84 + jaw, 95), (74, 102), (66, 104), (56, 100), (44 - jaw, 92), (36 - jaw, 82), (34, 68)]
    cv.poly(face, skin, form=((10, 5), (2, 1.5)))
    cv.oline(face, 2.2)
    # Keep the dialogue cast distinct, while giving every face Bolormaa's warm, softly lit finish.
    rose = tuple(round(skin[i] * 0.84 + rgb('#dc766b')[i] * 0.16) for i in range(3))
    cv.ell(46, 80, 4.5, 2.3, rose)
    cv.ell(84, 78, 3.4, 2.0, tuple(round(skin_d[i] * 0.7 + rose[i] * 0.3) for i in range(3)))
    cv.ell(54, 48, 3.0, 1.2, tuple(round(skin[i] * 0.7 + rgb('#fff0d7')[i] * 0.3) for i in range(3)))
    cv.ell(31, 76, 5, 8, skin); cv.ell(32, 77, 2.4, 4.5, skin_d); cv.oell(31, 76, 5, 8, 2.0)               # the ear we see
    # the features
    ek, (bi, bo), mk = FACE[expr]
    brows(cv, c, bi + (0 if fem else 1), bo)
    if blink and ek in ('open', 'wide', 'look', 'down', 'half'): eyes(cv, c, 'shut')
    elif ek == 'wink': eyes(cv, c, 'open', only=-1); eyes(cv, c, 'happy', only=1)
    else: eyes(cv, c, ek, (2, -2) if ek == 'look' else (0, 1) if ek == 'down' else (0, 0))
    if c.get('elder'):
        cv.line([(38, 70), (40, 74)], 1.2, skin_d); cv.line([(92, 66), (91, 70)], 1.2, skin_d); cv.line([(58, 82), (59, 90)], 1.2, skin_d); cv.line([(82, 80), (81, 88)], 1.2, skin_d)
    cv.line([(72, 72), (73, 78), (70, 81)], 2.0); cv.ell(67.5, 80.5, 1.0, 1.0, INK)                          # the nose line and a nostril
    if c.get('mark'): cv.ell(89, 72, 1.1, 1.1, INK)                                                         # the beauty mark under the far eye
    mouth(cv, c, mk, talk)
    if expr == 'shy': cv.ell(48, 80, 5, 2.5, (236, 140, 120)); cv.ell(86, 78, 4, 2.5, (236, 140, 120))
    if c.get('mustache') or c.get('beard'):
        if c.get('beard'): bd = [(46, 90), (92, 84), (86, 106), (70, 114), (52, 106)]; cv.poly(bd, hair, form=((3, 4), (1.5, 1.5))); cv.strands(bd, hair_lines(c['hair'][0], 48, 90, 86, 112, 10, 2), hair_d); cv.oline(bd, 2.0); cv.line([(62, 98), (64, 110)], 1.4, hair_l)
        mu = [(56, 87), (66, 83), (74, 83), (84, 85), (82, 89), (74, 87), (66, 87), (58, 91)]; cv.poly(mu, hair); cv.oline(mu, 1.6)
    if c.get('glasses'):
        cv.oell(51, 66, 12, 9.5, 2.2); cv.oell(83, 64, 9, 8.5, 2.2); cv.line([(63, 65), (74, 64)], 2.0); cv.line([(39, 66), (34, 70)], 2.0)
    # hair in front: from a parting, locks sweep out over the forehead on both sides; long hair falls by the cheeks
    if fem:
        left = [(60, 6), (57, 20), (53, 33), (50, 44), (46, 37), (43, 48), (40, 43), (36, 56), (35, 72), (24, 76), (20, 52), (26, 28), (40, 12)]
        right = [(62, 6), (66, 18), (72, 30), (80, 38), (86, 47), (88, 41), (92, 50), (94, 56), (95, 74), (96, 96), (104, 96), (106, 58), (100, 28), (84, 10)]
        for part in (left, right):
            cv.poly(part, hair, form=((4, 3), (2, 2)))
            cv.strands(part, hair_lines(c['hair'][0] + str(part[0]), min(p[0] for p in part), max(p[0] for p in part), 4, 100, 9), hair_d)
            cv.strands(part, hair_lines(c['hair'][2] + str(part[0]), min(p[0] for p in part), max(p[0] for p in part), 4, 60, 4), hair_l, 0.5)
        cv.line(left[:9], 2.2); cv.line(right[:11], 2.2)
        for pts in ([(56, 8), (42, 20), (30, 40)], [(54, 16), (44, 30), (38, 48)], [(50, 6), (32, 14), (22, 34)], [(48, 26), (42, 42)],
                    [(64, 8), (80, 16), (94, 36)], [(66, 16), (78, 28), (88, 42)], [(72, 6), (90, 12), (102, 32)], [(98, 44), (100, 88)],
                    [(18, 50), (16, 92)], [(26, 58), (24, 82)]):
            cv.line(bez(*pts) if len(pts) == 3 else pts, 1.6)
        cv.line([(48, 10), (60, 6), (76, 8)], 1.4, hair_l)
    else:   # men: short hair under the hat, or a side part
        top = [(34, 56), (34, 30), (46, 14), (64, 8), (84, 14), (96, 32), (96, 58), (90, 44), (70, 36), (50, 36), (40, 46)]
        cv.poly(top, hair, form=((3, 3), (2, 2)))
        cv.strands(top, hair_lines(c['hair'][0], 34, 96, 6, 56, 14), hair_d); cv.strands(top, hair_lines(c['hair'][2], 40, 90, 6, 34, 5), hair_l, 0.5)
        cv.oline(top, 2.2)
        for pts in ([(42, 24), (54, 34)], [(62, 12), (66, 30)], [(76, 16), (86, 34)]): cv.line(pts, 1.6)
        cv.line([(50, 14), (70, 12), (90, 28)], 1.4, hair_l)
        cv.line([(40, 29), (44, 23), (50, 19)], 1.2, hair_d)
        cv.line([(79, 19), (85, 25), (89, 32)], 1.2, hair_d)
    if do == 'braids':   # two thick braids hanging in front of the shoulders
        for x0, top, w in ((22, 84, 9), (98, 92, 7)):
            for y in range(top, 146, 9):
                lobe = [(x0 - w, y), (x0, y - 2), (x0 + w, y + 2), (x0 + w, y + 8), (x0, y + 10), (x0 - w, y + 6)]
                cv.poly(lobe, hair, form=((2.5, 2.5), (1.2, 1.2))); cv.line([(x0 - w, y + 6), (x0, y + 10), (x0 + w, y + 8)], 2.0); cv.line([(x0 - w + 2, y + 2), (x0 + 1, y + 1)], 1.4, hair_l)
            cv.line([(x0 - w, top), (x0 - w, 144)], 2.0); cv.line([(x0 + w, top + 2), (x0 + w, 144)], 2.0)
    # hats
    if hat == 'mongol':
        cv.poly([(44, 22), (54, 4), (74, 4), (84, 22)], rgb('#d83a2a'), form=((4, 3), (1.5, 1.5))); cv.poly([(60, 4), (68, 4), (66, 22), (62, 22)], rgb('#ff7050')); cv.oline([(44, 22), (54, 4), (74, 4), (84, 22)], 2.2)
        cv.poly([(24, 34), (104, 34), (98, 18), (30, 18)], rgb('#3a2820'), form=((5, 4), (2, 2))); cv.oline([(24, 34), (104, 34), (98, 18), (30, 18)], 2.2); cv.ell(64, 5, 3.4, 3.4, rgb('#ffcf3a')); cv.oell(64, 5, 3.4, 3.4, 1.4)
    elif hat == 'loovuuz':
        cv.poly([(42, 18), (64, 2), (86, 18)], rgb('#2f6fdb'), form=((4, 3), (1.5, 1.5))); cv.oline([(42, 18), (64, 2), (86, 18)], 2.2)
        fur = rgb('#8a5a33'); band = [(20, 36), (106, 36), (106, 16), (20, 16)]; cv.poly(band, fur, form=((5, 5), (2, 2)))
        cv.strands(band, [[(x, 15), (x + 1.5, 37)] for x in range(22, 106, 3)], tuple(round(v * 0.78) for v in fur), 0.6); cv.oline(band, 2.2)
        for x in (24, 102): fl = [(x - 6, 32), (x + 6, 32), (x + 6, 80), (x - 6, 80)]; cv.poly(fl, fur, form=((3, 4), (1.5, 1.5))); cv.oline(fl, 2.0)
        cv.line([(24, 22), (102, 22)], 1.6, rgb('#c49a6a'))
    elif hat == 'cap':
        cp = [(28, 38), (36, 12), (64, 4), (90, 12), (100, 38)]; cv.poly(cp, rgb('#3a5a8a'), form=((5, 4), (2, 2))); cv.oline(cp, 2.2)
        br = [(64, 34), (110, 34), (108, 42), (64, 40)]; cv.poly(br, rgb('#2a3f66')); cv.oline(br, 2.0)
    elif hat == 'flower':
        hb = [(24, 24), (102, 24), (102, 30), (24, 30)]; cv.poly(hb, rgb('#ff6fa8')); cv.oline(hb, 1.8)
        cv.ell(92, 24, 7, 7, rgb('#ffd34a')); cv.ell(92, 24, 3, 3, rgb('#ff8a1f')); cv.oell(92, 24, 7, 7, 1.6)
    # silver and turquoise: a chain from the parting to a framed oval at the hairline with a drop below it, and on each
    # side a square plate with long strands of silver and turquoise
    if c.get('jewels'):
        silver, turq, white = rgb('#d6dde6'), rgb('#22b4c8'), rgb('#ffffff')
        cv.line([(55, 6), (62, 18)], 2.0, silver)
        cv.ell(63, 24, 6.5, 6.5, silver); cv.ell(63, 24, 4.0, 4.4, turq); cv.ell(61.5, 22, 1.0, 1.0, white); cv.oell(63, 24, 6.5, 6.5, 1.6)
        cv.line([(64, 31), (65, 39)], 2.0, silver); cv.ell(65, 42, 2.0, 2.2, turq); cv.ell(65, 46.5, 1.6, 1.8, white)
        for x0, x1, bottom in ((14, 24, 84), (96, 104, 80)):
            pl = [(x0, 44), (x1, 44), (x1, 54), (x0, 54)]
            cv.poly(pl, silver); cv.poly([(x0 + 2.5, 46.5), (x1 - 2.5, 46.5), (x1 - 2.5, 51.5), (x0 + 2.5, 51.5)], turq); cv.oline(pl, 1.6)
            for k, xx in enumerate((x0 + 2.5, x1 - 2.5)):
                for y in range(55, bottom, 4): cv.line([(xx, y), (xx, y + 4)], 2.0, turq if (y // 4 + k) % 2 and y < 70 else silver)
    return finish(cv)


def bolormaa_reference():
    """Fit the supplied Bolormaa design to one dialogue frame without changing its colors or pixel edges."""
    im = Image.open(REFERENCE).convert('RGBA')
    w, h = im.size; fit_h = round(w * PH / PW); y = max(0, (h - fit_h) // 2)
    return im.crop((0, y, w, y + fit_h)).resize((PW, PH), Image.Resampling.NEAREST)


def bolormaa_frame(base, talk, blink):
    """Use the exact reference portrait and add only the small mouth/eyelid changes needed for dialogue animation."""
    frame = base.copy(); d = ImageDraw.Draw(frame)
    skin = base.getpixel((50, 80)); ink = rgb('#08060a') + (255,)
    # the patches cover the whole reference eye or mouth (lashes, iris, highlights, lips, smile line), so none of it shows twice
    if blink:
        d.polygon([(38, 64), (41, 59), (47, 58), (59, 59), (62, 62), (62, 71), (58, 74), (41, 74), (38, 70)], fill=skin)
        d.polygon([(74, 59), (77, 56), (90, 56), (91, 59), (91, 66), (88, 69), (77, 69), (74, 66)], fill=skin)
        d.line([(39, 66), (44, 68), (52, 69), (60, 67)], fill=ink, width=2)
        d.line([(75, 63), (80, 65), (86, 65), (90, 63)], fill=ink, width=2)
    if talk:
        d.polygon([(57, 87), (60, 84), (80, 84), (81, 87), (78, 96), (62, 96)], fill=skin)
        d.line([(59, 88), (63, 88)], fill=ink, width=1); d.line([(76, 87), (79, 86)], fill=ink, width=1)
        d.ellipse((63, 86, 76, 94), fill=rgb('#7e3437') + (255,), outline=ink, width=1)
        d.line([(66, 87), (73, 87)], fill=(255, 239, 224, 255), width=2)
        d.line([(66, 92), (73, 92)], fill=(208, 105, 103, 255), width=1)
    return frame


WARM, TAN, BROWN, LIGHT = ('#eaa77c', '#c9805a'), ('#d9986c', '#b87850'), ('#c08458', '#9a6440'), ('#f2b890', '#d48e68')
DARK, GREY = ('#2e170e', '#4a2818', '#6e3e26'), ('#8a8690', '#c4c0c8', '#ecebf0')
BLACKH = ('#1c1014', '#2e1c22', '#4a3440')
PEOPLE = {
    'bolormaa': dict(skin='#f0a77a', skin_d='#d7865c', iris='#5a2a12', brow='#1a1014', hair=('#2a140c', '#4a2616', '#6a3a22'), deel='#1d3a8f', deel_d='#132a66', panel='#1a8a8a', trim='#d4a23a', hairdo='braids', fem=True, jewels=True, mark=True),
    'emee': dict(skin=WARM[0], skin_d=WARM[1], iris='#5a3a2a', brow='#6a6470', hair=GREY, deel='#6a3fb0', deel_d='#4a2a80', panel='#8f5bd6', trim='#ffcf3a', hairdo='bun', fem=True, glasses=True, elder=True),
    'zasuul': dict(skin=TAN[0], skin_d=TAN[1], iris='#4a2a1a', hair=BLACKH, deel='#e8a812', deel_d='#b07a00', panel='#ffc94a', trim='#d83a2a', hat='mongol', mustache=True),
    'general': dict(skin=BROWN[0], skin_d=BROWN[1], iris='#3a2014', hair=BLACKH, deel='#2f7a3a', deel_d='#1f5428', panel='#3f9a4a', trim='#ffcf3a', hat='loovuuz', mustache=True, broad=True, medal=True),
    'historian': dict(skin=WARM[0], skin_d=WARM[1], iris='#5a2a7a', brow='#2a1610', hair=BLACKH, deel='#8a3fc0', deel_d='#5e2a8a', panel='#ffffff', trim='#ffcf3a', hairdo='bun', fem=True, glasses=True),
    'herder': dict(skin=TAN[0], skin_d=TAN[1], iris='#4a2a1a', hair=BLACKH, deel='#8a4a22', deel_d='#5e3014', panel='#2fa35a', trim='#ffcf3a', hat='mongol'),
    'fisher': dict(skin=BROWN[0], skin_d=BROWN[1], iris='#3a2a1a', brow='#c4c0c8', hair=GREY, deel='#16928c', deel_d='#0e6662', panel='#2a5fb8', trim='#ffffff', hat='cap', beard=True, elder=True),
    'farmer': dict(skin=WARM[0], skin_d=WARM[1], iris='#5a2a1a', brow='#2a1610', hair=('#2a140c', '#4a2414', '#6e3e26'), deel='#2fa34e', deel_d='#1f7034', panel='#ff6fa8', trim='#ffcf3a', hairdo='braids', hat='flower', fem=True),
    'teacher': dict(skin=LIGHT[0], skin_d=LIGHT[1], iris='#2a4a7a', brow='#2a1610', hair=('#1c1014', '#3a2418', '#5a3e30'), deel='#2f6fdb', deel_d='#1f4a96', panel='#ffffff', trim='#ffffff', hairdo='bob', fem=True, glasses=True),
    'trainer': dict(skin=TAN[0], skin_d=TAN[1], iris='#4a2a1a', hair=BLACKH, deel='#e89a12', deel_d='#a86a00', panel='#d83a2a', trim='#3a2820', hat='loovuuz'),
    'archer': dict(skin=BROWN[0], skin_d=BROWN[1], iris='#3a2014', hair=BLACKH, deel='#c8321e', deel_d='#8a2212', panel='#ffcf3a', trim='#ffcf3a', hat='mongol', medal=True),
    'craftsman': dict(skin=BROWN[0], skin_d=BROWN[1], iris='#3a2a1a', brow='#c4c0c8', hair=GREY, deel='#7a4a26', deel_d='#523018', panel='#2f6fdb', trim='#ffcf3a', hat='mongol', mustache=True, elder=True, broad=True, fur='#c8a070'),
}
FACES = ['neutral', 'happy', 'laugh', 'wink', 'surprised', 'think', 'sad', 'proud', 'tease', 'excited', 'worried', 'shy', 'curious', 'stern']


def main():
    import json, os, sys
    root = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Resources')
    out = os.environ.get('ART_OUT', os.path.join(root, 'Art')); prev = sys.argv[1] if len(sys.argv) > 1 else None
    demo = []
    for pid, c in PEOPLE.items():
        states = [(e, talk, blink) for e in FACES for (talk, blink) in ((0, False), (1, False), (0, True), (1, True))]
        frames = [bust(c, e, talk, blink) for e, talk, blink in states]
        if pid == 'bolormaa':
            base = bolormaa_reference()
            frames = [bolormaa_frame(base, talk, blink) for _expr, talk, blink in states]
        sh = Image.new('RGBA', (PW * 4, PH * len(FACES)))
        for i, im in enumerate(frames): sh.paste(im, ((i % 4) * PW, (i // 4) * PH))
        sh.save(os.path.join(out, f'bust_{pid}.png.bytes'), 'PNG', optimize=True)
        demo.append(frames[FACES.index('happy') * 4])
    if not os.environ.get('ART_OUT'):
        with open(os.path.join(root, 'Data', 'bust.json'), 'w') as f: json.dump({'W': PW, 'H': PH, 'faces': FACES, 'cols': 4, 'people': list(PEOPLE)}, f)
    if prev:
        m = Image.new('RGBA', (PW * 6 * 5, PH * 2 * 5), (52, 40, 110, 255))
        for i, im in enumerate(demo): m.alpha_composite(im.resize((PW * 5, PH * 5), Image.NEAREST), ((i % 6) * PW * 5, (i // 6) * PH * 5))
        m.save(os.path.join(prev, 'busts_all.png'))


if __name__ == '__main__':
    main()
