using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Rifle Gear 1단계 '핸드캐논'(2026-11-18 1차 시안 → 같은 날 2차: 포탄 → 전방 부채꼴 한 방 폭발).
// 사용자 지시(2차): "핸드캐논이 Starshot(산탄)의 공격범위를 그대로 가져올거야. 대신 산탄이 아니라 한방기술로 바뀌는거야. 전방에 부채꼴 모양 폭발이 일어나도록 바꿔줘."
// - 탄을 쏘지 않는다. 실행 첫 프레임(StartCoroutine이 첫 yield 전까지를 그 자리에서 실행한다)에 총구 앞 부채꼴 안의 적에게 한 번에 피해를 준다.
// - 범위는 옛 산탄 발사(StarMultipleShot)의 판정을 그대로 옮겼다. 옛 산탄은 5발(-10·-5·0·5·10도)이 Player 중심 + (0.8, 0)에서 출발해
//   정확히 6칸 날아가며 반지름 0.209 원으로 경로를 쓸었다(지형 무시, 관통, 탄마다 대상당 한 번). 그래서 범위 = 꼭짓점 apexOffset, 반경 fanRadius,
//   반각 fanHalfAngle의 부채꼴을 edgePadding만큼 둘레로 넓힌 모양이다(탄 사이 틈은 메운다). 벽은 옛 산탄처럼 무시한다.
// - 판정(규칙: RaycastMotor2D 기반, 위치를 직접 바꾸지 않는다): 모터의 OverlapAttackCircle(꼭짓점, 반경 + 여유)로 후보를 모은 뒤,
//   후보 Collider의 ClosestPoint로 부채꼴과의 거리를 재서 여유 안에 드는 대상만 맞힌다(InFan). 대상(HealthBase)마다 한 번만 맞는다.
// - 피해·쉴드·무적·소환 잠금·적중 보상(OnHitEnemy, 게이지) 처리는 RailgunWeaponSO와 같다. 넉백 출처는 쏜 순간의 Player 위치·방향이다
//   (옛 산탄 탄과 같은 값. 꼭짓점을 출처로 삼으면 총구에 겹친 적이 Player 쪽으로 끌려온다).
// - 화면의 전방 화염은 꼭짓점에서 보는 방향으로 즉시 내보내고 같은 프레임에 피해를 적용한다.
//   총구 섬광·발사음은 실행 피드백이 낸다. 연결된 적중 피드백은 맞은 자리에서 재생한다.
// - 화상 기폭(2026-11-21 사용자 결정): 화상을 부여하지 않는다. 직격을 맞은 대상의 화상 스택 n을 모두 소모하고, 그 대상에게
//   detonationDamage 피해의 폭발을 n번(첫 폭발 약 0.067초 뒤, 이후 1/9초 간격 — 2026-11-21 3차에 1.5배 빠르게) 준다(BurnDetonation, 물리 질의 없음).
[CreateAssetMenu(menuName = "Weapon/Hand Cannon")]
public class HandCannonWeaponSO : WeaponBaseSO
{
    [Header("Fan Blast")]
    [Tooltip("피해를 줄 적 판정 레이어")]
    public LayerMask damageMask;
    [Tooltip("부채꼴 꼭짓점: Player 중심에서의 위치(오른쪽을 볼 때 기준, 칸). 옛 산탄 탄의 출발점")]
    public Vector2 apexOffset = new Vector2(0.8f, 0f);
    [Tooltip("부채꼴 반경(꼭짓점부터, 칸). 옛 산탄 탄의 비행 거리")]
    [Min(0.1f)] public float fanRadius = 6f;
    [Tooltip("부채꼴 반각(도). 옛 산탄의 바깥 탄 각도")]
    [Range(0f, 89f)] public float fanHalfAngle = 10f;
    [Tooltip("부채꼴 둘레의 여유(칸). 옛 산탄 탄의 판정 반지름")]
    [Min(0f)] public float edgePadding = 0.209f;
    [Tooltip("부채꼴 폭발: 꼭짓점에서 보는 방향으로 돌려 재생")]
    public FeedbackDataSO explosionFeedback;
    [Tooltip("맞은 대상마다 맞은 자리에서 재생")]
    public FeedbackDataSO hitFeedback;
    [Tooltip("전방 화염과 연기(HandCannonFan). 최대 3벌을 재사용하며 BlastPlume은 무기가 직접 발사 프레임에 내보낸다")]
    public GameObject fanEffect;
    [Header("Burn Detonation")]
    [Tooltip("화상 기폭: 직격을 맞은 대상의 화상 스택마다 한 번씩 주는 연속 폭발 피해(2026-11-21 사용자 결정 기폭형, 기본 = 직격 100의 20%). 넉백·기술 게이지 없음")]
    [Min(0f)] public float detonationDamage = 20f;
    private const int MaxFanInstances = 3;
    [NonSerialized] private List<GameObject> fanInstances;

    private const float AxisSampleStep = 0.5f;   // 부채꼴 축 위 표본 간격(칸)
    private const float EdgeSampleStep = 1f;     // 부채꼴 양쪽 가장자리 위 표본 간격(칸)

    // 무기 SO는 Player마다 공유되지만 한 번 쏘는 동안만 쓰는 임시 버퍼다(직렬화하지 않는다).
    [NonSerialized] private List<Collider2D> overlapResults;
    [NonSerialized] private HashSet<HealthBase> damagedTargets;

    // 검증·디버그용: 마지막 폭발
    public Vector2 LastApex { get; private set; }
    public int LastFacing { get; private set; }
    public int LastHitCount { get; private set; }
    /// <summary>마지막 폭발에서 기폭한 화상 스택 합(대상마다 그 수만큼 연속 폭발).</summary>
    public int LastDetonatedStacks { get; private set; }

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        Fire(controller);
        yield break;
    }

    private void Fire(CharacterController2D controller)
    {
        if (controller == null || controller.Motor == null) return;
        overlapResults ??= new List<Collider2D>(32);
        damagedTargets ??= new HashSet<HealthBase>();

        int facing = controller.Facing >= 0 ? 1 : -1;
        Vector2 axis = new Vector2(facing, 0f);
        Vector2 center = controller.transform.position;
        Vector2 apex = center + new Vector2(apexOffset.x * facing, apexOffset.y);

        controller.AddRecoil(recoilPower, facing);

        LastApex = apex;
        LastFacing = facing;
        LastDetonatedStacks = 0;
        var rotation = Quaternion.Euler(0f, 0f, facing < 0 ? 180f : 0f);
        PlayBlast(apex, rotation);
        LastHitCount = ApplyDamage(controller, apex, axis);

        if (explosionFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(explosionFeedback, apex, rotation);
    }

    private void PlayBlast(Vector2 apex, Quaternion rotation)
    {
        var fan = TakeFan();
        if (fan == null) return;

        fan.SetActive(false);
        fan.transform.SetPositionAndRotation(apex, rotation);
        var plume = fan.transform.Find("BlastPlume")?.GetComponent<ParticleSystem>();
        if (plume != null)
        {
            var automaticEmission = plume.emission;
            automaticEmission.enabled = false;
        }
        fan.SetActive(true);

        if (plume == null) return;
        // 다음 입자 갱신을 기다리지 않고, 피해가 들어갈 때 이미 화염 한 장이 있다.
        plume.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        plume.Play(false);
        plume.Emit(1);
    }

    // 준비가 시작될 때 부채꼴 모양을 미리 한 벌 만들어 둔다(발사 프레임에 만들지 않게).
    public override void OnWindupStarted(CharacterController2D controller)
    {
        fanInstances?.RemoveAll(f => f == null);
        if (fanInstances == null || fanInstances.Count == 0) TakeFan();
    }

    // 재생이 끝난 한 벌을 돌려주고, 모두 재생 중이면 MaxFanInstances까지 새로 만든다(저장 기술로 잇달아 쏠 때). 피드백 입자 묶음(ParticlePool) 아래에 둔다.
    private GameObject TakeFan()
    {
        if (fanEffect == null) return null;
        fanInstances ??= new List<GameObject>();
        fanInstances.RemoveAll(f => f == null);   // 장면이 바뀌어 지워진 것
        foreach (var f in fanInstances)
            if (!IsPlaying(f)) return f;
        if (fanInstances.Count >= MaxFanInstances) return fanInstances[0];
        Transform parent = null;
        if (FeedbackPlayer.Instance != null)
        {
            parent = FeedbackPlayer.Instance.transform.Find("ParticlePool");
            if (parent == null) parent = FeedbackPlayer.Instance.transform;
        }
        var instance = Instantiate(fanEffect, parent);
        instance.SetActive(false);
        fanInstances.Add(instance);
        return instance;
    }

    private static bool IsPlaying(GameObject fan)
    {
        if (!fan.activeSelf) return false;
        foreach (var system in fan.GetComponentsInChildren<ParticleSystem>())
            if (system.IsAlive(false)) return true;
        return false;
    }

    // 부채꼴 안의 적에게 한 번씩 피해를 준다. 맞은 적 수를 돌려준다.
    private int ApplyDamage(CharacterController2D controller, Vector2 apex, Vector2 axis)
    {
        var filter = new ContactFilter2D();
        filter.SetLayerMask(damageMask);
        filter.useTriggers = true;

        // 이 프레임에 움직인 Transform을 판정에 반영한다(실행 단계는 Update에서 시작된다).
        RaycastMotor2D.InvalidateAttackQuerySync();
        int count = controller.Motor.OverlapAttackCircle(apex, fanRadius + edgePadding, filter, overlapResults);
        if (count <= 0) return 0;

        WeaponBaseSO.DamageInfo info = WithSource(damageInfo);
        if (controller.Faction == Faction.Player)
        {
            info.knockbackOrigin = controller.transform.position;
            info.knockbackFacing = controller.Facing;
        }
        else info.knockbackFacing = 0;

        float halfAngle = fanHalfAngle * Mathf.Deg2Rad;
        int hits = 0;
        damagedTargets.Clear();
        for (int i = 0; i < overlapResults.Count; i++)
        {
            Collider2D hit = overlapResults[i];
            if (hit == null) continue;
            HealthBase health = hit.GetComponentInParent<HealthBase>();
            if (health == null || damagedTargets.Contains(health)) continue;
            if (health is not EnemyHealth enemy || health.IsInvincible) continue;
            if (enemy.enemyShieldSystem != null && enemy.enemyShieldSystem.IsSpawnLocked) continue;
            if (!InFan(hit, apex, axis, halfAngle, out Vector2 hitPoint)) continue;   // 이 Collider는 밖이어도 같은 대상의 다른 Collider는 볼 수 있다
            damagedTargets.Add(health);

            // 화상 스택은 직격 전에 읽어 둔다. 직격으로 죽으면 풀 반환 → EnemyBurn.OnDisable이 스택을 0으로 지운다.
            var burn = enemy.GetComponent<EnemyBurn>();
            int burnStacks = burn != null ? burn.Stacks : 0;
            Bounds bodyBounds = burnStacks > 0 ? hit.bounds : default;   // 직격으로 죽어 꺼져도 기폭 연출을 이 자리에서 낸다
            // 핸드캐논은 더 이상 화상을 부여하지 않는다(2026-11-21). 화상에 걸린 대상이면 그 스택을 모두 소모해 스택 수만큼 연속 폭발한다(기폭).
            if (WeaponTraitEffects.ApplyEnemyHit(enemy, info, hitPoint) && burnStacks > 0)
            {
                if (burn != null) burn.Detonate();
                var blast = info;
                blast.damage = detonationDamage;
                blast.damageType = DamageType.Normal;
                blast.knockbackFacing = 0;
                BurnDetonation.Begin(controller, enemy, hit, burnStacks, blast, bodyBounds);
                LastDetonatedStacks += burnStacks;
            }
            OnHitEnemy?.Invoke(controller, hitPoint);
            if (hitFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(hitFeedback, hitPoint);
            hits++;
        }
        overlapResults.Clear();
        damagedTargets.Clear();
        return hits;
    }

    // Collider가 여유를 더한 부채꼴에 닿는지. 부채꼴 안의 표본 점(축 위·양쪽 가장자리 위)마다 Collider의 가장 가까운 점을 구하고,
    // 그 점과 부채꼴의 정확한 거리가 여유 이하이면 닿은 것으로 본다. 맞은 자리(hitPoint)는 가장 가까웠던 Collider 위의 점이다.
    private bool InFan(Collider2D collider, Vector2 apex, Vector2 axis, float halfAngle, out Vector2 hitPoint)
    {
        hitPoint = collider.bounds.center;
        float best = float.PositiveInfinity;
        Vector2 side = new Vector2(-axis.y, axis.x);
        Vector2 upper = axis * Mathf.Cos(halfAngle) + side * Mathf.Sin(halfAngle);
        Vector2 lower = axis * Mathf.Cos(halfAngle) - side * Mathf.Sin(halfAngle);

        for (float t = 0f; t <= fanRadius + 0.001f; t += AxisSampleStep)
            if (Probe(collider, apex, apex + axis * Mathf.Min(t, fanRadius), axis, halfAngle, ref best, ref hitPoint)) return true;
        for (float t = EdgeSampleStep; t <= fanRadius + 0.001f; t += EdgeSampleStep)
        {
            float r = Mathf.Min(t, fanRadius);
            if (Probe(collider, apex, apex + upper * r, axis, halfAngle, ref best, ref hitPoint)) return true;
            if (Probe(collider, apex, apex + lower * r, axis, halfAngle, ref best, ref hitPoint)) return true;
        }
        return false;
    }

    private bool Probe(Collider2D collider, Vector2 apex, Vector2 sample, Vector2 axis, float halfAngle, ref float best, ref Vector2 hitPoint)
    {
        Vector2 point = collider.ClosestPoint(sample);
        float distance = DistanceToFan(point - apex, axis, fanRadius, halfAngle);
        if (distance < best)
        {
            best = distance;
            hitPoint = point;
        }
        return distance <= edgePadding;
    }

    // 꼭짓점에서 본 점 d와 부채꼴(축 axis, 반경 radius, 반각 halfAngle 라디안) 사이의 거리. 안쪽이면 0.
    public static float DistanceToFan(Vector2 d, Vector2 axis, float radius, float halfAngle)
    {
        float along = Vector2.Dot(d, axis);
        float across = Mathf.Abs(axis.x * d.y - axis.y * d.x);
        float length = Mathf.Sqrt(along * along + across * across);
        if (length <= 1e-5f) return 0f;
        if (Mathf.Atan2(across, along) <= halfAngle) return Mathf.Max(0f, length - radius);
        // 가까운 쪽 가장자리(꼭짓점에서 반경 끝까지의 선분)까지의 거리
        var edge = new Vector2(Mathf.Cos(halfAngle), Mathf.Sin(halfAngle));
        var local = new Vector2(along, across);
        float t = Mathf.Clamp(Vector2.Dot(local, edge), 0f, radius);
        return (local - edge * t).magnitude;
    }
}
