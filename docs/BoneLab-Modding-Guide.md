# Гайд по написанию модов для BONELAB (VR)

> Изучено по: официальным докам MelonLoader, исходникам BoneLib (v3.2.2),
> шаблону NotEnoughPhotons/bonelab_template, репозиториям сообщества
> (Lakatrazz/BONELAB-Fusion, EtiTheSpirit/AvatarStatExtender и др.).

## 1. Стек технологий

| Компонент | Что это | Важно |
|---|---|---|
| **Unity** | Движок BONELAB | Игра на IL2CPP, не Mono |
| **MelonLoader (net6)** | Загрузчик модов | Мод = DLL в папке `Mods/`, .NET 6 |
| **Il2CppInterop** | Мост C#↔IL2CPP | Все игровые классы доступны как `Il2Cpp*` пространства имён |
| **Harmony (0Harmony)** | Патчинг методов | Prefix/Postfix/Transpiler хуки |
| **C#** | Язык модов | `TargetFramework: net6.0` |
| **LemonLoader** | Альтернатива ML | BoneLib поддерживает его |

## 2. Пространства имён игры (ключевые для VR)

- **`SLZ.marrow`** (в interop: `Il2CppSLZ.Marrow`) — основной API:
  - `RigManager` — игрок (аватар, руки, `SwitchAvatar()`, урон);
  - `Hand` / `BaseController` / `OpenControllerRig` — VR-руки и контроллеры
    (`m_CurrentAttachedGO` — что держит рука, `quickmenuEnabled`);
  - `Grip` / `GripPoint` — физика захвата предметов;
  - `Gun`, `Magazine`, `Ballistics` — оружие;
  - `Puppet` / `PuppetMasta` / `AIBrain` — NPC (ИИ, смерть, воскрешение);
  - `PhysicsRig` — физика игрока (толкание, удары);
  - `UIRig` — внутриигровой UI;
  - `Level` / `SceneStreaming.SceneStreamer` — загрузка уровней;
  - `Warehouse.*` (`Barcodes`, `AssetWarehouse`, `SpawnableV2Definition`) — контент/спавн через штрихкоды;
  - `GameLayers` — слои физики (Default, Character, Debris, Gun, Interactables...).
- **`SLZ.Bonelab`** (`Il2CppSLZ.Bonelab`) — легаси-классы: `Player_Health` (TAKEDAMAGE, Death), счёт.
- **`SLZ.VRMK`** (`Il2CppSLZ.VRMK`) — аватары (рост, пропорции тела).
- **`UnityEngine`** — стандартные `GameObject`, `Transform`, `MonoBehaviour`.

## 3. Жизненный цикл MelonMod

```csharp
public class Main : MelonMod {
    OnEarlyInitializeMelon() // до загрузки сцены — регистрация типов в IL2CPP
    OnInitializeMelon()      // основной старт: настройки, хуки
    OnSceneFirstAsync(SceneInfo) // первая сцена загружена — обращаться к игре
    OnUpdate()/OnFixedUpdate()/OnLateUpdate() // каждый кадр (аналог MonoBehaviour)
    OnGUIOnScreen()          // ImGui-отладка
}
```
Порядок важен: `ClassInjector.RegisterTypeInIl2Cpp<T>()` — в `OnEarlyInitializeMelon`,
Harmony-хуки — в `OnInitializeMelon`, поиск объектов игрока — через события загрузки уровня.

## 4. Harmony-хуки (реальные примеры из BoneLib/Hooking.cs)

```csharp
// postfix: после выстрела любого оружия
Harmony.Patch(
    AccessTools.Method(typeof(Gun), nameof(Gun.OnFire)),
    postfix: new HarmonyMethod(typeof(Hooks).GetMethod(nameof(OnFirePostfix))));

// prefix: перехват смены аватара (вернув false — отменить оригинальный метод)
[HarmonyPatch(typeof(RigManager), "SwitchAvatar")]
class Patch_SwitchAvatar {
    static bool Prefix(Avatar ___avatar) { ...; return true; }
}
```
Часто хукаемые методы игры: `RigManager.SwitchAvatar`, `Gun.OnFire`,
`Hand.AttachObject/DetachObject`, `Grip.OnAttachedToHand/OnDetachedFromHand`,
`AIBrain.OnDeath/OnResurrection`, `BehaviourBaseNav.KillStart/KillEnd`,
`Player_Health.TAKEDAMAGE / ApplyKillDamage / Death / LifeSavingDamgeDealt`,
`UIRig.Awake`, `PlayerMarker.OnPlayerSpawned`, `AssetWarehouse.OnReady`,
ready-экшен `MarrowGame`, загрузка сессий `SceneStreaming`.

## 5. Доступ к игроку в VR

```csharp
var rm = Object.FindObjectOfType<RigManager>();       // игрок
Hand left = rm.leftHand, right = rm.rightHand;        // VR-руки
GameObject held = right.m_CurrentAttachedGO;          // предмет в руке
ControllerRig.leftController ...                      // состояние кнопок контроллера
```
Лучше подписаться на `PlayerMarker.OnPlayerSpawned` (как BoneLib) и кэшировать
ссылки, чем искать объекты каждый кадр.

## 6. Сборка проекта

- csproj: `net6.0`, `GenerateAssemblyInfo=false`, ссылки на DLL игры:
  `$(BONELAB_DIR)/MelonLoader/net6/*.dll` (MelonLoader, 0Harmony, Il2CppInterop.*)
  и `$(BONELAB_DIR)/MelonLoader/Il2CppAssemblies/*.dll`
  (Assembly-CSharp, Il2CppSLZ.Marrow, Il2CppSLZ.Bonelab, Il2CppSLZ.VRMK, UnityEngine.*).
- Переменная окружения `BONELAB_DIR` — путь к `.exe` игры (стандарт BoneLib).
- Метаданные мода — класс `BuildInfo` (Name/Author/Version...) + атрибуты `[assembly: MelonInfo(...)]`.
- Готовый DLL → в `Mods/` игры.

## 7. Библиотеки-зависимости

- **BoneLib** — меню (BoneMenu), готовые события (Hooking.OnLevelLoaded, OnGrabObject,
  OnPreFireGun, OnNPCBrainDie...), Player-хелперы, нотификации, Hooking.CreateHook.
- **BONELAB-Fusion** — сетевая синхронизация (мультиплеер-фичи).
- **UniTask** — async-код в Unity-слое.

## 8. Публикация

Thunderstore (thunderstore.io/c/bonelab): `manifest.json` (name, version_number,
website_url, dependencies), README.md, icon.png 128x128, zip с DLL в `BONE/MODS/`.
Либо mod.io — встроенная мастерская игры.

## Источники
- github.com/MelonLoader/Documents, github.com/BepInEx/Il2CppInterop
- github.com/yowchap/BoneLib (изучены Main.cs, Hooking.cs, Player.cs, csproj)
- github.com/NotEnoughPhotons/bonelab_template
- github.com/Lakatrazz/BONELAB-Fusion
