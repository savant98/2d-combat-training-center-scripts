using UnityEngine;

namespace GameProject.AI.Abilities.Constructor
{
    [CreateAssetMenu(menuName = "Enemy/Ability Data/Constructor/Spin Chase")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "GameProject.AI.Abilities.Constructor", sourceAssembly: "Assembly-CSharp", sourceClassName: "Constructor_SpinChaseData")]
    public class ConstructorSpinChaseDataSO : EnemyAbilityDataSO
    {
        [Header("Wall approach and return")]
        [Min(0.1f)] public float repositionSpeed = 14f;
        [Min(0f)] public float wallClearance = 4.5f;
        [Min(1f)] public float wallSearchDistance = 80f;
        [Min(0.01f)] public float arrivalTolerance = 0.06f;
        [Min(1f)] public float travelTimeout = 8f;

        [Header("Accelerating chase")]
        [Min(0f)] public float clawOpenTime = 0.2f;
        [Range(0f, 1f)] public float playerSpeedRatio = 2f / 3f;
        [Min(0.01f)] public float accelerationTime = 1f;

        [Header("Rotating claw damage")]
        public WeaponBaseSO.DamageInfo damageInfo = new WeaponBaseSO.DamageInfo { damage = 20f };
        [Min(0.01f)] public float damageInterval = 0.4f;
        public LayerMask playerMask = 1 << 9;
    }
}
