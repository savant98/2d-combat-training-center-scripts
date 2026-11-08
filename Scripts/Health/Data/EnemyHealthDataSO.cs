using UnityEngine;

/// <summary>적 Prefab과 훈련 표적의 공통 체력 설정이다. 실행 중에는 이 에셋을 수정하지 않는다.</summary>
[CreateAssetMenu(menuName = "Enemy/Health Data")]
public sealed class EnemyHealthDataSO : ScriptableObject
{
    [Min(1f)] public float maxHP = 100f;
}
