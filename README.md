# BoneLabCapri

VR-мод для игры **BONELAB** (MelonLoader, .NET 6, C#). Репозиторий: https://github.com/GameGleke/BoneLabCapri

## Структура

```
docs/                     — изученные материалы по моддингу BONELAB
  BoneLab-Modding-Guide.md — полный гайд (стек, API, хуки, сборка)
src/CapriMod/             — исходники мода (MelonMod + Harmony-хуки)
```

## Быстрый старт (разработка)

1. Установить [MelonLoader](https://github.com/LemonLoaders-Maintained/LemonLoader-Stable/releases) в BONELAB (запустить установщик, указать папку с `BONELAB.exe`).
2. Задать переменную окружения `BONELAB_DIR` = путь к папке игры.
3. Сборка: `dotnet build src/CapriMod/CapriMod.csproj -c Release` — DLL автоматически копируется в `<игры>/Mods`.
4. Запустить игру в VR, смотреть `Log.txt` в папке игры.

## Изученные скиллы (кратко)

- Мод = **DLL net6.0** с классом `: MelonMod`; игра на **Unity IL2CPP**, API доступен через `Il2CppSLZ.Marrow` и т.д.
- Ключевые классы: `RigManager` (игрок), `Hand`/`BaseController` (VR-контроллеры), `Grip`, `Gun`, `Puppet`/`AIBrain` (NPC), `Player_Health`, `Warehouse/Barcodes` (спавн контента), `UIRig`.
- Патчинг — **Harmony** prefix/postfix (`Gun.OnFire`, `Hand.AttachObject`, `RigManager.SwitchAvatar`...).
- Ссылки на игрока кэшируются в хуке `PlayerMarker.OnPlayerSpawned` (паттерн BoneLib).
- Зависимости по желанию: **BoneLib** (меню/события), **Fusion** (сеть).

Подробнее — в [docs/BoneLab-Modding-Guide.md](docs/BoneLab-Modding-Guide.md).

## Статус

- [x] Изучен стек моддинга BONELAB (MelonLoader, Il2CppInterop, Harmony, SLZ API)
- [x] Каркас проекта мода (Main.cs, Hooking.cs, csproj с автодеплоем в Mods/)
- [ ] Конкретные фичи — уточнить, что должна делать Capri (новый предмет? оружие? механика?)
