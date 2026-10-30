# -*- coding: utf-8 -*-
"""Генерация озвучки фраз Капри для мода BoneLabCapri.

Голосовая модель — та же, что в репозитории Capricorn_ai_qwen:
edge-tts, голос ru-RU-SvetlanaNeural, rate=+5%, pitch=+8Hz (voice.py, строки 457-459),
плюс «цифровая обработка» как в Capri: питч-шифт через numpy (pitch_shift из voice.py)
и лёгкий глитч-эффект. Результат — WAV 16-bit mono, совместимые с Unity AudioSource.

Зависимости: pip install edge-tts numpy
Запуск:      python generate_voices.py
Выход:       ../../src/CapriModPlugin/Audio/*.wav + manifest.txt
"""
import asyncio
import io
import os
import sys
import wave

import numpy as np
import edge_tts

from phrases import PHRASES

VOICE = "ru-RU-SvetlanaNeural"   # голос Капри из Capricorn_ai_qwen
RATE = "+5%"
PITCH = "+8Hz"
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "src", "CapriModPlugin", "Audio")
OUT_DIR = os.path.abspath(OUT_DIR)


async def synth(text: str) -> bytes:
    buf = io.BytesIO()
    communicate = edge_tts.Communicate(text, VOICE, rate=RATE, pitch=PITCH)
    async for chunk in communicate.stream():
        if chunk["type"] == "audio":
            buf.write(chunk["data"])
    return buf.getvalue()


def mp3_to_wav_data(mp3_bytes: bytes):
    """Распаковка mp3 в float32 моно через ffmpeg (imageio-ffmpeg) или системный ffmpeg."""
    import shutil, subprocess, tempfile
    ff = shutil.which("ffmpeg")
    if not ff:
        try:
            import imageio_ffmpeg
            ff = imageio_ffmpeg.get_ffmpeg_exe()
        except ImportError:
            raise SystemExit("Нужен ffmpeg: pip install imageio-ffmpeg")
    with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as f:
        f.write(mp3_bytes)
        src = f.name
    dst = src + ".raw"
    subprocess.run([ff, "-y", "-loglevel", "error", "-i", src, "-f", "f32le",
                    "-acodec", "pcm_f32le", "-ac", "1", "-ar", "22050", dst], check=True)
    data = np.fromfile(dst, dtype=np.float32)
    os.unlink(src); os.unlink(dst)
    return data, 22050


def pitch_shift(x, semitones):
    """Как pitch_shift() из voice.py Capricorn_ai_qwen."""
    factor = 2 ** (semitones / 12)
    pos = np.arange(0, len(x) - 1, factor)
    return np.interp(pos, np.arange(len(x)), x).astype(np.float32)


def glitch_fx(x, sr, rng):
    """Лёгкий цифровой глитч Капри: короткие статореверс-вставки и амплитудный тремор."""
    y = x.copy()
    n = len(y)
    # случайные микро-глитчи (~каждые 2.5 сек)
    step = int(sr * 2.5)
    for start in range(step, n - step // 3, step):
        w = int(sr * 0.04)
        seg = y[start:start + w].copy()
        y[start:start + w] = seg[::-1]
    # цифровой тремор высоты тона
    t = np.arange(n) / sr
    y *= (1.0 + 0.03 * np.sin(2 * np.pi * 7.0 * t)).astype(np.float32)
    return y


def normalize(x, peak=0.9):
    m = float(np.max(np.abs(x))) if len(x) else 1.0
    return x / max(1e-6, m) * peak


def write_wav(path, x, sr):
    x16 = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(x16.tobytes())


async def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    rng = np.random.default_rng(7)
    manifest_lines = []
    idx = 0
    for category, texts in PHRASES.items():
        for i, text in enumerate(texts):
            idx += 1
            name = f"{category}_{i+1:02d}.wav"
            print(f"[{idx}] {name}: {text[:50]}...")
            mp3 = await synth(text)
            x, sr = mp3_to_wav_data(mp3)
            x = pitch_shift(x, +1.5)          # чуть выше — «юный цифровой» тон
            x = glitch_fx(x, sr, rng)
            x = normalize(x)
            write_wav(os.path.join(OUT_DIR, name), x, sr)
            manifest_lines.append(f"{name}|{category}|{text}")
    with open(os.path.join(OUT_DIR, "manifest.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(manifest_lines) + "\n")
    print(f"\nГотово: {idx} файлов -> {OUT_DIR}")


if __name__ == "__main__":
    asyncio.run(main())
