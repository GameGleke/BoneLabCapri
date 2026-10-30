using System;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>Простые настройки мода (JSON в MelonLoader/Mods/UserData/CapriMod/config.json).</summary>
    public static class Config
    {
        // Капри появляется каждые SpawnInterval секунд, стоит VisibleTime секунд
        public static float SpawnInterval = 25f;
        public static float VisibleTime = 8f;
        public static float IdleTalkInterval = 60f;

        // Телекинез
        public static float TelekinesisRange = 7f;      // радиус поиска цели от руки
        public static float TelekinesisForce = 14f;    // сила притяжения к руке

        // Scorch (урон по фокусу)
        public static float DamageTickInterval = 0.6f; // период тиков урона
        public static float DamageBase = 3f;           // базовый урон за тик
        public static float DamageRampPerSecond = 1.5f;// +за каждый секунду непрерывного удержания
        public static float DamageMax = 40f;           // потолок за тик
        public static float ScorchRange = 15f;         // дальность луча

        public static void LoadOrCreate()
        {
            try
            {
                string dir = Path.Combine(MelonUtils.UserDataDirectory, "CapriMod");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "config.json");
                if (!File.Exists(file))
                {
                    File.WriteAllText(file, BuildDefault());
                    MelonLogger.Msg("Создан config.json с настройками по умолчанию.");
                }
                Parse(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Config: {e.Message}");
            }
        }

        private static string BuildDefault() =>
            "{\n" +
            $"  \"SpawnInterval\": {SpawnInterval},\n" +
            $"  \"VisibleTime\": {VisibleTime},\n" +
            $"  \"IdleTalkInterval\": {IdleTalkInterval},\n" +
            $"  \"TelekinesisRange\": {TelekinesisRange},\n" +
            $"  \"TelekinesisForce\": {TelekinesisForce},\n" +
            $"  \"DamageTickInterval\": {DamageTickInterval},\n" +
            $"  \"DamageBase\": {DamageBase},\n" +
            $"  \"DamageRampPerSecond\": {DamageRampPerSecond},\n" +
            $"  \"DamageMax\": {DamageMax},\n" +
            $"  \"ScorchRange\": {ScorchRange}\n" +
            "}\n";

        /// <summary>Мини-парсер плоского JSON вида "key": value — без внешних зависимостей.</summary>
        private static void Parse(string json)
        {
            foreach (var line in json.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().Trim('"');
                string val = line.Substring(colon + 1).Trim().TrimEnd(',', ' ', '\r');
                if (!float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f)) continue;
                switch (key)
                {
                    case "SpawnInterval": SpawnInterval = f; break;
                    case "VisibleTime": VisibleTime = f; break;
                    case "IdleTalkInterval": IdleTalkInterval = f; break;
                    case "TelekinesisRange": TelekinesisRange = f; break;
                    case "TelekinesisForce": TelekinesisForce = f; break;
                    case "DamageTickInterval": DamageTickInterval = f; break;
                    case "DamageBase": DamageBase = f; break;
                    case "DamageRampPerSecond": DamageRampPerSecond = f; break;
                    case "DamageMax": DamageMax = f; break;
                    case "ScorchRange": ScorchRange = f; break;
                }
            }
        }
    }
}
