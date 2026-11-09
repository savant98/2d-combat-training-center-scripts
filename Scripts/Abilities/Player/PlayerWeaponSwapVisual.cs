using System.Collections.Generic;
using UnityEngine;

// 무기 세트를 바꿀 때의 표시(2026-11-09, 사용자 요청 "무기 교체는 효과도 바꿔야 해. 무기가 교체되었다는 아이콘을 활용해서 다시 만들어줘", "Player 테두리가 살짝 빛나는 방식으로 기존 효과에 추가").
// 실제 무기 모델은 PlayerWeaponAbility가 지금까지처럼 바로 바꾸고, 이 개체는 표시만 한다. DashVisual처럼 실행 중에 Player 아래에 만들므로 Player.prefab은 건드리지 않는다.
// - 교체 아이콘(Badge): GearUI의 무기 틀·링과 같은 모양이 머리 위에 튀어나온다. 이전 세트의 기본 무기 아이콘이 위로 호를 그리며 빠지고 새 세트의 아이콘이 아래에서 호를 그리며 가운데로 들어온다.
//   HUD 슬롯과 같은 무기 아이콘(WeaponBaseSO.weaponSprite)을 쓴다. 틀 둘레의 두 화살표가 아이콘이 바뀌는 동안 돌고, 새 아이콘이 자리 잡는 순간 링과 화살표가 세트 색으로 번쩍인다.
// - 테두리 빛: 몸체(Circle)와 새 무기 Sprite의 복사본을 Player/SpriteOutlineGlow로 그려 실루엣 둘레가 세트 색으로 살짝 빛난다. 실제 Sprite는 건드리지 않고 매 프레임 그 자세를 따라간다(Animator 뒤에 실행).
// 시간은 Time.deltaTime이다. 발광 부품(GlowMultiply)은 정점 알파가 적용되지 않으므로 발광색 크기로 사라지게 한다.
[DefaultExecutionOrder(110)]
public sealed class PlayerWeaponSwapVisual : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int SpriteRectId = Shader.PropertyToID("_SpriteRect");
    private static readonly int PivotUvId = Shader.PropertyToID("_PivotUv");
    private static readonly int ExpandId = Shader.PropertyToID("_Expand");
    private static readonly int UvPerWorldId = Shader.PropertyToID("_UvPerWorld");

    [Header("Parts")]
    [SerializeField] private Transform badge;
    [SerializeField] private SpriteRenderer frame;
    [SerializeField] private SpriteRenderer ring;
    [SerializeField] private SpriteRenderer arrows;
    [SerializeField] private SpriteRenderer outgoing;
    [SerializeField] private SpriteRenderer incoming;
    [SerializeField] private SpriteRenderer bodyOutline;
    [SerializeField] private SpriteRenderer weaponOutline;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float popTime = 0.09f;             // 틀이 튀어나오는 시간
    [SerializeField, Min(0f)] private float swapStart = 0.06f;              // 아이콘이 바뀌기 시작하는 시각
    [SerializeField, Min(0.01f)] private float swapTime = 0.18f;            // 아이콘이 바뀌는 시간
    [SerializeField, Min(0f)] private float holdEnd = 0.56f;                // 새 아이콘을 보여 주는 끝 시각
    [SerializeField, Min(0.01f)] private float fadeTime = 0.16f;

    [Header("Look")]
    [SerializeField] private Vector2 offset = new Vector2(0f, 2.15f);       // Player 기준 표시 위치
    [SerializeField] private float iconSize = 0.62f;                        // 아이콘 한 변(칸)
    [SerializeField] private float slideDistance = 0.55f;                   // 아이콘이 드나드는 거리
    [SerializeField] private float slideArc = 0.18f;                        // 호의 높이
    [SerializeField] private float arrowTurnDegrees = -200f;                // 교체 화살표가 도는 각도(시계 방향)
    [SerializeField] private float ringSpinDegrees = 120f;                  // 링이 초당 도는 각도
    [SerializeField] private float settleScale = 0.18f;                     // 새 아이콘이 자리 잡을 때 튀는 크기
    [SerializeField, Min(0.01f)] private float settleTime = 0.12f;
    [SerializeField] private float flashGain = 2.2f;                        // 자리 잡는 순간의 번쩍임
    [SerializeField, Min(0.01f)] private float flashDecay = 0.07f;

    [Header("Outline")]
    [SerializeField, Min(0f)] private float outlineMargin = 0.22f;          // 실루엣 바깥으로 그릴 여유(칸)
    [SerializeField, Min(0.01f)] private float outlineRiseTime = 0.06f;
    [SerializeField, Range(0f, 1f)] private float outlineStart = 0.45f;     // 교체 중 세기
    [SerializeField, Range(0f, 1f)] private float outlineSettled = 0.3f;    // 자리 잡은 뒤 줄어든 세기
    [SerializeField, Min(0f)] private float outlineFlash = 0.25f;           // 자리 잡는 순간 더해지는 세기
    [SerializeField, Min(0.01f)] private float outlineSettleTime = 0.32f;
    [SerializeField, Range(0f, 1f)] private float outlineWhite = 0.3f;      // 세트 색에 흰색을 섞는 비율

    private readonly Dictionary<Sprite, Sprite> fullRectSprites = new();
    private MaterialPropertyBlock block;
    private SpriteRenderer bodySource, weaponSource;
    private Color tint = Color.white;
    private float age = -1f;

    public bool IsPlaying => age >= 0f;

    public void Initialize(Transform body)
    {
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
        badge.SetLocalPositionAndRotation(offset, Quaternion.identity);
        bodySource = body != null ? body.GetComponent<SpriteRenderer>() : null;
        Hide();
    }

    public void Play(Sprite previousIcon, Sprite currentIcon, Color setColor, GameObject currentModel)
    {
        tint = setColor;
        age = 0f;
        weaponSource = currentModel != null ? currentModel.GetComponent<SpriteRenderer>() : null;
        SetupIcon(outgoing, previousIcon);
        SetupIcon(incoming, currentIcon);
        frame.enabled = true;
        ring.enabled = true;
        arrows.enabled = true;
        Evaluate();
    }

    private static void SetupIcon(SpriteRenderer icon, Sprite sprite)
    {
        icon.sprite = sprite;
        icon.enabled = sprite != null;
    }

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        foreach (Sprite sprite in fullRectSprites.Values)
            Destroy(sprite);
        fullRectSprites.Clear();
    }

    private void LateUpdate()
    {
        if (age < 0f)
            return;

        age += Time.deltaTime;
        if (age >= holdEnd + fadeTime)
        {
            Hide();
            return;
        }

        Evaluate();
    }

    private void Evaluate()
    {
        float fade = Mathf.Clamp01((age - holdEnd) / fadeTime);
        float alpha = 1f - fade * fade;
        float scale = Mathf.LerpUnclamped(0.55f, 1f, EaseOutBack(Mathf.Clamp01(age / popTime))) * Mathf.Lerp(1f, 1.1f, fade);
        badge.localScale = new Vector3(scale, scale, 1f);

        float swap = Mathf.Clamp01((age - swapStart) / swapTime);
        float eased = swap * swap * (3f - 2f * swap);
        float arc = Mathf.Sin(eased * Mathf.PI) * slideArc;

        // 나가는 아이콘은 왼쪽 위로, 들어오는 아이콘은 오른쪽 아래에서 가운데로 호를 그린다(화살표와 같은 시계 방향).
        ApplyIcon(outgoing, new Vector2(-slideDistance * eased, arc), Mathf.Lerp(1f, 0.6f, eased), new Color(0.82f, 0.86f, 0.92f, alpha * (1f - eased)));

        float settled = Mathf.Clamp01((age - swapStart - swapTime) / settleTime);
        float punch = swap >= 1f ? 1f + settleScale * Mathf.Sin(settled * Mathf.PI) : 1f;
        ApplyIcon(incoming, new Vector2(slideDistance * (1f - eased), -arc), Mathf.Lerp(0.6f, 1f, eased) * punch, new Color(1f, 1f, 1f, alpha * Mathf.Clamp01(eased * 1.6f)));

        float turned = 1f - Mathf.Pow(1f - swap, 3f);
        arrows.transform.localRotation = Quaternion.Euler(0f, 0f, arrowTurnDegrees * turned);
        ring.transform.localRotation = Quaternion.Euler(0f, 0f, -ringSpinDegrees * age);

        float flash = swap >= 1f ? Mathf.Exp(-(age - swapStart - swapTime) / flashDecay) : 0f;
        ApplyGlow(ring, 1f + flashGain * flash, alpha);
        ApplyGlow(arrows, 1f + flashGain * flash, alpha);

        Color frameColor = frame.color;
        frameColor.a = alpha;
        frame.color = frameColor;

        float settledAmount = Mathf.Clamp01((age - swapStart - swapTime) / outlineSettleTime);
        float strength = Mathf.Clamp01(age / outlineRiseTime) * (Mathf.Lerp(outlineStart, outlineSettled, settledAmount) + outlineFlash * flash) * alpha;
        ApplyOutline(bodyOutline, bodySource, strength);
        ApplyOutline(weaponOutline, weaponSource, strength);
    }

    private void ApplyIcon(SpriteRenderer icon, Vector2 position, float scale, Color color)
    {
        if (!icon.enabled)
            return;

        float size = iconSize / Mathf.Max(icon.sprite.bounds.size.x, 0.001f) * scale;
        icon.transform.localPosition = position;
        icon.transform.localScale = new Vector3(size, size, 1f);
        icon.color = color;
    }

    private void ApplyGlow(SpriteRenderer glow, float intensity, float alpha)
    {
        glow.GetPropertyBlock(block);
        block.SetColor(EmissionColorId, tint * intensity * alpha);
        glow.SetPropertyBlock(block);
    }

    // 실제 Sprite의 자세를 따라 복사본을 그린다. 실루엣 바깥까지 그리도록 사각형을 넓히고 셰이더의 UV도 그만큼 늘린다.
    private void ApplyOutline(SpriteRenderer overlay, SpriteRenderer source, float strength)
    {
        if (source == null || source.sprite == null || !source.enabled || !source.gameObject.activeInHierarchy)
        {
            overlay.enabled = false;
            return;
        }

        Sprite sprite = source.sprite;
        Transform target = source.transform;
        Vector3 lossy = target.lossyScale;
        Vector2 size = sprite.bounds.size;
        float worldWidth = Mathf.Abs(size.x * lossy.x), worldHeight = Mathf.Abs(size.y * lossy.y);

        if (worldWidth < 0.01f || worldHeight < 0.01f)
        {
            overlay.enabled = false;   // Clip이 모델을 0으로 줄인 순간
            return;
        }

        float expandX = (worldWidth + 2f * outlineMargin) / worldWidth;
        float expandY = (worldHeight + 2f * outlineMargin) / worldHeight;

        overlay.sprite = FullRect(sprite);
        overlay.enabled = true;
        overlay.transform.SetPositionAndRotation(target.position, target.rotation);
        overlay.transform.localScale = new Vector3(lossy.x * expandX, lossy.y * expandY, 1f);

        Rect rect = sprite.textureRect;
        float textureWidth = sprite.texture.width, textureHeight = sprite.texture.height;
        var uvRect = new Vector4(rect.xMin / textureWidth, rect.yMin / textureHeight, rect.xMax / textureWidth, rect.yMax / textureHeight);
        var pivot = new Vector4(
            uvRect.x + sprite.pivot.x / sprite.rect.width * (uvRect.z - uvRect.x),
            uvRect.y + sprite.pivot.y / sprite.rect.height * (uvRect.w - uvRect.y));

        overlay.GetPropertyBlock(block);
        block.SetColor(GlowColorId, Color.Lerp(tint, Color.white, outlineWhite));
        block.SetFloat(AlphaId, strength);
        block.SetVector(SpriteRectId, uvRect);
        block.SetVector(PivotUvId, pivot);
        block.SetVector(ExpandId, new Vector4(expandX, expandY));
        block.SetVector(UvPerWorldId, new Vector4((uvRect.z - uvRect.x) / worldWidth, (uvRect.w - uvRect.y) / worldHeight));
        overlay.SetPropertyBlock(block);
    }

    // 원본 Sprite는 Tight 메시라 넓히면 빛이 잘린다. 같은 텍스처로 사각형 메시 Sprite를 한 번만 만들어 쓴다.
    private Sprite FullRect(Sprite source)
    {
        if (!fullRectSprites.TryGetValue(source, out Sprite full))
        {
            full = Sprite.Create(
                source.texture,
                source.rect,
                new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height),
                source.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            full.name = source.name + "_Outline";
            fullRectSprites[source] = full;
        }

        return full;
    }

    private void Hide()
    {
        age = -1f;
        weaponSource = null;
        if (frame != null) frame.enabled = false;
        if (ring != null) ring.enabled = false;
        if (arrows != null) arrows.enabled = false;
        if (outgoing != null) outgoing.enabled = false;
        if (incoming != null) incoming.enabled = false;
        if (bodyOutline != null) bodyOutline.enabled = false;
        if (weaponOutline != null) weaponOutline.enabled = false;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
