using System;
using UnityEngine;

/// <summary>자율훈련장 전용 생존·회복과 간이 측정. 일반 적과 기존 튜토리얼 Dummy는 변경하지 않는다.</summary>
public class TrainingDummyHealth : EnemyHealth
{
    public const float TrainingHealth = 3000f;
    public const float IdleResetSeconds = 3f;

    public SelfTrainingStatistics PracticeStatistics { get; } = new SelfTrainingStatistics();
    public bool IsPracticeVisible { get; private set; }
    public float PracticeElapsed => PracticeStatistics.Duration;
    public event Action<TrainingDummyHealth, WeaponBaseSO.DamageInfo> Damaged;

    private SelfTrainingController training;
    private EnemyController controller;
    private EnemyUI trainingUI;
    private double lastHitTime = double.NegativeInfinity;
    private bool hasDamage;
    private Transform[] spawnTransforms;
    private Vector3[] spawnPositions;
    private Quaternion[] spawnRotations;
    private Vector3[] spawnScales;
    private bool[] spawnActiveStates;

    protected override void Awake()
    {
        maxHP = TrainingHealth;
        base.Awake();
        controller = GetComponentInParent<EnemyController>(true);
        CaptureSpawnPose();
        trainingUI = controller != null ? controller.GetComponentInChildren<EnemyUI>(true) : null;
        if (enemyShieldSystem != null)
            enemyShieldSystem.OnShieldDamaged += RecordDamage;
    }

    private void CaptureSpawnPose()
    {
        // 최초 Prefab 자세만 보관한다. 풀 반환 시 남은 재생 자세를 기준값으로 다시 읽지 않는다.
        if (spawnTransforms != null || controller == null) return;
        var model = controller.facingModel != null ? controller.facingModel.transform : controller.model.transform;
        spawnTransforms = model.GetComponentsInChildren<Transform>(true);
        spawnPositions = new Vector3[spawnTransforms.Length];
        spawnRotations = new Quaternion[spawnTransforms.Length];
        spawnScales = new Vector3[spawnTransforms.Length];
        spawnActiveStates = new bool[spawnTransforms.Length];
        for (int i = 0; i < spawnTransforms.Length; i++)
        {
            var part = spawnTransforms[i];
            spawnPositions[i] = part.localPosition;
            spawnRotations[i] = part.localRotation;
            spawnScales[i] = part.localScale;
            spawnActiveStates[i] = part.gameObject.activeSelf;
        }
    }

    public void RestoreSpawnPose()
    {
        if (spawnTransforms == null) return;
        for (int i = 0; i < spawnTransforms.Length; i++)
        {
            var part = spawnTransforms[i];
            if (part == null) continue;
            part.localPosition = spawnPositions[i];
            part.localRotation = spawnRotations[i];
            part.localScale = spawnScales[i];
        }
        // 모든 좌표를 돌린 뒤 표시 상태를 복구해 OnEnable이 중간 자세를 읽지 않게 한다.
        for (int i = 0; i < spawnTransforms.Length; i++)
            if (spawnTransforms[i] != null)
                spawnTransforms[i].gameObject.SetActive(spawnActiveStates[i]);
    }

    public void InitializePractice(SelfTrainingController owner, int shieldCapacity)
    {
        training = owner;
        if (enemyShieldSystem != null)
            enemyShieldSystem.ConfigureTrainingCapacity(shieldCapacity);
        ResetPractice();
    }

    public override void ResetHealth()
    {
        maxHP = TrainingHealth;
        base.ResetHealth();
    }

    public override void TakeDamage(WeaponBaseSO.DamageInfo info, Vector3 hitPosition)
    {
        if (IsInvincible || !IsValidHit(info)) return;
        base.TakeDamage(info, hitPosition);
        if (currentHP < 1f)
        {
            currentHP = 1f;
            if (trainingUI != null) trainingUI.ShowHP(currentHP / maxHP, false);
        }
        RecordDamage(info);
    }

    protected override void Die()
    {
        // 표시 피해는 그대로 집계하면서 사망·보상·Wave 완료는 발생시키지 않는다.
        currentHP = 1f;
    }

    private void RecordDamage(WeaponBaseSO.DamageInfo info)
    {
        if (!IsValidHit(info)) return;
        double now = Time.timeAsDouble;
        if (training != null) training.SynchronizeTime(now);

        bool freePractice = training == null || !training.IsAnalysisRunning;
        if (freePractice)
        {
            if (!IsPracticeVisible || now - lastHitTime >= IdleResetSeconds)
                PracticeStatistics.Begin(now);
            IsPracticeVisible = true;
            PracticeStatistics.Record(info, now);
        }
        else IsPracticeVisible = false;

        lastHitTime = now;
        hasDamage = true;
        Damaged?.Invoke(this, info);
    }

    private static bool IsValidHit(WeaponBaseSO.DamageInfo info)
    {
        return info.damage > 0f && !float.IsNaN(info.damage) && !float.IsInfinity(info.damage);
    }

    private void Update()
    {
        if (IsPracticeVisible) PracticeStatistics.Advance(Time.timeAsDouble);
        if (!hasDamage || Time.timeAsDouble - lastHitTime < IdleResetSeconds) return;
        if (IsPracticeVisible)
            PracticeStatistics.Complete(lastHitTime + IdleResetSeconds);
        RestoreVitals();
        IsPracticeVisible = false;
        hasDamage = false;
    }

    private void LateUpdate()
    {
        // 스폰·브레이크 회수에서도 훈련 표적은 플레이어에게 접촉 피해를 주지 않는다.
        if (controller != null) controller.CanTouchDamageActive(false);
    }

    public void ResetPractice()
    {
        IsPracticeVisible = false;
        hasDamage = false;
        lastHitTime = double.NegativeInfinity;
        PracticeStatistics.Begin(Time.timeAsDouble);
        RestoreVitals();
    }

    private void RestoreVitals()
    {
        ResetHealth();
        if (enemyShieldSystem != null)
            enemyShieldSystem.ResetTrainingShield();
        else if (trainingUI != null)
            trainingUI.ResetEnemyHUD();
    }

    private void OnDisable()
    {
        IsPracticeVisible = false;
        hasDamage = false;
        training = null;
        Damaged = null;
    }

    private void OnDestroy()
    {
        if (enemyShieldSystem != null)
            enemyShieldSystem.OnShieldDamaged -= RecordDamage;
    }
}
