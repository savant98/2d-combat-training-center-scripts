using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 업그레이드 메뉴 안의 하위 페이지(무장 개요·무장 교체·무기 상세) 공통 동작(2026-11-18).
/// 사용자 지시: "무장들은 업그레이드 메뉴에서 교체, 장착이 가능하도록 … 기존 Upgrade 기능은 같은 UpgradePanel의 다른 페이지로 남겨줘."
/// - UpgradeMenu를 입력 스택 맨 아래에 둔 채 그 위에 쌓인다(UpgradePurchaseDialog와 같은 방식). 그래서 UpgradeMenu는 계속 IsActive이고
///   LobbyStation 장치도 접히지 않는다.
/// - 마우스 연결은 Awake에서 buttons[] 기준으로 한 번만 하므로 버튼 수는 Prefab에 고정 배치하고, 남는 버튼은 숨긴다.
/// - 방향 이동은 배열 순서 대신 실제 버튼 중심 좌표로 이웃을 고른다(OptionMenu.FindDirectionalIndex와 같은 계산).
/// - 페이지 전체의 표시·입력은 CanvasGroup 하나로 켜고 끈다. 보드의 제목·재화·안내 줄은 모든 페이지가 함께 쓰고 안내 문구만 바꾼다.
/// - PlayerHUD 기어 모양의 원(GlowMultiply 가산 발광 재질)은 CanvasGroup 투명도를 받지 않는다. 그래서 페이지와 보드의 투명도를 곱한 값이
///   glowVisibleAlpha를 넘을 때만 그 Image들을 켜고(숨긴 페이지·닫힌 메뉴에서는 끈다), 보이는 동안 HUD처럼 천천히 돌린다(2026-11-18 2차).
/// </summary>
public abstract class UpgradeMenuPage : SelectableMenuUI
{
    [Header("Page")]
    [Tooltip("페이지 전체의 투명도·상호작용·레이캐스트를 함께 켜고 끄는 CanvasGroup")]
    [SerializeField] protected CanvasGroup pageGroup;
    [Tooltip("보드 아래쪽 공통 안내 줄(HologramHint). 이 페이지가 보일 때 아래 문구로 바꾼다")]
    [SerializeField] protected TextMeshProUGUI hintText;
    [SerializeField] protected string hint = "방향키 이동 · Z 선택 · X 돌아가기";
    [Tooltip("페이지를 바꿀 때 서로 겹쳐 사라지고 나타나는 시간(실제 시간)")]
    [SerializeField, Min(0f)] protected float fadeDuration = .18f;
    [Tooltip("페이지가 보인 직후 이 시간(실제 시간) 동안은 클릭을 받지 않는다. 앞 페이지에서 누른 더블클릭의 두 번째 클릭이 새 페이지의 버튼을 바로 누르지 않게 한다")]
    [SerializeField, Min(0f)] protected float pointerClickGuard = .3f;
    private float pointerReadyTime;

    [Header("Glow Rings")]
    [Tooltip("GlowMultiply(가산 발광) 재질을 쓰는 Image. 이 셰이더는 CanvasGroup 투명도를 받지 않으므로 페이지가 보일 때만 켠다")]
    [SerializeField] protected Image[] glowImages = Array.Empty<Image>();
    [Tooltip("보이는 동안 HUD 기어처럼 도는 원")]
    [SerializeField] protected RectTransform[] spinners = Array.Empty<RectTransform>();
    [Tooltip("원이 도는 빠르기(초당 각도, 실제 시간). PlayerHUD의 무기 원과 같은 값")]
    [SerializeField] protected float spinSpeed = 25f;
    [Tooltip("페이지·보드 투명도를 곱한 값이 이보다 크면 발광 원을 켠다")]
    [SerializeField, Range(0f, 1f)] protected float glowVisibleAlpha = .98f;
    private CanvasGroup[] alphaChain;
    private int glowState = -1;   // -1 = 아직 적용 전, 0 = 꺼짐, 1 = 켜짐

    private static readonly Color Unselected = new Color(1f, 1f, 1f, 0f);

    // 장착·구매 연출처럼 이 페이지가 직접 입력을 막아 둔 동안 true
    protected bool InputSuppressed { get; set; }

    protected bool CanReceiveInput => IsActive && isActiveAndEnabled && !InputSuppressed
        && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
        && UIManager.Instance.IsCurrentInputTarget(this);

    public bool IsVisible => pageGroup != null && pageGroup.blocksRaycasts;

    protected override void Awake()
    {
        buttons ??= Array.Empty<RectTransform>();
        images ??= Array.Empty<Image>();
        texts ??= Array.Empty<TextMeshProUGUI>();
        glowImages ??= Array.Empty<Image>();
        spinners ??= Array.Empty<RectTransform>();
        base.Awake();
    }

    public override void Close()
    {
        // 장면이 내려가는 중에는 UIManager가 먼저 사라질 수 있다.
        if (IsActive && UIManager.Instance != null) base.Close();
    }

    #region Input
    protected virtual bool IsNavigable(int index) =>
        buttons != null && index >= 0 && index < buttons.Length && buttons[index] != null && buttons[index].gameObject.activeInHierarchy;

    protected override void ProcessMove(Vector2 direction)
    {
        if (!CanReceiveInput) return;
        int next = FindDirectionalIndex(currentIndex, direction);
        if (next >= 0) ChangeIndex(next);
    }

    public override void OnConfirm()
    {
        if (CanReceiveInput && IsNavigable(currentIndex)) base.OnConfirm();
    }

    public override void OnCancel()
    {
        if (CanReceiveInput) base.OnCancel();
    }

    protected override void OnPointerEnter(int index)
    {
        if (!CanReceiveInput || !IsNavigable(index)) return;
        ChangeIndex(index);
    }

    protected override void OnPointerClick(int index)
    {
        if (!CanReceiveInput || !IsNavigable(index)) return;
        if (Time.unscaledTime < pointerReadyTime) return;   // 페이지가 막 바뀌었다(더블클릭의 두 번째 클릭)
        ChangeIndex(index);
        OnConfirm();
    }

    // OptionMenu.FindDirectionalIndex와 같은 계산: 진행 방향으로 1 이상 떨어진 후보 중 (전진 거리 + 옆 거리 × 3)이 가장 작은 버튼.
    protected virtual int FindDirectionalIndex(int from, Vector2 direction)
    {
        if (buttons == null || from < 0 || from >= buttons.Length || buttons[from] == null) return -1;
        if (direction.sqrMagnitude < .01f) return -1;
        direction.Normalize();
        var space = (RectTransform)transform;
        Vector2 start = Center(space, buttons[from]);
        Vector2 side = new Vector2(-direction.y, direction.x);
        int closest = -1;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i == from || !IsNavigable(i)) continue;
            Vector2 delta = Center(space, buttons[i]) - start;
            float forward = Vector2.Dot(delta, direction);
            if (forward < 1f) continue;
            float score = forward + Mathf.Abs(Vector2.Dot(delta, side)) * 3f;
            if (score < bestScore) { bestScore = score; closest = i; }
        }
        return closest;
    }

    private static Vector2 Center(RectTransform space, RectTransform target) =>
        space.InverseTransformPoint(target.TransformPoint(target.rect.center));

    // 이동 소리 없이 선택 위치만 맞춘다(페이지를 열거나 돌아올 때). 남아 있던 강조 Tween도 모두 정리한다.
    protected void FocusSilently(int index)
    {
        if (buttons == null || buttons.Length == 0) return;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null) { buttons[i].DOKill(); buttons[i].localScale = Vector3.one; }
            if (images != null && i < images.Length && images[i] != null) { images[i].DOKill(); images[i].color = Unselected; }
        }
        if (!IsNavigable(index)) index = FirstNavigable();
        currentIndex = Mathf.Clamp(index, 0, buttons.Length - 1);
        prevIndex = -1;
        UpdateHighlight(true);
    }

    protected int FirstNavigable()
    {
        for (int i = 0; i < buttons.Length; i++) if (IsNavigable(i)) return i;
        return 0;
    }
    #endregion

    #region Visibility
    protected void ShowPage(bool instant = false)
    {
        pointerReadyTime = Time.unscaledTime + pointerClickGuard;
        SetGroupVisible(pageGroup, true, instant ? 0f : fadeDuration);
        ApplyHint();
    }

    protected void HidePage(bool instant = false) => SetGroupVisible(pageGroup, false, instant ? 0f : fadeDuration);

    public void ApplyHint()
    {
        if (hintText != null && !string.IsNullOrEmpty(hint)) hintText.text = hint;
    }

    protected static void SetGroupVisible(CanvasGroup group, bool visible, float duration)
    {
        if (group == null) return;
        group.DOKill();
        group.interactable = group.blocksRaycasts = visible;
        if (duration <= 0f) group.alpha = visible ? 1f : 0f;
        else group.DOFade(visible ? 1f : 0f, duration).SetUpdate(true);
    }

    // Animator(보드 열기·닫기)와 DOTween(페이지 페이드)이 투명도를 바꾼 뒤 그리기 전에 맞춘다.
    protected virtual void LateUpdate() => UpdateGlow();

    /// <summary>발광 원을 지금 보이는 정도에 맞춰 켜고 끄고, 보이면 돌린다. 검사 도구도 부른다.</summary>
    protected void UpdateGlow()
    {
        // 발광 원은 투명도를 받지 않아 흐리게 할 수 없으므로, 보드 열기·닫기(투명도 + 세로 크기 0.04 → 1)와 페이지 페이드가
        // 거의 끝났을 때만 켠다. 그 전에 켜면 눌린 보드 위에서 원래 밝기로 번쩍인다(2026-11-18 독립 검토).
        bool show = isActiveAndEnabled && VisibleAlpha() > glowVisibleAlpha && ChainScaleY() >= glowVisibleAlpha;
        int state = show ? 1 : 0;
        if (state != glowState)
        {
            glowState = state;
            foreach (var image in glowImages)
                if (image != null) image.enabled = show;
        }
        if (!show || Mathf.Approximately(spinSpeed, 0f)) return;
        float step = spinSpeed * Time.unscaledDeltaTime;
        foreach (var spinner in spinners)
            if (spinner != null) spinner.Rotate(0f, 0f, step);
    }

    // 이 페이지부터 위로 CanvasGroup 투명도를 곱한다(ignoreParentGroups에서 멈춘다). 구조는 고정이라 처음 한 번만 찾는다.
    public float VisibleAlpha()
    {
        if (alphaChain == null)
        {
            var chain = new List<CanvasGroup>();
            foreach (var group in GetComponentsInParent<CanvasGroup>(true))
            {
                chain.Add(group);
                if (group.ignoreParentGroups) break;
            }
            if (pageGroup != null && !chain.Contains(pageGroup)) chain.Insert(0, pageGroup);
            alphaChain = chain.ToArray();
        }
        float alpha = 1f;
        foreach (var group in alphaChain)
            if (group != null && group.enabled) alpha *= group.alpha;
        return alpha;
    }

    // 투명도 사슬의 CanvasGroup들 중 가장 작은 세로 크기(보드 열기·닫기 Clip이 Root/UpgradePanel의 세로 크기를 0.04 ↔ 1로 바꾼다).
    private float ChainScaleY()
    {
        if (alphaChain == null) VisibleAlpha();
        float scale = 1f;
        foreach (var group in alphaChain)
        {
            // 패널 루트(World Space Canvas, 배율 0.01)처럼 Canvas가 붙은 곳의 고정 배율은 열고 닫는 크기가 아니므로 뺀다.
            if (group == null || group.TryGetComponent<Canvas>(out _)) continue;
            scale = Mathf.Min(scale, Mathf.Abs(group.transform.localScale.y));
        }
        return scale;
    }
    #endregion

    #region Shared Helpers
    public static PlayerWeaponAbility FindWeaponAbility()
    {
        var game = GameManager.Instance;
        if (game == null || game.Player == null || game.Player.AbilityManager == null) return null;
        return game.Player.AbilityManager.AttackAbility;
    }

    public static string CategoryName(ArmamentCategory category) => category == ArmamentCategory.Melee ? "근접" : "원거리";
    public static string CategoryCode(ArmamentCategory category) => category == ArmamentCategory.Melee ? "MELEE" : "RANGED";

    // 세트(자리)에 정해진 분류. 세트를 읽지 못하면 자리 번호의 약속(0 = 근접, 1 = 원거리)을 따른다.
    public static ArmamentCategory CategoryOf(PlayerWeaponAbility ability, int setIndex)
    {
        WeaponSet set = ability != null ? ability.GetWeaponSet(setIndex) : null;
        if (set != null) return set.armamentCategory;
        return setIndex == 0 ? ArmamentCategory.Melee : ArmamentCategory.Ranged;
    }

    // 해금 상태 요약(2026-11-20 훈련 개편, 훈련 필증 해금): 기본 지급 / 해금 완료 / 무장과 함께 해금 / 잠김 · 필증 n / 사용 가능.
    // 비용 0 시절의 옛 해금 기록(weaponRecords)은 보지 않는다. A·B 모듈 강화 줄은 숨겨 강화 상태도 표시하지 않는다.
    public static string UpgradeSummary(WeaponBaseSO weapon)
    {
        if (weapon == null || string.IsNullOrWhiteSpace(weapon.weaponName)) return string.Empty;
        switch (ArmamentUnlocks.SourceOf(weapon))
        {
            case UnlockSource.Default: return "기본 지급";
            case UnlockSource.Purchased: return "해금 완료";
            case UnlockSource.ViaArmament: return "무장과 함께 해금";
            case UnlockSource.Locked: return LockedLabel(weapon);
            default: return "사용 가능";
        }
    }

    // 잠긴 기술의 상태 글자. 이 기술을 바로 살 수 있으면 가격을 붙인다(무장에 묶인 기술은 '잠김'만).
    public static string LockedLabel(WeaponBaseSO weapon)
    {
        var entry = ArmamentUnlocks.PurchasableEntry(weapon);
        return entry != null ? "잠김 · " + ArmamentUnlocks.PriceLabel(entry.price) : "잠김";
    }

    // 이 기술을 지금 쓸 수 있는지(훈련 필증 해금 판정, ArmamentUnlocks). Gear 원의 밝은/어두운 재질을 고를 때 쓴다.
    public static bool IsUnlocked(WeaponBaseSO weapon) => weapon != null && ArmamentUnlocks.IsWeaponUnlocked(weapon);

    // 여러 줄 글자에 예비 글꼴 글자(가운뎃점 등)가 섞이면 그 줄만 높이가 달라진다. 줄 높이를 글꼴 기본값으로 고정한다.
    public static string EvenLines(string text) => "<line-height=100%>" + (text ?? string.Empty);

    public static void PlayFeedbackAt(FeedbackDataSO feedback, Transform at)
    {
        if (feedback == null || at == null || FeedbackPlayer.Instance == null) return;
        FeedbackPlayer.Instance.Play(feedback, at);
    }
    #endregion
}
