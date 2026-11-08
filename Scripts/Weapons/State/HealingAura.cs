using UnityEngine;

// 회복(BurstHealing) 준비 동안 Player 둘레에 펼쳐지는 초록 문양과 입자(2026-11-08, 사용자 요청 "StarSpear·Hammer 등 Player의 다른 무장을 참고한 이펙트와 Animation을 만들되,
// 회복 계열이므로 초록색 발광 이펙트도 함께").
// 판정은 없다. 회복량·준비 시간은 지금까지처럼 HealingWeaponSO와 PlayerWeaponAbility가 정하고, 이 개체는 진행을 보여 주기만 한다.
// - 준비(Charge): 문양(Player/HealingSigil)의 호가 준비 시간에 맞춰 찬다. 빛 알갱이가 몸으로 모이고(StarSpear 충전과 같은 방식), 십자 입자가 떠오르며, 호가 회복 표식을 지날 때마다 불꽃이 튄다.
//   몸 가운데의 빛과 별빛은 호가 찰수록 커진다.
// - 완료(Complete): 무기가 실행 단계에서 부르면 문양이 흰빛으로 번쩍이며 벌어지고 사라진다. 폭발 효과·회복음은 실행 피드백(BurstHealExecuteFeedback)이 맡는다.
// - 취소(Cancel): Player가 맞거나 잡히는 등으로 공격 상태에서 벗어나면 깜빡이며 빠르게 꺼진다. 코루틴에 기대지 않고 공격 상태를 직접 본다(PlayerLaserBeam과 같은 방식).
// 준비음은 취소되는 즉시 멈춰야 하므로 피드백 대신 이 개체가 SoundManager의 Loop로 재생한다. 소리는 준비 시간보다 조금 길게 만들어 Time Scale이 조금 줄어도 다시 시작하지 않는다.
// 시간은 Time.deltaTime이라 히트스톱 때 함께 멈춘다. 값은 MaterialPropertyBlock으로 넘긴다.
// Player 이동(FixedUpdate)과 Animator 뒤에 자리를 잡도록 실행 순서를 늦춘다.
[DefaultExecutionOrder(110)]
public class HealingAura : WorldEntityBase
{
    private enum Phase { None, Charge, Complete, Cancel }

    private const int MarkCount = 3;   // 25%·50%·75% 표식에서 불꽃을 낸다. 100%는 회복 폭발이 대신한다.

    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int SpinId = Shader.PropertyToID("_Spin");
    private static readonly int OpenId = Shader.PropertyToID("_Open");
    private static readonly int DirectionId = Shader.PropertyToID("_Direction");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int StartAngleId = Shader.PropertyToID("_StartAngle");

    [Header("Parts")]
    [SerializeField] private SpriteRenderer sigil;
    [SerializeField] private SpriteRenderer orb;
    [SerializeField] private SpriteRenderer starGlint;
    [SerializeField] private ParticleSystem motes;
    [SerializeField] private ParticleSystem rising;
    [SerializeField] private ParticleSystem markSparks;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float raiseTime = 0.1f;        // 문양이 펼쳐지는 시간
    [SerializeField, Min(0.01f)] private float completeTime = 0.22f;    // 회복 순간 벌어지며 사라지는 시간
    [SerializeField, Min(0.01f)] private float cancelTime = 0.16f;      // 취소될 때 꺼지는 시간

    [Header("Look")]
    [SerializeField] private float spinSpeed = -0.9f;                   // 톱니 고리 회전(라디안/초). 해머·본체와 반대로 돈다
    [SerializeField] private float orbStartSize = 0.9f, orbEndSize = 2.7f;
    [SerializeField] private float orbStartAlpha = 0.25f, orbEndAlpha = 0.75f;
    [SerializeField] private float starStartSize = 1.2f, starEndSize = 2.8f;
    [SerializeField] private float starStartAlpha = 0.15f, starEndAlpha = 0.85f;
    [SerializeField] private float starSpinDegrees = 200f;

    [Header("Mark Sparks")]
    [SerializeField, Min(0)] private int sparksPerMark = 8;
    [SerializeField, Min(0f)] private float sparkSpeedMin = 3f, sparkSpeedMax = 6.5f;

    private MaterialPropertyBlock block;
    private Phase phase = Phase.None;
    private float phaseAge, windupTime = 1f, spin, starAngle;
    private float direction = 1f, ringRadius = 1.62f, startAngle;
    private float quadSize = 6.2f;
    private int marksPlayed;
    private PlayerController player;
    private SoundHandle chargeLoop;

    // 값들
    private float progress, alpha, flash, open = 1f, orbAlpha, orbSize, starAlpha, starSize;

    public bool IsCharging => phase == Phase.Charge && !isReturning;
    public bool IsRunning => phase != Phase.None && !isReturning;
    public float Progress => progress;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (sigil != null && sigil.sharedMaterial != null)
        {
            ringRadius = sigil.sharedMaterial.GetFloat(RadiusId);
            startAngle = sigil.sharedMaterial.GetFloat(StartAngleId) * Mathf.Deg2Rad;
        }
        if (orb != null && orb.sprite != null) quadSize = orb.sprite.bounds.size.x;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        phase = Phase.None;
        phaseAge = spin = starAngle = progress = alpha = flash = orbAlpha = orbSize = starAlpha = starSize = 0f;
        open = 1f;
        marksPlayed = 0;
        player = null;
        ClearParticles();
        Apply();
    }

    public void BeginCharge(CharacterController2D owner, float windupTime, SoundOptions chargeSound)
    {
        this.owner = owner;
        player = owner as PlayerController;
        this.windupTime = Mathf.Max(0.01f, windupTime);
        direction = owner != null && owner.Facing < 0 ? -1f : 1f;
        phase = Phase.Charge;
        phaseAge = 0f;
        if (motes != null) motes.Play();
        if (rising != null) rising.Play();
        if (SoundManager.Instance != null)
            chargeLoop = SoundManager.Instance.PlayLoop(chargeSound, transform.position);
        Evaluate();
        Apply();
    }

    // 실행 단계 시작: 무기가 회복을 적용하는 프레임에 바로 부른다.
    public void Complete()
    {
        if (phase != Phase.Charge) return;
        StopChargeSound();
        StopEmitting();
        progress = 1f;
        phase = Phase.Complete;
        phaseAge = 0f;
        Evaluate();
        Apply();
    }

    private void Cancel()
    {
        StopChargeSound();
        ClearParticles();
        phase = Phase.Cancel;
        phaseAge = 0f;
    }

    private void OnDisable()
    {
        StopChargeSound();   // 풀 회수·방 전환으로 꺼져도 소리가 남지 않게 한다
    }

    private void LateUpdate()
    {
        if (phase == Phase.None || isReturning) return;
        if (owner == null || !owner.gameObject.activeInHierarchy)
        {
            ForceReturnToPool();
            return;
        }

        transform.position = owner.transform.position;
        float dt = Time.deltaTime;
        phaseAge += dt;
        spin += spinSpeed * dt;
        starAngle += starSpinDegrees * dt;

        if (phase == Phase.Charge && !IsWindupAlive()) Cancel();

        if ((phase == Phase.Complete && phaseAge >= completeTime) || (phase == Phase.Cancel && phaseAge >= cancelTime))
        {
            phase = Phase.None;
            ForceReturnToPool();
            return;
        }

        if (phase == Phase.Charge) PlayMarkSparks();
        Evaluate();
        Apply();
    }

    private bool IsWindupAlive()
    {
        if (player == null || player.AbilityManager == null) return false;
        var abilities = player.AbilityManager;
        if (abilities.AttackAbility == null || abilities.AttackAbility.AttackPhase == AbilityState.None) return false;
        return abilities.StateMachine.CurrentState == PlayerState.Attack;
    }

    private void PlayMarkSparks()
    {
        if (markSparks == null) return;
        float charge = phaseAge / windupTime;
        while (marksPlayed < MarkCount && charge >= 0.25f * (marksPlayed + 1))
        {
            float angle = startAngle + 0.25f * (marksPlayed + 1) * Mathf.PI * 2f;
            Vector3 point = new Vector3(Mathf.Sin(angle) * direction, Mathf.Cos(angle), 0f) * ringRadius;
            var emit = new ParticleSystem.EmitParams { position = point, applyShapeToPosition = false };
            for (int i = 0; i < sparksPerMark; i++)
            {
                float direction2D = Random.Range(0f, Mathf.PI * 2f);
                emit.velocity = new Vector3(Mathf.Cos(direction2D), Mathf.Sin(direction2D), 0f) * Random.Range(sparkSpeedMin, sparkSpeedMax);
                markSparks.Emit(emit, 1);
            }
            marksPlayed++;
        }
    }

    private void Evaluate()
    {
        float t;
        switch (phase)
        {
            case Phase.Charge:
                progress = Mathf.Clamp01(phaseAge / windupTime);
                t = Mathf.Clamp01(phaseAge / raiseTime);
                open = Mathf.LerpUnclamped(0.7f, 1f, EaseOutBack(t));
                alpha = Mathf.Clamp01(phaseAge / 0.06f);
                flash = 0f;
                float bodyCharge = Mathf.Pow(progress, 1.2f);
                orbSize = Mathf.Lerp(orbStartSize, orbEndSize, bodyCharge);
                orbAlpha = Mathf.Lerp(orbStartAlpha, orbEndAlpha, bodyCharge) * alpha;
                starSize = Mathf.Lerp(starStartSize, starEndSize, bodyCharge);
                starAlpha = Mathf.Lerp(starStartAlpha, starEndAlpha, bodyCharge) * alpha;
                break;
            case Phase.Complete:
                t = Mathf.Clamp01(phaseAge / completeTime);
                progress = 1f;
                open = Mathf.Lerp(1f, 1.22f, 1f - (1f - t) * (1f - t));
                alpha = (1f - t) * (1f - t);
                flash = Mathf.Pow(1f - t, 1.5f);
                orbSize = orbEndSize * Mathf.Lerp(1f, 1.2f, t);
                orbAlpha = orbEndAlpha * Mathf.Clamp01(1f - t * 2.2f);
                starSize = starEndSize * Mathf.Lerp(1f, 1.25f, t);
                starAlpha = starEndAlpha * Mathf.Clamp01(1f - t * 2.2f);
                break;
            case Phase.Cancel:
                t = Mathf.Clamp01(phaseAge / cancelTime);
                open = Mathf.Lerp(1f, 0.92f, t);
                alpha = (1f - t) * (Mathf.Repeat(phaseAge, 0.06f) < 0.03f ? 1f : 0.35f);   // 전원이 나가듯 깜빡이며 꺼진다
                flash = 0f;
                orbAlpha = 0f;
                starAlpha = 0f;
                break;
        }
    }

    private void Apply()
    {
        if (sigil != null)
        {
            sigil.GetPropertyBlock(block);
            block.SetFloat(ProgressId, progress);
            block.SetFloat(AlphaId, alpha);
            block.SetFloat(FlashId, flash);
            block.SetFloat(SpinId, spin);
            block.SetFloat(OpenId, open);
            block.SetFloat(DirectionId, direction);
            sigil.SetPropertyBlock(block);
        }
        ApplySprite(orb, orbSize, orbAlpha, 0f);
        ApplySprite(starGlint, starSize, starAlpha, starAngle);
    }

    private void ApplySprite(SpriteRenderer sprite, float size, float spriteAlpha, float degrees)
    {
        if (sprite == null) return;
        sprite.transform.localScale = new Vector3(size / quadSize, size / quadSize, 1f);
        sprite.transform.localRotation = Quaternion.Euler(0f, 0f, degrees);
        sprite.color = new Color(1f, 1f, 1f, spriteAlpha);
    }

    private void StopEmitting()
    {
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (rising != null) rising.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void ClearParticles()
    {
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (rising != null) rising.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (markSparks != null) markSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void StopChargeSound()
    {
        if (chargeLoop != null && SoundManager.Instance != null)
            SoundManager.Instance.StopLoop(chargeLoop);
        chargeLoop = null;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
