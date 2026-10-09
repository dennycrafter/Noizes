#!/usr/bin/env python3
"""Generate the 8 default Noizes sounds as small wav files (44.1 kHz 16-bit mono).
Pure stdlib, no licensing concerns - synthesized from scratch."""
import math, wave, struct, os

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "sounds")

def tone(freq, dur, amp=0.4, partials=((1.0, 1.0),), decay=3.0, attack=0.005):
    n = int(SR * dur)
    out = []
    for i in range(n):
        t = i / SR
        s = sum(k * math.sin(2 * math.pi * freq * h * t) for h, k in partials)
        out.append(amp * s * min(1.0, t / attack) * math.exp(-decay * t))
    return out

def silence(dur):
    return [0.0] * int(SR * dur)

def concat(*segs):
    out = []
    for s in segs:
        out += s
    return out

def mix(*segs):
    n = max(len(s) for s in segs)
    out = [0.0] * n
    for s in segs:
        for i, v in enumerate(s):
            out[i] += v
    return out

def write(name, samples):
    peak = max(abs(s) for s in samples) or 1.0
    if peak > 0.9:
        samples = [s * 0.9 / peak for s in samples]
    path = os.path.join(OUT, name)
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767)) for s in samples))
    print("wrote", name, len(samples) / SR, "s")

C5, E5, G5, A5, C6, E6, G6 = 523.25, 659.25, 783.99, 880.0, 1046.5, 1318.5, 1568.0

os.makedirs(OUT, exist_ok=True)

# 01-chime: two-note bell chime
write("01-chime.wav", mix(tone(A5, 0.8, 0.35, ((1, 1), (2, 0.3))), tone(E6, 0.6, 0.18, ((1, 1), (2.7, 0.2)))))
# 02-bell: low bell with inharmonic partials
write("02-bell.wav", tone(660, 1.2, 0.3, ((1, 1), (2.4, 0.4), (3.9, 0.2)), decay=2.5))
# 03-arp-up: rising arpeggio C5 E5 G5 C6
write("03-arp-up.wav", concat(tone(C5, 0.14), tone(E5, 0.14), tone(G5, 0.14), tone(C6, 0.3, decay=4.0)))
# 04-two-tone: gentle two-tone notice
write("04-two-tone.wav", concat(tone(700, 0.22), tone(525, 0.32, decay=4.0)))
# 05-triad: soft major chord
write("05-triad.wav", mix(tone(C5, 0.9, 0.22, decay=2.0), tone(E5, 0.9, 0.2, decay=2.0), tone(G5, 0.9, 0.18, decay=2.0)))
# 06-sparkle: urgent rising blips
write("06-sparkle.wav", concat(tone(1200, 0.1), silence(0.02), tone(1600, 0.1), silence(0.02), tone(2000, 0.22, decay=4.0)))
# 07-marimba: woody single knock
write("07-marimba.wav", tone(C5, 0.5, 0.4, ((1, 1), (4, 0.25)), decay=7.0))
# 08-success: G5 -> C6 fifth flourish
write("08-success.wav", mix(concat(tone(G5, 0.25), tone(C6, 0.5, decay=3.5)), tone(G6, 0.35, 0.12)))

print("done ->", os.path.abspath(OUT))
