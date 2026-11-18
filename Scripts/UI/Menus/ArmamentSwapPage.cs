using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 무장 교체 페이지(2026-11-18). 개요 페이지의 큰 원(장착 무장)에서 열린다.
/// 사용자 지시: "근접무기와 원거리 무기끼리만 교체 가능하도록" → 그 자리와 같은 분류의 무장만 카드로 보여 준다(목록과 개수는 Player_Weapon_Data.armaments에서 읽는다).
/// - 확인하면 확인 창 없이 바로 장착한다(교체는 무료이고 언제든 되돌릴 수 있다). 성공하면 짧은 연출 뒤 개요로 돌아간다.
/// - 이미 장착한 카드를 고르면 그대로 개요로 돌아간다. 장착할 수 없으면(준비 중·공격 중 등) 경고 창을 띄운다.
/// - 카드는 Prefab에 최대 개수만큼 미리 두고, 남는 카드는 숨긴다. 보이는 카드는 개수에 맞춰 한 줄에 columnsPerRow개씩 가운데 정렬한다.
/// - 장착 연출을 시작할 때 ILobbyStationActivity.Performed를 낸다. 업그레이드 장치(LobbyStation)가 이를 받아 작업 동작을 재생한다(2026-11-18 2차).
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentSwapPage : UpgradeMenuPage, ILobbyStationActivity
{
    [Header("Cards")]
    [SerializeField] private ArmamentSwapCard[] cards = new ArmamentSwapCard[0];
    [SerializeField] private Vector2 cardSize = new Vector2(660f, 330f);
    [SerializeField] private Vector2 cardGap = new Vector2(30f, 15f);
    [Min(1)] [SerializeField] private int columnsPerRow = 2;
    [Tooltip("카드 영역의 가운데(페이지 좌표). 줄 수에 맞춰 이 점을 중심으로 쌓는다")]
    [SerializeField] private Vector2 cardAreaCenter = new Vector2(0f, -48f);

    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI captionText;
    [SerializeField] private TextMeshProUGUI equippedText;

    [Header("Equip")]
    [Tooltip("경고 창(업그레이드 구매 확인 창의 경고 모드를 함께 쓴다)")]
    [SerializeField] private UpgradePurchaseDialog dialog;
    [SerializeField] private FeedbackDataSO equipFeedback;
    [SerializeField] private FeedbackDataSO failFeedback;
    [Tooltip("장착 연출을 보여 준 뒤 개요로 돌아가기까지의 시간(실제 시간)")]
    [SerializeField, Min(0f)] private float returnDelay = .45f;

    private readonly List<PlayerArmamentSO> shown = new List<PlayerArmamentSO>();
    private ArmamentOverviewPage host;
    private PlayerWeaponAbility ability;
    private int setIndex;
    private int returnIndex;
    private Coroutine equipRoutine;
    private bool ownsInputBlock;

    /// <summary>무장 장착 연출이 시작되는 순간(장치가 일하는 순간).</summary>
    public event Action Performed;

    protected override void Awake()
    {
        var rects = new List<RectTransform>();
        var marks = new List<UnityEngine.UI.Image>();
        var valid = new List<ArmamentSwapCard>();
        foreach (var card in cards)
        {
            if (card == null || card.HitArea == null || card.Highlight == null)
            {
                Debug.LogError($"[{name}] 교체 카드 연결이 비어 있습니다.", this);
                continue;
            }
            rects.Add(card.HitArea);
            marks.Add(card.Highlight);
            valid.Add(card);
        }
        cards = valid.ToArray();
        buttons = rects.ToArray();
        images = marks.ToArray();
        base.Awake();
        OnSelected += HandleSelected;
        OnCanceled += HandleCanceled;
    }

    private void OnDestroy()
    {
        OnSelected -= HandleSelected;
        OnCanceled -= HandleCanceled;
    }

    private void OnDisable() => ClosePage(true);

    /// <summary>개요 페이지가 부른다. 그 자리와 같은 분류의 무장이 하나도 없으면 열지 않고 false.</summary>
    public bool OpenFor(ArmamentOverviewPage owner, PlayerWeaponAbility weaponAbility, int slot, int focusBack)
    {
        if (cards.Length == 0 || weaponAbility == null || weaponAbility.GetWeaponSet(slot) == null || UIManager.Instance == null) return false;
        ArmamentCategory category = CategoryOf(weaponAbility, slot);
        shown.Clear();
        foreach (var armament in weaponAbility.Armaments)
            if (armament != null && armament.category == category && !shown.Contains(armament)) shown.Add(armament);
        if (shown.Count == 0) return false;
        if (shown.Count > cards.Length)
        {
            Debug.LogWarning($"[{name}] {CategoryName(category)} 무장이 {shown.Count}개라 카드 {cards.Length}장에 앞의 것만 보입니다. 카드를 늘려 주세요.", this);
            shown.RemoveRange(cards.Length, shown.Count - cards.Length);
        }

        host = owner;
        ability = weaponAbility;
        setIndex = slot;
        returnIndex = focusBack;
        if (titleText != null) titleText.text = CategoryName(category) + " 무장 교체";
        if (captionText != null) captionText.text = CategoryName(category) + " 자리에는 " + CategoryName(category) + " 무장만 장착할 수 있습니다. 바꾼 무장은 바로 적용됩니다.";
        RefreshCards();
        LayoutCards();

        ShowPage();
        Open();
        int equipped = shown.IndexOf(ability.GetEquippedArmament(setIndex));
        FocusSilently(equipped >= 0 ? equipped : 0);
        return true;
    }

    /// <summary>입력 스택에서 내리고 숨긴다. 진행 중인 장착 연출과 입력 차단도 정리한다.</summary>
    public void ClosePage(bool instant)
    {
        StopEquipSequence();
        Close();
        if (pageGroup != null && (pageGroup.blocksRaycasts || pageGroup.alpha > 0f)) HidePage(instant);
        host = null;
        ability = null;
    }

    protected override bool IsNavigable(int index) => index < shown.Count && base.IsNavigable(index);

    private void RefreshCards()
    {
        var equipped = ability != null ? ability.GetEquippedArmament(setIndex) : null;
        for (int i = 0; i < cards.Length; i++)
        {
            bool used = i < shown.Count;
            cards[i].gameObject.SetActive(used);
            if (used) cards[i].Bind(shown[i], shown[i] == equipped);
        }
        if (equippedText != null)
            equippedText.text = "현재 장착  " + (equipped != null ? equipped.DisplayName : "없음");
    }

    private void LayoutCards()
    {
        int count = shown.Count;
        int perRow = Mathf.Max(1, columnsPerRow);
        int rows = Mathf.CeilToInt(count / (float)perRow);
        float pitchY = cardSize.y + cardGap.y;
        float pitchX = cardSize.x + cardGap.x;
        float topY = cardAreaCenter.y + (rows - 1) * pitchY * .5f;
        for (int i = 0; i < count; i++)
        {
            int row = i / perRow;
            int inRow = Mathf.Min(perRow, count - row * perRow);
            int col = i % perRow;
            float x = cardAreaCenter.x + (col - (inRow - 1) * .5f) * pitchX;
            var rect = (RectTransform)cards[i].transform;
            rect.anchoredPosition = new Vector2(x, topY - row * pitchY);
        }
    }

    private void HandleSelected(int index)
    {
        if (!CanReceiveInput || ability == null || index < 0 || index >= shown.Count) return;
        PlayerArmamentSO armament = shown[index];
        if (ability.GetEquippedArmament(setIndex) == armament) { ReturnToOverview(); return; }
        if (armament.normal == null) { Warn("아직 준비 중인 무장입니다.", index); return; }
        if (!ability.CanEquipArmament(setIndex, armament))
        {
            Warn("공격하거나 기술을 저장하는 동안에는 무장을 바꿀 수 없습니다.", index);
            return;
        }
        if (!ability.TryEquipArmament(setIndex, armament))
        {
            Warn("무장을 바꾸지 못했습니다. 다시 시도해 주세요.", index);
            return;
        }
        equipRoutine = StartCoroutine(EquipSequence(index));
    }

    private void HandleCanceled()
    {
        if (CanReceiveInput) ReturnToOverview();
    }

    // 장착은 이미 끝났다. 바뀐 카드 표시와 짧은 연출을 보여 주는 동안만 입력을 막는다.
    private IEnumerator EquipSequence(int index)
    {
        InputSuppressed = true;
        if (UIManager.Instance != null)
        {
            ownsInputBlock = true;
            UIManager.Instance.SetUIInputBlock(true);
        }
        RaisePerformed();
        RefreshCards();
        cards[index].PlayEquipPulse();
        PlayFeedbackAt(equipFeedback, cards[index].FeedbackPoint);
        if (returnDelay > 0f) yield return new WaitForSecondsRealtime(returnDelay);
        equipRoutine = null;
        InputSuppressed = false;
        ReleaseInputBlock();
        if (IsActive) ReturnToOverview();
    }

    private void StopEquipSequence()
    {
        var routine = equipRoutine;
        equipRoutine = null;
        if (routine != null) StopCoroutine(routine);
        InputSuppressed = false;
        ReleaseInputBlock();
    }

    // 구독자(장치) 쪽 예외가 장착 연출과 입력 차단 해제를 막지 않게 한다.
    private void RaisePerformed()
    {
        try { Performed?.Invoke(); }
        catch (Exception error) { Debug.LogException(error, this); }
    }

    private void ReleaseInputBlock()
    {
        if (!ownsInputBlock) return;
        ownsInputBlock = false;
        if (UIManager.Instance != null) UIManager.Instance.SetUIInputBlock(false);
    }

    private void ReturnToOverview()
    {
        var owner = host;
        int focus = returnIndex;
        ClosePage(false);
        if (owner != null) owner.ReturnFromChild(focus);
    }

    private void Warn(string message, int index)
    {
        if (index >= 0 && index < cards.Length) PlayFeedbackAt(failFeedback, cards[index].FeedbackPoint);
        if (dialog == null || !dialog.ShowWarning(message, RestoreFocus, "무장 교체"))
            Debug.LogWarning(message, this);
    }

    private void RestoreFocus()
    {
        if (IsActive) FocusSilently(currentIndex);
    }
}
