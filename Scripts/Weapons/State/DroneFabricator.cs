using UnityEngine;

// 보조무기 드론 소환(DroneSpawnWeaponSO) 준비 동안 드론이 나타날 자리에서 드론을 만들어 내는 연출(2026-11-09, 사용자 요청 "PlayerHealing의 이펙트·Animation·사운드를 참고해 드론 생성 이펙트·Animation·사운드를 바꿔 달라").
// 판정은 없다. 생성 시점·준비 시간은 지금까지처럼 DroneSpawnWeaponSO와 PlayerWeaponAbility가 정하고, 이 개체는 진행을 보여 주기만 한다.
// - 준비(Charge): 제작 틀(Player/DroneFabrication)의 다이얼 호가 준비 시간에 맞춰 찬다. 제작 선이 아래에서 위로 쓸고 지나가며 그 아래로 실제 드론 Sprite의 홀로그램(Player/DroneHologram)이 나타나고,
//   제작 선 양 끝에서 용접 불꽃이 튄다. Player의 별에서 제작 선까지 연결 빔이 이어지고, 홀로그램 프로펠러는 실제 드론의 회전 속도까지 빨라진다. 다이얼 구간 경계(1/3·2/3)를 지날 때 불꽃이 튄다.
// - 완료(Complete): 무기가 실행 단계에서 부르면 홀로그램이 흰빛으로 번쩍이며 실제 드론으로 바뀌고, 틀은 벌어지며 사라진다. 폭발 효과·완료음은 드론의 생성 피드백(DroneSpawnExecuteFeedback)이 맡는다.
// - 취소(Cancel): Player가 맞거나 잡히는 등으로 공격 상태에서 벗어나면 깜빡이며 빠르게 꺼진다. 코루틴에 기대지 않고 공격 상태를 직접 본다(HealingAura와 같은 방식).
// 준비음은 취소되는 즉시 멈춰야 하므로 피드백 대신 이 개체가 SoundManager의 Loop로 재생한다. 소리는 준비 시간보다 조금 길게 만들어 Time Scale이 조금 줄어도 다시 시작하지 않는다.
// 시간은 Time.deltaTime이라 히트스톱 때 함께 멈춘다. 값은 MaterialPropertyBlock으로 넘긴다.
// Player 이동(FixedUpdate)과 Animator 뒤에 자리를 잡도록 실행 순서를 늦춘다.
[DefaultExecutionOrder(110)]
public class DroneFabricator : WorldEntityBase
{
    private enum Phase { None, Charge, Complete, Cancel }

    private const int StageCount = 3;   // 다이얼은 세 구간이다. 1/3·2/3에서 불꽃을 내고, 마지막은 생성 폭발이 대신한다.

    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int SpinId = Shader.PropertyToID("_Spin");
    private static readonly int OpenId = Shader.PropertyToID("_Open");
    private static readonly int DirectionId = Shader.PropertyToID("_Direction");
    private static readonly int GantryYId = Shader.PropertyToID("_GantryY");
    private static readonly int GantryAlphaId = Shader.PropertyToID("_GantryAlpha");
    private static readonly int LengthId = Shader.PropertyToID("_Length");
    private static readonly int ScrollId = Shader.PropertyToID("_Scroll");
    private static readonly int RevealYId = Shader.PropertyToID("_RevealY");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int StartAngleId = Shader.PropertyToID("_StartAngle");

    [Header("Parts")]
    [SerializeField] private SpriteRenderer frame;
    [SerializeField] private SpriteRenderer link;
    [SerializeField] private Transform hologram;
    [SerializeField] private Transform rotor;
    [SerializeField] private SpriteRenderer[] hologramSprites;
    [SerializeField] private ParticleSystem motes;
    [SerializeField] private ParticleSystem weldSparks;
    [SerializeField] private ParticleSystem stageSparks;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float raiseTime = 0.12f;       // 틀이 펼쳐지는 시간
    [SerializeField, Min(0.01f)] private float completeTime = 0.28f;    // 생성 순간 벌어지며 사라지는 시간
    [SerializeField, Min(0.01f)] private float cancelTime = 0.18f;      // 취소될 때 꺼지는 시간
    [SerializeField, Min(0.01f)] private float hologramFlashTime = 0.12f;

    [Header("Layout")]
    [SerializeField] private float frameCenterY = -0.32f;               // 틀 중심(드론 루트 기준). 드론 몸통이 루트 아래로 치우쳐 있다.
    [SerializeField] private float gantryHalfHeight = 0.98f;
    [SerializeField] private float gantryHalfWidth = 1.3f;
    [SerializeField] private float linkStartY = 0.62f;                  // Player 몸 기준 들어 올린 별의 높이
    [SerializeField] private float linkWidth = 0.5f;

    [Header("Look")]
    [SerializeField] private float spinSpeed = 1.1f;                    // 톱니 고리 회전(라디안/초)
    [SerializeField] private float rotorTilt = 65f;
    [SerializeField] private float rotorStartSpeed = 90f, rotorEndSpeed = 1080f;   // 초당 도. 끝 속도는 실제 드론 프로펠러와 같다.
    [SerializeField] private float linkScrollSpeed = 5f;
    [SerializeField] private float hologramScrollSpeed = 1.2f;

    [Header("Sparks")]
    [SerializeField, Min(0f)] private float weldRate = 55f;             // 양 끝 합계 초당 개수
    [SerializeField, Min(0)] private int sparksPerStage = 10;
    [SerializeField, Min(0f)] private float stageSparkSpeedMin = 3f, stageSparkSpeedMax = 6.5f;

    private MaterialPropertyBlock block;
    private Phase phase = Phase.None;
    private float phaseAge, windupTime = 1f, spin, rotorAngle, scroll, linkScroll, weldBudget;
    private float direction = 1f, ringRadius = 1.42f, startAngle;
    private float quadSize = 6.2f;
    private int stagesPlayed;
    private Vector3 anchorOffset;
    private PlayerController player;
    private SoundHandle chargeLoop;

    // 값들
    private float progress, alpha, flash, open = 1f, gantryAlpha, hologramAlpha, hologramFlash, linkAlpha;

    public bool IsCharging => phase == Phase.Charge && !isReturning;
    public bool IsRunning => phase != Phase.None && !isReturning;
    public float Progress => progress;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (frame != null && frame.sharedMaterial != null)
        {
            ringRadius = frame.sharedMaterial.GetFloat(RadiusId);
            startAngle = frame.sharedMaterial.GetFloat(StartAngleId) * Mathf.Deg2Rad;
        }
        if (frame != null && frame.sprite != null) quadSize = frame.sprite.bounds.size.x;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        phase = Phase.None;
        phaseAge = spin = rotorAngle = scroll = linkScroll = weldBudget = progress = alpha = flash = gantryAlpha = hologramAlpha = hologramFlash = linkAlpha = 0f;
        open = 1f;
        stagesPlayed = 0;
        player = null;
        if (hologram != null) hologram.localPosition = Vector3.zero;
        ClearParticles();
        Apply();
    }

    public void BeginCharge(CharacterController2D owner, float windupTime, Vector3 anchorOffset, SoundOptions chargeSound)
    {
        this.owner = owner;
        this.anchorOffset = anchorOffset;
        player = owner as PlayerController;
        this.windupTime = Mathf.Max(0.01f, windupTime);
        direction = owner != null && owner.Facing < 0 ? -1f : 1f;
        phase = Phase.Charge;
        phaseAge = 0f;
        if (motes != null) motes.Play();
        if (SoundManager.Instance != null)
            chargeLoop = SoundManager.Instance.PlayLoop(chargeSound, transform.position);
        Evaluate();
        Apply();
    }

    // 실행 단계 시작: 무기가 드론을 만드는 프레임에 바로 부른다.
    public void Complete()
    {
        if (phase != Phase.Charge) return;
        StopChargeSound();
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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

        transform.position = owner.transform.position + anchorOffset;
        float dt = Time.deltaTime;
        phaseAge += dt;
        spin += spinSpeed * dt;
        scroll += hologramScrollSpeed * dt;
        linkScroll += linkScrollSpeed * dt;
        rotorAngle += Mathf.Lerp(rotorStartSpeed, rotorEndSpeed, progress * progress) * dt;

        if (phase == Phase.Charge && !IsWindupAlive()) Cancel();

        if ((phase == Phase.Complete && phaseAge >= completeTime) || (phase == Phase.Cancel && phaseAge >= cancelTime))
        {
            phase = Phase.None;
            ForceReturnToPool();
            return;
        }

        if (phase == Phase.Charge)
        {
            PlayStageSparks();
            PlayWeldSparks(dt);
        }
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

    private float GantryY => Mathf.Lerp(-gantryHalfHeight, gantryHalfHeight, progress);

    private void PlayStageSparks()
    {
        if (stageSparks == null) return;
        float charge = phaseAge / windupTime;
        while (stagesPlayed < StageCount - 1 && charge >= (stagesPlayed + 1f) / StageCount)
        {
            float angle = startAngle + (stagesPlayed + 1f) / StageCount * Mathf.PI * 2f;
            Vector3 point = new Vector3(Mathf.Sin(angle) * direction, Mathf.Cos(angle), 0f) * ringRadius + new Vector3(0f, frameCenterY, 0f);
            var emit = new ParticleSystem.EmitParams { position = point, applyShapeToPosition = false };
            for (int i = 0; i < sparksPerStage; i++)
            {
                float direction2D = Random.Range(0f, Mathf.PI * 2f);
                emit.velocity = new Vector3(Mathf.Cos(direction2D), Mathf.Sin(direction2D), 0f) * Random.Range(stageSparkSpeedMin, stageSparkSpeedMax);
                stageSparks.Emit(emit, 1);
            }
            stagesPlayed++;
        }
    }

    private void PlayWeldSparks(float dt)
    {
        if (weldSparks == null) return;
        weldBudget += weldRate * dt;
        int count = Mathf.FloorToInt(weldBudget);
        weldBudget -= count;
        float y = frameCenterY + GantryY;
        for (int i = 0; i < count; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(side * gantryHalfWidth * open, y, 0f),
                applyShapeToPosition = false,
                velocity = new Vector3(side * Random.Range(1.2f, 4.5f), Random.Range(-1.5f, 3f), 0f),
            };
            weldSparks.Emit(emit, 1);
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
                open = Mathf.LerpUnclamped(0.72f, 1f, EaseOutBack(t));
                alpha = Mathf.Clamp01(phaseAge / 0.06f);
                flash = 0f;
                gantryAlpha = alpha;
                hologramAlpha = alpha;
                hologramFlash = 0f;
                linkAlpha = alpha * Mathf.Lerp(0.55f, 1f, progress);
                break;
            case Phase.Complete:
                t = Mathf.Clamp01(phaseAge / completeTime);
                progress = 1f;
                open = Mathf.Lerp(1f, 1.24f, 1f - (1f - t) * (1f - t));
                alpha = (1f - t) * (1f - t);
                flash = Mathf.Pow(1f - t, 1.5f);
                gantryAlpha = 0f;
                float flashT = Mathf.Clamp01(phaseAge / hologramFlashTime);
                hologramFlash = Mathf.Pow(1f - flashT, 1.5f);
                hologramAlpha = 1f - flashT;
                linkAlpha = Mathf.Clamp01(1f - phaseAge / 0.1f);
                break;
            case Phase.Cancel:
                t = Mathf.Clamp01(phaseAge / cancelTime);
                float flicker = Mathf.Repeat(phaseAge, 0.06f) < 0.03f ? 1f : 0.3f;   // 전원이 나가듯 깜빡이며 꺼진다
                open = Mathf.Lerp(1f, 0.9f, t);
                alpha = (1f - t) * flicker;
                flash = 0f;
                gantryAlpha = 0f;
                hologramAlpha = (1f - t) * flicker;
                hologramFlash = 0f;
                linkAlpha = 0f;
                if (hologram != null) hologram.localPosition = new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(-0.03f, 0.03f), 0f);
                break;
        }
    }

    private void Apply()
    {
        float gantryY = GantryY;
        float gantryWorldY = transform.position.y + frameCenterY + gantryY;

        if (frame != null)
        {
            frame.GetPropertyBlock(block);
            block.SetFloat(ProgressId, progress);
            block.SetFloat(AlphaId, alpha);
            block.SetFloat(FlashId, flash);
            block.SetFloat(SpinId, spin);
            block.SetFloat(OpenId, open);
            block.SetFloat(DirectionId, direction);
            block.SetFloat(GantryYId, gantryY);
            block.SetFloat(GantryAlphaId, gantryAlpha);
            frame.SetPropertyBlock(block);
        }

        if (link != null)
        {
            float startY = owner != null ? owner.transform.position.y + linkStartY : transform.position.y;
            float length = Mathf.Max(0.01f, gantryWorldY - startY);
            var t = link.transform;
            t.position = new Vector3(transform.position.x, startY + length * 0.5f, t.position.z);
            t.localScale = new Vector3(linkWidth / quadSize, length / quadSize, 1f);
            link.GetPropertyBlock(block);
            block.SetFloat(AlphaId, linkAlpha);
            block.SetFloat(LengthId, length);
            block.SetFloat(ScrollId, linkScroll);
            link.SetPropertyBlock(block);
        }

        if (rotor != null) rotor.localRotation = Quaternion.Euler(rotorTilt, 0f, rotorAngle);

        if (hologramSprites != null)
        {
            foreach (var sprite in hologramSprites)
            {
                if (sprite == null) continue;
                sprite.GetPropertyBlock(block);
                block.SetFloat(RevealYId, gantryWorldY);
                block.SetFloat(AlphaId, hologramAlpha);
                block.SetFloat(FlashId, hologramFlash);
                block.SetFloat(ScrollId, scroll);
                sprite.SetPropertyBlock(block);
            }
        }
    }

    private void ClearParticles()
    {
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (weldSparks != null) weldSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (stageSparks != null) stageSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
