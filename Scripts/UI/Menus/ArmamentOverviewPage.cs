using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 업그레이드 메뉴를 열면 처음 보이는 무장 개요 페이지(2026-11-18).
/// 사용자 지시: "무기 교체 기능과 Upgrade를 한눈에 볼 수 있도록 … 무기 버튼을 누르면 상세 업그레이드 페이지 혹은 교체 페이지가 나오는 거지."
/// - 왼쪽 열 = 근접 자리, 오른쪽 열 = 원거리 자리. 맨 아래 큰 원(장착 무장) → 교체 페이지, 위로 쌓인 Gear 1~3 원 → 그 무기의 상세 업그레이드 페이지.
/// - 2026-11-18 2차: 사용자 지시 "이제 이전 UpgradeMenu는 삭제해"로 옛 8칸 강화 격자와 '전체 업그레이드 목록' 버튼을 지웠다. 구매는 상세 페이지에서만 한다.
/// - 이 페이지에서 X를 누르면 UpgradeMenu의 기존 닫기 처리(능력 중단·Animator·카메라·HUD)로 메뉴를 닫는다.
/// UpgradeMenu와는 OpenFromMenu로 받은 콜백만 주고받는다(UpgradeMenu가 이 페이지를 알고, 이 페이지는 UpgradeMenu의 내부를 모른다).
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentOverviewPage : UpgradeMenuPage
{
    [Header("Tree")]
    [Tooltip("왼쪽부터 무기 자리 열(근접, 원거리). 열마다 장착 무장 + Gear 1~3 원 4개가 선택 대상이 된다")]
    [SerializeField] private ArmamentTreeColumn[] columns = Array.Empty<ArmamentTreeColumn>();

    [SerializeField] private ArmamentTreeNode[] secondaryNodes = Array.Empty<ArmamentTreeNode>();

    [Header("Child Pages")]
    [SerializeField] private ArmamentSwapPage swapPage;
    [SerializeField] private WeaponDetailPage detailPage;

    [Header("Notice")]
    [Tooltip("비어 있는 Gear 단계를 골랐을 때 잠깐 보이는 안내")]
    [SerializeField] private TextMeshProUGUI noticeText;
    [SerializeField, Min(0f)] private float noticeDuration = 1.4f;
    [SerializeField] private FeedbackDataSO emptyStageFeedback;

    // 선택 대상 순서대로: 열과 단계(0 = 장착 무장, 1~3 = Gear).
    private readonly List<ArmamentTreeColumn> targetColumns = new List<ArmamentTreeColumn>();
    private readonly List<int> targetStages = new List<int>();
    private Action closeMenu;
    private Action refreshCurrency;
    private PlayerWeaponAbility observed;
    private Sequence noticeSequence;
    private bool initialized;

    protected override void Awake()
    {
        var rects = new List<RectTransform>();
        var marks = new List<Image>();
        foreach (var column in columns)
        {
            if (column == null) continue;
            for (int stage = 0; stage <= ArmamentTreeColumn.StageCount; stage++)
            {
                var node = column.GetNode(stage);
                if (node == null || node.Button == null || node.Highlight == null)
                {
                    Debug.LogError($"[{name}] {column.name}의 {stage}단계 원 연결이 비어 있습니다.", this);
                    continue;
                }
                rects.Add(node.Button);
                marks.Add(node.Highlight);
                targetColumns.Add(column);
                targetStages.Add(stage);
            }
        }
        for (int i = 0; i < secondaryNodes.Length && i < columns.Length; i++)
        {
            var node = secondaryNodes[i];
            if (node == null || node.Button == null || node.Highlight == null) continue;
            rects.Add(node.Button); marks.Add(node.Highlight); targetColumns.Add(columns[i]); targetStages.Add(4);
        }
        buttons = rects.ToArray();
        images = marks.ToArray();
        base.Awake();
        OnSelected += HandleSelected;
        OnCanceled += HandleCanceled;
        if (noticeText != null) noticeText.alpha = 0f;
        initialized = buttons.Length > 0;
    }

    private void OnDestroy()
    {
        OnSelected -= HandleSelected;
        OnCanceled -= HandleCanceled;
        Observe(null);
        noticeSequence?.Kill();
    }

    private void OnDisable() => CloseAllPages();

    #region UpgradeMenu Entry
    /// <summary>
    /// UpgradeMenu.OpenMenu가 자기 Open 뒤에 부른다. 이 페이지를 입력 스택 맨 위에 올린다. 열 수 없으면 false(UpgradeMenu가 메뉴를 닫는다).
    /// closeMenu: 이 페이지에서 X로 메뉴를 닫을 때, refreshCurrency: 재화(훈련 필증) 표시 갱신.
    /// </summary>
    public bool OpenFromMenu(Action onCloseMenu, Action onRefreshCurrency)
    {
        if (!initialized || !isActiveAndEnabled || UIManager.Instance == null) return false;
        closeMenu = onCloseMenu;
        refreshCurrency = onRefreshCurrency;
        CloseChildren();
        HideNotice();
        var ability = FindWeaponAbility();
        Observe(ability);
        Refresh();
        ShowPage(true);
        Open();
        FocusSilently(DefaultIndex(ability));
        return true;
    }

    /// <summary>
    /// UpgradeMenu가 닫힐 때(Close·OnDisable) 부른다. 하위 페이지와 이 페이지를 입력 스택에서 내린다.
    /// 보이는 화면은 그대로 두어 보드가 접히는 닫기 연출과 함께 사라지게 하고, 다음에 열 때 다시 맞춘다.
    /// </summary>
    public void CloseAllPages()
    {
        CloseChildren();
        HideNotice();
        Observe(null);
        Close();
    }

    // 하위 페이지(교체·상세)가 닫히며 이 페이지로 돌아올 때 부른다.
    public void ReturnFromChild(int focusIndex)
    {
        if (!IsActive) return;
        Refresh();
        refreshCurrency?.Invoke();
        ShowPage();
        FocusSilently(focusIndex);
    }

    // 상세 페이지에서 구매한 뒤 보드 위의 재화 표시를 맞춘다.
    public void RefreshCurrency() => refreshCurrency?.Invoke();
    #endregion

    protected override int FindDirectionalIndex(int from, Vector2 direction)
    {
        if (from < 0 || from >= targetColumns.Count || targetColumns[from] == null)
            return base.FindDirectionalIndex(from, direction);

        // 좌우 키는 같은 단계의 반대쪽 무장으로 이동한다. 가운데에 놓인 보조 무기는 아래 키로도 접근할 수 있다.
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            int slot = targetColumns[from].SetIndex;
            int stage = targetStages[from];
            for (int i = 0; i < targetColumns.Count; i++)
            {
                var column = targetColumns[i];
                if (column != null && (column.SetIndex - slot) * direction.x > 0f
                    && targetStages[i] == stage && IsNavigable(i)) return i;
            }
        }
        return base.FindDirectionalIndex(from, direction);
    }

    private void HandleSelected(int index)
    {
        if (!CanReceiveInput || index < 0 || index >= targetColumns.Count) return;
        var column = targetColumns[index];
        int stage = targetStages[index];
        if (column == null) return;

        var ability = FindWeaponAbility();
        if (stage == 4)
        {
            if (swapPage == null) return;
            HidePage();
            if (!swapPage.OpenSecondaryFor(this, ability, column.SetIndex, index)) { ShowPage(); FocusSilently(index); }
            return;
        }
        if (stage == 0)
        {
            if (swapPage == null) return;
            HidePage();
            if (!swapPage.OpenFor(this, ability, column.SetIndex, index))
            {
                ShowPage();
                FocusSilently(index);
                ShowNotice("바꿔 낄 수 있는 " + CategoryName(column.Category) + " 무장이 없습니다.", column.GetNode(0));
            }
            return;
        }

        WeaponBaseSO weapon = column.GetGearWeapon(stage);
        if (weapon == null)
        {
            ShowNotice("이 무장에는 Gear " + stage + "단계 기술이 없습니다.", column.GetNode(stage));
            return;
        }
        if (detailPage == null) return;
        HidePage();
        string armamentName = column.Armament != null ? column.Armament.DisplayName : CategoryName(column.Category);
        if (!detailPage.OpenFor(this, weapon, stage, armamentName, column.Category, index))
        {
            ShowPage();
            FocusSilently(index);
        }
    }

    private void HandleCanceled()
    {
        if (!CanReceiveInput) return;
        // UpgradeMenu의 기존 닫기 처리. 그 안에서 CloseAllPages가 다시 불려 이 페이지도 내려간다.
        if (closeMenu != null) closeMenu.Invoke();
        else CloseAllPages();
    }

    // 하위 페이지는 개요가 열리거나 메뉴가 닫힐 때 보이지 않는 상태이므로 바로 숨긴다(꺼지는 중인 오브젝트에 Tween을 남기지 않는다).
    private void CloseChildren()
    {
        if (swapPage != null) swapPage.ClosePage(true);
        if (detailPage != null) detailPage.ClosePage(true);
    }

    private void Refresh()
    {
        var ability = FindWeaponAbility();
        foreach (var column in columns)
            if (column != null) column.Refresh(ability);
        var catalog = ArmamentUnlockCatalogSO.Load();
        for (int i = 0; i < secondaryNodes.Length && i < columns.Length; i++)
        {
            if (secondaryNodes[i] == null || columns[i] == null) continue;
            var weapon = ability != null ? ability.GetWeaponSet(columns[i].SetIndex)?.secondary : null;
            // 빈 보조 무기 칸(해금 전, 2026-11-20 훈련 개편)도 그 상태를 그린다. 고르면 교체 페이지에서 해금·장착한다.
            // HUD와 같은 모습(회색 잠금 링 + 작은 자물쇠)으로 그린다(독립 검토: 원래 노드 재질은 dim = lit = HudSlotNormal이라 밝은 링 그대로였다).
            if (weapon != null) secondaryNodes[i].Show(weapon.weaponSprite, weapon.DisplayName, "보조 무기 · Z 교체", true);
            else secondaryNodes[i].ShowLockedEmpty("비어 있음", "보조 무기 · Z 교체",
                catalog != null ? catalog.lockedRingMaterial : null, catalog != null ? catalog.lockGlyph : null);
        }
    }

    private void Observe(PlayerWeaponAbility ability)
    {
        if (observed == ability) return;
        if (observed != null) observed.ArmamentChanged -= HandleArmamentChanged;
        observed = ability;
        if (observed != null) observed.ArmamentChanged += HandleArmamentChanged;
    }

    private void HandleArmamentChanged(int setIndex)
    {
        if (IsActive) Refresh();
    }

    // 처음 선택: 지금 들고 있는 자리의 장착 무장 원
    private int DefaultIndex(PlayerWeaponAbility ability)
    {
        int held = ability != null ? ability.CurrentSetIndex : 0;
        for (int i = 0; i < targetColumns.Count; i++)
            if (targetColumns[i] != null && targetColumns[i].SetIndex == held && targetStages[i] == 0) return i;
        return 0;
    }

    private void ShowNotice(string message, ArmamentTreeNode at)
    {
        if (at != null) PlayFeedbackAt(emptyStageFeedback, at.FeedbackPoint);
        if (noticeText == null) return;
        noticeSequence?.Kill();
        noticeText.text = message;
        noticeText.alpha = 1f;
        noticeSequence = DOTween.Sequence().SetUpdate(true)
            .AppendInterval(noticeDuration)
            .Append(noticeText.DOFade(0f, .3f));
    }

    private void HideNotice()
    {
        noticeSequence?.Kill();
        noticeSequence = null;
        if (noticeText != null) noticeText.alpha = 0f;
    }
}
