using MelonLoader;

namespace CapriMod
{
    internal static class BuildInfo
    {
        public const string Name = "CapriMod";
        public const string Description = "BoneLabCapri — VR-мод для BONELAB (прототип).";
        public const string Author = "GameGleke";
        public const string Company = null;
        public const string Version = "0.1.0";
        public const string DownloadLink = "https://github.com/GameGleke/BoneLabCapri";
    }

    [assembly: MelonInfo(typeof(Main), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author, BuildInfo.DownloadLink)]
    [assembly: MelonGame("Steel Reign", "BONELAB")]
    [assembly: VerifyLoadMelonWithAttribute] // совместимость с LemonLoader
}
