using System;
using System.Collections;
using UnityEngine;

// StarSpear(Star 세트 기어 2단계, 이름 '별창 발사', 2026-11-08 StarCannon에서 이름 변경): 한 번 쓰면 Player 머리 위 발사점 둘레에 별 창을 부채꼴로 소환하고(summonInterval 0이면 한 번에),
// 잠시 뒤 창마다 가장 가까운 적을 조준해 차례로 쏜다. 창은 선회 속도가 제한된 유도로 아주 빠르게 날아가 적·벽에 닿으면 폭발한다.
// 소환·유지·조준·발사 순서는 StarSpearVolley가 맡고, 이 무기는 실행 단계 첫 프레임에 일제 사격 하나를 시작한다.
// Player의 공격 단계(준비·실행·회수)는 짧게 끝나고, 소환된 창은 Player를 조금 늦게 따라다니므로 그동안 이동·다른 행동을 할 수 있다.
// 주변(targetSearchRadius)에 적이 없으면 바로 쏘지 않고 noTargetWaitTime까지 기다리다가, 적이 나타나면 그 적을, 끝까지 없으면 보는 쪽으로 쏜다.
[CreateAssetMenu(menuName = "Weapon/StarSpear")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "StarCannonWeaponSO")]
public class StarSpearWeaponSO : WeaponBaseSO
{
    [Header("Spawn")]
    public LayerMask platformLayer;
    public StarSpearVolley volleyPrefab;
    public StarSpear spearPrefab;

    [Header("Summon")]
    [Min(1)] public int spearCount = 5;
    [Min(0f)] public float summonInterval = 0f;       // 창 사이 소환 간격(0 = 한 번에)
    [Min(0f)] public float holdTime = 0.45f;           // 마지막 창 소환 뒤 첫 발사까지(적이 있을 때)
    [Min(0f)] public float launchInterval = 0.15f;     // 창 사이 발사 간격(소환 순서대로)
    [Min(0f)] public float aimTime = 0.25f;            // 발사 전 목표를 향해 도는 시간
    public Vector2 firePointOffset = new Vector2(0f, 4f);      // Player 중심에서 발사점(부채꼴 중심)까지 = 옛 StarCannon 탄이 생기던 높이
    [Min(0f)] public float fanRadius = 2.2f;           // 발사점에서 창 판정 중심까지
    public float fanBackAngle = 165f;                  // 오른쪽을 볼 때 첫 창(뒤쪽) 각도
    public float fanFrontAngle = 15f;                  // 오른쪽을 볼 때 마지막 창(앞쪽) 각도

    [Header("Hold")]
    [Min(0f)] public float followRate = 4f;            // 발사점이 Player를 따라가는 비율(초당 남은 거리의 비율, PlayerDrone과 같은 식 — 클수록 바짝 따라감)
    [Min(0f)] public float noTargetWaitTime = 5f;      // 주변에 적이 없을 때 발사를 미루고 기다리는 최대 시간(누적)

    [Header("Flight")]
    [Min(0.1f)] public float spearSpeed = 150f;
    [Min(0f)] public float turnRateDegrees = 452.3f;   // 최소 선회 반경 = 속도 / 각속도(라디안) ≈ 19칸
    [Min(0.01f)] public float maxFlightTime = 0.24f;   // 최대 비행 거리 = 속도 × 시간 = 36칸
    [Min(0f)] public float targetSearchRadius = 16f;

    [Header("Explosion")]
    [Min(0f)] public float explosionRadius = 3.75f;    // Player 크기(충돌체 지름 1.5칸) × 2.5

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (controller != null && volleyPrefab != null && spearPrefab != null && WorldEntityPool.Instance != null)
        {
            Vector3 firePoint = controller.transform.position + (Vector3)firePointOffset;
            var volley = WorldEntityPool.Instance.Spawn(volleyPrefab, firePoint, Quaternion.identity);
            volley.Begin(controller, this, WithSource(damageInfo), (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint));
        }
        yield break;
    }

    // 소환 순서(뒤 → 앞) i번째 창의 부채꼴 각도. 왼쪽을 보면 좌우를 뒤집는다.
    public float SlotAngle(int index, int facing)
    {
        float t = spearCount <= 1 ? 0.5f : index / (float)(spearCount - 1);
        float angle = Mathf.Lerp(fanBackAngle, fanFrontAngle, t);
        return facing < 0 ? 180f - angle : angle;
    }

    public float SummonTime(int index) => index * summonInterval;
    // 적이 있을 때의 발사 시각(일제 사격 시작 기준). 적이 없어 기다리면 기다린 만큼 늦어진다.
    public float LaunchTime(int index) => (spearCount - 1) * summonInterval + holdTime + index * launchInterval;
    public float FirstAimTime => LaunchTime(0) - aimTime;
}
