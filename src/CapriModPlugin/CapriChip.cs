using System;
using Il2CppSLZ.Bonelab;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;

namespace CapriMod
{
    /// <summary>
    /// «CHIP OF CAPRI» — чип-конструкт. Предмет, который экипируется в слот головного убора
    /// (как в аниме: чип вставляется в голову). Пока надет — включает присутствие Капри,
    /// телекинез и Scorch. Сняли — Капри исчезает с репликой прощания.
    ///
    /// Реализация: MonoBehavour, вешаемый на GameObject предмета. Игрок берёт чип руками,
    /// подносит к голове — игра сама экипирует его в слот Headwear (у предмета есть
    /// компонент EquippableSlot от BoneSmash/контента). Мы слушаем attach/detach через
    /// BaseEquippable-события в Update-поллинге (надёжнее хуков на разных версиях игры).
    /// </summary>
    public class CapriChip : MonoBehaviour
    {
        // Barcode нашего контента (совпадает с JSON в ModContent/chip)
        public const string BARCODE = "capri.chip.v1";

        [Header("Capri Chip")]
        public bool IsChip = true;

        private Transform _headAttachPoint;      // точка крепления на голове игрока
        private bool _wasEquipped;
        private float _lastStateCheck;

        private void OnEnable()
        {
            // Подстраховка: если предмет уже был где-то прицеплен после загрузки уровня
            _wasEquipped = false;
        }

        private void Update()
        {
            // Проверяем состояние раз в 0.15 c (attach к голове = equipped)
            if (Time.time - _lastStateCheck < 0.15f) return;
            _lastStateCheck = Time.time;

            bool equipped = IsEquippedOnHead();
            if (equipped == _wasEquipped) return;
            _wasEquipped = equipped;

            if (equipped)
            {
                MelonLogger.Msg("CapriMod: чип Капри установлен в голову.");
                CapriController.OnChipEquipped(this);
            }
            else
            {
                MelonLogger.Msg("CapriMod: чип Капри снят.");
                CapriController.OnChipRemoved();
            }
        }

        /// <summary>Чип «в голове», если он прикреплён к точке экипировки RigManager-а.</summary>
        private bool IsEquippedOnHead()
        {
            var rig = Main.Rig;
            if (rig == null || rig.m_Headpoint == null) return false;
            _headAttachPoint ??= rig.m_Headpoint.transform;
            // При экипировке предмет становится child'ом точки головы
            return transform.parent != null &&
                   (transform.parent == _headAttachPoint || transform.parent.IsChildOf(_headAttachPoint));
        }

        private void OnDestroy()
        {
            if (_wasEquipped) CapriController.OnChipRemoved();
        }
    }
}
