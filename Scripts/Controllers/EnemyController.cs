using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyController : CharacterController2D
{
    [Header("Physics Setting")]
    public bool GravityOn = false;
    public float Gravity = -30f;

    [Header("Health")]
    public EnemyHealth enemyHealth;

    [Header("Display")]
    [Tooltip("화면에 보이는 표준 이름(보스 등장 이름표·보스 체력바). 비우면 오브젝트 이름을 쓴다.")]
    [SerializeField] private string displayName;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : name.Replace("(Clone)", string.Empty).Trim();

    public EnemyController Poolkey { get; set; }
    public EnemyHealth Health => enemyHealth;
    public AiBrain Brain => brain;
    [SerializeField, Tooltip("TrainingDummy는 종류와 쉴드 상태에 관계없이 끕니다.")]
    private bool weaponKnockbackEnabled = true;
    public bool CanReceiveWeaponKnockback => weaponKnockbackEnabled && !(enemyHealth is TrainingDummyHealth);
    private AnimatorOverrideController animatorOverride;
    // 패턴 Animation 교체용 캐시. Attack0_Windup·Execute·Recover 원본 Clip을 처음 교체할 때 한 번만 이름으로 찾아 둔다.
    private AnimationClip[] attackOriginals;
    // ApplyOverrides에 넘길 (원본, 새 Clip) 3쌍. 매번 비우고 다시 채워 할당하지 않는다.
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> attackOverrides = new(3);
    [Header("몸통 접촉 피해")]
    [Tooltip("Swarmer·Bulwark처럼 몸통 접촉 자체가 공격인 적만 켭니다.")]
    [SerializeField] private bool continuousBodyDamage;
    public bool ContinuousBodyDamage => continuousBodyDamage;
    public bool CanDealContactDamage => canTouchDamage && isActiveAndEnabled
        && (brain == null || !brain.IsPaused) && (enemyHealth == null || enemyHealth.currentHP > 0f);
    private TouchDamage[] contactSources;
    private bool canTouchDamage = true;
    private EnemyShieldSystem shieldSystem;
    private EnemySpawnController spawnController;
    private EnemyTeleporter teleporter;
    private WeaponBaseSO.DamageInfo damageInfo;
    private AiBrain brain;
    private Action waveDeathHandler;
    private float initialGravity = 0f;

    [Header("쉴드 브레이크 낙하")]
    [SerializeField, Tooltip("중력 없이 나는 적이 쉴드 브레이크 동안 중력(-55)으로 바닥이나 통과 발판에 떨어집니다. 평소 비행 규칙은 그대로입니다.")]
    private bool fallOnShieldBreak;
    private bool breakFalling;
    private bool oneWayLandingRequested;
    private bool baseOneWayLanding;
    private bool baseFloorDetection;
    public bool FallOnShieldBreak => fallOnShieldBreak;
    // 검증용: 브레이크로 떨어지는 중인지, 패턴이 통과 발판 착지를 요청했는지
    public bool IsBreakFalling => breakFalling;
    public bool IsOneWayLandingRequested => oneWayLandingRequested;

    protected override void Awake()
    {
        base.Awake();
        initialGravity = Gravity;
        // 평소 값은 Awake에서 정한 그대로 둔다(브레이크 낙하·패턴 요청이 끝나면 이 값으로 돌아간다).
        baseOneWayLanding = motor.SupportsOneWayPlatforms;
        baseFloorDetection = floorDetectionSpeedControl;
        brain = GetComponent<AiBrain>();
        spawnController = GetComponent<EnemySpawnController>();
        teleporter = GetComponent<EnemyTeleporter>();
        contactSources = GetComponentsInChildren<TouchDamage>(true);

        if (overrideAnimator != null)
        {
            animatorOverride = Instantiate(overrideAnimator);
            animator.runtimeAnimatorController = animatorOverride;
        }

        if (enemyHealth == null)
            return;

        // Shield 없다면 취소
        if (enemyHealth.enemyShieldSystem == null)
            return;

        shieldSystem = enemyHealth.enemyShieldSystem;
        shieldSystem.OnBreak += HandleBreak;
        shieldSystem.OnBreakEnd += HandleBreakEnd;
    }
    protected override Vector2 CalculateVelocity()
    {
        Velocity.y += GravityOn || breakFalling ? Gravity * Time.deltaTime : 0;
        // 착지 콜백이 Recover로 바꾸기 전에 이번 물리 이동의 공격 구간과 피해량을 보존한다.
        foreach (var source in contactSources)
            if (source != null) source.CaptureBodyContactStep();
        return Velocity;
    }
    public void ApplyWeaponKnockback(WeaponBaseSO.DamageInfo info)
    {
        // 쉴드 종류는 소진·브레이크 중에도 일반 피격 넉백을 받지 않는다.
        // 쉴드 브레이크 순간의 기존 전용 외력은 HandleBreak에서 별도로 처리한다.
        if (!CanReceiveWeaponKnockback || enemyHealth == null || enemyHealth.enemyShieldSystem != null ||
            IgnoreExternalForces || !isActiveAndEnabled || info.knockbackFacing == 0 ||
            info.sourceWeapon == null)
            return;

        float power = info.sourceWeapon.knockbackPower;
        if (power <= 0f || float.IsNaN(power) || float.IsInfinity(power))
            return;

        float offset = transform.position.x - info.knockbackOrigin.x;
        float direction = Mathf.Abs(offset) > 0.001f ? Mathf.Sign(offset) : info.knockbackFacing;
        AddKnockback(Vector2.right * (direction * power));
    }

    private void HandleBreak()
    {
        // 즉시 멈춤
        Velocity = Vector2.zero;
        externalForce = Vector2.zero;
        CanTouchDamageActive(false);

        // Dummy는 무장 피격과 쉴드 소진 모두 위치를 밀지 않는다. 브레이크 상태 전환은 유지한다.
        if (!CanReceiveWeaponKnockback) return;

        Vector2 force = knockbackForce;
        force.x *= GameManager.Instance.Player.transform.position.x - transform.position.x < 0 ? -1f : 1f;

        Gravity = -55f;
        // 비행 적은 브레이크 동안만 중력을 받아 바닥(통과 발판 포함)에 떨어진다. 지상 적은 원래대로 GravityOn을 쓴다.
        if (fallOnShieldBreak && !GravityOn)
            SetBreakFalling(true);

        AddKnockback(force);
    }
    private void HandleBreakEnd()
    {
        // 브레이크 종료시 호출
        // 떨어져 있던 비행 적은 그 자리에서 멈춘 채 다시 뜬다(이후 순찰이 원래 높이로 데려간다).
        if (breakFalling)
        {
            SetBreakFalling(false);
            Velocity = Vector2.zero;
        }
        Gravity = initialGravity;
        CanTouchDamageActive(true);
    }
    public void ApplyAttackAnimation(AnimationSetSO set)
    {
        // 예전에는 인덱서(이름)로 3번 넣어 넣을 때마다 다시 바인딩(3회, 비용은 Transform 수에 비례)했다.
        // 같은 원본 Clip에 같은 순서·같은 Clip을 ApplyOverrides로 한 번에 넣어 다시 바인딩을 1회로 줄인다(최종 Override 결과는 같다).
        // 같은 Clip이어도 건너뛰지 않고 늘 다시 바인딩한다. 예전 세 번의 바인딩 사이에는 Animator 평가가 없었으므로
        // 패턴 시작 순간의 자세를 기본값으로 잡는 결과도 예전과 같다.
        if (attackOriginals == null)
            attackOriginals = FindAttackOriginals();
        attackOverrides.Clear();
        AddAttackOverride(attackOriginals[0], set.windup);
        AddAttackOverride(attackOriginals[1], set.execute);
        AddAttackOverride(attackOriginals[2], set.recover);
        // 이름이 맞는 원본이 없으면 인덱서도 바꾸지 않았으므로, 넣을 것이 없을 때는 다시 바인딩하지 않는다.
        if (attackOverrides.Count > 0)
            animatorOverride.ApplyOverrides(attackOverrides);
    }
    private AnimationClip[] FindAttackOriginals()
    {
        var clips = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        animatorOverride.GetOverrides(clips);
        return new[] { FindOriginalClip(clips, "Attack0_Windup"), FindOriginalClip(clips, "Attack0_Execute"), FindOriginalClip(clips, "Attack0_Recover") };
    }
    // 인덱서가 이름으로 찾던 원본 Clip(EnemyBase.controller 안에서 원본 이름은 겹치지 않는다).
    private static AnimationClip FindOriginalClip(List<KeyValuePair<AnimationClip, AnimationClip>> clips, string name)
    {
        foreach (var pair in clips)
            if (pair.Key != null && pair.Key.name == name)
                return pair.Key;
        return null;
    }
    private void AddAttackOverride(AnimationClip original, AnimationClip clip)
    {
        if (original != null)
            attackOverrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(original, clip));
    }
    public void RequestTouchDamage(PlayerHealth player, float dmg, bool isBodyContact = true)
    {
        dmg = isBodyContact ? ResolveBodyContactDamage(dmg) : ResolveContactDamage(dmg);
        ApplyContactDamage(player, dmg);
    }

    // 같은 물리 이동의 마지막 착지 타격만 보존하며 다음 Recover 틱에는 다시 0으로 수집된다.
    internal void ApplyBodyContactStep(PlayerHealth player, float capturedDamage)
        => ApplyContactDamage(player, capturedDamage);

    private void ApplyContactDamage(PlayerHealth player, float dmg)
    {
        if (!CanDealContactDamage || player == null || !float.IsFinite(dmg) || dmg <= 0f) return;
        damageInfo.damageType = DamageType.Normal;
        damageInfo.damage = dmg;
        player.TakeDamage(damageInfo, transform.position);
    }

    /// <summary>검·다리 등 별도 무기 판정의 기존 패턴별 피해 설정을 보존한다.</summary>
    public float ResolveContactDamage(float defaultDamage)
    {
        var pattern = brain != null ? brain.current : null;
        var settings = pattern != null && pattern.IsRunning ? pattern.Data : null;
        return settings != null && settings.overrideContactDamage ? Mathf.Max(0f, settings.contactDamage) : defaultDamage;
    }

    /// <summary>상시 접촉 적 외에는 실제 몸통 공격 구간에서만 별도 피해를 허용한다.</summary>
    public float ResolveBodyContactDamage(float defaultDamage)
    {
        if (!CanDealContactDamage) return 0f;
        if (continuousBodyDamage) return Mathf.Max(0f, defaultDamage);
        var pattern = brain != null ? brain.current : null;
        return pattern != null && pattern.isActiveAndEnabled && pattern.IsRunning
            && pattern.Data != null && pattern.Data.bodyAttackEnabled && pattern.IsBodyAttackActive
            ? Mathf.Max(0f, pattern.Data.bodyAttackDamage) : 0f;
    }

    public void FacingUpdate(int value)
    {
        Facing = value;
        facingModel.transform.localScale = new Vector3(Facing, 1, 1);
    }

    // 패턴이 잠시 통과 발판 위에 내려앉아야 할 때(Aegis 내려찍기) 켠다. 패턴이 끝나거나 멈출 때 반드시 끈다.
    public void SetOneWayLanding(bool value)
    {
        oneWayLandingRequested = value;
        ApplyGroundBody();
    }
    private void SetBreakFalling(bool value)
    {
        breakFalling = value;
        ApplyGroundBody();
    }
    private void ResetGroundBody()
    {
        breakFalling = false;
        oneWayLandingRequested = false;
        ApplyGroundBody();
    }
    // 평소 값에 브레이크 낙하와 패턴 요청을 더해 통과 발판 착지와 착지 때 세로 속도 정리를 정한다.
    // 위로 오를 때는 통과 발판을 늘 지나가므로(모터 규칙) 바닥에서 다시 떠오를 때 걸리지 않는다.
    private void ApplyGroundBody()
    {
        if (motor != null)
            motor.SupportsOneWayPlatforms = baseOneWayLanding || breakFalling || oneWayLandingRequested;
        floorDetectionSpeedControl = baseFloorDetection || breakFalling;
    }
    public void CanTouchDamageActive(bool value)
    {
        canTouchDamage = value;
    }
    public void EnterFreeze()
    {
        // 위치, Velocity, 애니메이션 리셋
        brain.PauseBrain();

        if (shieldSystem != null)
            shieldSystem.IsSpawnLocked = true;

        //animator.speed = 0f;
        animator.SetTrigger("Spawn");
        Velocity = Vector2.zero;
        // 브레이크 중 사망·풀 반환처럼 브레이크 종료가 오지 않는 경로도 평소 비행 상태로 되돌린다.
        ResetGroundBody();

        // 중력 끄기
        Gravity = 0f;

        ColliderActive(false);
        CanTouchDamageActive(false);
        enemyHealth.IsInvincible = true;
    }
    public void ExitFreeze()
    {
        animator.speed = 1f;
        animator.SetTrigger("Idle");
        // Spawn Animation이 끝나고나면 무적 해제, AiBrain 동작, 몸통 데미지 활성화
        brain.ResumeBrain();

        if (shieldSystem != null)
            shieldSystem.IsSpawnLocked = false;

        // 물리 복구
        model.SetActive(true);
        Gravity = initialGravity;

        ColliderActive(true);
        CanTouchDamageActive(true);
        enemyHealth.IsInvincible = false;
    }
    public IEnumerator OnSpawn()
    {
        motor.ResetOneWayState();
        spawnController.ResetSpawnSequence();
        if (enemyHealth is TrainingDummyHealth trainingDummy)
        {
            // 비키 관절도 Prefab 기준으로 돌린 뒤 새 등장 상태를 시작한다.
            trainingDummy.RestoreSpawnPose();
            animator.Rebind();
            animator.Update(0f);
        }
        EnterFreeze();
        brain.ResetForSpawn();

        enemyHealth.ResetHealth();
        if (shieldSystem != null)
            shieldSystem.ResetShield();

        if (teleporter != null)
            teleporter.Cancel();

        yield return spawnController.PlaySpawnSequence();

        ExitFreeze();
    }
    public void OnDespawn()
    {
        ClearWaveDeathHandler();
        brain.PauseBrain();
        ResetGroundBody();
        spawnController.ResetSpawnSequence();
        if (enemyHealth is TrainingDummyHealth trainingDummy)
            trainingDummy.RestoreSpawnPose();
    }
    public void SetWaveDeathHandler(Action handler)
    {
        // 기존 핸들러 제거
        if (waveDeathHandler != null)
            Health.OnDeath -= handler;

        waveDeathHandler = handler;
        Health.OnDeath += waveDeathHandler;
    }
    public void ClearWaveDeathHandler()
    {
        if (waveDeathHandler == null)
            return;

        Health.OnDeath -= waveDeathHandler;
        waveDeathHandler = null;
    }
}
