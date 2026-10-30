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
        private Transform _scorchTarget;
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
            var camTf = Camera.main != null ? Camera.main.transform : Main.Rig.m_Headpoint.transform;

            // --- появления в случайных местах ---
            if (!_entityOut && Time.time >= _nextSpawnTime)
            {
                Vector3 p = CapriService.RandomSpawnPointNear(camTf);
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
            HandleTelekinesis(camTf);
            HandleScorch(camTf);
        }

        private InputState ReadRightController()
        {
            // Основной путь — игровые Hand-компоненты (учитывают калибровку игры).
            // ВАЖНО: Controller/Hand — Il2Cpp-объекты; читаем только простые float-поля,
            // это безопасно из Mono-потока MelonUpdate.
            var hand = Main.RightHand ?? Main.LeftHand;
            if (hand != null)
            {
                try
                {
                    var c = hand.m_Controller as BaseController;
                    if (c != null)
                        return new InputState(c.m_GripAxis > 0.6f, c.m_TriggerAxis > 0.75f);
                }
                catch { /* межверсионные различия — падаем в XR fallback */ }
            }
            // Fallback напрямую из XR (чистый UnityEngine.XR — всегда совместим)
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!dev.isValid) dev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool grip = false, trig = false;
            if (dev.TryGetFeatureValue(CommonUsages.gripButton, out bool gb)) grip = gb;
            if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb)) trig = tb;
            return new InputState(grip, trig);
        }

        private struct InputState
        {
            public readonly bool Grip, Trigger;
            public InputState(bool g, bool t) { Grip = g; Trigger = t; }
        }

        // ================= ТЕЛЕКИНЕЗ =================
        private void HandleTelekinesis(Transform camTf)
        {
            var st = ReadRightController();

            if (st.Grip && !_gripWasHeld)
            {
                _gripWasHeld = true;
                _lastPlayerActionTime = Time.time;
                TryGrabNearestRigidbody(camTf);
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
                Vector3 anchor = GetHandPosition() + camTf.forward * 0.9f;
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
            var hand = Main.RightHand;
            if (hand != null && hand.m_Controller != null) return hand.m_Controller.transform.position;
            if (Main.Rig?.rightHand?.transform != null) return Main.Rig.rightHand.transform.position;
            return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        }

        private void TryGrabNearestRigidbody(Transform from)
        {
            Collider[] hits = Physics.OverlapSphere(from.position, Config.TelekinesisRange);
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
                float d = Vector3.Distance(from.position, rb.position);
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
        private void HandleScorch(Transform camTf)
        {
            var st = ReadRightController();

            if (st.Trigger && !_triggerWasHeld)
            {
                _triggerWasHeld = true;
                _scorchHoldStart = Time.time;
                _lastDamageTick = 0f;
                FindScorchTarget(camTf);
                if (_scorchTarget != null)
                    CapriService.Speak(VoiceBank.Category.Attack, CapriEntity.Instance?.VoiceAnchor);
                EnsureBeam();
            }
            else if (st.Trigger && _triggerWasHeld)
            {
                // удержание: периодический урон, растущий со временем
                float hold = Time.time - _scorchHoldStart;
                if (_scorchTarget == null) FindScorchTarget(camTf);
                if (_scorchTarget != null && Time.time - _lastDamageTick >= Config.DamageTickInterval)
                {
                    _lastDamageTick = Time.time;
                    float dmg = Mathf.Min(Config.DamageBase + Config.DamageRampPerSecond * hold, Config.DamageMax);
                    DealDamage(_scorchTarget, dmg);
                }
                UpdateBeam(camTf);
            }
            else if (!st.Trigger && _triggerWasHeld)
            {
                _triggerWasHeld = false;
                EndScorch();
            }
        }

        private void FindScorchTarget(Transform camTf)
        {
            _scorchTarget = null;
            if (Physics.Raycast(camTf.position, camTf.forward, out var hit, Config.ScorchRange))
            {
                // цель — NPC или другой rig (кто угодно с RigManager/AIBrain), но не сам игрок
                var rm = hit.collider.GetComponentInParent<RigManager>();
                if (rm != null && rm != Main.Rig)
                {
                    _scorchTarget = rm.transform;
                    return;
                }
                // Марро-цель без rig'а — всё равно бьём по хиту, если у него есть жёсткость здоровья
                var hp = hit.collider.GetComponentInParent<Player_Health>();
                if (hp != null) { _scorchTarget = hp.transform; return; }
            }
        }

        /// <summary>Урон цели. Ищем компонент здоровья (NPC Health / Player_Health) и наносим урон.
        /// Рефлексия по IL2CPP-методам — чтобы работать на разных версиях игры без жёсткой привязки
        /// к сигнатуре TAKEDAMAGE.</summary>
        private void DealDamage(Transform target, float amount)
        {
            if (target == null) return;

            // 1) NPC: SLZ.Marrow.NPCHealth.TAKEDAMAGE(...)
            var npc = target.GetComponent<Il2CppSLZ.Marrow.NPCHealth>();
            if (npc != null && TryInvokeTake(npc, amount)) return;

            // 2) Игрок/аватар: SLZ.Bonelab.Player_Health.TAKEDAMAGE(...)
            var ph = target.GetComponent<Player_Health>();
            if (ph != null && TryInvokeTake(ph, amount)) return;

            // 3) Универсальный запасной путь — физический импульс (визуальный эффект)
            var rb = target.GetComponentInChildren<Rigidbody>();
            if (rb != null) rb.AddExplosionForce(amount * 40f, rb.position, 3f);
        }

        private static bool TryInvokeTake(object healthComp, float amount)
        {
            try
            {
                var type = healthComp.GetType(); // Il2CppSystem type через interop
                foreach (var m in type.GetMethods())
                {
                    if (!string.Equals(m.Name, "TAKEDAMAGE", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) continue;
                    if (ps[0].ParameterType != typeof(float)) continue;

                    var args = new object[ps.Length];
                    args[0] = amount;
                    for (int i = 1; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt == typeof(bool)) args[i] = true;                 // damageDirection/etc
                        else if (pt.IsEnum) args[i] = System.Activator.CreateInstance(pt); // Physical/Pure
                        else args[i] = null;                                    // ссылки (Transform/Vector?)
                    }
                    m.Invoke(healthComp, args);
                    return true;
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning($"DealDamage({healthComp?.GetType().Name}): {e.Message}");
            }
            return false;
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

        private void UpdateBeam(Transform camTf)
        {
            if (_beam == null) return;
            if (_scorchTarget == null) { _beam.enabled = false; return; }
            _beam.enabled = true;
            Vector3 head = Main.Rig != null && Main.Rig.m_Headpoint != null
                ? Main.Rig.m_Headpoint.transform.position + camTf.forward * 0.15f
                : camTf.position;
            Vector3 tgt = _scorchTarget.position + Vector3.up * 1.2f;
            // дрожание луча
            tgt += UnityEngine.Random.insideUnitSphere * 0.06f;
            _beam.SetPosition(0, head);
            _beam.SetPosition(1, tgt);
        }

        private void EndScorch()
        {
            _scorchTarget = null;
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
