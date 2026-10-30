# BoneLabCapri — CapriMod 🧠💾

**Мод для BONELAB (VR)**: Капри — ИИ-конструкт из чипа, по мотивам аниме
**«Cyberpunk: Edgerunners» (Бегущие по краю)**.

Пока игрок носит **CHIP OF CAPRI** в слоте головы:

- 📢 **Капри появляется в случайных местах уровня** — голографическая фигура,
  видимая **только вам**, при появлении произносит заранее озвученные фразы;
- 🗣️ **Озвучка** — 32 клипа, сгенерированные той же голосовой моделью TTS,
  что используется в [Capricorn_ai_qwen](https://github.com/GameGleke/Capricorn_ai_qwen)
  (edge-tts `ru-RU-SvetlanaNeural` + питч-шифт и глитч-обработка), все WAV вшиты в DLL;
- 🖐️ **Телекинез** — удержание Grip притягивает ближайший физический объект к руке;
- 🔥 **Scorch** — удерживайте Trigger, глядя на цель: периодический урон,
  растущий со временем удержания (как «прожигание» нейролинка в аниме);
- 💀 Реагирует на снятие чипа и смерть игрока отдельными репликами.

> ⚠️ Фразы заготовлены заранее (вопрос→ответ пока не реализовано — по плану).

## 📦 Установка

**Полная пошаговая инструкция по ручной установке — в [INSTALL.md](INSTALL.md).**

Кратко: установите MelonLoader (LemonLoader Stable) → соберите
`CapriMod.dll` (`dotnet build src/CapriModPlugin/CapriMod.csproj -c Release`)
→ скопируйте DLL в `<папка игры>\Mods\`. Всё содержимое (озвучка, модель чипа)
внутри одного файла.

## Структура репозитория

```
INSTALL.md                  — ручная установка мода в игру (читай отсюда)
docs/BoneLab-Modding-Guide.md — гайд по стеку моддинга BONELAB
src/CapriModPlugin/         — исходники мода (MelonMod, .NET 6, Harmony):
    Main.cs                 — точка входа, хуки PlayerMarker/Player_Health
    Bridge.cs               — мост Mono<->IL2CPP, фабрика предмета «чип»
    CapriChip.cs            — надевание/снятие чипа (слот головы)
    CapriEntity.cs          — Капри: появление в случайных местах, видимость только владельцу
    CapriController.cs      — телекинез (Grip) и Scorch (Trigger, урон по времени удержания)
    VoiceBank.cs            — загрузка WAV из EmbeddedResource, проигрывание
    Config.cs               — настройки (UserData/CapriMod_config.json)
    Audio/                  — 32 озвученных клипа + manifest.txt (вшиты в DLL)
src/CapriMod/               — ранний каркас (исторически)
assets_src/tts/             — скрипт генерации озвучки (phrases.py, generate_voices.py)
```

## Сборка

Нужен .NET SDK 6+ и установленный BONELAB с MelonLoader:

```bat
set BONELAB_DIR=C:\...\steamapps\common\Bonelab
dotnet build src\CapriModPlugin\CapriMod.csproj -c Release
```

DLL автоматически копируется в `<игры>\Mods`.

## Обновление озвучки

```bash
cd assets_src/tts
pip install edge-tts numpy
python generate_voices.py   # пересобирает ../../src/CapriModPlugin/Audio/*.wav
```

Тексты фраз — в `assets_src/tts/phrases.py`, они же дублируются в `manifest.txt`
(показываются субтитром рядом с Капри).

## Статус

- [x] Изучен стек моддинга BONELAB (MelonLoader, Il2CppInterop, Harmony, SLZ API)
- [x] Чип-предмет, экипируемый в голову; логика надевания/снятия
- [x] Капри: случайные точки появления, видимость только владельцу чипа
- [x] Озвучка 32 фраз голосом из Capricorn_ai_qwen, вшита в DLL
- [x] Телекинез (Grip) и Scorch (Trigger, урон растёт с удержанием)
- [ ] Ответы на вопросы игрока (диалог) — в планах
- [ ] Кастомная модель Капри в Unity/BoneSmash (сейчас — процедурная голограмма)

Стек и API: см. [docs/BoneLab-Modding-Guide.md](docs/BoneLab-Modding-Guide.md).
