using MelonLoader;

// ВАЖНО: атрибуты сборки идут ДО namespace (иначе ошибка CS1730)
[assembly: MelonInfo(typeof(CapriMod.Main), "CapriMod", "1.0.0", "GameGleke", "https://github.com/GameGleke/BoneLabCapri")]
[assembly: MelonGame("Steel Reign", "BONELAB")]

namespace CapriMod
{
    internal static class BuildInfo
    {
        public const string Name = "CapriMod";
        public const string Description = "Капри — конструкт из чипа (Cyberpunk: Edgerunners) для BONELAB VR.";
        public const string Author = "GameGleke";
        public const string Company = null;
        public const string Version = "1.0.0";
        public const string DownloadLink = "https://github.com/GameGleke/BoneLabCapri";
    }
}
