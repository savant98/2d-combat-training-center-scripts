using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 업그레이드 메뉴의 잠긴 원(개요 Gear 원·교체 카드·상세 아이콘) 위에 얹는 기계식 잠금 부품과 해금 연출(2026-11-20 훈련 개편).
/// 사용자 요청: "해금 Animation도 UpgradeMenu의 기계적인 움직임을 살려서".
/// - 부품: 흑연색 잠금 톱니(HudSlotGear) + 좌우 걸쇠 두 개(흑연 몸체 + 안쪽 흰 선) + 흰 선 자물쇠. 일반 UI 재질이라 색 투명도를 받는다.
/// - 연출(실제 시간, 1.8초): 톱니가 장전(-15°) 뒤 반 바퀴 돌고 → 걸쇠가 벌어지며 빠지고 → 톱니·자물쇠가 떨어져 사라지고 →
///   어두운 링이 재질 세기로 차오르며 120° 더 돌고 → 아이콘이 들렸다 1.05초에 제자리에 끼워진다(체결 = Engaged) → 링이 가라앉는다.
/// - 크기(Scale)는 바꾸지 않는다(기계 부품은 회전·이동으로만 움직인다).
/// - 링(GlowMultiply)은 Image.color·CanvasGroup 투명도를 받지 않으므로 재질 사본의 _Intensity·_EmissionColor로 바꾸고, 끝나면 공유 재질로 돌려놓는다.
/// - DOTween을 쓰지 않고 매 프레임 Evaluate로 직접 계산한다. 페이지가 코루틴을 멈추면 CompleteImmediately로 최종 자세를 놓는다(남는 Tween 없음).
/// tools/Unity/ArmamentUnlockSetup.cs가 부품을 만들고 연결한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentLockOverlay : MonoBehaviour
{
    // 시간표(초, 실제 시간). 검증 도구도 이 상수를 읽는다.
    public const float Length = 1.8f, WindupEnd = .15f, TurnEnd = .55f, ReleaseSoundTime = .35f, JawStart = .40f, JawEnd = .70f,
        JawFadeStart = .60f, JawFadeEnd = .80f, DropStart = .55f, DropEnd = .80f, FillStart = .60f, LiftStart = .80f, SeatStart = .98f,
        EngageTime = 1.05f, SettleEnd = 1.45f;
    // 크기·각도(D = 덮는 원 지름). 각도는 반시계가 양수.
    public const float WindupDegrees = -15f, TurnDegrees = 180f, JawPreload = .02f, JawTravel = .24f, JawTilt = 14f, DropDistance = .25f,
        IconLift = .06f, IconSeatTilt = -20f, RingPeak = 1.5f, RingSpinExtra = 120f, LockedIconAlpha = .32f;
    // 멈춰 있던 편집기가 한 번에 넘기는 긴 프레임은 한 걸음으로 자른다.
    private const float MaxStep = .1f;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("덮는 대상")]
    [Tooltip("잠금 부품이 덮는 원(GlowMultiply). 연출 중 재질 세기만 바꾼다")]
    [SerializeField] private Image ring;
    [Tooltip("대상 아이콘. 잠긴 동안 흐리게 두고, 해금 때 들렸다 제자리에 끼워진다")]
    [SerializeField] private Image icon;

    [Header("잠금 부품")]
    [SerializeField] private RectTransform gear;
    [SerializeField] private RectTransform jawLeft;
    [SerializeField] private RectTransform jawRight;
    [SerializeField] private RectTransform glyph;
    [Tooltip("투명도를 함께 바꿀 부품 그래픽(톱니·걸쇠·흰 선·자물쇠)")]
    [SerializeField] private Graphic[] parts = Array.Empty<Graphic>();

    [Header("소리(임시 연결, 사용자가 청취 후보를 고르기 전)")]
    [Tooltip("걸쇠가 풀릴 때(ReleaseSoundTime)")]
    [SerializeField] private SoundOptions releaseSound;
    [Tooltip("아이콘이 끼워질 때(EngageTime)")]
    [SerializeField] private SoundOptions engageSound;

    private bool cached;
    private float diameter = 100f;
    private Vector2 gearRest, jawLeftRest, jawRightRest, glyphRest, iconRest;
    private float iconRestAngle, iconAlpha = 1f;
    private float[] partAlphas = Array.Empty<float>();
    private bool[] partIsJaw = Array.Empty<bool>();

    private Material ringCopy, ringFrom, ringTo;
    private float ringSpinApplied;
    private bool releasedCue, engagedCue, finishPending;

    /// <summary>잠금 부품이 보이는 상태(잠김)인가.</summary>
    public bool IsLocked { get; private set; }
    /// <summary>해금 연출이 진행 중인가.</summary>
    public bool IsPlaying { get; private set; }
    /// <summary>연출 경과 시간(검증용).</summary>
    public float Elapsed { get; private set; }
    /// <summary>아이콘이 제자리에 끼워지는 순간(EngageTime)에 한 번.</summary>
    public event Action Engaged;

    public Image Ring => ring;
    public Image Icon => icon;
    public RectTransform GearPart => gear;
    public RectTransform JawLeft => jawLeft;
    public RectTransform JawRight => jawRight;
    public RectTransform GlyphPart => glyph;
    /// <summary>연출 중 링에 넣은 재질 사본(없으면 null). 검증 도구가 정리 여부를 본다.</summary>
    public Material RingCopy => ringCopy;

    private void OnEnable()
    {
        if (!finishPending) return;
        finishPending = false;
        SetLocked(false);
    }

    private void OnDisable()
    {
        // 꺼지는 동안에는 자식 오브젝트를 켜고 끄지 않는다. 재질만 정리하고 켜질 때 마무리한다.
        ReleaseRingCopy(true);
        if (IsPlaying)
        {
            IsPlaying = false;
            finishPending = true;
        }
    }

    private void OnDestroy() => ReleaseRingCopy(false);

    /// <summary>즉시 자세: 잠김이면 부품을 제자리에 불투명하게 두고 아이콘을 흐리게, 해제면 부품을 끄고 아이콘을 원래대로.</summary>
    public void SetLocked(bool locked)
    {
        CacheRest();
        if (IsPlaying)
        {
            IsPlaying = false;
            ReleaseRingCopy(true);
        }
        finishPending = false;
        IsLocked = locked;
        SetPartsActive(locked);
        ResetParts();
        ApplyIcon(iconRest, iconRestAngle, locked ? LockedIconAlpha : iconAlpha);
    }

    /// <summary>
    /// 해금 연출. from = 잠긴 링 재질, to = 해금한 링 재질(공유 에셋). 끝나면 링이 to를 쓰고 부품은 꺼진다.
    /// 페이지 코루틴이 안쪽 단계를 직접 돌린다(예외가 바깥 finally까지 전해지게).
    /// </summary>
    public IEnumerator PlayUnlock(Material from, Material to)
    {
        CacheRest();
        ReleaseRingCopy(false);
        IsLocked = true;
        SetPartsActive(true);
        ResetParts();
        BeginRing(from, to);
        IsPlaying = true;
        finishPending = false;
        releasedCue = engagedCue = false;
        Elapsed = 0f;
        Evaluate(0f);
        while (Elapsed < Length)
        {
            yield return null;
            if (!IsPlaying) yield break;   // CompleteImmediately·비활성화로 끝남
            Elapsed = Mathf.Min(Length, Elapsed + Mathf.Min(Time.unscaledDeltaTime, MaxStep));
            Evaluate(Elapsed);
            FireCues(Elapsed);
        }
        IsPlaying = false;
        ReleaseRingCopy(true);
        ringSpinApplied = 0f;
        SetLocked(false);
    }

    /// <summary>중단: 재질 사본을 버리고 링을 to로, 부품을 끄고 아이콘을 제자리·불투명으로 둔다.</summary>
    public void CompleteImmediately(Material to)
    {
        CacheRest();
        IsPlaying = false;
        ReleaseRingCopy(false);
        if (ring != null && to != null && ring.material != to) ring.material = to;
        ringSpinApplied = 0f;
        SetLocked(false);
    }

    /// <summary>t초 자세를 그대로 적용한다(PlayUnlock과 검증이 함께 쓴다). 소리·이벤트는 내지 않는다.</summary>
    public void Evaluate(float t)
    {
        CacheRest();
        float d = diameter;

        // 잠금 톱니: 장전(반대 방향 15°) → 반 바퀴. 0.55초부터 자물쇠와 함께 아래로 떨어지며 사라진다.
        float gearAngle = t < WindupEnd
            ? Mathf.Lerp(0f, WindupDegrees, OutQuad(Ratio(t, 0f, WindupEnd)))
            : Mathf.Lerp(WindupDegrees, WindupDegrees + TurnDegrees, InOutCubic(Ratio(t, WindupEnd, TurnEnd)));
        float drop = InQuad(Ratio(t, DropStart, DropEnd));
        Vector2 down = Vector2.down * (DropDistance * d * drop);
        if (gear != null)
        {
            gear.anchoredPosition = gearRest + down;
            gear.localRotation = Quaternion.Euler(0f, 0f, gearAngle);
        }
        if (glyph != null) glyph.anchoredPosition = glyphRest + down;

        // 걸쇠: 장전 동안 안쪽으로 조금 물렸다가(긴장) 바깥으로 벌어지며 기울어 빠진다.
        float preload = OutQuad(Ratio(t, 0f, WindupEnd)) * JawPreload * d;
        float open = t < JawStart ? 0f : OutBack(Ratio(t, JawStart, JawEnd));
        float outward = t < JawStart ? -preload : Mathf.LerpUnclamped(-JawPreload * d, JawTravel * d, open);
        float tilt = JawTilt * Mathf.Clamp01(open);
        if (jawLeft != null)
        {
            jawLeft.anchoredPosition = jawLeftRest + new Vector2(-outward, 0f);
            jawLeft.localRotation = Quaternion.Euler(0f, 0f, tilt);
        }
        if (jawRight != null)
        {
            jawRight.anchoredPosition = jawRightRest + new Vector2(outward, 0f);
            jawRight.localRotation = Quaternion.Euler(0f, 0f, -tilt);
        }

        float jawAlpha = 1f - Ratio(t, JawFadeStart, JawFadeEnd);
        float dropAlpha = 1f - drop;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == null) continue;
            float baseAlpha = i < partAlphas.Length ? partAlphas[i] : 1f;
            bool jaw = i < partIsJaw.Length && partIsJaw[i];
            SetAlpha(part, baseAlpha * (jaw ? jawAlpha : dropAlpha));
        }

        // 링: 재질 세기로 차오르며(체결 순간 해금 세기의 1.5배) 120° 더 돈다. 공회전(spinners)과 겹치지 않게 증분으로 더한다.
        if (ring != null)
        {
            float extra = RingSpinExtra * OutCubic(Ratio(t, FillStart, EngageTime));
            if (!Mathf.Approximately(extra, ringSpinApplied))
            {
                ring.rectTransform.Rotate(0f, 0f, extra - ringSpinApplied);
                ringSpinApplied = extra;
            }
        }
        if (ringCopy != null)
        {
            float fromIntensity = Intensity(ringFrom), toIntensity = Intensity(ringTo), peak = toIntensity * RingPeak;
            Color fromColor = Emission(ringFrom), toColor = Emission(ringTo);
            if (t < EngageTime)
            {
                float fill = InQuad(Ratio(t, FillStart, EngageTime));
                ringCopy.SetFloat(IntensityId, Mathf.Lerp(fromIntensity, peak, fill));
                ringCopy.SetColor(EmissionColorId, Color.LerpUnclamped(fromColor, toColor, fill));
            }
            else
            {
                float settle = OutCubic(Ratio(t, EngageTime, SettleEnd));
                ringCopy.SetFloat(IntensityId, Mathf.Lerp(peak, toIntensity, settle));
                ringCopy.SetColor(EmissionColorId, toColor);
            }
            if (t >= SettleEnd) ReleaseRingCopy(true);
        }

        // 아이콘: 흐린 채 기다렸다가 들리며 기울고(0.80~0.98), 1.05초에 제자리로 끼워진다(InBack: 살짝 더 들렸다 탁 앉는다).
        if (t < LiftStart) ApplyIcon(iconRest, iconRestAngle, LockedIconAlpha);
        else if (t < SeatStart)
        {
            float p = OutQuad(Ratio(t, LiftStart, SeatStart));
            ApplyIcon(iconRest + Vector2.up * (IconLift * d * p), iconRestAngle + IconSeatTilt * p, Mathf.Lerp(LockedIconAlpha, iconAlpha, p));
        }
        else if (t < EngageTime)
        {
            float p = InBack(Ratio(t, SeatStart, EngageTime));
            ApplyIcon(iconRest + Vector2.up * (IconLift * d * (1f - p)), iconRestAngle + IconSeatTilt * (1f - p), iconAlpha);
        }
        else ApplyIcon(iconRest, iconRestAngle, iconAlpha);
    }

    private void FireCues(float t)
    {
        if (!releasedCue && t >= ReleaseSoundTime)
        {
            releasedCue = true;
            PlaySound(releaseSound);
        }
        if (!engagedCue && t >= EngageTime)
        {
            engagedCue = true;
            PlaySound(engageSound);
            try { Engaged?.Invoke(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    private void PlaySound(SoundOptions sound)
    {
        if (sound == null || sound.clip == null || SoundManager.Instance == null) return;
        SoundManager.Instance.PlaySFX(sound, transform.position);
    }

    // 처음 쓸 때 부품·아이콘의 제자리와 투명도를 기억한다(Prefab에 저장된 자세가 기준).
    private void CacheRest()
    {
        if (cached) return;
        cached = true;
        var rect = transform as RectTransform;
        if (rect != null)
        {
            float size = Mathf.Min(rect.rect.width, rect.rect.height);
            if (size <= 1f) size = Mathf.Min(rect.sizeDelta.x, rect.sizeDelta.y);
            if (size > 1f) diameter = size;
        }
        if (gear != null) gearRest = gear.anchoredPosition;
        if (jawLeft != null) jawLeftRest = jawLeft.anchoredPosition;
        if (jawRight != null) jawRightRest = jawRight.anchoredPosition;
        if (glyph != null) glyphRest = glyph.anchoredPosition;
        if (icon != null)
        {
            iconRest = icon.rectTransform.anchoredPosition;
            iconRestAngle = Mathf.DeltaAngle(0f, icon.rectTransform.localEulerAngles.z);
            iconAlpha = icon.color.a > LockedIconAlpha + .05f ? icon.color.a : 1f;
        }
        parts ??= Array.Empty<Graphic>();
        partAlphas = new float[parts.Length];
        partIsJaw = new bool[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            partAlphas[i] = parts[i] != null ? parts[i].color.a : 1f;
            partIsJaw[i] = parts[i] != null && (Under(parts[i].transform, jawLeft) || Under(parts[i].transform, jawRight));
        }
    }

    private void SetPartsActive(bool active)
    {
        SetActive(gear, active);
        SetActive(jawLeft, active);
        SetActive(jawRight, active);
        SetActive(glyph, active);
    }

    private void ResetParts()
    {
        if (gear != null) { gear.anchoredPosition = gearRest; gear.localRotation = Quaternion.identity; }
        if (jawLeft != null) { jawLeft.anchoredPosition = jawLeftRest; jawLeft.localRotation = Quaternion.identity; }
        if (jawRight != null) { jawRight.anchoredPosition = jawRightRest; jawRight.localRotation = Quaternion.identity; }
        if (glyph != null) { glyph.anchoredPosition = glyphRest; glyph.localRotation = Quaternion.identity; }
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null) SetAlpha(parts[i], i < partAlphas.Length ? partAlphas[i] : 1f);
    }

    private void ApplyIcon(Vector2 position, float angle, float alpha)
    {
        if (icon == null) return;
        var rect = icon.rectTransform;
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
        var rotation = Quaternion.Euler(0f, 0f, angle);
        if (rect.localRotation != rotation) rect.localRotation = rotation;
        SetAlpha(icon, alpha);
    }

    private void BeginRing(Material from, Material to)
    {
        if (ring == null) return;
        ringFrom = from != null ? from : ring.material;
        ringTo = to != null ? to : ringFrom;
        ringSpinApplied = 0f;
        if (ringFrom == null) return;
        ringCopy = new Material(ringFrom) { name = ringFrom.name + " (Unlock)", hideFlags = HideFlags.DontSave };
        ring.material = ringCopy;
    }

    // 재질 사본을 버린다. restore면 링을 해금 재질(공유 에셋)로 돌려놓는다.
    private void ReleaseRingCopy(bool restore)
    {
        if (ringCopy == null) return;
        if (ring != null && ring.material == ringCopy) ring.material = restore && ringTo != null ? ringTo : ringFrom;
        var copy = ringCopy;
        ringCopy = null;
        if (Application.isPlaying) Destroy(copy);
        else DestroyImmediate(copy);
    }

    private static float Intensity(Material material) =>
        material != null && material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 1f;

    private static Color Emission(Material material) =>
        material != null && material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.white;

    private static void SetActive(Component part, bool active)
    {
        if (part != null && part.gameObject.activeSelf != active) part.gameObject.SetActive(active);
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color color = graphic.color;
        if (Mathf.Approximately(color.a, alpha)) return;
        color.a = alpha;
        graphic.color = color;
    }

    private static bool Under(Transform target, Transform root)
    {
        if (root == null) return false;
        for (var current = target; current != null; current = current.parent)
            if (current == root) return true;
        return false;
    }

    private static float Ratio(float t, float start, float end) => end <= start ? (t >= end ? 1f : 0f) : Mathf.Clamp01((t - start) / (end - start));
    private static float OutQuad(float x) => 1f - (1f - x) * (1f - x);
    private static float InQuad(float x) => x * x;
    private static float OutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
    private static float InOutCubic(float x) => x < .5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
    private const float BackC1 = 1.70158f, BackC3 = BackC1 + 1f;
    private static float OutBack(float x) => 1f + BackC3 * Mathf.Pow(x - 1f, 3f) + BackC1 * Mathf.Pow(x - 1f, 2f);
    private static float InBack(float x) => BackC3 * x * x * x - BackC1 * x * x;
}
