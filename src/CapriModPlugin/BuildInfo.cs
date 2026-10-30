using MelonLoader;

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

    [assembly: MelonInfo(typeof(Main), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author, BuildInfo.DownloadLink)]
    [assembly: MelonGame("Steel Reign", "BONELAB")]
}
