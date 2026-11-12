using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// Wave 준비 중 Player를 가두는 구속 상자의 표시다. 이동을 실제로 막는 것은 RaycastMotor2D의 이동 구속이고,
// 이 컴포넌트는 그 영역(Motor.Confinement) 둘레에 상자를 조립·유지·해체하는 모습과 안내판만 그린다.
// - 조립: 모서리 브래킷 네 개가 바깥에서 날아와 맞물리고, 변을 따라 발광선이 닫히고, 위쪽에 안내판이 펼쳐진다.
// - 유지: 발광선이 맥동하고 둘레를 빛이 돈다. Player가 벽을 밀면 그 면이 번쩍이고 브래킷이 살짝 밀린다.
// - 해체: 안내판이 접히고 발광선이 꺼진 뒤 브래킷이 바깥으로 빠져나간다.
// - 크레인(시작 좌표로 옮기는 Wave): 화면 위에서 줄 두 가닥에 매달린 스프레더가 내려와 걸쇠로 상자 위쪽 모서리를 문다.
//   옮기는 것은 WavePlayerStart가 구속 영역을 움직여서 하고, 여기서는 줄·도르래·걸쇠의 모습만 그린다. 다 옮기면 걸쇠를 풀고 올라간다.
// 브래킷·발광선·안내판 판·크레인은 Mesh 두 장(몸체: 일반 혼합, 발광: 가산)을 매 프레임 다시 채워 그린다.
[DisallowMultipleComponent]
public sealed class WavePlayerContainment : MonoBehaviour
{
    [Header("구성")]
    [SerializeField] private MeshFilter bodyMesh;      // 브래킷 몸체·안내판 판(일반 혼합)
    [SerializeField] private MeshFilter glowMesh;      // 발광선·표시등·충격 고리(가산)
    [SerializeField] private MeshRenderer field;       // 상자 안쪽 홀로그램 면(Wave/ContainmentField)
    [SerializeField] private Transform sign;           // 안내판 글자의 부모
    [SerializeField] private TextMeshPro title;
    [SerializeField] private TextMeshPro caption;

    [Header("움직임(초·칸)")]
    [SerializeField, Min(.05f)] private float engageDuration = .32f;
    [SerializeField, Min(.05f)] private float releaseDuration = .26f;
    [SerializeField] private float frameThickness = .075f;
    [SerializeField] private float bracketLength = .44f;
    [SerializeField] private float bracketTravel = .62f;
    [SerializeField] private float signGap = .36f;
    [SerializeField] private Vector2 signSize = new Vector2(2.7f, .64f);

    [Header("크레인(초·칸)")]
    [SerializeField, Min(.05f)] private float craneDropDuration = .34f;    // 스프레더가 내려오는(올라가는) 시간
    [SerializeField, Min(.03f)] private float craneLockDuration = .12f;    // 걸쇠가 닫히는(열리는) 시간
    [SerializeField] private float craneDropHeight = 3.2f;                 // 내려오기 시작하는 높이
    [SerializeField] private float spreaderGap = .05f;                     // 상자 윗변과 스프레더 사이
    [SerializeField] private float spreaderThickness = .075f;
    [SerializeField] private Vector2 craneBlockSize = new Vector2(.36f, .22f);
    [SerializeField] private float cableSpacing = .17f;                    // 두 줄 사이
    [SerializeField] private float cableWidth = .024f;
    [SerializeField] private float craneSignClearance = .7f;               // 도르래 블록과 안내판 사이(줄이 드러나는 길이)

    [Header("색(선형 HDR)")]
    [SerializeField] private Color bodyColor = new Color(.05f, .055f, .065f, .96f);
    [SerializeField] private Color rimColor = new Color(.42f, .43f, .46f, 1f);
    [SerializeField] private Color lineColor = new Color(.30f, 1.75f, 2.3f, 1f);      // 중립 기능의 파랑·청록 발광
    [SerializeField] private Color whiteGlow = new Color(1.7f, 1.8f, 1.9f, 1f);
    [SerializeField] private Color signBack = new Color(.02f, .028f, .036f, .9f);

    [Header("소리")]
    [SerializeField] private FeedbackDataSO engageFeedback;
    [SerializeField] private FeedbackDataSO releaseFeedback;
    [SerializeField] private FeedbackDataSO bumpFeedback;

    private enum Phase { Off, Engaging, Held, Releasing }
    private static readonly int RevealId = Shader.PropertyToID("_Reveal");
    private static readonly int ImpactId = Shader.PropertyToID("_Impact");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private Phase phase;
    private float clock;            // 현재 단계의 경과 시간
    private float heldTime;         // 맥동·둘레 빛에 쓰는 누적 시간
    private float releaseFrom = 1f; // 해체를 시작할 때 조립이 얼마나 진행돼 있었는지(조립 도중에 풀리면 그만큼만 되돌린다)
    private PlayerController target;
    private Rect area;
    private readonly float[] impact = new float[4];     // 왼쪽·오른쪽·위·아래 벽을 민 세기(감쇠)
    private readonly bool[] touching = new bool[4];
    private float ring = -1f;       // 조립 순간 퍼지는 충격 고리의 경과 시간
    private Mesh body, glow;
    private readonly List<Vector3> bodyVertices = new(), glowVertices = new();
    private readonly List<Color> bodyColors = new(), glowColors = new();
    private readonly List<int> bodyTriangles = new(), glowTriangles = new();
    private MaterialPropertyBlock properties;
    // 크레인
    private bool craneWanted;       // 상자를 물고 있으려는지
    private float craneReach;       // 스프레더가 내려온 정도(0 = 화면 위쪽, 1 = 상자 위)
    private float craneLock;        // 걸쇠가 닫힌 정도
    private float craneFlash;       // 걸쇠가 닫히는 순간의 섬광(감쇠)
    private float craneLean;        // 줄의 기울기(라디안, 양수면 위 끝이 오른쪽)
    private float craneSpin;        // 도르래 회전(도)
    private float cableFlow;        // 줄을 따라 흐르는 빛의 위치
    private Vector2 craneVelocity;  // 상자를 옮기는 속도(칸/초)
    private Camera view;

    public bool IsEngaged => phase == Phase.Engaging || phase == Phase.Held;
    /// <summary>크레인이 내려와 걸쇠까지 닫았는지. 이때부터 상자를 들어 옮긴다.</summary>
    public bool CraneAttached => craneWanted && IsEngaged && craneReach >= 1f && craneLock >= 1f;
    /// <summary>크레인이 화면에 나와 있는지(내려오는 중·물고 있는 중·올라가는 중).</summary>
    public bool CraneVisible => craneReach > 0f;
    public PlayerController Target => target;
    public Rect Area => area;
    public float Assembly => phase == Phase.Held ? 1f : phase == Phase.Engaging ? Mathf.Clamp01(clock / engageDuration) : 0f;

    private void Awake() => Prepare();
    // Prefab이 꺼진 채 저장돼 있어도 Engage가 먼저 불릴 수 있으므로 필요한 순간에 한 번 준비한다.
    private void Prepare()
    {
        if (body != null) return;
        properties = new MaterialPropertyBlock();
        body = NewMesh("ContainmentBody"); glow = NewMesh("ContainmentGlow");
        if (bodyMesh != null) bodyMesh.sharedMesh = body;
        if (glowMesh != null) glowMesh.sharedMesh = glow;
    }
    private static Mesh NewMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        return mesh;
    }

    // 상자를 씌운다. 이미 씌운 상태에서 다시 부르면(갇힌 채 텔레포트) 도착한 자리에서 브래킷만 다시 맞물린다.
    public void Engage(PlayerController player)
    {
        bool again = IsEngaged && target == player;
        target = player;
        if (target != null) area = target.Motor.Confinement;
        clock = again ? engageDuration * .45f : 0f;
        phase = Phase.Engaging;
        ring = 0f;
        // 새로 씌우는 상자에는 이전 크레인이 남아 있지 않다.
        if (!again) { craneWanted = false; craneReach = craneLock = craneFlash = craneLean = 0f; craneVelocity = Vector2.zero; }
        Prepare();
        gameObject.SetActive(true);
        System.Array.Clear(impact, 0, impact.Length);
        System.Array.Clear(touching, 0, touching.Length);
        Play(engageFeedback);
        Draw();
    }

    // 상자를 푼다. 해체 연출이 끝나면 스스로 꺼진다.
    public void Disengage()
    {
        if (phase == Phase.Off || phase == Phase.Releasing) return;
        releaseFrom = Assembly;
        phase = Phase.Releasing;
        clock = 0f;
        // 상자가 풀리면 크레인도 걸쇠를 풀고 올라간다.
        craneWanted = false; craneVelocity = Vector2.zero;
        Play(releaseFeedback);
    }

    /// <summary>크레인을 내려 상자 위쪽을 물게 한다. 상자가 다 조립된 뒤에 걸쇠가 닫힌다(CraneAttached).</summary>
    public void AttachCrane() => craneWanted = true;
    /// <summary>걸쇠를 풀고 크레인을 위로 올려 보낸다. 상자는 그대로 둔다.</summary>
    public void DetachCrane() { craneWanted = false; craneVelocity = Vector2.zero; }
    /// <summary>상자를 옮기고 있는 속도(칸/초). 줄의 기울기와 도르래 회전에 쓴다.</summary>
    public void DriveCrane(Vector2 velocity) => craneVelocity = velocity;

    private void Play(FeedbackDataSO feedback)
    {
        if (feedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(feedback, new Vector3(area.center.x, area.center.y, 0f));
    }

    private void LateUpdate()
    {
        float delta = Time.deltaTime;
        clock += delta; heldTime += delta;
        if (ring >= 0f) { ring += delta; if (ring > .3f) ring = -1f; }
        if (phase == Phase.Engaging && clock >= engageDuration) { phase = Phase.Held; clock = 0f; }
        UpdateCrane(delta);
        // 상자를 다 풀었어도 크레인이 올라가는 동안에는 켜 둔다.
        if (phase == Phase.Releasing && clock >= releaseDuration && craneReach <= 0f)
        {
            phase = Phase.Off; target = null;
            gameObject.SetActive(false);
            return;
        }
        if (IsEngaged)
        {
            // 대상이 사라졌거나 다른 경로로 구속이 풀렸으면 표시도 푼다.
            if (target == null || !target.Motor.IsConfined) { Disengage(); }
            else { area = target.Motor.Confinement; TrackWalls(delta); }
        }
        Draw();
    }

    // 크레인의 진행: 내려와서(스프레더) 상자가 조립되면 문다(걸쇠). 풀 때는 걸쇠부터 열고 올라간다.
    private void UpdateCrane(float delta)
    {
        if (craneWanted && IsEngaged)
        {
            craneReach = Mathf.MoveTowards(craneReach, 1f, delta / craneDropDuration);
            if (craneReach >= 1f && phase == Phase.Held)
            {
                bool open = craneLock < 1f;
                craneLock = Mathf.MoveTowards(craneLock, 1f, delta / craneLockDuration);
                if (open && craneLock >= 1f) craneFlash = 1f;
            }
        }
        else
        {
            craneLock = Mathf.MoveTowards(craneLock, 0f, delta / craneLockDuration);
            if (craneLock <= 0f) craneReach = Mathf.MoveTowards(craneReach, 0f, delta / craneDropDuration);
        }
        if (craneReach <= 0f) { craneLean = 0f; return; }
        craneFlash = Mathf.MoveTowards(craneFlash, 0f, delta * 4f);
        // 줄은 옮기는 쪽으로 위 끝이 앞서 기운다(상자가 줄에 끌려오는 모습). 물고 있을 때만 기운다.
        float lean = Mathf.Clamp(craneVelocity.x * .006f, -.14f, .14f) * craneLock;
        craneLean = Mathf.Lerp(craneLean, lean, 1f - Mathf.Exp(-delta * 9f));
        // 줄을 감고 푸는 만큼, 가로로 달리는 만큼 도르래가 돈다.
        craneSpin = Mathf.Repeat(craneSpin + (craneVelocity.y * 150f - craneVelocity.x * 55f) * delta, 360f);
        cableFlow += delta * (1.1f + craneVelocity.magnitude * .09f);
    }

    // Player가 상자 벽을 밀고 있는지 본다. 닿는 순간 그 면이 번쩍이고, 계속 밀면 약하게 켜져 있다.
    private void TrackWalls(float delta)
    {
        Rect bodyRect = target.Motor.ConfinedBody;
        Vector2 push = target.Velocity + (target.IgnoreExternalForces ? Vector2.zero : target.externalForce);
        const float contact = .012f, speed = .2f;
        Touch(0, bodyRect.xMin - area.xMin < contact && push.x < -speed);
        Touch(1, area.xMax - bodyRect.xMax < contact && push.x > speed);
        Touch(2, area.yMax - bodyRect.yMax < contact && push.y > speed);
        // 바닥은 늘 딛고 있으므로 세게 내려앉을 때만 반응한다.
        Touch(3, bodyRect.yMin - area.yMin < contact && push.y < -6f);
        for (int i = 0; i < impact.Length; i++)
            impact[i] = Mathf.MoveTowards(impact[i], touching[i] ? .35f : 0f, delta * (impact[i] > .35f ? 5f : 2.2f));
    }
    private void Touch(int side, bool now)
    {
        if (now && !touching[side])
        {
            impact[side] = 1f;
            if (bumpFeedback != null && FeedbackPlayer.Instance != null)
                FeedbackPlayer.Instance.Play(bumpFeedback, new Vector3(side == 0 ? area.xMin : side == 1 ? area.xMax : area.center.x, side == 2 ? area.yMax : side == 3 ? area.yMin : area.center.y, 0f));
        }
        touching[side] = now;
    }

    private static float Seg(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
    private static float Smooth(float x) => x * x * (3f - 2f * x);
    private static float OutCubic(float x) { x = 1f - x; return 1f - x * x * x; }
    private static float InCubic(float x) => x * x * x;

    // 이 프레임의 모습을 정해 Mesh와 안내판에 적용한다.
    private void Draw()
    {
        if (body == null) return;
        transform.position = new Vector3(area.center.x, area.center.y, 0f);
        bodyVertices.Clear(); bodyColors.Clear(); bodyTriangles.Clear();
        glowVertices.Clear(); glowColors.Clear(); glowTriangles.Clear();

        // 단계별 진행도: 브래킷이 바깥에 있는 정도, 발광선이 닫힌 정도, 면이 켜진 정도, 안내판이 펼쳐진 정도, 글자가 보이는 정도
        float away, opacity, lines, glass, leader, plate, text, lockFlash;
        if (phase == Phase.Releasing)
        {
            float t = Mathf.Clamp01(clock / releaseDuration);
            away = OutCubic(Seg(t, .25f, 1f)) * 1.2f;
            opacity = 1f - Seg(t, .55f, 1f);
            lines = releaseFrom * (1f - Seg(t, 0f, .4f));
            glass = releaseFrom * (1f - Seg(t, .12f, .5f)) + (1f - Seg(t, 0f, .15f)) * .6f * releaseFrom;
            leader = releaseFrom * (1f - Seg(t, .35f, .6f));
            plate = releaseFrom * (1f - Smooth(Seg(t, .08f, .45f)));
            text = releaseFrom * (1f - Seg(t, 0f, .2f));
            lockFlash = (1f - Seg(t, 0f, .2f)) * .7f;
        }
        else
        {
            float t = phase == Phase.Held ? 1f : Mathf.Clamp01(clock / engageDuration);
            // 빠르게 다가와 '탁' 맞물리고 살짝 튕긴다.
            away = 1f - InCubic(Seg(t, 0f, .52f));
            if (t >= .52f) away = -.055f * Mathf.Sin(Seg(t, .52f, .72f) * Mathf.PI);
            opacity = Seg(t, 0f, .16f);
            lines = Smooth(Seg(t, .56f, .9f));
            glass = Seg(t, .66f, 1f);
            leader = Seg(t, .5f, .72f);
            plate = OutCubic(Seg(t, .62f, .96f));
            text = Seg(t, .84f, 1f);
            lockFlash = phase == Phase.Held ? 0f : Seg(t, .5f, .56f) * (1f - Seg(t, .56f, .9f));
        }

        float halfWidth = area.width * .5f, halfHeight = area.height * .5f;
        float pulse = .78f + .22f * Mathf.Sin(heldTime * 5.2f);
        DrawBrackets(halfWidth, halfHeight, away, opacity, lockFlash, pulse);
        DrawLines(halfWidth, halfHeight, lines, pulse);
        DrawRing(halfWidth, halfHeight);
        // 크레인은 안내판보다 먼저 그려 줄이 판 뒤로 지나가게 한다.
        DrawCrane(halfWidth, halfHeight, pulse);
        DrawSign(halfHeight, leader, plate, text, pulse);
        Upload(body, bodyVertices, bodyColors, bodyTriangles);
        Upload(glow, glowVertices, glowColors, glowTriangles);

        if (field != null)
        {
            field.transform.localScale = new Vector3(area.width, area.height, 1f);
            properties.SetFloat(RevealId, Mathf.Clamp01(glass));
            properties.SetVector(ImpactId, new Vector4(impact[0], impact[1], impact[2], impact[3]));
            properties.SetVector(SizeId, new Vector4(area.width, area.height, 0f, 0f));
            field.SetPropertyBlock(properties);
            field.enabled = glass > .001f;
        }
    }

    // 모서리 브래킷: 영역 경계 바깥에 붙는 ㄱ자 몸체, 바깥 가장자리의 흰 테두리, 모서리 표시등.
    private void DrawBrackets(float halfWidth, float halfHeight, float away, float opacity, float lockFlash, float pulse)
    {
        if (opacity <= 0f) return;
        Color fill = bodyColor; fill.a *= opacity;
        Color rim = rimColor; rim.a *= opacity;
        for (int i = 0; i < 4; i++)
        {
            float sx = i == 0 || i == 3 ? -1f : 1f, sy = i < 2 ? -1f : 1f;
            // 벽을 밀면 그 면에 붙은 브래킷이 살짝 밀려난다.
            float pushX = (sx < 0 ? impact[0] : impact[1]) * .035f, pushY = (sy > 0 ? impact[2] : impact[3]) * .035f;
            var corner = new Vector2(sx * (halfWidth + pushX), sy * (halfHeight + pushY)) + new Vector2(sx, sy) * (away * bracketTravel);
            float t = frameThickness, length = bracketLength;
            // 가로 팔과 세로 팔(모서리 사각 포함)
            Rect horizontal = Rect.MinMaxRect(Mathf.Min(corner.x - sx * length, corner.x + sx * t), Mathf.Min(corner.y, corner.y + sy * t),
                Mathf.Max(corner.x - sx * length, corner.x + sx * t), Mathf.Max(corner.y, corner.y + sy * t));
            Rect vertical = Rect.MinMaxRect(Mathf.Min(corner.x, corner.x + sx * t), Mathf.Min(corner.y - sy * length, corner.y + sy * t),
                Mathf.Max(corner.x, corner.x + sx * t), Mathf.Max(corner.y - sy * length, corner.y + sy * t));
            Box(bodyVertices, bodyColors, bodyTriangles, horizontal, fill);
            Box(bodyVertices, bodyColors, bodyTriangles, vertical, fill);
            // 바깥 가장자리의 얇은 테두리
            const float edge = .014f;
            float outerX = corner.x + sx * t, outerY = corner.y + sy * t;
            Box(bodyVertices, bodyColors, bodyTriangles, Span(corner.x - sx * length, outerX, outerY - sy * edge, outerY), rim);
            Box(bodyVertices, bodyColors, bodyTriangles, Span(outerX - sx * edge, outerX, corner.y - sy * length, outerY), rim);
            // 모서리 표시등: 맞물리는 순간 흰색으로 번쩍이고, 잠긴 뒤에는 청록으로 맥동한다.
            Color lamp = Color.Lerp(lineColor * (.55f * pulse), whiteGlow * 1.6f, lockFlash) * opacity;
            float lampHalf = t * .32f;
            var lampCenter = corner + new Vector2(sx, sy) * (t * .5f);
            Box(glowVertices, glowColors, glowTriangles, new Rect(lampCenter.x - lampHalf, lampCenter.y - lampHalf, lampHalf * 2f, lampHalf * 2f), Opaque(lamp));
            // 팔 끝의 작은 표시선
            Color tick = lineColor * (.5f * opacity);
            Box(glowVertices, glowColors, glowTriangles, Span(corner.x - sx * length, corner.x - sx * (length - .05f), corner.y + sy * t * .3f, corner.y + sy * t * .7f), Opaque(tick));
            Box(glowVertices, glowColors, glowTriangles, Span(corner.x + sx * t * .3f, corner.x + sx * t * .7f, corner.y - sy * length, corner.y - sy * (length - .05f)), Opaque(tick));
        }
    }

    // 변의 발광선: 브래킷 사이를 양 끝에서 가운데로 닫는다. 민 면은 밝아지고, 잠긴 뒤에는 빛 한 점이 둘레를 돈다.
    private void DrawLines(float halfWidth, float halfHeight, float amount, float pulse)
    {
        if (amount <= 0f) return;
        const float width = .028f;
        float offset = frameThickness * .5f;
        for (int side = 0; side < 4; side++)
        {
            bool vertical = side < 2;
            float half = vertical ? halfHeight : halfWidth;
            float gapStart = bracketLength;          // 모서리에서 브래킷 팔이 끝나는 지점
            float reach = Mathf.Max(0f, half - gapStart) * amount;
            if (reach <= 0f) continue;
            float fixedAxis = (side == 0 ? -halfWidth : side == 1 ? halfWidth : side == 2 ? halfHeight : -halfHeight);
            fixedAxis += (side == 0 || side == 3 ? -1f : 1f) * (offset + impact[side] * .035f);
            Color color = lineColor * (pulse * (1f + 2.6f * impact[side]));
            float lineWidth = width * (1f + 1.2f * impact[side]);
            // 양 끝에서 가운데로 자란 두 토막
            for (int end = -1; end <= 1; end += 2)
            {
                float from = end * (half - gapStart), to = end * (half - gapStart - reach);
                if (vertical) Box(glowVertices, glowColors, glowTriangles, Span(fixedAxis - lineWidth * .5f, fixedAxis + lineWidth * .5f, from, to), Opaque(color));
                else Box(glowVertices, glowColors, glowTriangles, Span(from, to, fixedAxis - lineWidth * .5f, fixedAxis + lineWidth * .5f), Opaque(color));
            }
        }
        if (phase != Phase.Held) return;
        // 둘레를 도는 빛(1.8초에 한 바퀴)
        float perimeter = 4f * (halfWidth + halfHeight);
        float head = Mathf.Repeat(heldTime / 1.8f, 1f) * perimeter;
        Vector2 point = PerimeterPoint(head, halfWidth + offset, halfHeight + offset, out bool onVertical);
        float along = .16f, across = .05f;
        Box(glowVertices, glowColors, glowTriangles,
            onVertical ? new Rect(point.x - across * .5f, point.y - along * .5f, across, along) : new Rect(point.x - along * .5f, point.y - across * .5f, along, across),
            Opaque(whiteGlow * .9f));
    }
    // 왼쪽 아래 모서리에서 시계 반대 방향으로 잰 둘레 위의 점.
    private static Vector2 PerimeterPoint(float distance, float halfWidth, float halfHeight, out bool vertical)
    {
        float width = halfWidth * 2f, height = halfHeight * 2f;
        if (distance < width) { vertical = false; return new Vector2(-halfWidth + distance, -halfHeight); }
        distance -= width;
        if (distance < height) { vertical = true; return new Vector2(halfWidth, -halfHeight + distance); }
        distance -= height;
        if (distance < width) { vertical = false; return new Vector2(halfWidth - distance, halfHeight); }
        distance -= width;
        vertical = true; return new Vector2(-halfWidth, halfHeight - distance);
    }

    // 맞물리는 순간 상자에서 바깥으로 한 번 퍼지는 사각 고리.
    private void DrawRing(float halfWidth, float halfHeight)
    {
        if (ring < 0f) return;
        float t = Mathf.Clamp01((ring - engageDuration * .5f) / .22f);
        if (t <= 0f || t >= 1f) return;
        float grow = Mathf.Lerp(.1f, .75f, OutCubic(t)), width = Mathf.Lerp(.06f, .012f, t);
        Color color = lineColor * ((1f - t) * (1f - t) * 1.3f);
        float x = halfWidth + grow, y = halfHeight + grow;
        Box(glowVertices, glowColors, glowTriangles, Span(-x, x, -y - width, -y), Opaque(color));
        Box(glowVertices, glowColors, glowTriangles, Span(-x, x, y, y + width), Opaque(color));
        Box(glowVertices, glowColors, glowTriangles, Span(-x - width, -x, -y, y), Opaque(color));
        Box(glowVertices, glowColors, glowTriangles, Span(x, x + width, -y, y), Opaque(color));
    }

    // 크레인: 화면 위에서 내려온 줄 두 가닥 끝에 도르래 블록과 스프레더가 달려 있고, 스프레더 양 끝의 걸쇠가 상자 위쪽 모서리를 문다.
    // 비공격 장치라 몸체는 짙은 흑연색, 기능선과 표시등은 청록·흰색 발광이다.
    private void DrawCrane(float halfWidth, float halfHeight, float pulse)
    {
        if (craneReach <= 0f) return;
        // 내려오는 동안에는 제자리보다 위에 있다.
        float drop = (1f - OutCubic(craneReach)) * craneDropHeight;
        float appear = Mathf.Clamp01(craneReach * 4f);
        Color fill = bodyColor; fill.a *= appear;
        Color rim = rimColor; rim.a *= appear;
        float bottom = halfHeight + frameThickness + spreaderGap + drop;     // 스프레더 아랫면
        float beamTop = bottom + spreaderThickness;
        float reach = halfWidth + frameThickness + .07f;                     // 스프레더 반폭
        float locked = Smooth(craneLock);
        Color lamp = Color.Lerp(lineColor * (.3f + .5f * locked * pulse), whiteGlow * 1.7f, craneFlash) * appear;
        const float edge = .014f;

        // 줄 두 가닥: 블록 윗면에서 화면 위 바깥까지. 옮기는 쪽으로 위 끝이 앞선다.
        float blockTop = beamTop + craneBlockSize.y;
        if (view == null) view = Camera.main;
        float viewTop = view != null && view.orthographic ? view.transform.position.y + view.orthographicSize + 2f : area.center.y + 30f;
        float topY = Mathf.Max(blockTop + 3f, viewTop - area.center.y);
        float lean = Mathf.Tan(craneLean) * (topY - blockTop);
        Color cable = Color.Lerp(bodyColor, rimColor, .55f); cable.a = appear;
        for (int side = -1; side <= 1; side += 2)
        {
            var from = new Vector2(side * cableSpacing * .5f, blockTop - .02f);
            var to = new Vector2(side * cableSpacing * .5f + lean, topY);
            Line(bodyVertices, bodyColors, bodyTriangles, from, to, cableWidth, cable);
            // 줄을 타고 올라가는 작은 빛(배선의 흐름)
            const float spacing = 1.7f, length = .16f;
            float span = topY - blockTop;
            for (float at = Mathf.Repeat(cableFlow + (side > 0 ? spacing * .5f : 0f), spacing); at < span - length; at += spacing)
            {
                var a = Vector2.Lerp(from, to, at / span);
                var b = Vector2.Lerp(from, to, (at + length) / span);
                Line(glowVertices, glowColors, glowTriangles, a, b, cableWidth * .7f, Opaque(lineColor * (.42f * appear)));
            }
        }

        // 스프레더: 상자보다 조금 넓은 가로 빔, 윗변의 얇은 테두리, 가운데 기능선
        Box(bodyVertices, bodyColors, bodyTriangles, Span(-reach, reach, bottom, beamTop), fill);
        Box(bodyVertices, bodyColors, bodyTriangles, Span(-reach, reach, beamTop - edge, beamTop), rim);
        Box(glowVertices, glowColors, glowTriangles, Span(-reach + .13f, reach - .13f, bottom + spreaderThickness * .36f, bottom + spreaderThickness * .6f), Opaque(lamp * .8f));

        // 걸쇠: 빔 양 끝에서 내려와 상자 위쪽 모서리의 바깥을 문다. 열려 있을 때는 바깥으로 벌어져 있다.
        for (int side = -1; side <= 1; side += 2)
        {
            float open = (1f - locked) * .14f;
            float inner = side * (halfWidth + frameThickness + .012f + open);
            float outer = inner + side * .05f;
            float clawBottom = halfHeight - .17f + drop;
            Box(bodyVertices, bodyColors, bodyTriangles, Span(inner, outer, clawBottom, bottom), fill);
            Box(bodyVertices, bodyColors, bodyTriangles, Span(outer - side * edge, outer, clawBottom, bottom), rim);
            // 끝의 턱: 닫히면 모서리 아래로 들어가 받친다.
            Box(bodyVertices, bodyColors, bodyTriangles, Span(inner - side * .045f * locked, outer, clawBottom, clawBottom + .04f), fill);
            // 표시등: 닫히는 순간 흰색으로 번쩍이고 물고 있는 동안 청록으로 켜져 있다.
            float lampY = bottom - .07f;
            Box(glowVertices, glowColors, glowTriangles, Span(inner + side * .012f, outer - side * .012f, lampY - .022f, lampY + .022f), Opaque(lamp));
        }

        // 도르래 블록: 빔 가운데 위의 작은 상자. 안에서 도르래 두 개가 줄을 감고 푼다.
        float halfBlock = craneBlockSize.x * .5f;
        Box(bodyVertices, bodyColors, bodyTriangles, Span(-halfBlock, halfBlock, beamTop, blockTop), fill);
        Box(bodyVertices, bodyColors, bodyTriangles, Span(-halfBlock, halfBlock, blockTop - edge, blockTop), rim);
        Box(bodyVertices, bodyColors, bodyTriangles, Span(-halfBlock, -halfBlock + edge, beamTop, blockTop), rim);
        Box(bodyVertices, bodyColors, bodyTriangles, Span(halfBlock - edge, halfBlock, beamTop, blockTop), rim);
        for (int side = -1; side <= 1; side += 2)
        {
            var center = new Vector2(side * cableSpacing * .5f, beamTop + craneBlockSize.y * .5f);
            float angle = (side > 0 ? craneSpin : -craneSpin) * Mathf.Deg2Rad;
            Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .058f, normal = new Vector2(-axis.y, axis.x);
            Quad(glowVertices, glowColors, glowTriangles, center - axis - normal, center + axis - normal, center + axis + normal, center - axis + normal, Opaque(lineColor * (.5f * appear)));
            Box(glowVertices, glowColors, glowTriangles, new Rect(center.x - .016f, center.y - .016f, .032f, .032f), Opaque(whiteGlow * (.9f * appear)));
        }
    }

    // 안내판: 상자 윗변에서 선이 올라가 판이 가운데에서 양옆으로 펼쳐지고, 글자와 흐르는 진행 띠가 켜진다.
    private void DrawSign(float halfHeight, float leader, float plate, float text, float pulse)
    {
        // 크레인이 물고 있으면 판을 도르래 블록보다 한참 위로 올린다. 블록에서 나온 줄이 보이다가 판 뒤로 지나가, 판이 줄에 걸린 표지처럼 보인다.
        // 상자와 판을 잇던 선은 줄이 대신한다.
        float craneRise = Smooth(Mathf.Clamp01(craneReach)) * Mathf.Max(0f, spreaderGap + spreaderThickness + craneBlockSize.y + craneSignClearance - signGap);
        leader *= 1f - Mathf.Clamp01(craneReach);
        float bottom = halfHeight + frameThickness + signGap + craneRise;
        Vector2 center = new Vector2(0f, bottom + signSize.y * .5f);
        if (sign != null) sign.localPosition = center;
        SetText(title, text);
        SetText(caption, text * .8f);
        if (leader > 0f)
        {
            float top = Mathf.Lerp(halfHeight + frameThickness, bottom, leader);
            Box(glowVertices, glowColors, glowTriangles, Span(-.012f, .012f, halfHeight + frameThickness, top), Opaque(lineColor * .8f));
        }
        if (plate <= 0f) return;
        float half = signSize.x * .5f * plate, height = signSize.y;
        Color back = signBack; back.a *= Mathf.Clamp01(plate * 2f);
        Box(bodyVertices, bodyColors, bodyTriangles, new Rect(-half, bottom, half * 2f, height), back);
        // 위아래 발광선과 양 끝 꺾쇠
        Color line = lineColor * (.85f * pulse);
        const float thin = .016f;
        Box(glowVertices, glowColors, glowTriangles, Span(-half, half, bottom + height - thin, bottom + height), Opaque(line));
        Box(glowVertices, glowColors, glowTriangles, Span(-half, half, bottom, bottom + thin), Opaque(line * .7f));
        float notch = Mathf.Min(.13f, height * .35f);
        Box(glowVertices, glowColors, glowTriangles, Span(-half, -half + thin * 1.5f, bottom, bottom + notch), Opaque(whiteGlow * .8f));
        Box(glowVertices, glowColors, glowTriangles, Span(-half, -half + thin * 1.5f, bottom + height - notch, bottom + height), Opaque(whiteGlow * .8f));
        Box(glowVertices, glowColors, glowTriangles, Span(half - thin * 1.5f, half, bottom, bottom + notch), Opaque(whiteGlow * .8f));
        Box(glowVertices, glowColors, glowTriangles, Span(half - thin * 1.5f, half, bottom + height - notch, bottom + height), Opaque(whiteGlow * .8f));
        if (text <= 0f) return;
        // 자물쇠 표시(왼쪽): 몸통과 고리
        float lockX = -signSize.x * .5f + .24f, lockY = bottom + height * .5f - .03f;
        Color lamp = whiteGlow * (.9f * text);
        Box(glowVertices, glowColors, glowTriangles, new Rect(lockX - .075f, lockY - .07f, .15f, .12f), Opaque(lamp));
        Box(glowVertices, glowColors, glowTriangles, Span(lockX - .055f, lockX - .03f, lockY + .05f, lockY + .13f), Opaque(lamp));
        Box(glowVertices, glowColors, glowTriangles, Span(lockX + .03f, lockX + .055f, lockY + .05f, lockY + .13f), Opaque(lamp));
        Box(glowVertices, glowColors, glowTriangles, Span(lockX - .055f, lockX + .055f, lockY + .13f, lockY + .155f), Opaque(lamp));
        // 진행 띠: 어두운 홈 위를 밝은 토막이 왼쪽에서 오른쪽으로 흐른다(지형을 다시 맞추는 중).
        float trackLeft = -signSize.x * .5f + .46f, trackRight = signSize.x * .5f - .2f, trackY = bottom + .085f;
        Box(glowVertices, glowColors, glowTriangles, Span(trackLeft, trackRight, trackY, trackY + .028f), Opaque(lineColor * (.16f * text)));
        float span = trackRight - trackLeft, chunk = .42f;
        float head = trackLeft - chunk + Mathf.Repeat(heldTime / .9f, 1f) * (span + chunk);
        float from = Mathf.Max(trackLeft, head), to = Mathf.Min(trackRight, head + chunk);
        if (to > from) Box(glowVertices, glowColors, glowTriangles, Span(from, to, trackY, trackY + .028f), Opaque(lineColor * (1.1f * text)));
    }
    private static void SetText(TextMeshPro label, float alpha)
    {
        if (label == null) return;
        bool show = alpha > .01f;
        if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
        if (show && !Mathf.Approximately(label.alpha, alpha)) label.alpha = alpha;
    }

    private static Color Opaque(Color color) { color.a = 1f; return color; }
    private static Rect Span(float x0, float x1, float y0, float y1)
        => Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Max(x0, x1), Mathf.Max(y0, y1));
    private static void Box(List<Vector3> vertices, List<Color> colors, List<int> triangles, Rect rect, Color color)
    {
        int index = vertices.Count;
        vertices.Add(new Vector3(rect.xMin, rect.yMin)); vertices.Add(new Vector3(rect.xMax, rect.yMin));
        vertices.Add(new Vector3(rect.xMax, rect.yMax)); vertices.Add(new Vector3(rect.xMin, rect.yMax));
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
        triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
    }
    // 네 점을 차례로 이은 사각형(기울어진 것도 그릴 수 있다).
    private static void Quad(List<Vector3> vertices, List<Color> colors, List<int> triangles, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int index = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
        triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
    }
    // 두 점을 잇는 굵기 있는 선.
    private static void Line(List<Vector3> vertices, List<Color> colors, List<int> triangles, Vector2 from, Vector2 to, float width, Color color)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude < 1e-8f) return;
        Vector2 side = new Vector2(-direction.y, direction.x).normalized * (width * .5f);
        Quad(vertices, colors, triangles, from - side, to - side, to + side, from + side, color);
    }
    private static void Upload(Mesh mesh, List<Vector3> vertices, List<Color> colors, List<int> triangles)
    {
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, false);
        // 크레인 줄이 화면 위 바깥까지 뻗으므로 넉넉하게 잡는다(오브젝트 하나라 가려내기 정확도는 필요 없다).
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(40f, 400f, 1f));
    }

    private void OnDestroy()
    {
        if (body != null) Destroy(body);
        if (glow != null) Destroy(glow);
    }
}
