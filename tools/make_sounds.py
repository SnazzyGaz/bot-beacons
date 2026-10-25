#!/usr/bin/env python3
"""Synthesises the Bot Beacons alert sounds (original, made from scratch: sine/noise maths only, no samples).
Writes 44.1 kHz 16-bit mono WAVs. Usage: python3 tools/make_sounds.py sounds
Message sounds are levelled to -18 dBFS RMS (peak <= -4), attention to -14 dBFS RMS (peak <= -1.5) (the app scales them for Quiet/Normal/Loud)."""
import sys, os, numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 44100
rng = np.random.default_rng(40)   # fixed seed: the same files every run

def t_(d): return np.arange(int(SR * d)) / SR
def env(n, a=0.005, r=0.2, d=None):
    """attack a s, exponential decay with time constant r s; d = total length (s)"""
    t = np.arange(n) / SR
    e = np.minimum(1, t / max(a, 1e-4)) * np.exp(-np.maximum(0, t - a) / r)
    return e
def fade(x, fi=0.004, fo=0.03):
    n = len(x); a = int(SR * fi); b = int(SR * fo)
    x = x.copy(); x[:a] *= np.linspace(0, 1, a); x[n - b:] *= np.linspace(1, 0, b) ** 2; return x
def lp(x, f, o=4): return sosfilt(butter(o, f, 'low', fs=SR, output='sos'), x)
def hp(x, f, o=2): return sosfilt(butter(o, f, 'high', fs=SR, output='sos'), x)
def bp(x, lo, hi, o=2): return sosfilt(butter(o, [lo, hi], 'band', fs=SR, output='sos'), x)
def sine(f, d, ph=0.0):
    t = t_(d); f = np.broadcast_to(f, t.shape) if np.ndim(f) else np.full(t.shape, f)
    return np.sin(2 * np.pi * np.cumsum(f) / SR + ph)
def pad(x, d): n = int(SR * d); return np.concatenate([x, np.zeros(max(0, n - len(x)))])[:n]
def mix(d, *parts):
    out = np.zeros(int(SR * d))
    for at, x in parts:
        i = int(SR * at); j = min(len(out), i + len(x)); out[i:j] += x[:j - i]
    return out
def norm(x, rms_db, peak_db):
    """match loudness (RMS) across voices, but never exceed the peak ceiling"""
    x = fade(x - np.mean(x)); x = x / (np.max(np.abs(x)) + 1e-9)
    g = min(10 ** (rms_db / 20) / np.sqrt(np.mean(x ** 2)), 10 ** (peak_db / 20))
    return x * g

# ---------------------------------------------------------------- voices (k = brightness/pitch factor for attention)
def chime(k=1.0, d=0.55):
    """neutral soft chime: two sine bell tones a fourth apart, gentle partials"""
    def tone(f, dd):
        x = sine(f, dd) + 0.25 * sine(2.0 * f, dd) * env(int(SR * dd), r=0.08) + 0.08 * sine(3.0 * f, dd) * env(int(SR * dd), r=0.05)
        return x * env(int(SR * dd), a=0.004, r=0.22)
    f1, f2 = 784 * k, 1046.5 * k
    return mix(d, (0, tone(f1, d)), (0.11, 0.85 * tone(f2, d - 0.11)))

# ---------------------------------------------------------------- Bot Beacons voices (neutral, original)
def orb(k=1.0, d=0.6):
    """glassy shimmer: two soft sines with a slow beat"""
    n = int(SR * d); t = np.arange(n) / SR
    x = sine(988 * k, d) + 0.8 * sine(992 * k, d) + 0.35 * sine(1482 * k, d) * env(n, r=0.12)
    return x * env(n, a=0.02, r=0.22)
def robot(k=1.0, d=0.5):
    """friendly two-note square-ish bleep, softened"""
    def b(f, dd):
        n = int(SR * dd); ph = 2 * np.pi * f * np.arange(n) / SR
        return lp(sum(np.sin(h * ph) / h for h in (1, 3, 5)), 3000) * env(n, a=0.003, r=0.07)
    return mix(d, (0, b(660 * k, 0.16)), (0.15, b(990 * k, 0.3)))
def cat(k=1.0, d=0.55):
    """a tiny "mrrp": a short rising-falling purr-trill (synthesised, no recording)"""
    n = int(SR * d); t = np.arange(n) / SR
    f = (330 + 260 * np.sin(np.pi * np.clip(t / 0.35, 0, 1))) * k
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = (np.sin(ph) + 0.4 * np.sin(2 * ph) + 0.15 * np.sin(3 * ph)) * (0.7 + 0.3 * np.sin(2 * np.pi * 24 * t))
    return lp(x, 2500) * np.minimum(1, t / 0.02) * np.exp(-np.maximum(0, t - 0.2) / 0.1)

def attention(voice):
    """same voice, double pulse, a bit higher and brighter, max ~0.9 s"""
    a = voice(k=1.12, d=0.42); a = a + 0.25 * lp(hp(a, 1800), 5000)   # brighter, but nothing shrill
    return mix(0.9, (0, a), (0.45, a))

BEACONS = {"orb": orb, "robot": robot, "cat": cat, "default": chime}

def write(path, x): wavfile.write(path, SR, np.int16(np.clip(x, -1, 1) * 32767))

if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    voices = BEACONS
    for name, v in voices.items():
        m = norm(v(), -18.0, -4.0); a = norm(attention(v), -14.0, -1.5)
        write(os.path.join(out, name + ".wav"), m); write(os.path.join(out, name + "_attention.wav"), a)
        print(f"{name:12s} message {len(m)/SR:.2f}s  attention {len(a)/SR:.2f}s")
