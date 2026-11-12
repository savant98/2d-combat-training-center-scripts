using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Stomper Wrestling Grab")]
public sealed class StomperWrestlingGrabDataSO : EnemyAbilityDataSO
{
    [Min(.1f)] public float grabRadius = 9f;
    [Min(.05f)] public float extendDuration = .24f;
    [Min(.1f)] public float blackoutDuration = 1.5f;
    [Min(.1f)] public float fakeRiseDuration = 2.5f;
    [Min(.1f)] public float spinDuration = 5f;
    [Min(.1f)] public float accelerationDuration = 2.5f;
    [Min(.1f)] public float maxAngularSpeed = 1800f;
    [Min(.1f)] public float descentDuration = 2.3f;
    [Min(.02f)] public float impactFrameLeadTime = .3f;
    [Min(.01f)] public float impactFrameDuration = .15f;
    [Range(5, 75)] public float impactContactAngle = 25f;
    [Min(.1f)] public float verticalDecelerationDuration = 4;
    [Min(.1f)] public float descentVisualSpeed = 110;
    [Range(0,2)] public float cameraLeadDistance = 1.1f;
    [Min(.1f)] public float gameplayHandoffDuration = 3.2f;
    [Min(0)] public float flightMotionScale = .0025f;
    [Min(.1f)] public float riseScrollSpeed = 180f;
    [Min(.1f)] public float descentScrollSpeed = 220f;
    [Header("Grab Camera Shots")]
    [Min(1)] public float playerCloseupSize = 2.2f;
    [Min(1)] public float bossCloseupSize = 3.2f;
    [Range(.25f,.75f)] public float playerFocusFraction = .4f;
    [Min(.05f)] public float closeupBlendDuration = .65f;
    [Min(.1f)] public float risePullbackDuration = 6.5f;
    [Tooltip("로켓 시작 구도에서 보이는 사각형 비율")]
    [Range(0,1)] public float rocketBossVisibleFraction = .5f;
    [Min(.05f)] public float rocketFramingBlendDuration = .4f;
    [Range(.4f,.75f)] public float rocketRiseEndViewportY = .58f;
    [Range(.25f,1)] public float rocketRiseTravelFraction = .85f;
    [Range(.25f,.75f)] public float descentBossStartViewportY = .5f;
    [Range(0,1)] public float preImpactBossVisibleFraction = .5f;
    [Tooltip("0이면 포획 직전 게임 화면의 확대율을 사용한다.")]
    [Min(0)] public float landingViewSize = 0;
    [Range(.05f,.35f)] public float landingFloorViewport = .2f;
    public GameObject trainingBackdrop;
    public Material travelBackdropMaterial;
    public Material roomExtensionMaterial;
    public GameObject rocketThrusterPrefab;
    public AnimationClip riseClip;
    public SoundOptions rocketIgnitionSound;
    public SoundOptions rocketLoopSound;
    public SoundOptions impactExplosionSound;
    [Min(.05f)] public float impactHoldDuration = .85f;
    [Min(.05f)] public float fadeOutDuration = .45f;
    [Min(.1f)] public float slamSpeed = 42;
    [Min(.1f)] public float travelTimeout = 3;
    public WeaponBaseSO.DamageInfo damage = new WeaponBaseSO.DamageInfo { damage = 30, damageType = DamageType.Normal };
    public PlayerGrabDataSO playerGrab;
    public Material previewMaterial;
    public Sprite previewFill;
    public SoundOptions warningLoopSound;
    // 회전 궤적은 프레임마다 점을 찍는 TrailRenderer 대신, 지난 spinArcDuration초의 호 전체를 계산해 그리는 Enemy/SwingArc 표시를 쓴다.
    public SpriteRenderer spinArcPrefab;
    [Min(.02f)] public float spinArcDuration = .2f;
    public Material motionLineMaterial;
    public FeedbackDataSO gripFeedback;
    public FeedbackDataSO impactFeedback;
    public FeedbackDataSO spinWindFeedback;
    public AnimationClip recoilClip;
    public float EvaluateSpinAngle(float elapsed)
    {
        float t = Mathf.Clamp(elapsed, 0, spinDuration);
        float ramp = Mathf.Min(accelerationDuration, spinDuration);
        return t <= ramp ? maxAngularSpeed * t * t / (2 * ramp)
            : maxAngularSpeed * (t - ramp * .5f);
    }
    public float EvaluateFlightScroll(float elapsed)
    {
        if(elapsed<=fakeRiseDuration) { float p=Mathf.Clamp01(elapsed/fakeRiseDuration); return -riseScrollSpeed*p*p; }
        float t=elapsed-fakeRiseDuration;
        if(t<=verticalDecelerationDuration) return -riseScrollSpeed*(1-Mathf.SmoothStep(0,1,t/verticalDecelerationDuration));
        return descentScrollSpeed*Mathf.SmoothStep(0,1,(t-verticalDecelerationDuration)/Mathf.Max(.1f,spinDuration-verticalDecelerationDuration));
    }
    // 회전 속도를 유지한 채 접촉 방향을 맞추고 임팩트 프레임과 복귀 구간을 확보한다.
    public float ContactAngle(float finalDegrees = -1) { DescentPlan(finalDegrees,out var contact, out _); return contact; }
    public float DescentTime(float finalDegrees = -1) { DescentPlan(finalDegrees,out _, out var duration); return duration; }
    public float ImpactFrameStart(float finalDegrees = -1)
    {
        float duration=DescentTime(finalDegrees);
        return duration-Mathf.Min(Mathf.Max(.02f,impactFrameLeadTime),duration-.02f);
    }
    public float ImpactFrameEnd(float finalDegrees = -1)
    {
        return Mathf.Min(DescentTime(finalDegrees)-.01f,ImpactFrameStart(finalDegrees)+Mathf.Max(.01f,impactFrameDuration));
    }
    private void DescentPlan(float finalDegrees,out float contact, out float duration)
    {
        float speed = Mathf.Max(.1f, maxAngularSpeed);
        float start = EvaluateSpinAngle(spinDuration);
        float target = finalDegrees>0 ? finalDegrees : impactContactAngle;
        float minimumDuration=Mathf.Max(.4f,impactFrameLeadTime+.1f);
        contact = target + 360 * Mathf.Floor((start + speed * Mathf.Max(descentDuration,minimumDuration) - target + .001f) / 360);
        if(contact<start+speed*minimumDuration-.001f)
            contact=target+360*Mathf.Ceil((start+speed*minimumDuration-target)/360);
        duration=Mathf.Max(.01f,(contact-start)/speed);
    }
    public float EvaluateDescentAngle(float elapsed,float finalDegrees = -1)
    {
        return Mathf.Min(ContactAngle(finalDegrees),EvaluateSpinAngle(spinDuration)+Mathf.Max(.1f,maxAngularSpeed)*Mathf.Max(0,elapsed));
    }
    public float EvaluateDescentSpeed(float elapsed,float finalDegrees = -1) => Mathf.Max(.1f,maxAngularSpeed);
}
