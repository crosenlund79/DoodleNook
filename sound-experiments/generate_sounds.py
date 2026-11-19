#!/usr/bin/env python3
"""
DoodleNook - sound identity experiments.

Three short, original sounds made entirely by programmatic synthesis
(no samples, no recorded or third-party audio):

  A  The Playful Doodle      pencil squiggle, a dot, a flick and a wooden flourish
  B  The Cozy Nook           felt tap on a wooden box, warm kalimba notes, a little "smile"
  C  The Mischievous Artist  a cap pops off, bounces across the desk... and then: boing

Techniques
  * modal synthesis       - wood, marimba bars, kalimba tines, plastic and desk resonances
                            as sums of exponentially damped partials
  * texture scanning      - pencil-on-paper: a random "paper tooth" texture read back at
                            the speed of the pencil, so faster strokes are brighter and
                            reversals growl lower, the way real graphite does
  * additive + formants   - the cartoon "boing": a harmonic twang whose pitch and two
                            vocal-like formants wobble together
  * synthetic room reverb - band-split decaying noise, darker in the highs for warmth

Usage
    python3 generate_sounds.py          # writes the three WAVs next to this script

Output: 48 kHz, 16-bit PCM, stereo WAV, loudness-matched, true-peak below -1 dBTP.
Requires numpy and scipy. Every random source is seeded, so output is reproducible.
"""
from __future__ import annotations

import os
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 48_000
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
TWO_PI = 2.0 * np.pi


# --------------------------------------------------------------------------- basics
def n_of(sec: float) -> int:
    return int(round(sec * SR))


def tvec(sec: float) -> np.ndarray:
    return np.arange(n_of(sec)) / SR


def lp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, "lowpass", fs=SR, output="sos"), x)


def hp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, "highpass", fs=SR, output="sos"), x)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(signal.butter(order, [lo, hi], "bandpass", fs=SR, output="sos"), x)


def peq(x, f, gain_db, q):
    """RBJ peaking EQ."""
    a_ = 10 ** (gain_db / 40)
    w0 = TWO_PI * f / SR
    alpha = np.sin(w0) / (2 * q)
    b = [1 + alpha * a_, -2 * np.cos(w0), 1 - alpha * a_]
    a = [1 + alpha / a_, -2 * np.cos(w0), 1 - alpha / a_]
    return signal.lfilter(b, a, x)


def high_shelf(x, f, gain_db, q=0.707):
    """RBJ high shelf."""
    a_ = 10 ** (gain_db / 40)
    w0 = TWO_PI * f / SR
    cw, sa = np.cos(w0), np.sin(w0) / (2 * q) * 2 * np.sqrt(a_)
    b = [a_ * ((a_ + 1) + (a_ - 1) * cw + sa),
         -2 * a_ * ((a_ - 1) + (a_ + 1) * cw),
         a_ * ((a_ + 1) + (a_ - 1) * cw - sa)]
    a = [(a_ + 1) - (a_ - 1) * cw + sa,
         2 * ((a_ - 1) - (a_ + 1) * cw),
         (a_ + 1) - (a_ - 1) * cw - sa]
    return signal.lfilter(b, a, x)


def cosine_fade_out(n):
    return 0.5 * (1 + np.cos(np.linspace(0, np.pi, n)))


def smoothstep(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def noise_burst(rng, sec, lo, hi):
    """A tiny band-limited contact noise with a smooth (Hann) envelope."""
    k = max(8, n_of(sec))
    return bp(rng.standard_normal(k) * np.hanning(k), lo, hi)


def modal(f0, partials, dur, attack=0.001, rng=None, bend=None, max_hz=6500):
    """Sum of exponentially damped partials.

    partials: iterable of (ratio, amplitude, decay_seconds) - decay is the 1/e time.
    bend:     optional per-sample pitch multiplier (for cartoon pitch scoops).
    attack:   time constant of a smooth onset, which also softens the 'mallet'.
    """
    t = tvec(dur)
    pitch = np.ones_like(t) if bend is None else bend[: len(t)]
    base_phase = TWO_PI * f0 * np.cumsum(pitch) / SR
    y = np.zeros_like(t)
    for i, (ratio, amp, decay) in enumerate(partials):
        if f0 * ratio > max_hz:
            continue  # keep it soft: no partials up in the piercing/airy range
        phase0 = 0.0 if (rng is None or i == 0) else rng.uniform(0, TWO_PI)
        y += amp * np.exp(-t / decay) * np.sin(ratio * base_phase + phase0)
    return y * (1 - np.exp(-t / attack))


# --------------------------------------------------------------------------- instruments
def marimba(f0, vel, rng, soft=0.45):
    """Rosewood-ish bar: tuned 1:4:10 partials plus a tube resonance that beats softly."""
    d1 = float(np.clip(0.50 * (262 / f0) ** 0.6, 0.16, 1.3))
    parts = [
        (1.0, 1.0, d1),
        (1.0013, 0.12, d1 * 0.85),                    # resonator tube, slow shimmer
        (3.98, 0.40 * (1 - soft) + 0.05, d1 * 0.15),
        (9.86, 0.12 * (1 - soft) + 0.01, d1 * 0.05),
    ]
    y = modal(f0, parts, d1 * 5.5, attack=0.0005 + 0.0022 * soft, rng=rng)
    y[: n_of(0.004)] += 0.35 * noise_burst(rng, 0.004, 150, 1400 - 700 * soft)[: n_of(0.004)]
    return vel * y


def kalimba(f0, vel, rng, dur=None, bend=None, decay_scale=1.0):
    """Thumb piano tine: strong fundamental, inharmonic 'plink' partial near 6x, tiny box."""
    d1 = float(np.clip(0.55 * (440 / f0) ** 0.35, 0.3, 1.0)) * decay_scale
    parts = [
        (1.0, 1.0, d1),
        (1.0023, 0.16, d1 * 0.9),          # neighbouring mode -> gentle beating
        (2.0, 0.03, d1 * 0.3),
        (5.93, 0.20, 0.085),               # the characteristic kalimba "plink"
        (5.99, 0.07, 0.06),
        (15.4, 0.025, 0.02),
    ]
    dur = dur or d1 * 6
    y = modal(f0, parts, dur, attack=0.0009, rng=rng, bend=bend)
    # thumb leaving the tine + the little wooden box answering
    y[: n_of(0.003)] += 0.12 * noise_burst(rng, 0.003, 900, 3200)[: n_of(0.003)]
    y += 0.10 * modal(235, [(1, 1, 0.045), (1.63, 0.4, 0.03)], dur, attack=0.0015, rng=rng)
    return vel * y


def wood_tok(f0, vel, rng, soft=0.6, dur=0.3):
    """A soft knock on a small hollow wooden object."""
    ratios = [1.0, 1.52, 2.17, 2.81, 3.62, 4.91]
    amps = [1.0, 0.55, 0.36, 0.24, 0.15, 0.08]
    decays = [0.065, 0.045, 0.030, 0.022, 0.015, 0.010]
    parts = [(r, a * np.exp(-soft * i * 0.55), d) for i, (r, a, d) in enumerate(zip(ratios, amps, decays))]
    y = modal(f0, parts, dur, attack=0.0006 + 0.0025 * soft, rng=rng)
    y[: n_of(0.003)] += 0.25 * noise_burst(rng, 0.003, 300, 2200)[: n_of(0.003)]
    return vel * y


def pencil(speed, pressure, rng, grain=0.006, bright=5600, body=1.0):
    """Graphite on paper.

    A random 'paper tooth' texture (noise + sparse fibre catches) is scanned at the
    pencil's speed. Slow motion stretches the texture into a low, grainy growl; fast
    motion turns it into a brighter hiss - so direction changes in a squiggle are
    audible as a rhythmic 'skritch-skritch'.
    """
    pos = np.cumsum(speed)
    size = int(pos[-1]) + 4
    # heavy-tailed 'tooth': a gaussian bed plus a cubed-gaussian crackle component
    tex = 0.7 * rng.standard_normal(size) + 0.7 * rng.standard_normal(size) ** 3 / np.sqrt(15)
    catches = rng.random(size) < grain
    tex[catches] += rng.choice([-1.0, 1.0], catches.sum()) * rng.uniform(3, 7, catches.sum())
    y = np.interp(pos, np.arange(size), tex)
    y *= pressure * np.power(np.maximum(speed, 0), 0.65)
    hiss = hp(y, 650)
    hiss = peq(hiss, 2100, 3.5, 1.1)       # paper/pencil body resonance
    hiss = peq(hiss, 4200, -3.0, 1.0)      # take the edge off the most sensitive region
    hiss = lp(hiss, bright, order=4)
    thump = lp(y, 380, order=2) * 1.6      # the paper on the desk underneath
    return hiss + body * thump


def pencil_dot(vel, rng):
    """Pencil tip tapped onto paper: small dry wooden tick with a desk thump."""
    tick = modal(1180, [(1, 1, 0.010), (1.83, 0.6, 0.007), (2.9, 0.3, 0.004)], 0.08, attack=0.0003, rng=rng)
    thump = modal(170, [(1, 1, 0.028), (1.7, 0.4, 0.016)], 0.12, attack=0.0012, rng=rng)
    out = np.zeros(n_of(0.12))
    out[: len(tick)] += 0.5 * tick
    out += 0.8 * thump
    out[: n_of(0.002)] += 0.15 * noise_burst(rng, 0.002, 1500, 5000)[: n_of(0.002)]
    return vel * out


def pop(rng, f_start=330, f_end=1080, sweep=0.022, dur=0.09):
    """A round cartoon 'plop' - like a marker cap leaving its pen (cavity pitch rises)."""
    t = tvec(dur)
    f = f_start * (f_end / f_start) ** np.minimum(t / sweep, 1.0)
    ph = TWO_PI * np.cumsum(f) / SR
    env = (1 - np.exp(-t / 0.0007)) * np.exp(-t / 0.017)
    y = env * (np.sin(ph) + 0.22 * np.sin(2 * ph) * np.exp(-t / 0.006))
    y[: n_of(0.0015)] += 0.25 * noise_burst(rng, 0.0015, 700, 2600)[: n_of(0.0015)]
    return y


def cap_bounce(vel, pitch, rng):
    """A plastic cap hitting a wooden desk, with a cartoon pitched 'bup' riding on it."""
    dur = 0.11
    t = tvec(dur)
    f = pitch * (0.78 + 0.22 * (1 - np.exp(-t / 0.008)))   # tiny upward chirp = 'bup'
    ph = TWO_PI * np.cumsum(f) / SR
    blip = np.sin(ph) * (1 - np.exp(-t / 0.0006)) * np.exp(-t / 0.020)
    tick = modal(1, [(1750, 1, 0.006), (2750, 0.6, 0.004), (3900, 0.25, 0.003)], dur, attack=0.0003, rng=rng)
    thump = modal(155, [(1, 1, 0.030), (1.6, 0.45, 0.018)], dur, attack=0.0010, rng=rng)
    return vel * (0.85 * blip + 0.22 * np.sqrt(vel) * tick + 0.55 * thump)


def boing(f0, rng, dur=1.1):
    """Jaw-harp-style 'doyoyoyoing': a twang whose pitch and formants wobble together."""
    t = tvec(dur)
    scoop = 1 - 0.20 * np.exp(-t / 0.014)                 # springs up into pitch
    wob_rate = 8.5 + 7.5 * np.exp(-t / 0.16)              # 16 Hz wobble relaxing to 8.5 Hz
    wob = np.sin(TWO_PI * np.cumsum(wob_rate) / SR)
    f = f0 * scoop * (1 + 0.085 * np.exp(-t / 0.28) * wob)
    ph = TWO_PI * np.cumsum(f) / SR
    depth = np.exp(-t / 0.40)
    f1 = 560 * 2 ** (0.95 * depth * wob)                  # 'o' <-> 'i' : the 'oi-oi-oi'
    f2 = 1350 * 2 ** (0.55 * depth * wob)
    y = np.zeros_like(t)
    kmax = int(3800 / f0)
    for k in range(1, kmax + 1):
        fk = k * f
        h1 = 1 / np.sqrt(1 + 9.0 * (fk / f1 - f1 / fk) ** 2)
        h2 = 1 / np.sqrt(1 + 16.0 * (fk / f2 - f2 / fk) ** 2)
        schroeder = np.pi * k * (k - 1) / kmax      # spreads harmonic phases: twangy, not buzzy
        y += (k ** -1.0) * (h1 + 0.45 * h2) * np.sin(k * ph + schroeder)
    y *= (1 - np.exp(-t / 0.0018)) * np.exp(-t / 0.23)
    y[-n_of(0.25):] *= cosine_fade_out(n_of(0.25))
    y[: n_of(0.003)] += 0.35 * noise_burst(rng, 0.003, 400, 2400)[: n_of(0.003)]
    return y


# --------------------------------------------------------------------------- mixing
class Mix:
    """Stereo bus with constant-power panning and a mono reverb send."""

    def __init__(self, dur):
        self.n = n_of(dur)
        self.dry = np.zeros((2, self.n))
        self.send = np.zeros(self.n)

    def add(self, x, at, gain=1.0, pan=0.0, send=0.15):
        i = n_of(at)
        k = min(len(x), self.n - i)
        if k <= 0:
            return
        x = x[:k].copy()
        taper = min(n_of(0.008), k)                      # no source may ever end abruptly
        x[-taper:] *= cosine_fade_out(taper)
        th = (np.clip(pan, -1, 1) + 1) * np.pi / 4
        self.dry[0, i:i + k] += gain * np.cos(th) * x[:k]
        self.dry[1, i:i + k] += gain * np.sin(th) * x[:k]
        self.send[i:i + k] += gain * send * x[:k]

    def render(self, rt60, warmth, seed):
        ir_l, ir_r = room_ir(rt60, warmth, seed), room_ir(rt60, warmth, seed + 1)
        wet = np.vstack([signal.fftconvolve(self.send, ir_l)[: self.n],
                         signal.fftconvolve(self.send, ir_r)[: self.n]])
        return self.dry + wet


def room_ir(rt60, warmth, seed, predelay=0.006):
    """Small, soft room: band-split decaying noise (highs die fastest) + a few early echoes."""
    rng = np.random.default_rng(seed)
    size = n_of(rt60 * 1.3)
    t = np.arange(size) / SR

    def band(lo, hi, rt):
        x = rng.standard_normal(size)
        x = hp(x, lo) if lo else x
        x = lp(x, hi) if hi else x
        return x * 10 ** (-3 * t / rt)

    tail = (band(None, 650, rt60 * 1.15) + band(650, 2800, rt60)
            + (0.7 - 0.4 * warmth) * band(2800, 7500, rt60 * (0.55 - 0.25 * warmth)))
    tail *= 1 - np.exp(-t / 0.004)
    tail /= np.sqrt(np.sum(tail ** 2))
    early = np.zeros(size)
    for _ in range(7):
        d = rng.uniform(0.002, 0.022)
        early[n_of(d)] += rng.choice([-1, 1]) * rng.uniform(0.3, 0.7) * (1 - d / 0.03)
    early = lp(early, 4500 - 2000 * warmth)
    early /= np.sqrt(np.sum(early ** 2)) + 1e-12
    ir = 0.85 * tail + 0.5 * early
    ir[-n_of(rt60 * 0.2):] *= cosine_fade_out(n_of(rt60 * 0.2))
    return np.concatenate([np.zeros(n_of(predelay)), ir])


def finish(st, total, fade, shelf_db=-3.0):
    """Clean up: DC/rumble high-pass, soften the top, trim, and fade the tail to true zero."""
    st = np.vstack([high_shelf(hp(ch, 38), 6500, shelf_db) for ch in st])
    st = np.vstack([lp(ch, 14000, order=2) for ch in st])
    st = st[:, : n_of(total)]
    nf = n_of(fade)
    st[:, -nf:] *= cosine_fade_out(nf)
    st[:, : n_of(0.0005)] *= np.linspace(0, 1, n_of(0.0005))   # first sample is exactly 0
    return st


# --------------------------------------------------------------------------- the three designs
def version_a():
    """The Playful Doodle: skritch-skritch-skritch . tik . fwip-plink-plink-plink-PLING"""
    rng = np.random.default_rng(1101)
    m = Mix(1.7)

    # 1) a quick looping squiggle - loop rate speeds up as the hand gets going
    sq = 0.40
    t = tvec(sq)
    loop_rate = 7.5 + 4.5 * t / sq
    loop_ph = TWO_PI * np.cumsum(loop_rate) / SR
    speed = 0.07 + 0.48 * np.abs(np.sin(loop_ph / 2)) ** 1.2
    speed *= 1 + 0.06 * rng.standard_normal(len(t)).cumsum() / np.sqrt(len(t))   # wobbly hand
    pressure = smoothstep(t / 0.012) * (1 - smoothstep((t - (sq - 0.03)) / 0.03))
    pressure *= 0.85 + 0.15 * np.sin(TWO_PI * 2.2 * t)
    scribble = pencil(speed, pressure, rng)
    pan = 0.18 * np.sin(loop_ph / 2 + 0.3)            # pencil drifts across the page
    i0 = 0
    th = (pan + 1) * np.pi / 4
    g = 0.47
    m.dry[0, i0:i0 + len(t)] += g * np.cos(th) * scribble
    m.dry[1, i0:i0 + len(t)] += g * np.sin(th) * scribble
    m.send[i0:i0 + len(t)] += g * 0.10 * scribble

    # 2) pen lifts ... and dots it
    m.add(pencil_dot(0.9, rng), 0.455, gain=0.75, pan=0.12, send=0.12)

    # 3) the flick: accelerating stroke that lifts off the paper ("fwip")
    fl = 0.09
    t = tvec(fl)
    speed = 0.10 + 0.62 * (t / fl) ** 1.6
    pressure = smoothstep(t / 0.006) * (1 - t / fl) ** 1.4
    m.add(pencil(speed, pressure, rng, body=0.5), 0.53, gain=0.42, pan=0.2, send=0.1)

    # 4) the flourish: a quick wooden grace run landing on a cheeky sixth (C6 colour)
    notes = [(523.25, 0.555, 0.38, -0.25), (659.26, 0.592, 0.45, -0.10),
             (783.99, 0.628, 0.52, 0.05), (880.00, 0.700, 0.78, 0.18)]
    for f, at, vel, p in notes:
        m.add(marimba(f, vel, rng, soft=0.5), at, gain=0.62, pan=p, send=0.18)

    st = m.render(rt60=0.32, warmth=0.6, seed=11)
    return finish(st, total=1.62, fade=0.40)


def version_b():
    """The Cozy Nook: tok . bum+plink . plink . plink~ (a soft, smiling F major 7)"""
    rng = np.random.default_rng(2202)
    m = Mix(2.0)

    # a felt-soft tap on a little wooden box: "knock, it's me"
    m.add(wood_tok(300, 1.0, rng, soft=0.72), 0.0, gain=0.62, pan=-0.05, send=0.22)

    # warm low marimba F3 underneath, like a cushion
    m.add(marimba(174.61, 0.85, rng, soft=0.9), 0.065, gain=0.62, pan=-0.12, send=0.25)

    # kalimba A4 -> C5 -> E5, relaxed and slightly swung
    m.add(kalimba(440.00, 0.62, rng), 0.070, gain=0.45, pan=-0.18, send=0.25)
    m.add(kalimba(523.25, 0.58, rng), 0.235, gain=0.45, pan=0.10, send=0.25)

    # the personality: the last note 'smiles' - it starts a little flat and lifts into tune
    dur = 2.0
    t = tvec(dur)
    smile = 1 - 0.028 * np.exp(-t / 0.055) + 0.004 * np.exp(-t / 0.5) * np.sin(TWO_PI * 5.2 * t)
    m.add(kalimba(659.26, 0.50, rng, dur=dur, bend=smile, decay_scale=0.9), 0.425,
          gain=0.42, pan=0.25, send=0.30)

    st = m.render(rt60=0.55, warmth=0.9, seed=22)
    return finish(st, total=1.90, fade=0.70, shelf_db=-4.5)


def version_c():
    """The Mischievous Artist: plop! . bup-bup-bip-bip-bi-bi-b . . . doyoyoyoing"""
    rng = np.random.default_rng(3303)
    m = Mix(1.8)

    # the cap pops off
    m.add(pop(rng), 0.0, gain=0.50, pan=-0.10, send=0.12)

    # ...and skitters away across the desk (closer bounces, higher, quieter, drifting right)
    at, gap, vel, pitch = 0.135, 0.125, 1.0, 430.0
    for i in range(8):
        m.add(cap_bounce(vel, pitch, rng), at, gain=0.55, pan=-0.05 + 0.09 * i, send=0.14 + 0.02 * i)
        at += gap
        gap *= 0.70
        vel *= 0.72
        pitch *= 1.055

    # a comic beat of silence ... then the punchline
    m.add(boing(146.83, rng), 0.70, gain=0.50, pan=0.0, send=0.15)

    st = m.render(rt60=0.30, warmth=0.6, seed=33)
    return finish(st, total=1.62, fade=0.35)


# --------------------------------------------------------------------------- output
def loudness_lufs(st):
    """ITU-R BS.1770 integrated loudness (K-weighting, 400 ms blocks, gated)."""
    pre_b, pre_a = _k_weighting()
    z = []
    blk, hop = n_of(0.4), n_of(0.1)
    weighted = [signal.lfilter(pre_b[1], pre_a[1], signal.lfilter(pre_b[0], pre_a[0], ch)) for ch in st]
    for s in range(0, st.shape[1] - blk + 1, hop):
        z.append(sum(np.mean(w[s:s + blk] ** 2) for w in weighted))
    z = np.array(z)
    lk = -0.691 + 10 * np.log10(z + 1e-20)
    z = z[lk > -70]
    rel = -0.691 + 10 * np.log10(np.mean(z)) - 10
    lk = -0.691 + 10 * np.log10(z)
    return -0.691 + 10 * np.log10(np.mean(z[lk > rel]))


def _k_weighting():
    # high shelf (head) + RLB high-pass, coefficients re-derived for SR (BS.1770-4)
    f0, g, q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    k = np.tan(np.pi * f0 / SR)
    vh = 10 ** (g / 20)
    vb = vh ** 0.4996667741545416
    a0 = 1 + k / q + k * k
    b1 = [(vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0]
    a1 = [1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0]
    f0, q = 38.13547087602444, 0.5003270373238773
    k = np.tan(np.pi * f0 / SR)
    a2 = [1, 2 * (k * k - 1) / (1 + k / q + k * k), (1 - k / q + k * k) / (1 + k / q + k * k)]
    b2 = [1, -2, 1]
    return (b1, b2), (a1, a2)


def true_peak_db(st):
    up = signal.resample_poly(st, 4, 1, axis=1)
    return 20 * np.log10(np.max(np.abs(up)) + 1e-20)


def write_wav(path, st, seed):
    """TPDF-dithered 16-bit PCM; dither is gated off in the faded tail so it ends in pure zeros."""
    rng = np.random.default_rng(seed)
    lsb = 1 / 32768
    dither = (rng.random(st.shape) - rng.random(st.shape)) * lsb
    gate = (np.abs(st) > 2 * lsb).astype(float)
    gate = np.vstack([np.convolve(g, np.ones(n_of(0.005)), "same") > 0 for g in gate]).astype(float)
    pcm = np.round((st + dither * gate) * 32767).clip(-32768, 32767).astype(np.int16)
    wavfile.write(path, SR, pcm.T)


DESIGNS = [
    ("A", "DoodleNook_A_PlayfulDoodle.wav", version_a),
    ("B", "DoodleNook_B_CozyNook.wav", version_b),
    ("C", "DoodleNook_C_MischievousArtist.wav", version_c),
]

TARGET_LUFS = -18.0       # all three matched, so the comparison is about character, not volume
CEILING_DBTP = -1.0


def main():
    rendered = [(tag, name, fn()) for tag, name, fn in DESIGNS]
    # one common loudness target, lowered if any sound would exceed the true-peak ceiling
    target = TARGET_LUFS
    for _, _, st in rendered:
        headroom = CEILING_DBTP - true_peak_db(st)
        target = min(target, loudness_lufs(st) + headroom)
    for i, (tag, name, st) in enumerate(rendered):
        st = st * 10 ** ((target - loudness_lufs(st)) / 20)
        path = os.path.join(OUT_DIR, name)
        write_wav(path, st, seed=900 + i)
        print(f"{tag}: {name}  {st.shape[1] / SR:.2f}s  "
              f"{loudness_lufs(st):6.1f} LUFS  true peak {true_peak_db(st):5.1f} dBTP")


if __name__ == "__main__":
    main()
