using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Parabolic Dash")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ParabolicDashData")]
public class ParabolicDashDataSO : EnemyAbilityDataSO
{
    [Header("Parabolic Settings")]
    public float maxHeight = 4f;
    public float flyDuration = 1f;
    public float maxDistance = 10f;
    [Min(0f), Tooltip("해머 머리를 내려칠 때 주는 피해입니다. 0이면 해머 공격 판정을 끕니다.")]
    public float hammerDamage;
}
