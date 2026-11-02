# The wrestler of the online arena (Бөхийн өргөө), drawn like a fighting-game sprite: a stocky Mongolian bökh in zodog
# (the open-chested jacket with long sleeves), shuudag (the short briefs) and gutal (the boots with upturned toes), in
# every pose of the bout: the stance, footwork, shoves, sweeps, grabs, the clinch and its throws, falls and the eagle dance.
# Drawn smooth like hero.py (4× supersampled, then scaled down) on a skeleton, one 128×128 cell per frame, facing right.
#
# The skin and the zodog are tinted in the game (any skin, red or blue zodog), so each frame is drawn three times: with
# both tints black, with white skin, and with white cloth. Shading multiplies the tint, so the picture is linear in the
# tints: arena_fighter.png holds the untinted parts (outline, hair, boots, eyes, the black of the tinted parts) and
# arena_fighter_tint.png how much skin (red channel) and cloth (green channel) each pixel takes. The game adds them up:
#   colour = fixed + skin_weight × skin + cloth_weight × cloth
# The frame table goes to Resources/Data/arena_frames.json.
#   python3 unity/Tools/art/fighter.py [preview_dir]
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SS = 4
CELL = 128
ANCHOR = 56   # the pelvis x in the cell: wrestlers reach forward more than back
GROUND = 122
COLS = 10
HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, '..', '..', 'Assets', 'Resources', 'Art')
DATA = os.path.join(HERE, '..', '..', 'Assets', 'Resources', 'Data')

INK = (44, 26, 32)
HAIR, HAIR_L = (34, 24, 22), (70, 52, 44)
BOOT, BOOT_D, BOOT_L = (92, 52, 30), (58, 32, 20), (132, 86, 50)
BOOT_TRIM = [(200, 52, 43), (46, 138, 74), (232, 190, 70)]
GOLD = (236, 196, 84)
ROPE = (238, 222, 172)


def dirv(a):
    r = math.radians(a)
    return (math.sin(r), math.cos(r))


def add(a, b, k=1.0):
    return (a[0] + b[0] * k, a[1] + b[1] * k)


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def rot(p, c, deg):
    r = math.radians(deg); s, co = math.sin(r), math.cos(r)
    x, y = p[0] - c[0], p[1] - c[1]
    return (c[0] + x * co - y * s, c[1] + x * s + y * co)


class Pen:
    """Draws in cell pixels on a 4× image. Colours for the tinted parts are a shade k of the skin or cloth tint."""
    def __init__(self, skin, cloth):
        self.im = Image.new('RGBA', (CELL * SS, CELL * SS), (0, 0, 0, 0)); self.d = ImageDraw.Draw(self.im)
        self.skin, self.cloth = skin, cloth

    def t(self, kind, k):
        base = self.skin if kind == 'skin' else self.cloth
        return tuple(int(min(255, v * k)) for v in base) + (255,)

    def P(self, pts): return [(x * SS, y * SS) for x, y in pts]

    def ell(self, c, rx, ry, fill):
        self.d.ellipse([(c[0] - rx) * SS, (c[1] - ry) * SS, (c[0] + rx) * SS, (c[1] + ry) * SS], fill=fill)

    def poly(self, pts, fill): self.d.polygon(self.P(pts), fill=fill)

    def line(self, pts, col, w=1.0): self.d.line(self.P(pts), fill=col, width=max(1, int(w * SS)), joint='curve')

    def capsule(self, a, b, r0, r1, fill, line=None, lw=1.0):
        ang = math.atan2(b[1] - a[1], b[0] - a[0]); nx, ny = -math.sin(ang), math.cos(ang)
        if line:
            self.capsule(a, b, r0 + lw, r1 + lw, line)
        pts = [(a[0] + nx * r0, a[1] + ny * r0), (b[0] + nx * r1, b[1] + ny * r1), (b[0] - nx * r1, b[1] - ny * r1), (a[0] - nx * r0, a[1] - ny * r0)]
        self.ell(a, r0, r0, fill); self.ell(b, r1, r1, fill); self.poly(pts, fill)

    def shaded(self, a, b, r0, r1, kind, lw=0.9):
        """A rounded limb in a tint: outlined, in shadow, with its lit side on top (the light comes from above)."""
        self.capsule(a, b, r0, r1, self.t(kind, 0.62), INK + (255,), lw)
        off = (-0.0, -0.32)
        self.capsule(add(a, off, r0), add(b, off, r1), r0 * 0.66, r1 * 0.66, self.t(kind, 0.9))


# ---------------------------------------------------------------- the skeleton
def body(pose):
    """Joint positions for a pose (see P), before the whole body is turned and set on the ground."""
    lean = pose['lean']; u = (math.sin(math.radians(lean)), -math.cos(math.radians(lean))); fwd = (-u[1], u[0])
    pelvis = (ANCHOR + pose['dx'], 80.0)
    T = lambda x, y: (pelvis[0] + fwd[0] * x + u[0] * y, pelvis[1] + fwd[1] * x + u[1] * y)
    j = {'pelvis': pelvis, 'T': T, 'u': u, 'fwd': fwd}
    j['neck'] = T(0, 33)
    hu = rot(u, (0, 0), pose['tilt'])
    j['head'] = add(add(j['neck'], hu, 12.5), fwd, 2)
    j['shF'], j['shB'] = T(7, 29), T(-9, 30)
    j['hipF'], j['hipB'] = T(4, 1), T(-4, 1)
    for side, sh in (('F', j['shF']), ('B', j['shB'])):
        a1, a2 = pose['fa' if side == 'F' else 'ba']
        el = add(sh, dirv(a1), 18); ha = add(el, dirv(a2), 17)
        j['el' + side], j['ha' + side] = el, ha
    for side, hip in (('F', j['hipF']), ('B', j['hipB'])):
        l1, l2, foot = pose['fl' if side == 'F' else 'bl']
        kn = add(hip, dirv(l1), 22); an = add(kn, dirv(l2), 21)
        j['kn' + side], j['an' + side], j['ft' + side] = kn, an, foot
    return j


def place(pose, j):
    """Turns the body (pose rot, around the pelvis) and stands it on the ground (unless it is in the air or lying)."""
    keys = [k for k in j if isinstance(j[k], tuple) and len(j[k]) == 2 and k not in ('u', 'fwd')]
    if pose['rot']:
        for k in keys: j[k] = rot(j[k], j['pelvis'], pose['rot'])
    lowest = max(max(j['anF'][1], j['anB'][1]) + 4.5, j['head'][1] + 10, max(j['haF'][1], j['haB'][1]) + 4, j['pelvis'][1] + 9)
    dy = GROUND - lowest - pose['lift']
    for k in keys: j[k] = (j[k][0], j[k][1] + dy)
    turn = pose['rot']
    j['T'] = (lambda T0, d: (lambda x, y: (lambda p: (p[0], p[1] + d))(rot(T0(x, y), (ANCHOR + pose['dx'], 80.0), turn))))(j['T'], dy)
    return j


# ---------------------------------------------------------------- the parts
def arm(p, j, side):
    sh, el, ha = j['sh' + side], j['el' + side], j['ha' + side]
    wrist = lerp(el, ha, 0.78)
    p.shaded(sh, el, 7.8, 6.6, 'cloth')
    p.ell(lerp(sh, el, 0.45), 7.2, 7.2, p.t('cloth', 0.62))   # the biceps fills the sleeve
    p.ell(add(lerp(sh, el, 0.45), (0, -2.2)), 4.6, 4.2, p.t('cloth', 0.9))
    p.shaded(el, wrist, 6.6, 5.4, 'cloth')
    p.capsule(lerp(el, ha, 0.7), lerp(el, ha, 0.8), 5.6, 5.6, GOLD + (255,), INK + (255,), 0.7)  # the cuff
    p.ell(ha, 5.6, 5.6, INK + (255,)); p.ell(ha, 4.7, 4.7, p.t('skin', 0.74)); p.ell(add(ha, (-0.7, -1.1)), 2.9, 2.6, p.t('skin', 0.95))


def leg(p, j, side):
    hip, kn, an, foot = j['hip' + side], j['kn' + side], j['an' + side], j['ft' + side]
    p.shaded(hip, kn, 9.2, 6.8, 'skin')
    q = lerp(hip, kn, 0.42); p.ell(add(q, (0.8, -1.6)), 5.6, 4.4, p.t('skin', 0.95))   # the quads
    top = lerp(kn, an, 0.25)
    p.shaded(kn, top, 6.8, 6.4, 'skin')
    # the boot: leather up the shin, then the foot with its toe curling up
    p.capsule(top, an, 7.0, 6.0, BOOT_D + (255,), INK + (255,), 0.9)
    p.capsule(add(top, (-0.7, -0.3)), add(an, (-0.7, -0.3)), 5.0, 4.2, BOOT + (255,))
    for i, cc in enumerate(BOOT_TRIM):
        q = lerp(top, an, 0.18 + i * 0.22)
        p.capsule(add(q, (-3.8, 0)), add(q, (3.8, 0)), 1.1, 1.1, cc + (255,))
    fd = dirv(90 + foot)  # along the sole, toward the toe
    up = (fd[1], -fd[0]) if fd[0] >= 0 else (-fd[1], fd[0])
    heel = add(add(an, fd, -4.5), up, -3.6); toe = add(add(an, fd, 11), up, -2.2); curl = add(add(an, fd, 13.8), up, 1.8)
    sole = [add(heel, up, -1.2), add(toe, up, -1.4), curl, add(toe, up, 2.6), add(add(an, fd, 4), up, 5), add(an, up, 5.5), add(heel, up, 3)]
    p.poly([add(q, (0, 0)) for q in sole], INK + (255,))
    inner = [lerp(q, an, 0.18) for q in sole]
    p.poly(inner, BOOT + (255,))
    p.line([add(heel, up, -0.4), add(toe, up, -0.6), curl], BOOT_D + (255,), 1.0)
    p.line([add(add(an, fd, 1), up, 3.2), add(add(an, fd, 8), up, 1.4)], BOOT_TRIM[0] + (255,), 1.0)


def torso(p, j):
    T = j['T']
    skin_out = [T(12, 0), T(15, 9), T(16, 17), T(19.5, 24), T(15, 32), T(5, 36), T(-6, 36), T(-15, 33), T(-19, 24), T(-15, 12), T(-12, 0)]
    p.poly([add(q, (0, 0)) for q in skin_out], INK + (255,))
    inner = [lerp(q, T(0, 17), 0.07) for q in skin_out]
    p.poly(inner, p.t('skin', 0.66))
    # the lit chest and belly
    p.poly([T(10, 2), T(13.5, 10), T(14.5, 17.5), T(17.5, 23.5), T(13, 30), T(2, 32), T(-3, 22), T(-2, 8)], p.t('skin', 0.92))
    p.ell(T(10, 24.5), 7.5, 4.6, p.t('skin', 1.0))   # the pecs catch the light
    # pecs and belly lines
    p.line([T(18, 21.5), T(12, 19), T(5, 19.6), T(-1, 23.5)], p.t('skin', 0.56), 1.1)
    p.line([T(10.5, 20.5), T(10, 16.5)], p.t('skin', 0.64), 0.9)
    for y in (14, 9.5, 5.5):
        p.line([T(6, y), T(13, y + 0.4)], p.t('skin', 0.7), 0.8)
    p.ell(T(15.5, 20.2), 1.1, 1.1, p.t('skin', 0.5))
    # the zodog: a yoke over both shoulders and the back panel, open at the chest, tied by a string round the belly
    yoke = [T(8, 37), T(16, 33.5), T(13, 27), T(1, 26.5), T(-3, 15), T(-17, 12.5), T(-21, 24), T(-17, 33.5), T(-6, 38)]
    p.poly(yoke, INK + (255,))
    p.poly([lerp(q, T(-5, 27), 0.1) for q in yoke], p.t('cloth', 0.66))
    p.poly([T(7, 35.5), T(14, 32.5), T(11, 28), T(-1, 27.5), T(-8, 34), T(-4, 36.5)], p.t('cloth', 0.95))
    p.line([T(12.5, 27.5), T(1.5, 27), T(-2.5, 16), T(-16, 13.4)], GOLD + (255,), 0.9)
    p.line([T(-15, 13), T(-2, 11.5), T(10, 10.5), T(15, 10.8)], ROPE + (255,), 1.0)
    p.ell(T(14.5, 10.8), 1.3, 1.3, ROPE + (255,))
    # the shuudag
    briefs = [T(-13.5, 5), T(13, 5), T(14, -1), T(8, -9.5), T(0, -11), T(-8, -9.5), T(-14, -1)]
    p.poly(briefs, INK + (255,))
    p.poly([lerp(q, T(0, -2), 0.1) for q in briefs], p.t('cloth', 0.7))
    p.poly([T(-9, 3.6), T(10, 3.6), T(10.5, -0.5), T(5, -6), T(-6, -6), T(-10, -1)], p.t('cloth', 0.95))
    p.line([T(-12, 3.8), T(11.6, 3.8)], p.t('cloth', 0.5), 1.0)


EXPR = {}


def head(p, j, face):
    n, h, fwd = j['neck'], j['head'], j['fwd']
    p.capsule(n, lerp(n, h, 0.55), 7.0, 6.0, p.t('skin', 0.6), INK + (255,), 0.9)   # a bull neck
    p.ell(h, 9.6, 10.6, INK + (255,)); p.ell(h, 8.7, 9.7, p.t('skin', 0.72)); p.ell(add(h, (1.2, -1.0)), 6.8, 8.0, p.t('skin', 0.92))
    p.ell(add(h, (3.5, 7.0)), 5.2, 3.0, p.t('skin', 0.8))   # a strong jaw
    hx, hy = h
    # the nose and the ear
    p.ell((hx + 8.3, hy + 1.6), 2.0, 2.2, INK + (255,)); p.ell((hx + 8.0, hy + 1.5), 1.4, 1.6, p.t('skin', 0.85))
    p.ell((hx - 3.5, hy + 1.0), 2.4, 3.0, INK + (255,)); p.ell((hx - 3.4, hy + 1.0), 1.7, 2.3, p.t('skin', 0.66))
    # short hair, shaved at the back
    p.d.chord([(hx - 9.3) * SS, (hy - 10.6) * SS, (hx + 8.4) * SS, (hy + 6) * SS], 180, 352, fill=INK + (255,))
    p.d.chord([(hx - 8.5) * SS, (hy - 9.8) * SS, (hx + 7.6) * SS, (hy + 5) * SS], 182, 350, fill=HAIR + (255,))
    p.line([(hx - 4, hy - 7.5), (hx + 2, hy - 8.6)], HAIR_L + (255,), 1.0)
    p.poly([(hx - 8.8, hy - 3), (hx - 8.2, hy + 4), (hx - 6, hy + 4), (hx - 6.5, hy - 3)], HAIR + (150,))
    ex, ey = hx + 4.2, hy + 0.2   # the near eye; the far one is a sliver beside the nose
    ink = INK + (255,)
    if face in ('fierce', 'shout', 'grit'):
        p.line([(ex - 3.4, ey - 4.6), (ex + 2.6, ey - 2.4)], ink, 1.6)          # the brow, down toward the nose
        p.ell((ex, ey), 2.3, 1.7, (255, 255, 255, 255)); p.ell((ex + 0.9, ey + 0.2), 1.2, 1.4, ink)
        p.line([(ex - 2.6, ey - 1.6), (ex + 2.4, ey - 1.0)], ink, 0.9)
        p.line([(hx + 7.2, ey - 1.6), (hx + 7.9, ey + 0.6)], ink, 0.9)
    elif face == 'hurt':
        p.line([(ex - 3, ey - 3.4), (ex + 2.6, ey - 4.6)], ink, 1.4)
        p.line([(ex - 2.2, ey - 1.2), (ex + 1.6, ey + 0.4), (ex - 2.2, ey + 1.6)], ink, 1.1)
    elif face == 'ko':
        for s in (-1, 1): p.line([(ex - 1.8, ey - 1.8 * s), (ex + 1.8, ey + 1.8 * s)], ink, 1.0)
    elif face == 'dazed':
        pts = [(ex + math.cos(a) * a / 5.5, ey + math.sin(a) * a / 5.5) for a in [i * 0.5 for i in range(20)]]
        p.line(pts, ink, 0.7)
    elif face == 'happy':
        p.line([(ex - 3.4, ey - 4.8), (ex + 2.4, ey - 5.4)], ink, 1.3)
        p.line([(ex - 2.2, ey + 0.6), (ex, ey - 1.4), (ex + 2.2, ey + 0.6)], ink, 1.2)
    mx, my = hx + 5.4, hy + 5.6
    if face in ('shout', 'hurt', 'happy'):
        p.ell((mx, my + 0.6), 2.6, 2.0 if face != 'happy' else 1.6, ink); p.ell((mx + 0.2, my + 0.9), 1.8, 1.2, (150, 50, 60, 255))
        p.line([(mx - 1.8, my - 0.6), (mx + 2.0, my - 0.6)], (255, 255, 255, 255), 0.7)
    elif face == 'grit':
        p.ell((mx, my), 2.6, 1.3, ink); p.line([(mx - 1.8, my), (mx + 1.8, my)], (255, 255, 255, 255), 0.8)
    elif face == 'ko':
        p.ell((mx, my + 0.4), 1.4, 1.6, ink)
    else:
        p.line([(mx - 2.4, my + 0.4), (mx + 2.0, my - 0.2)], ink, 1.0)


def draw(pose, skin, cloth):
    p = Pen(skin, cloth)
    j = place(pose, body(pose))
    arm(p, j, 'B'); leg(p, j, 'B'); torso(p, j); leg(p, j, 'F'); head(p, j, pose['face']); arm(p, j, 'F')
    return p.im


def outline(im):
    """A soft dark outline round the whole figure (the same in all three renders)."""
    a = im.split()[3]
    grown = a.filter(ImageFilter.MaxFilter(2 * SS + 1))
    base = Image.new('RGBA', im.size, INK + (0,)); base.putalpha(grown)
    base.alpha_composite(im)
    return base


# ---------------------------------------------------------------- the poses
def P(lean=26, tilt=-14, fa=(70, 100), ba=(58, 92), fl=(30, -6, 0), bl=(-26, -16, 0), dx=0, lift=0, rot=0, face='fierce'):
    return dict(lean=lean, tilt=tilt, fa=fa, ba=ba, fl=fl, bl=bl, dx=dx, lift=lift, rot=rot, face=face)


def frames():
    F = []
    def add_pose(name, poses): F.append((name, poses))
    add_pose('idle', [P(lean=26 + d, fa=(70 + d, 100 + d), ba=(58 - d, 92 - d), fl=(30, -6 + d / 2, 0)) for d in (0, 1.5, 3, 1.5)])
    walk = []
    for i in range(6):
        ph = i / 6 * 2 * math.pi; s = math.sin(ph)
        walk.append(P(lean=27, fa=(70 + 6 * s, 102), ba=(58 - 6 * s, 92), fl=(14 + 20 * s, -10 + 8 * max(0, -s), 0), bl=(-12 - 20 * s, -18 + 8 * max(0, s), 0), lift=1.5 * abs(math.cos(ph))))
    add_pose('walk', walk)
    add_pose('dash', [P(lean=40, tilt=-22, fa=(60, 90), ba=(40, 70), fl=(55, 10, 0), bl=(-40, -50, 20), lift=4), P(lean=34, fa=(66, 96), ba=(50, 84), fl=(40, -4, 0), bl=(-34, -26, 0))])
    add_pose('guard', [P(lean=14, tilt=-6, fa=(40, 168), ba=(30, 160), fl=(26, -4, 0), bl=(-24, -14, 0))])
    add_pose('block', [P(lean=4, tilt=4, fa=(30, 160), ba=(22, 152), fl=(24, -2, 0), bl=(-30, -18, 0), dx=-3, face='grit')])
    add_pose('parry', [P(lean=12, tilt=-8, fa=(118, 150), ba=(40, 120), fl=(30, -6, 0), bl=(-26, -16, 0), face='shout'), P(lean=18, fa=(96, 120), ba=(50, 100))])
    add_pose('shove', [P(lean=18, fa=(30, 80), ba=(24, 70), fl=(26, -6, 0), bl=(-26, -16, 0)),
                       P(lean=36, tilt=-22, fa=(94, 94), ba=(90, 92), fl=(42, -2, 0), bl=(-30, -24, 0), dx=4, face='shout'),
                       P(lean=30, fa=(70, 96), ba=(62, 90), fl=(36, -4, 0), bl=(-28, -18, 0), dx=2)])
    add_pose('push', [P(lean=22, fa=(20, 70), ba=(16, 60), fl=(22, -6, 0), bl=(-30, -18, 0)),
                      P(lean=46, tilt=-30, fa=(104, 104), ba=(100, 100), fl=(60, 6, 0), bl=(-36, -40, 10), dx=2, face='shout'),
                      P(lean=38, fa=(76, 98), ba=(70, 92), fl=(48, 0, 0), bl=(-32, -26, 0), dx=6)])
    add_pose('sweep', [P(lean=18, fa=(70, 110), ba=(56, 96), fl=(14, -12, 0), bl=(-14, -10, 0)),
                       P(lean=14, fa=(80, 120), ba=(60, 100), fl=(50, 40, 10), bl=(-10, -20, 0), face='grit'),
                       P(lean=8, tilt=-4, fa=(100, 130), ba=(40, 90), fl=(76, 88, 40), bl=(-4, -26, 0), dx=-2, face='shout'),
                       P(lean=16, fa=(80, 110), ba=(56, 96), fl=(40, 12, 0), bl=(-16, -18, 0))])
    add_pose('charge', [P(lean=30, fa=(20, 120), ba=(14, 110), fl=(26, -8, 0), bl=(-30, -20, 0)),
                        P(lean=58, tilt=-36, fa=(60, 150), ba=(40, 130), fl=(66, 10, 0), bl=(-40, -44, 10), dx=10, face='shout'),
                        P(lean=44, fa=(50, 120), ba=(36, 104), fl=(52, 0, 0), bl=(-34, -30, 0), dx=6)])
    add_pose('grab', [P(lean=30, fa=(96, 118), ba=(86, 110), fl=(32, -6, 0), bl=(-28, -18, 0)),
                      P(lean=40, tilt=-24, fa=(106, 120), ba=(98, 112), fl=(46, -2, 0), bl=(-32, -26, 0), dx=4, face='shout'),
                      P(lean=46, tilt=-16, fa=(60, 50), ba=(52, 40), fl=(44, 0, 0), bl=(-34, -28, 0), dx=3, face='grit')])
    add_pose('lunge', [P(lean=34, fa=(80, 110), ba=(70, 100), fl=(24, -14, 0), bl=(-24, -18, 0)),
                       P(lean=56, tilt=-36, fa=(112, 118), ba=(104, 110), fl=(66, 16, 0), bl=(-46, -60, 30), dx=4, lift=2, face='shout'),
                       P(lean=54, tilt=-20, fa=(56, 40), ba=(46, 34), fl=(60, 8, 0), bl=(-40, -44, 10), dx=8, face='grit')])
    add_pose('hit', [P(lean=-10, tilt=14, fa=(20, -10), ba=(-10, -30), fl=(24, 4, 0), bl=(-30, -22, 0), dx=-4, face='hurt'),
                     P(lean=0, tilt=8, fa=(30, 10), ba=(10, -10), fl=(26, 0, 0), bl=(-28, -18, 0), dx=-2, face='hurt')])
    add_pose('stagger', [P(lean=-18, tilt=18, fa=(150, 200), ba=(120, 180), fl=(30, 10, 0), bl=(-24, -20, 0), dx=-5, face='hurt'),
                         P(lean=-12, tilt=12, fa=(170, 220), ba=(100, 160), fl=(20, 0, 0), bl=(-30, -24, 0), dx=-3, face='hurt')])
    add_pose('dizzy', [P(lean=22, tilt=10, fa=(10, 0), ba=(4, -6), fl=(20, -4, 0), bl=(-20, -12, 0), dx=-2, face='dazed'),
                       P(lean=14, tilt=-6, fa=(16, 6), ba=(-4, -10), fl=(22, -2, 0), bl=(-22, -14, 0), dx=2, face='dazed')])
    add_pose('trip', [P(lean=-10, tilt=20, fa=(150, 170), ba=(120, 150), fl=(100, 70, 0), bl=(60, 30, 0), rot=-38, lift=16, face='hurt')])
    add_pose('down', [P(lean=0, tilt=0, fa=(120, 60), ba=(40, 20), fl=(70, -20, 0), bl=(46, -30, 0), rot=-90, face='ko')])
    add_pose('getup', [P(lean=10, tilt=-10, fa=(-20, 0), ba=(-30, -10), fl=(100, -10, 0), bl=(80, -20, 0), rot=-50, face='grit'),
                       P(lean=30, fa=(30, 50), ba=(10, 30), fl=(80, -10, 0), bl=(-60, -60, 40), face='grit')])
    add_pose('clinch_lead', [P(lean=34, tilt=-10, fa=(122, 150), ba=(96, 104), fl=(30, -8, 0), bl=(-30, -20, 0)),
                             P(lean=36, tilt=-12, fa=(118, 146), ba=(98, 106), fl=(32, -6, 0), bl=(-30, -18, 0))])
    add_pose('clinch_def', [P(lean=30, tilt=-18, fa=(100, 110), ba=(116, 142), fl=(28, -8, 0), bl=(-28, -20, 0)),
                            P(lean=32, tilt=-16, fa=(102, 112), ba=(114, 140), fl=(30, -6, 0), bl=(-28, -18, 0))])
    add_pose('jolt', [P(lean=14, tilt=-4, fa=(70, 70), ba=(64, 64), fl=(20, -4, 0), bl=(-36, -24, 0), dx=-4, face='shout')])
    add_pose('t_hook', [P(lean=30, tilt=-10, fa=(120, 148), ba=(96, 104), fl=(50, 20, 10), bl=(-26, -18, 0), face='grit'),
                        P(lean=34, tilt=-12, fa=(118, 140), ba=(92, 100), fl=(64, 40, 20), bl=(-22, -16, 0), face='grit')])
    add_pose('t_hip', [P(lean=40, tilt=-10, fa=(130, 120), ba=(100, 90), fl=(10, -10, 0), bl=(10, -6, 0), dx=6, face='grit'),
                       P(lean=50, tilt=-14, fa=(140, 110), ba=(104, 80), fl=(-6, -16, 0), bl=(20, 0, 0), dx=9, face='grit')])
    add_pose('t_lift', [P(lean=22, tilt=-12, fa=(70, 80), ba=(64, 76), fl=(56, -24, 0), bl=(-40, -46, 0), face='grit'),
                        P(lean=18, tilt=-16, fa=(66, 76), ba=(60, 70), fl=(66, -30, 0), bl=(-48, -56, 0), face='grit')])
    add_pose('throwing', [P(lean=10, tilt=-20, fa=(150, 170), ba=(140, 160), fl=(26, -6, 0), bl=(-26, -16, 0), face='shout'),
                          P(lean=46, tilt=-24, fa=(100, 60), ba=(90, 50), fl=(48, 0, 0), bl=(-30, -34, 0), dx=4, face='shout'),
                          P(lean=34, tilt=-10, fa=(60, 40), ba=(50, 30), fl=(40, -4, 0), bl=(-30, -22, 0), dx=3)])
    add_pose('thrown', [P(lean=-20, tilt=20, fa=(160, 200), ba=(130, 170), fl=(40, 20, 0), bl=(-10, -20, 0), rot=-30, lift=14, face='hurt'),
                        P(lean=-10, tilt=20, fa=(170, 190), ba=(150, 170), fl=(90, 60, 0), bl=(60, 40, 0), rot=-110, lift=26, face='hurt'),
                        P(lean=0, tilt=10, fa=(120, 70), ba=(60, 30), fl=(70, -10, 0), bl=(50, -24, 0), rot=-86, lift=2, face='ko')])
    add_pose('counter', [P(lean=12, tilt=-10, fa=(70, 40), ba=(110, 140), fl=(40, -20, 0), bl=(-40, -40, 0), dx=-2, face='grit')])
    add_pose('offbal', [P(lean=52, tilt=-6, fa=(150, 190), ba=(30, -20), fl=(40, 10, 0), bl=(-30, -10, 0), dx=4, face='hurt')])
    add_pose('escape', [P(lean=20, fa=(94, 96), ba=(90, 94), fl=(20, -6, 0), bl=(-34, -24, 0), dx=-2, face='shout'),
                        P(lean=10, fa=(96, 96), ba=(92, 92), fl=(10, -4, 0), bl=(-40, -30, 0), dx=-6, face='grit')])
    add_pose('super', [P(lean=8, tilt=-24, fa=(150, 130), ba=(172, 160), fl=(70, 0, 0), bl=(-20, -14, 0), face='shout'),
                       P(lean=56, tilt=-36, fa=(112, 118), ba=(106, 112), fl=(66, 16, 0), bl=(-46, -60, 30), dx=4, lift=3, face='shout')])
    win = []
    for i in range(4):
        up = i % 2 == 0
        win.append(P(lean=6, tilt=-20, fa=(150 if up else 112, 130 if up else 100), ba=(160 if up else 122, 140 if up else 108),
                     fl=(78 if i < 2 else 26, 6 if i < 2 else -4, 0), bl=(-14, -10, 0), lift=2 if up else 0, face='happy'))
    add_pose('win', win)
    add_pose('lose', [P(lean=34, tilt=24, fa=(6, -4), ba=(0, -8), fl=(16, -2, 0), bl=(-16, -10, 0), face='hurt')])
    return F


def fit(pose):
    """How far to move a frame so its soles (or, lying, its lowest part) are on the ground and nothing leaves the cell:
    measured on a plain render, then used for all three, so they stay the same picture."""
    l, t, r, btm = outline(draw(pose, (255, 255, 255), (255, 255, 255))).split()[3].getbbox()
    dy = (GROUND - pose['lift']) * SS - btm
    dx = 0
    if l < 2 * SS: dx = 2 * SS - l
    elif r > (CELL - 2) * SS: dx = (CELL - 2) * SS - r
    if t + dy < 1 * SS: dy = 1 * SS - t
    return int(round(dx)), int(round(dy))


def render(pose, skin, cloth, shift):
    im = outline(draw(pose, skin, cloth))
    moved = Image.new('RGBA', im.size, (0, 0, 0, 0)); moved.paste(im, shift)
    return moved.resize((CELL, CELL), Image.LANCZOS)


def main():
    prev = sys.argv[1] if len(sys.argv) > 1 else None
    table, flat = {}, []
    for name, poses in frames():
        table[name] = [len(flat), len(poses)]
        flat.extend(poses)
    rows = (len(flat) + COLS - 1) // COLS
    W, H = COLS * CELL, rows * CELL
    fixed = Image.new('RGBA', (W, H), (0, 0, 0, 0)); tint = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    black, white = (0, 0, 0), (255, 255, 255)
    for i, pose in enumerate(flat):
        x, y = (i % COLS) * CELL, (i // COLS) * CELL
        shift = fit(pose)
        r0, r1, r2 = (np.asarray(render(pose, s, c, shift)).astype(np.int16) for s, c in ((black, black), (white, black), (black, white)))
        sk = np.clip(r1[..., 0] - r0[..., 0], 0, 255); cl = np.clip(r2[..., 0] - r0[..., 0], 0, 255)
        fixed.paste(Image.fromarray(np.clip(r0, 0, 255).astype(np.uint8), 'RGBA'), (x, y))
        tw = np.stack([sk, cl, np.zeros_like(sk), r0[..., 3]], -1).astype(np.uint8)
        tint.paste(Image.fromarray(tw, 'RGBA'), (x, y))
    fixed.save(os.path.join(ART, 'arena_fighter.png.bytes'), 'PNG', optimize=True)
    tint.save(os.path.join(ART, 'arena_fighter_tint.png.bytes'), 'PNG', optimize=True)
    meta = {'cell': CELL, 'cols': COLS, 'ground': GROUND, 'anchor': ANCHOR, 'unitsPerPx': 1.8, 'poses': table}
    with open(os.path.join(DATA, 'arena_frames.json'), 'w') as f: json.dump(meta, f, indent=1)
    if prev:
        os.makedirs(prev, exist_ok=True)
        for tag, skin, cloth in (('red', (237, 187, 147), (200, 48, 42)), ('blue', (185, 122, 74), (42, 86, 200))):
            F = np.asarray(fixed).astype(np.float32); Tw = np.asarray(tint).astype(np.float32) / 255
            rgb = F[..., :3] + Tw[..., 0:1] * np.array(skin) + Tw[..., 1:2] * np.array(cloth)
            out = np.concatenate([np.clip(rgb, 0, 255), F[..., 3:4]], -1).astype(np.uint8)
            im = Image.fromarray(out, 'RGBA'); bg = Image.new('RGBA', im.size, (226, 214, 196, 255)); bg.alpha_composite(im)
            d = ImageDraw.Draw(bg)
            for name, (s, n) in table.items(): d.text(((s % COLS) * CELL + 2, (s // COLS) * CELL + 2), name, fill=(0, 0, 0))
            bg.resize((W * 2, H * 2), Image.NEAREST).save(os.path.join(prev, f'fighter_{tag}.png'))
    print(f'{len(flat)} frames, {W}x{H}')


if __name__ == '__main__':
    main()
