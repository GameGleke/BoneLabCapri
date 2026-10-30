using System;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;
using UnityEngine.XR;

namespace CapriMod
{
    /// <summary>
    /// Мозг мода: следит за состоянием чипа, расписанием появлений Капри и обрабатывает
    /// VR-контроллеры (Grip = телекинез, Trigger = Scorch/урон по фокусу).
    /// Висит на DontDestroyOnLoad-объекте.
    /// </summary>
    public class CapriController : MonoBehaviour
    {
        public static bool ChipEquipped { get; private set; }

        private static CapriChip _activeChip;
        private float _nextSpawnTime;
        private float _spawnHideAt;
        private bool _entityOut;
        private float _lastPlayerActionTime;   // для idle-реплик
        private float _lastIdleTalk;

        // === телекинез ===
        private Rigidbody _tkTarget;
        private ConfigurableJoint _tkJoint;
        private bool _gripWasHeld;
        private float _telekinesisStartedAt;

        // === scorch (урон по фокусу) ===
        private bool _triggerWasHeld;
        private float _scorchHoldStart = -1f;
        private float _lastDamageTick;
        private LineRenderer _beam;

        // ================= чип =================
        public static void OnChipEquipped(CapriChip chip)
        {
            _activeChip = chip;
            ChipEquipped = true;
        }

        public static void OnChipRemoved()
        {
            if (!ChipEquipped) return;
            ChipEquipped = false;
            _activeChip = null;
            var ctrl = Main.Instance != null ? Main.Runner?.GetComponent<CapriController>() : null;
            ctrl?.OnChipTakenOff();
        }

        private void OnChipTakenOff()
        {
            ReleaseTelekinesis();
            EndScorch();
            if (CapriEntity.Instance != null)
            {
                CapriService.Speak(VoiceBank.Category.ChipRemoved, CapriEntity.Instance.VoiceAnchor);
            }
            else
            {
                // Капри уже исчезла — прощаемся «в никуда» через голос рядом с головой игрока
                VoiceBank.MarkSpoke();
            }
            CapriEntity.Despawn();
            _entityOut = false;
            _nextSpawnTime = 0f; // при следующем надевании появится сразу
        }

        // ================= цикл =================
        private void Update()
        {
            if (!ChipEquipped || Main.Rig == null) return;

            // --- появления в случайных местах ---
            if (!_entityOut && Time.time >= _nextSpawnTime)
            {
                Vector3 p = CapriService.RandomSpawnPointNear();
                var e = CapriEntity.Spawn(p, Quaternion.identity);
                CapriService.Speak(VoiceBank.Category.Spawn, e.VoiceAnchor);
                _entityOut = true;
                _spawnHideAt = Time.time + Config.VisibleTime;
            }
            if (_entityOut && Time.time >= _spawnHideAt)
            {
                CapriEntity.Despawn();
                _entityOut = false;
                _nextSpawnTime = Time.time + Config.SpawnInterval * UnityEngine.Random.Range(0.7f, 1.4f);
            }

            // --- idle-реплики, если игрок давно ничего не делал ---
            if (_entityOut && Time.time - _lastPlayerActionTime > Config.IdleTalkInterval &&
                Time.time - _lastIdleTalk > Config.IdleTalkInterval)
            {
                _lastIdleTalk = Time.time;
                CapriService.Speak(VoiceBank.Category.Idle, CapriEntity.Instance?.VoiceAnchor);
            }

            // --- способности ---
            HandleTelekinesis();
            HandleScorch();
        }

        private InputState ReadRightController()
        {
            // Основной путь — игровые Hand/BaseController через GameBridge (изолирует IL2CPP).
            if (GameBridge.ReadControllerAxes(out float g, out float t))
                return new InputState(g > 0.6f, t > 0.75f);
            // Fallback напрямую из XR (чистый UnityEngine.XR — всегда совместим)
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (!dev.isValid) dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            bool grip = false, trig = false;
            if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gb)) grip = gb;
            if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool tb)) trig = tb;
            return new InputState(grip, trig);
        }

        private struct InputState
        {
            public readonly bool Grip, Trigger;
            public InputState(bool g, bool t) { Grip = g; Trigger = t; }
        }

        // ================= ТЕЛЕКИНЕЗ =================
        private void HandleTelekinesis()
        {
            var st = ReadRightController();

            if (st.Grip && !_gripWasHeld)
            {
                _gripWasHeld = true;
                _lastPlayerActionTime = Time.time;
                TryGrabNearestRigidbody();
                if (_tkTarget != null)
                {
                    _telekinesisStartedAt = Time.time;
                    CapriService.Speak(VoiceBank.Category.Telekinesis, CapriEntity.Instance?.VoiceAnchor);
                }
                else
                {
                    CapriService.Speak(VoiceBank.Category.NoTarget, CapriEntity.Instance?.VoiceAnchor);
                }
            }
            else if (!st.Grip && _gripWasHeld)
            {
                _gripWasHeld = false;
                ReleaseTelekinesis();
            }

            // цель «летит» к точке между рукой и взглядом
            if (_tkTarget != null && _tkJoint != null)
            {
                Vector3 anchor = GetHandPosition() + GameBridge.ViewDirection * 0.9f;
                _tkJoint.connectedAnchor = _tkJoint.connectedBody
                    ? WorldToConnected(anchor)
                    : anchor;
            }
        }

        private Vector3 WorldToConnected(Vector3 world)
        {
            // connectedAnchor задаётся в локальных координатах connectedBody (тут — мир, body=null у joint-holder)
            var holder = _tkJoint.GetComponentInParent<Transform>();
            return holder != null ? holder.InverseTransformPoint(world) : world;
        }

        private Vector3 GetHandPosition()
        {
            try
            {
                var hand = Main.RightHand ?? Main.LeftHand;
                if (hand != null && hand.transform != null) return hand.transform.position;
            }
            catch { /* межпоточная/версионная проблема — падаем на голову */ }
            return GameBridge.HeadPosition;
        }

        private void TryGrabNearestRigidbody()
        {
            Vector3 from = GameBridge.ViewOrigin;
            Collider[] hits = Physics.OverlapSphere(from, Config.TelekinesisRange);
            Rigidbody best = null;
            float bestDist = float.MaxValue;
            foreach (var col in hits)
            {
                if (col.isTrigger) continue;
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                // не хватаем части тела игрока и сам чип
                if (rb.GetComponentInParent<RigManager>() != null) continue;
                if (rb.GetComponentInParent<CapriChip>() != null) continue;
                float d = Vector3.Distance(from, rb.position);
                if (d < bestDist) { bestDist = d; best = rb; }
            }
            if (best == null) return;

            _tkTarget = best;
            var go = new GameObject("CapriTKJoint");
            go.transform.position = best.position;
            _tkJoint = go.AddComponent<ConfigurableJoint>();
            _tkJoint.autoConfigureConnectedJoint = true;
            _tkJoint.connectedBody = best;
            _tkJoint.xMotion = ConfigurableJointMotion.Locked;
            _tkJoint.yMotion = ConfigurableJointMotion.Locked;
            _tkJoint.zMotion = ConfigurableJointMotion.Locked;
            _tkJoint.angularXMotion = ConfigurableJointMotion.Free;
            _tkJoint.angularYMotion = ConfigurableJointMotion.Free;
            _tkJoint.angularZMotion = ConfigurableJointMotion.Free;
            var s = new JointDrive
            {
                positionSpring = Config.TelekinesisForce * 60f,
                positionDamping = Config.TelekinesisForce * 8f,
                maximumForce = 2500f
            };
            _tkJoint.targetJoint = _tkJoint;
            _tkJoint.drive = s;
            _tkJoint.xDrive = s; _tkJoint.yDrive = s; _tkJoint.zDrive = s;
        }

        private void ReleaseTelekinesis()
        {
            if (_tkJoint != null) Destroy(_tkJoint.gameObject);
            _tkJoint = null;
            _tkTarget = null;
        }

        // ================= SCORCH (урон по фокусу) =================
        private void HandleScorch()
        {
            var st = ReadRightController();

            if (st.Trigger && !_triggerWasHeld)
            {
                _triggerWasHeld = true;
                _scorchHoldStart = Time.time;
                _lastDamageTick = 0f;
                FindScorchTarget();
                if (_scorchTargetGO != null)
                    CapriService.Speak(VoiceBank.Category.Attack, CapriEntity.Instance?.VoiceAnchor);
                EnsureBeam();
            }
            else if (st.Trigger && _triggerWasHeld)
            {
                // удержание: периодический урон, растущий со временем
                float hold = Time.time - _scorchHoldStart;
                if (_scorchTargetGO == null) FindScorchTarget();
                if (_scorchTargetGO != null && Time.time - _lastDamageTick >= Config.DamageTickInterval)
                {
                    _lastDamageTick = Time.time;
                    float dmg = Mathf.Min(Config.DamageBase + Config.DamageRampPerSecond * hold, Config.DamageMax);
                    ApplyScorchDamage(dmg);
                }
                UpdateBeam();
            }
            else if (!st.Trigger && _triggerWasHeld)
            {
                _triggerWasHeld = false;
                EndScorch();
            }
        }

        private GameObject _scorchTargetGO;

        private void FindScorchTarget()
        {
            GameBridge.FindLookTarget(Config.ScorchRange, out _scorchTargetGO);
        }

        /// <summary>Периодический урон цели + физический толчок (визуальный фидбек scorch).</summary>
        private void ApplyScorchDamage(float dmg)
        {
            if (_scorchTargetGO == null) return;
            var health = GameBridge.GetHealthComponent(_scorchTargetGO, out string kind);
            bool dealt = health != null && GameBridge.TakeDamage(health, dmg, GameBridge.ViewDirection);
            GameBridge.ApplyPhysicalPush(_scorchTargetGO, dealt ? dmg : dmg * 0.5f);
            // перегрузка чипа бьёт и по носителю: при долгом удержании (>4 c) — самоповреждение, как в аниме
            float hold = Time.time - _scorchHoldStart;
            if (hold > Config.ScorchSelfDamageAfter && kind != null)
            {
                var self = GameBridge.LocalPlayerHealth();
                if (self != null) GameBridge.TakeDamage(self, dmg * Config.ScorchSelfDamageFraction, Vector3.up);
            }
        }

        private void EnsureBeam()
        {
            if (_beam != null) return;
            var go = new GameObject("CapriScorchBeam");
            _beam = go.AddComponent<LineRenderer>();
            _beam.widthMultiplier = 0.04f;
            _beam.material = new Material(Shader.Find("Sprites/Default"));
            _beam.startColor = new Color(1f, 0.35f, 0.1f, 0.9f);
            _beam.endColor = new Color(1f, 0.85f, 0.2f, 0.6f);
            _beam.positionCount = 2;
            _beam.enabled = false;
        }

        private void UpdateBeam()
        {
            if (_beam == null) return;
            if (_scorchTargetGO == null) { _beam.enabled = false; return; }
            _beam.enabled = true;
            Vector3 head = GameBridge.ViewOrigin + GameBridge.ViewDirection * 0.15f;
            try
            {
                Vector3 tgt = _scorchTargetGO.transform.position + Vector3.up * 1.2f;
                tgt += UnityEngine.Random.insideUnitSphere * 0.06f; // дрожание луча
                _beam.SetPosition(0, head);
                _beam.SetPosition(1, tgt);
            }
            catch { _beam.enabled = false; }
        }

        private void EndScorch()
        {
            _scorchTargetGO = null;
            _scorchHoldStart = -1f;
            if (_beam != null) _beam.enabled = false;
        }

        // ================= смерть игрока =================
        public void NotifyPlayerDeath()
        {
            if (CapriEntity.Instance != null)
                CapriService.Speak(VoiceBank.Category.Death, CapriEntity.Instance.VoiceAnchor);
        }

        private void OnDestroy()
        {
            ReleaseTelekinesis();
            EndScorch();
            CapriEntity.Despawn();
            ChipEquipped = false;
        }
    }
}
