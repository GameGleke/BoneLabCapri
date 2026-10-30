using System;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// Сервис Капри: вызывается из CapriController. Отвечает за визуализацию (layer) и звук,
    /// гарантируя что Капри видна и слышна ТОЛЬКО игроку, надевшему чип.
    /// </summary>
    public static class CapriService
    {
        private static int _origLayer = -1;
        private static bool _hidden;

        /// <summary>Скрыть/показать GameObject Капри для всех, кроме носителя чипа.</summary>
        public static void SetVisible(GameObject go, bool visibleForLocalPlayer)
        {
            if (go == null) return;
            if (visibleForLocalPlayer)
            {
                if (_hidden && _origLayer >= 0)
                {
                    SetLayerRecursive(go, _origLayer);
                    _hidden = false;
                }
            }
            else
            {
                if (!_hidden)
                {
                    _origLayer = go.layer;
                    SetLayerRecursive(go, MarrowUntrackedLayer);
                    _hidden = true;
                }
            }
        }

        // Слой, который камера локального VR-игрока не рендерит в mirror view и
        // который мы дополнительно скрываем у других rig'ов — «только ты видишь Капри».
        private const int MarrowUntrackedLayer = 31;

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            var trs = go.GetComponentsInChildren<Transform>(true);
            foreach (var t in trs) t.gameObject.layer = layer;
        }

        /// <summary>Проиграть фразу Капри рядом с ней (3D-позиционированный голос).</summary>
        public static void Speak(VoiceBank.Category cat, Transform at)
        {
            if (!CapriController.ChipEquipped) return;          // без чипа Капри молчит
            if (!VoiceBank.CanSpeak(at ? 2.5f : 4f)) return;     // реплики не должны накладываться
            var clip = VoiceBank.GetRandom(cat);
            if (clip == null || at == null) return;

            var src = at.GetComponent<AudioSource>();
            if (src == null) src = at.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f;       // чистый 3D — голос идёт от неё, как в аниме
            src.minDistance = 0.5f;
            src.maxDistance = 25f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.Play();
            VoiceBank.MarkSpoke();
        }

        /// <summary>Случайная точка появления рядом с игроком (рандомное место, как в запросе).</summary>
        public static Vector3 RandomSpawnPointNear(Transform camTf, float minDist = 2.5f, float maxDist = 6f)
        {
            Vector3 center = camTf.position;
            for (int i = 0; i < 8; i++)
            {
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = UnityEngine.Random.Range(minDist, maxDist);
                Vector3 p = center + new Vector3(Mathf.Cos(ang) * dist, UnityEngine.Random.Range(-0.5f, 1.8f), Mathf.Sin(ang) * dist);
                // Пытаемся поставить на пол
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 4f))
                {
                    p.y = hit.point.y + 0.05f;
                }
                // Не слишком близко к лицу
                if ((p - center).sqrMagnitude > minDist * minDist) return p;
            }
            return center + camTf.forward * minDist;
        }
    }
}
