using UnityEngine;
using System.Collections;
using System;

public class EnemyShieldSystem : MonoBehaviour
{
    [Header("ShieldSystem Settings")]
    [SerializeField] private EnemyShieldDataSO data;
    [SerializeField] private SpriteRenderer[] sprites;

    public float CurrentShield { get; private set; }
    public bool IsSpawnLocked { get; set; }

    public event Action OnBreak;
    public event Action OnBreakEnd;

    public event Action<WeaponBaseSO.DamageInfo> OnShieldDamaged;
    public float MaximumShield => data.maxShield;
    private EnemyShieldDataSO trainingData;

    // 공유 SO를 수정하지 않고 자율훈련용 인스턴스의 용량만 조절한다.
    public void ConfigureTrainingCapacity(float capacity)
    {
        if (trainingData == null)
        {
            trainingData = Instantiate(data);
            data = trainingData;
        }
        data.maxShield = Mathf.Max(1f, capacity);
        ResetTrainingShield();
    }

    public void ResetTrainingShield()
    {
        if (isBreaking)
        {
            aiBrain.ResumeBrain();
            OnBreakEnd?.Invoke();
        }
        ResetShield();
    }

    private void OnDestroy()
    {
        if (trainingData != null) Destroy(trainingData);
        if (shieldMaterials == null) return;
        foreach (var material in shieldMaterials)
            if (material != null) Destroy(material);
    }

    private EnemyHealth health;
    private EnemyUI enemyUI;
    private AiBrain aiBrain;
    private Animator animator;
    private Material[] shieldMaterials;

    private int breakAnimHash;
    private float regenTimer;
    private float breakEndTime;
    private float breakableEndTime;
    private bool breakable = false;
    private bool isBreaking = false;

    private Coroutine coroutine;
    private void Awake()
    {
        CurrentShield = data.maxShield;
        if (sprites != null)
        {
            shieldMaterials = new Material[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null)
                {
                    shieldMaterials[i] = Instantiate(sprites[i].material);
                    sprites[i].material = shieldMaterials[i];
                }
            }
        }
    }
    public void ResetShield()
    {
        CurrentShield = data.maxShield;
        breakable = false;
        isBreaking = false;
        regenTimer = 0f;
        breakEndTime = 0f;
        breakableEndTime = 0f;
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }
        SetShieldEmission(Color.black);
        if (sprites != null)
            foreach (var sprite in sprites)
                if (sprite != null) sprite.enabled = false;

        enemyUI.ResetEnemyHUD();
    }
    public void SetShieldSystem(EnemyHealth health, EnemyUI enemyUI, AiBrain aiBrain)
    {
        this.health = health;
        this.enemyUI = enemyUI;
        this.aiBrain = aiBrain;
        animator = aiBrain.gameObject.GetComponent<EnemyController>().animator;
        breakAnimHash = Animator.StringToHash(data.breakAnimTrigger);
    }
    private void Update()
    {
        if (isBreaking)
        {
            if (Time.time >= breakEndTime)
            {
                ExitBreakState();
            }
            return;
        }

        // 브레이크 가능 상태에서는 재생(regen) 안함
        if (breakable)
        {
            // 일정 시간까지 Break시키 못하면 쉴드재생
            if (Time.time >= breakableEndTime)
            {
                breakable = false;
                RecoverShield(data.breakFailedRecoveryValue);
            }

            return;
        }

        // 피해 직후 regenDelay 동안 재생 안함
        if (regenTimer > 0)
        {
            regenTimer -= Time.deltaTime;
            return;
        }

        // 쉴드 회복
        if (CurrentShield < data.maxShield)
        {
            CurrentShield = Mathf.Min(data.maxShield, CurrentShield + data.shieldRegenRate * Time.deltaTime);
            enemyUI.ShowShield(CurrentShield / data.maxShield, health.IsBossEnemy);
        }
    }

    public void TakeDamage(WeaponBaseSO.DamageInfo info, Vector3 hitPosition)
    {
        if (IsSpawnLocked || health.IsInvincible)
            return;

        if (isBreaking)
        {
            // 브레이크 상태에서는 HP 데미지
            health.TakeDamage(info, hitPosition);
            return;
        }

        if (CurrentShield > 0)
        {
            ApplyShieldDamage(info, hitPosition);
            return;
        }

        if (breakable && info.damageType == DamageType.Skill)
        {
            EnterBreakState();
            return;
        }
    }
    private void ApplyShieldDamage(WeaponBaseSO.DamageInfo dmgInfo, Vector3 hitPosition)
    {
        OnShieldDamaged?.Invoke(dmgInfo);
        CurrentShield -= dmgInfo.damage;
        regenTimer = data.regenDelay;

        // UI 표시
        enemyUI.ShowShield(CurrentShield / data.maxShield, health.IsBossEnemy);
        FloatingTextManager.Instance.ShowDamage(dmgInfo, hitPosition + Vector3.up * 0.5f);

        if (CurrentShield > 0)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);

            coroutine = StartCoroutine(ShieldHitFlash());
            return;
        }

        // 쉴드 완전 파괴
        CurrentShield = 0;
        breakable = true;

        // 일정 시간 동안만 Break 가능
        breakableEndTime = Time.time + data.breakableDuration;

        // 피드백 재생
        enemyUI.ShowBreakable();
        UIManager.Instance.StartShieldBreakEffect();
        FeedbackPlayer.Instance.Play(data.shieldBreakFeedback, transform);
    }
    private void EnterBreakState()
    {
        isBreaking = true;
        breakable = false;

        aiBrain.PauseBrain();

        breakEndTime = Time.time + data.breakDuration;

        // Break 애니메이션
        if (animator)
            animator.SetTrigger(breakAnimHash);

        //물리 중지 & 넉백 트리거
        OnBreak?.Invoke();

        //피드백 재생
        enemyUI.ShowKnockDown();
        UIManager.Instance.StartBreakEffect();
        FeedbackPlayer.Instance.Play(data.breakFeedback, transform);
    }
    private void ExitBreakState()
    {
        isBreaking = false;

        //쉴드 완전 회복
        RecoverShield(1f);

        aiBrain.ResumeBrain();

        // Break 종료 트리거
        OnBreakEnd?.Invoke();

        // enemy 상태 UI 숨기기
        enemyUI.HideKnockDown();
        enemyUI.HideBreakable();
    }
    private void RecoverShield(float recoveryValue)
    {
        CurrentShield = data.maxShield * recoveryValue;
        regenTimer = data.regenDelay;

        // 선택적으로 브레이크 실패 피드백
        if (data.shieldRegenFeedback != null)
            FeedbackPlayer.Instance.Play(data.shieldRegenFeedback, transform);

        enemyUI.ShowShield(CurrentShield / data.maxShield, health.IsBossEnemy);
        enemyUI.HideBreakable();
    }

    private IEnumerator ShieldHitFlash()
    {
        // 타격 직후 모든 쉴드 스프라이트 활성화
        if (sprites != null)
        {
            foreach (var s in sprites)
            {
                if (s != null) s.enabled = true;
            }
        }

        SetShieldEmission(Color.cyan * 2f);
        float intensityValue = 2f;

        while (intensityValue >= 0f)
        {
            SetShieldEmission(Color.cyan * intensityValue);
            intensityValue -= Time.deltaTime * 5f;
            yield return null;
        }

        // 연출 종료 후 모든 쉴드 스프라이트 비활성화
        if (sprites != null)
        {
            foreach (var s in sprites)
            {
                if (s != null) s.enabled = false;
            }
        }
    }

    // 배열 내 모든 쉴드 재질의 색상을 일괄 업데이트하는 헬퍼 함수
    private void SetShieldEmission(Color color)
    {
        if (shieldMaterials == null) return;

        foreach (var mat in shieldMaterials)
        {
            if (mat != null) mat.SetColor("_EmissionColor", color);
        }
    }
}
