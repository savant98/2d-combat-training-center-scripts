using UnityEngine;

namespace GameProject.AI.Abilities.Constructor
{
    [CreateAssetMenu(menuName = "Enemy/Ability Data/Constructor/Claw Slam")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "GameProject.AI.Abilities.Constructor", sourceAssembly: "Assembly-CSharp", sourceClassName: "Constructor_ClawSlamData")]
    public class ConstructorClawSlamDataSO : EnemyAbilityDataSO
    {
        [Header("Claw Slam Hitbox")]
        public MeleeHitbox meleeHitbox;
        public WeaponBaseSO.DamageInfo damageInfo;

        [Header("Windup (Rise)")]
        public float windupRiseSpeed = 8f;

        [Header("Execute (Track)")]
        [Tooltip("공중에서 수평으로 플레이어를 쫓아다니는 시간")]
        public float trackDuration = 1.5f;
        [Tooltip("거리에 비례하여 플레이어를 쫓는 가속도")]
        public float trackAcceleration = 50f;
        [Tooltip("브레이크(마찰력) 역할. 수치가 낮을수록 더 많이 미끄러짐")]
        public float trackDrag = 5f;

        [Header("Execute (Telegraph & Slam)")]
        [Tooltip("추적 종료 후, 내려찍기 직전 공중에 멈춰있는 경고(Telegraph) 시간")]
        public float telegraphDelay = 0.2f;
        public float slamSpeed = 30f;

        [Header("Preview Line")]
        public LaserPreviewLine previewLinePrefab;
        public Color previewLineColor = Color.red;
        public float previewLineWidth = 0.08f;
        public float previewLineLength = 20f;
        public LayerMask previewBlockMask;
    }
}