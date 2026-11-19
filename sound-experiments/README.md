# DoodleNook — sound identity experiments

Three exploratory sound identities for DoodleNook. They are sketches to compare, not approved assets, and are not wired into any application code.

| File | Length | Idea |
|---|---|---|
| `DoodleNook_A_PlayfulDoodle.wav` | 1.62 s | *skritch-skritch-skritch · tik · fwip-plink-plink-plink-PLING* — a quick looping pencil squiggle, a pen-lift and a dot, a flick off the page, and a fast wooden grace run that lands on a cheeky sixth (C6 colour). |
| `DoodleNook_B_CozyNook.wav` | 1.90 s | *tok · bum · plink · plink~* — a felt-soft tap on a little wooden box, a warm low marimba "cushion", and kalimba notes outlining F major 7. The last note "smiles": it starts slightly flat and lifts into tune. |
| `DoodleNook_C_MischievousArtist.wav` | 1.62 s | *plop! · bup-bup-bip-bip-bi-bi-b · … · doyoyoyoing* — a marker cap pops off and skitters across the desk (bounces speed up, rise in pitch and drift to the right), a comic beat of silence, then a jaw-harp-style boing as the punchline. |

## Format

- WAV, PCM 16-bit, 48 kHz, stereo (centred, with a subtle room; sums cleanly to mono)
- All three are loudness-matched at **−18 LUFS** (ITU-R BS.1770), so comparisons are about character rather than volume. True peaks are −2.5 to −6.5 dBTP, so there is no clipping.
- High-frequency content is deliberately restrained (no partials above ~6.5 kHz in the tonal parts, a soft high shelf and a 14 kHz roll-off on everything).
- Every file starts and ends on exact digital zero. Tails are faded with a raised cosine, and dither is gated off in the fade, so there are no end clicks or noise.

## How they were made

Everything is synthesised from scratch in `generate_sounds.py`. No samples or recordings are used.

- **Modal synthesis** builds wood, marimba bars, kalimba tines, the plastic cap and the desk from sums of damped partials.
- **Texture scanning** makes the pencil. A random "paper tooth" texture is read back at the speed of the pencil, so fast strokes are brighter, reversals growl lower, and the squiggle's loops become an audible rhythm.
- **Additive synthesis with moving formants** produces the boing. Its pitch and two vowel-like formants wobble together ("oi-oi-oi").
- **A synthetic room reverb** (band-split decaying noise, darker in the highs) adds warmth.

To regenerate (deterministic, seeded):

```bash
pip install numpy scipy
python3 generate_sounds.py
```

Each design is a short, readable function (`version_a`, `version_b`, `version_c`). Note timings, pitches, gains and room size are easy to tweak there.
