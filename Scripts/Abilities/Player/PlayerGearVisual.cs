using UnityEngine;

// Gear 1~3단계의 준비·준비 완료·해제를 몸 둘레 효과와 소리로 보여 준다(2026-11-09, 사용자 요청 "Gear의 준비·준비 완료를 어울리는 소리와 이펙트로 전면 변경").
// 판정과 시간은 지금까지처럼 PlayerWeaponAbility가 정하고 이 개체는 표시만 한다. 머리 위 GearUI(PlayerGearUI)와 같은 값·같은 칸 수로 함께 움직인다.
// DashVisual처럼 PlayerWeaponAbility가 실행 중에 Player 아래에 만들므로 Player.prefab은 건드리지 않는다.
// - 준비(SetCharging): 톱니 고리(Player/GearShiftRing)가 조여 들며 빨라지고, 진행 호가 차오르며, 이번 단계의 클램프가 바깥에서 미끄러져 들어온다. 빛 알갱이가 몸으로 모인다.
//   소리는 취소되는 즉시 멈춰야 하므로 피드백 대신 SoundManager의 Loop로 재생한다.
// - 준비 완료(SetWaiting 첫 프레임): 단계별 lockFeedback(체결 폭발·소리·흔들림)을 재생하고, 고리가 흰빛으로 번쩍인다.
// - 대기(SetWaiting): 호가 남은 시간만큼 줄어들고 톱니 고리가 시계처럼 한 칸씩 끊어 돈다. 칸마다 틱 소리와 불꽃이 나고, 남은 시간이 적으면 깜빡인다.
//   무기를 아직 쓸 수 없으면(게이지·재사용 대기) 고리 색이 붉게 어두워진다.
// - 끝(End): 무기를 쏘면 몸으로 빨려 들어가고, 손을 떼거나 시간이 다 되면 벌어지며 깜빡여 꺼지고 해제음이 난다. 맞거나 다른 행동으로 끊기면 조용히 사라진다.
// 시간은 Time.deltaTime이라 히트스톱 때 함께 멈춘다. Player 이동과 Animator 뒤에 값을 맞추도록 실행 순서를 늦춘다.
[DefaultExecutionOrder(110)]
public sealed class PlayerGearVisual : MonoBehaviour
{
    public enum EndReason
    {
        Silent,         // 맞음·잡힘·대시·무기 변경처럼 다른 일이 끊었을 때
        Disengaged,     // 손을 떼거나 대기 시간이 끝나 풀렸을 때
        Fired,          // 단계 무기를 쏘았을 때
    }

    private enum Phase { Inactive, Charging, Waiting, Ending }

    private const int ClampCount = 3;

    private static readonly int StageColorId = Shader.PropertyToID("_StageColor");
    private static readonly int PaleColorId = Shader.PropertyToID("_PaleColor");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int SpinId = Shader.PropertyToID("_Spin");
    private static readonly int OpenId = Shader.PropertyToID("_Open");
    private static readonly int StageId = Shader.PropertyToID("_Stage");
    private static readonly int SlideId = Shader.PropertyToID("_Slide");
    private static readonly int LitId = Shader.PropertyToID("_Lit");
    private static readonly int ReadyId = Shader.PropertyToID("_Ready");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");

    [Header("Parts")]
    [SerializeField] private SpriteRenderer ring;
    [SerializeField] private ParticleSystem motes;
    [SerializeField] private ParticleSystem tickSparks;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float appearTime = 0.05f;          // 고리가 나타나는 시간
    [SerializeField, Min(0.01f)] private float lockFlashTime = 0.2f;        // 체결 순간의 흰빛이 사라지는 시간
    [SerializeField, Min(0.01f)] private float firedTime = 0.12f;           // 무기를 쏠 때 빨려 들어가는 시간
    [SerializeField, Min(0.01f)] private float disengagedTime = 0.18f;      // 풀릴 때 벌어지며 꺼지는 시간
    [SerializeField, Min(0.01f)] private float silentTime = 0.08f;          // 다른 일로 끊겼을 때 사라지는 시간

    [Header("Look")]
    [SerializeField, Range(0f, 1f)] private float paleWhite = 0.55f;        // 호·클램프의 밝은 쪽 색(단계 색에 흰색을 섞는 비율. 낮을수록 단계 색이 선명하다)
    [SerializeField] private float chargeOpenStart = 1.55f;                 // 준비 시작 때 고리 크기 배율
    [SerializeField] private float lockOpenKick = 0.14f;                    // 체결 순간 벌어지는 크기
    [SerializeField] private float chargeSpinStart = 2f, chargeSpinEnd = 11f;   // 준비 중 톱니 회전(라디안/초)
    [SerializeField] private float waitTickSharpness = 50f;                 // 한 칸 움직일 때의 빠르기
    [SerializeField, Range(0f, 1f)] private float warningRemaining = 0.3f;  // 이 비율보다 남은 시간이 적으면 깜빡인다
    [SerializeField] private float warningBlinkInterval = 0.1f;

    [Header("Particles")]
    [SerializeField] private float[] moteRatesByStage = { 45f, 65f, 90f };
    [SerializeField, Min(1)] private int waitTickCount = 8;                 // 대기 시간을 나누는 칸 수
    [SerializeField, Min(0)] private int sparksPerTick = 2;
    [SerializeField, Min(0f)] private float tickSparkSpeedMin = 2.5f, tickSparkSpeedMax = 5.5f;

    private PlayerWeaponDataSO data;
    private PlayerGearUI gearUI;
    private FeedbackPlayer feedbackPlayer;
    private MaterialPropertyBlock block;
    private SoundHandle chargeLoop, holdLoop;

    private Phase phase = Phase.Inactive;
    private EndReason endReason;
    private int stage, lastTick;
    private float phaseAge, progress, flash, spin, spinTarget, ready = 1f;
    private float alpha, open = 1f, slide, lit;
    private float ringRadius = 1.34f;
    private Color stageColor = Color.white;

    public bool IsShowing => phase != Phase.Inactive;
    public bool IsCharging => phase == Phase.Charging;
    public bool IsWaiting => phase == Phase.Waiting;
    public int Stage => stage;
    public float Progress => progress;
    public int LastTick => lastTick;
    public bool IsChargeSoundPlaying => chargeLoop != null && chargeLoop.IsValid;
    public bool IsHoldSoundPlaying => holdLoop != null && holdLoop.IsValid;

    public void Initialize(PlayerWeaponDataSO weaponData, PlayerGearUI ui, FeedbackPlayer feedback)
    {
        data = weaponData;
        gearUI = ui;
        feedbackPlayer = feedback;
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (ring != null && ring.sharedMaterial != null)
            ringRadius = ring.sharedMaterial.GetFloat(RadiusId);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        Hide();
    }

    public void SetCharging(int targetStage, float fill, bool weaponReady)
    {
        if (phase != Phase.Charging || stage != targetStage)
            BeginCharge(targetStage);

        progress = Mathf.Clamp01(fill);
        ready = weaponReady ? 1f : 0f;
    }

    public void SetWaiting(int lockedStage, float remaining, bool weaponReady)
    {
        if (phase != Phase.Waiting || stage != lockedStage)
            Lock(lockedStage);

        progress = Mathf.Clamp01(remaining);
        ready = weaponReady ? 1f : 0f;

        int tick = Mathf.FloorToInt((1f - progress) * waitTickCount);
        if (tick > lastTick)
        {
            lastTick = tick;
            if (tick < waitTickCount)
                Tick();
        }
    }

    public void End(EndReason reason)
    {
        if (phase == Phase.Inactive || phase == Phase.Ending)
            return;

        StopLoops();
        if (motes != null)
            motes.Stop(true, reason == EndReason.Fired ? ParticleSystemStopBehavior.StopEmitting : ParticleSystemStopBehavior.StopEmittingAndClear);

        endReason = reason;
        phase = Phase.Ending;
        phaseAge = 0f;

        if (reason == EndReason.Disengaged && SoundManager.Instance != null && data != null)
            SoundManager.Instance.PlaySound(data.gearDisengageSound, transform.position);
    }

    private void OnDisable()
    {
        StopLoops();   // Player가 꺼지거나 방을 옮겨도 소리가 남지 않게 한다
    }

    private void BeginCharge(int targetStage)
    {
        StopLoops();
        stage = targetStage;
        phase = Phase.Charging;
        phaseAge = 0f;
        flash = 0f;
        spinTarget = spin;
        ApplyStageColor();

        GearStagePresentation presentation = GetPresentation(stage);
        if (presentation != null && SoundManager.Instance != null)
            chargeLoop = SoundManager.Instance.PlayLoop(presentation.chargeSound, transform.position);

        if (motes != null)
        {
            var main = motes.main;
            main.startColor = Color.Lerp(stageColor, Color.white, 0.25f);
            var emission = motes.emission;
            emission.rateOverTime = moteRatesByStage[Mathf.Clamp(stage - 1, 0, moteRatesByStage.Length - 1)];
            motes.Play(true);
        }

        ring.enabled = true;
    }

    private void Lock(int lockedStage)
    {
        StopLoops();
        stage = lockedStage;
        phase = Phase.Waiting;
        phaseAge = 0f;
        lastTick = 0;
        flash = 1f;
        spinTarget = spin;
        ApplyStageColor();

        if (motes != null)
            motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (SoundManager.Instance != null && data != null)
            holdLoop = SoundManager.Instance.PlayLoop(data.gearHoldSound, transform.position);

        GearStagePresentation presentation = GetPresentation(stage);
        if (presentation != null && feedbackPlayer != null)
            feedbackPlayer.Play(presentation.lockFeedback, transform);

        ring.enabled = true;
    }

    private void Tick()
    {
        spinTarget += Mathf.PI / 6f;   // 톱니 한 칸(12개)

        if (SoundManager.Instance != null && data != null)
            SoundManager.Instance.PlaySound(data.gearTickSound, transform.position);

        if (tickSparks == null)
            return;

        for (int i = 0; i < sparksPerTick; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 outward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 tangent = new Vector2(-outward.y, outward.x);
            tickSparks.Emit(new ParticleSystem.EmitParams
            {
                position = transform.position + (Vector3)(outward * ringRadius),
                velocity = (tangent + outward * 0.4f) * Random.Range(tickSparkSpeedMin, tickSparkSpeedMax),
                startColor = Color.Lerp(stageColor, Color.white, 0.4f),
                applyShapeToPosition = false,
            }, 1);
        }
    }

    private void LateUpdate()
    {
        if (phase == Phase.Inactive)
            return;

        float dt = Time.deltaTime;
        phaseAge += dt;
        flash = Mathf.MoveTowards(flash, 0f, dt / lockFlashTime);

        switch (phase)
        {
            case Phase.Charging:
                spin += Mathf.Lerp(chargeSpinStart, chargeSpinEnd, progress) * dt;
                spinTarget = spin;
                alpha = Mathf.Clamp01(phaseAge / appearTime);
                open = Mathf.Lerp(chargeOpenStart, 1f, 1f - Mathf.Pow(1f - progress, 3f));
                slide = 1f - progress;
                lit = progress;
                break;

            case Phase.Waiting:
                spin = Mathf.Lerp(spin, spinTarget, 1f - Mathf.Exp(-waitTickSharpness * dt));
                bool blink = progress < warningRemaining && Mathf.Repeat(phaseAge, warningBlinkInterval * 2f) >= warningBlinkInterval;
                alpha = blink ? 0.55f : 1f;
                open = 1f + lockOpenKick * flash;
                slide = 0f;
                lit = 1f;
                break;

            case Phase.Ending:
                if (phaseAge >= EndDuration())
                {
                    Hide();
                    return;
                }
                EvaluateEnding();
                break;
        }

        Apply();
    }

    private float EndDuration()
    {
        return endReason switch
        {
            EndReason.Fired => firedTime,
            EndReason.Disengaged => disengagedTime,
            _ => silentTime,
        };
    }

    private void EvaluateEnding()
    {
        float t = Mathf.Clamp01(phaseAge / EndDuration());
        float startAlpha = alpha;
        switch (endReason)
        {
            case EndReason.Fired:
                open = Mathf.Lerp(1f, 0.72f, 1f - (1f - t) * (1f - t));
                alpha = (1f - t) * (1f - t);
                flash = Mathf.Max(flash, 0.6f * (1f - t));
                break;
            case EndReason.Disengaged:
                open = Mathf.Lerp(1f, 1.22f, t);
                alpha = (1f - t) * (Mathf.Repeat(phaseAge, 0.05f) < 0.025f ? 1f : 0.4f);   // 전원이 나가듯 깜빡이며 꺼진다
                break;
            default:
                alpha = Mathf.Min(startAlpha, 1f - t);
                break;
        }
    }

    private void Apply()
    {
        if (ring == null)
            return;

        ring.GetPropertyBlock(block);
        block.SetColor(StageColorId, stageColor);
        block.SetColor(PaleColorId, Color.Lerp(stageColor, Color.white, paleWhite));
        block.SetFloat(ProgressId, progress);
        block.SetFloat(AlphaId, alpha);
        block.SetFloat(FlashId, flash);
        block.SetFloat(SpinId, spin);
        block.SetFloat(OpenId, open);
        block.SetFloat(StageId, Mathf.Min(stage, ClampCount));
        block.SetFloat(SlideId, slide);
        block.SetFloat(LitId, lit);
        block.SetFloat(ReadyId, ready);
        ring.SetPropertyBlock(block);
    }

    private void Hide()
    {
        phase = Phase.Inactive;
        phaseAge = 0f;
        progress = flash = spin = spinTarget = alpha = slide = lit = 0f;
        open = 1f;
        stage = lastTick = 0;
        ready = 1f;
        StopLoops();
        if (ring != null) ring.enabled = false;
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (tickSparks != null) tickSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void ApplyStageColor()
    {
        stageColor = gearUI != null ? gearUI.GetStageColor(stage) : Color.white;
    }

    private GearStagePresentation GetPresentation(int forStage)
    {
        if (data == null || data.gearStagePresentations == null)
            return null;

        int index = forStage - 1;
        return index >= 0 && index < data.gearStagePresentations.Length ? data.gearStagePresentations[index] : null;
    }

    private void StopLoops()
    {
        if (SoundManager.Instance != null)
        {
            if (chargeLoop != null) SoundManager.Instance.StopLoop(chargeLoop);
            if (holdLoop != null) SoundManager.Instance.StopLoop(holdLoop);
        }
        chargeLoop = null;
        holdLoop = null;
    }
}
