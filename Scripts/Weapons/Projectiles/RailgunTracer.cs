using UnityEngine;

// 레일건(RailgunWeaponSO)이 쏜 빛줄기를 그리는 풀 개체(2026-11-18, Railgun 무장 1차 시안).
// 판정은 무기가 쏘는 순간 끝낸다. 이 개체는 보이는 것만 맡고, 쏜 자리(월드)에 남는다(Player가 움직여도 빛줄기가 따라가지 않는다).
// - 본체: 쏘는 순간 굵게 번쩍(_Flash)였다가 thinTime 동안 가늘어지고, fadeTime에 사라진다.
// - 잔광: 본체보다 넓고 어두운 빛이 afterglowTime 동안 조금씩 넓어지며 옅어진다.
// - 포구·벽 끝: 별빛·빛 점이 flareTime 동안 커지며 사라진다. 벽 끝 표시는 벽에 막혔을 때만 켠다.
// - 전류 입자: 빛줄기를 따라 옆으로 튀는 작은 불꽃을 한 번에 뿌리고, 벽에 막혔으면 벽에서 뒤로 튄다(월드 공간, 개체가 돌아갈 때까지 남는다).
// - 본체·잔광은 Player 레이저 셰이더(Player/Laser)를 색만 바꾼 재질로, 사각형 크기와 값 전달은 PlayerLaserBeam.DrawBeam과 같다(MaterialPropertyBlock).
//   포구·벽 끝은 HammerSkillBlast 별빛(Sprite는 나이 0으로 그려진다)이고, 사라짐은 SpriteRenderer 색의 알파로 준다(셰이더가 정점 색을 곱한다).
// - 시간은 Time.deltaTime 기준이라 히트스톱 때 함께 멈춘다.
public class RailgunTracer : WorldEntityBase
{
    private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
    private static readonly int MarginId = Shader.PropertyToID("_Margin");
    private static readonly int LengthId = Shader.PropertyToID("_Length");
    private static readonly int HalfWidthId = Shader.PropertyToID("_HalfWidth");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    [Header("Parts")]
    [SerializeField] private SpriteRenderer beamBody;
    [SerializeField] private SpriteRenderer afterglow;
    [SerializeField] private SpriteRenderer muzzleHalo;
    [SerializeField] private SpriteRenderer muzzleFlare;
    [SerializeField] private SpriteRenderer impactFlare;
    [SerializeField] private ParticleSystem sparks;

    [Header("Beam")]
    [SerializeField, Min(0.001f)] private float peakHalfWidth = 0.24f;    // 쏘는 순간 반폭(칸)
    [SerializeField, Min(0.001f)] private float thinHalfWidth = 0.06f;    // 가늘어진 뒤 반폭
    [SerializeField, Min(0.001f)] private float thinTime = 0.07f;         // 굵은 폭에서 가는 폭까지(초)
    [SerializeField, Min(0.01f)] private float fadeTime = 0.2f;           // 본체가 사라지는 시각(초)
    [SerializeField, Min(0f)] private float flashTime = 0.06f;            // 흰 번쩍임이 꺼지는 시각(초)
    [SerializeField, Min(0f)] private float flowSpeed = 1f;               // 빛줄기 무늬가 앞으로 흐르는 빠르기(셰이더 _Flow = 나이 × 이 값)
    [SerializeField, Min(0f)] private float blockedOverlap = 0.12f;       // 벽에 닿으면 벽 속으로 조금 더 그려 틈을 없앤다
    [SerializeField, Min(0.01f)] private float beamMargin = 0.6f, beamEndMargin = 1.2f;

    [Header("Afterglow")]
    [SerializeField, Min(0.001f)] private float afterglowHalfWidth = 0.16f;
    [SerializeField, Min(0f)] private float afterglowSpread = 0.6f;       // 사라지는 동안 넓어지는 비율
    [SerializeField, Min(0.01f)] private float afterglowTime = 0.45f;
    [SerializeField, Range(0f, 1f)] private float afterglowAlpha = 0.6f;

    [Header("Flare")]
    [SerializeField, Min(0f)] private float muzzleFlareSize = 2.4f;       // 칸(사각형 한 변)
    [SerializeField, Min(0f)] private float muzzleHaloSize = 1.6f;
    [SerializeField, Min(0f)] private float impactFlareSize = 1.8f;
    [SerializeField, Min(0.01f)] private float flareTime = 0.14f;
    [SerializeField, Min(0f)] private float flareGrow = 0.5f;             // 사라지는 동안 커지는 비율

    [Header("Sparks")]
    [SerializeField, Min(0f)] private float sparkDensity = 1.1f;          // 빛줄기 1칸당 개수
    [SerializeField, Min(0)] private int maxSparks = 40;
    [SerializeField] private Vector2 sparkSpeed = new Vector2(3f, 9f);    // 옆으로 튀는 속도(칸/초)
    [SerializeField] private Vector2 sparkLife = new Vector2(0.1f, 0.28f);
    [SerializeField, Min(0)] private int impactSparks = 10;
    [SerializeField] private Vector2 impactSparkSpeed = new Vector2(6f, 14f);

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO impactFeedback;               // 벽에 막혔을 때 끝에서 한 번

    private MaterialPropertyBlock block;
    private float quadSize = 6.2f;
    private Vector2 origin;
    private Vector2 direction = Vector2.right;
    private float length, age, lifetime;
    private bool blocked, playing;
    private int playFrame = -1;

    // 읽기 전용 상태(검증·디버그용)
    internal override bool PreserveAfterBossDefeat => IsPlaying;
    public bool IsPlaying => playing && !isReturning;
    public Vector2 Origin => origin;
    public Vector2 Direction => direction;
    public float Length => length;
    public bool Blocked => blocked;
    public float Age => age;

    protected override void Awake()
    {
        base.Awake();
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        if (beamBody != null && beamBody.sprite != null) quadSize = beamBody.sprite.bounds.size.x;
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetState();
    }

    public override void ForceReturnToPool()
    {
        ResetState();
        base.ForceReturnToPool();
    }

    private void ResetState()
    {
        playing = false;
        age = 0f;
        playFrame = -1;
        SetRenderers(false);
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // 쏜 순간 한 번 부른다. origin = 총구(월드), length = 총구에서 끝까지(칸), blocked = 벽에 막혔는지.
    public void Play(Vector2 origin, Vector2 direction, float length, bool blocked)
    {
        this.origin = origin;
        this.direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.right;
        this.length = Mathf.Max(0f, length);
        this.blocked = blocked;
        float angle = Mathf.Atan2(this.direction.y, this.direction.x) * Mathf.Rad2Deg;
        transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, 0f, angle));

        age = 0f;
        playing = true;
        playFrame = Time.frameCount;
        lifetime = Mathf.Max(Mathf.Max(fadeTime, afterglowTime), Mathf.Max(flareTime, sparkLife.y)) + 0.05f;
        SetRenderers(true);
        if (impactFlare != null) impactFlare.enabled = blocked;

        EmitSparks();
        if (blocked && impactFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(impactFeedback, End, Quaternion.Euler(0f, 0f, angle + 180f));
        ApplyVisuals();
    }

    private Vector2 End => origin + direction * length;

    private void Update()
    {
        if (!playing || isReturning) return;
        if (Time.frameCount == playFrame) return;   // 쏜 프레임에는 처음 모습(가장 굵고 밝은)을 그대로 그린다
        age += Time.deltaTime;
        if (age >= lifetime)
        {
            playing = false;
            ReturnToPool();
            return;
        }
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        float drawLength = blocked ? length + blockedOverlap : length;

        // 본체: 굵게 번쩍 → 가늘어짐 → 사라짐
        float fade = Mathf.Clamp01(age / fadeTime);
        float alpha = 1f - fade * fade;
        float halfWidth = Mathf.Lerp(peakHalfWidth, thinHalfWidth, EaseOut(age / thinTime)) * (1f - 0.5f * fade);
        float flash = flashTime > 0f ? 1f - Mathf.Clamp01(age / flashTime) : 0f;
        if (beamBody != null)
        {
            bool show = alpha > 0.001f;
            if (beamBody.enabled != show) beamBody.enabled = show;
            if (show) DrawBeam(beamBody, drawLength, halfWidth, flash, alpha, age * flowSpeed);
        }

        // 잔광: 넓고 어둡게 남았다가 넓어지며 옅어진다
        float glowT = Mathf.Clamp01(age / afterglowTime);
        float glowAlpha = afterglowAlpha * (1f - glowT) * (1f - glowT);
        if (afterglow != null)
        {
            bool show = glowAlpha > 0.001f;
            if (afterglow.enabled != show) afterglow.enabled = show;
            if (show) DrawBeam(afterglow, drawLength, afterglowHalfWidth * (1f + afterglowSpread * EaseOut(glowT)), 0f, glowAlpha, age * flowSpeed * 0.5f);
        }

        // 포구·벽 끝 번쩍임
        float flareT = Mathf.Clamp01(age / flareTime);
        float flareAlpha = (1f - flareT) * (1f - flareT);
        float grow = 1f + flareGrow * EaseOut(flareT);
        SetFlare(muzzleHalo, Vector2.zero, muzzleHaloSize * grow, flareAlpha);
        SetFlare(muzzleFlare, Vector2.zero, muzzleFlareSize * grow, flareAlpha);
        if (blocked) SetFlare(impactFlare, new Vector2(length, 0f), impactFlareSize * grow, flareAlpha);
    }

    private void SetFlare(SpriteRenderer flare, Vector2 localPosition, float size, float alpha)
    {
        if (flare == null) return;
        bool show = alpha > 0.001f && size > 0f;
        if (flare.enabled != show) flare.enabled = show;
        if (!show) return;
        Transform t = flare.transform;
        t.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
        float scale = size / quadSize;
        t.localScale = new Vector3(scale, scale, 1f);
        Color c = flare.color;
        c.a = alpha;
        flare.color = c;
    }

    // PlayerLaserBeam.DrawBeam과 같은 사각형: 포구 뒤 beamMargin부터 끝 뒤 beamEndMargin까지, 높이는 번짐이 끝나는 곳 + 여유.
    private void DrawBeam(SpriteRenderer renderer, float length, float halfWidth, float flashValue, float alpha, float flow)
    {
        float quadLength = beamMargin + Mathf.Max(length, 0f) + beamEndMargin;
        float wide = Mathf.Max(halfWidth, 0.05f);
        float quadHeight = 2f * (wide * 1.3f + 5f * (0.14f + 0.38f * wide));
        Transform t = renderer.transform;
        t.localPosition = new Vector3(quadLength * 0.5f - beamMargin, 0f, 0f);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(quadLength / quadSize, quadHeight / quadSize, 1f);
        renderer.GetPropertyBlock(block);
        block.SetVector(QuadSizeId, new Vector4(quadLength, quadHeight, 0f, 0f));
        block.SetFloat(MarginId, beamMargin);
        block.SetFloat(LengthId, Mathf.Max(length, 0.001f));
        block.SetFloat(HalfWidthId, Mathf.Max(halfWidth, 0.001f));
        block.SetFloat(FlowId, flow);
        block.SetFloat(FlashId, flashValue);
        block.SetFloat(AlphaId, alpha);
        renderer.SetPropertyBlock(block);
    }

    private void EmitSparks()
    {
        if (sparks == null) return;
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        sparks.Play();
        Vector2 side = new Vector2(-direction.y, direction.x);
        int count = Mathf.Min(maxSparks, Mathf.CeilToInt(length * sparkDensity));
        for (int i = 0; i < count; i++)
        {
            float along = Random.Range(0f, length);
            float speed = Random.Range(sparkSpeed.x, sparkSpeed.y);
            float sideSign = Random.value < 0.5f ? -1f : 1f;
            var p = new ParticleSystem.EmitParams
            {
                position = origin + direction * along + side * Random.Range(-0.05f, 0.05f),
                velocity = side * (sideSign * speed) + direction * (Random.Range(-0.3f, 1f) * speed * 0.5f),
                startLifetime = Random.Range(sparkLife.x, sparkLife.y),
                applyShapeToPosition = false,
            };
            sparks.Emit(p, 1);
        }

        if (!blocked) return;
        Vector2 end = End;
        for (int i = 0; i < impactSparks; i++)
        {
            float a = Random.Range(-70f, 70f) * Mathf.Deg2Rad;
            Vector2 back = -direction * Mathf.Cos(a) + side * Mathf.Sin(a);
            var p = new ParticleSystem.EmitParams
            {
                position = end,
                velocity = back * Random.Range(impactSparkSpeed.x, impactSparkSpeed.y),
                startLifetime = Random.Range(sparkLife.x, sparkLife.y),
                applyShapeToPosition = false,
            };
            sparks.Emit(p, 1);
        }
    }

    private void SetRenderers(bool show)
    {
        if (beamBody != null) beamBody.enabled = show;
        if (afterglow != null) afterglow.enabled = show;
        if (muzzleHalo != null) muzzleHalo.enabled = show;
        if (muzzleFlare != null) muzzleFlare.enabled = show;
        if (impactFlare != null) impactFlare.enabled = show && blocked;
    }

    private static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u * u;
    }
}
