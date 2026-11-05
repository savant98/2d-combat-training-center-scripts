using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Flying Chase")]
public class FlyingChaseDataSO : FlyingAbilityDataSO
{
    [Header("Chase")]
    [Tooltip("플레이어에게 접근하는 시간의 최소·최대(초)입니다. 끝나면 Recover를 거쳐 다음 패턴(거리 유지)을 예약합니다.")]
    public Vector2 chaseDuration = new(2f, 3f);
    [Tooltip("켜면 플레이어에게 닿은 뒤 contactHoldTime이 지나면 추적을 끝냅니다.")]
    public bool endOnContact = true;
    [Tooltip("적과 플레이어의 중심이 이 거리(칸) 안으로 들어오면 닿은 것으로 봅니다.")]
    [Min(0f)] public float contactDistance = 1.1f;
    [Tooltip("닿은 뒤 추적을 끝내기까지의 시간(초)입니다.")]
    [Min(0f)] public float contactHoldTime = 0.15f;

    [Header("Retreat")]
    [Tooltip("Recover 동안 플레이어 반대쪽으로 물러나는 목표 거리(칸)입니다. 닿은 자리에 머물러 접촉 피해가 반복되지 않게 합니다.")]
    [Min(0f)] public float retreatDistance = 2f;
}
