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
/// - 2026-11-20 훈련 개편(훈련 필증 해금): 해금하지 않은 무장·보조 무기 카드는 잠김 배지·잠금 부품을 보인다. 고르면 확인 창 → 필증 차감·해금 기록(저장 1번)
///   → 해금 연출(ILobbyStationUnlockActivity.UnlockPerformed로 장치의 해금 동작). 무장은 해금만 하고 이 페이지에 머물며(다음 Z로 장착),
///   보조 무기는 해금 뒤 그 자리에 바로 장착하고 개요로 돌아간다. 필증이 부족하면 확인 창 없이 경고한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentSwapPage : UpgradeMenuPage, ILobbyStationActivity, ILobbyStationUnlockActivity
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

    [Header("Unlock")]
    [Tooltip("해금 연출 시작(임시 연결: UnlockWeaponFeedback)")]
    [SerializeField] private FeedbackDataSO unlockFeedback;
    [Tooltip("아이콘이 끼워지는 체결 순간(임시 연결: UnlockCompleteFeedback)")]
    [SerializeField] private FeedbackDataSO completeFeedback;

    private readonly List<PlayerArmamentSO> shown = new List<PlayerArmamentSO>();
    private readonly List<WeaponBaseSO> secondaryShown = new();
    private bool secondaryMode;
    private int ShownCount => secondaryMode ? secondaryShown.Count : shown.Count;
    private ArmamentOverviewPage host;
    private PlayerWeaponAbility ability;
    private int setIndex;
    private int returnIndex;
    private Coroutine equipRoutine;
    // 장착·해금 연출이 진행 중인가. 연출이 첫 단계에서 끝나면(기다릴 것이 없을 때) StartCoroutine이 돌려준 끝난 코루틴을
    // equipRoutine에 남기지 않도록 이 값으로 판단한다(WeaponDetailPage.purchaseInProgress와 같은 방식).
    private bool sequenceRunning;
    private bool ownsInputBlock;
    private int unlockingCard = -1;   // 해금 연출 중인 카드(없으면 -1)
    // 해금 연출 중 체결 순간에 걸어 둔 구독. 코루틴을 StopCoroutine으로 끊으면 finally가 돌지 않으므로 중단 때 직접 푼다.
    private ArmamentLockOverlay engagedOverlay;
    private Action engagedHandler;

    /// <summary>무장 장착 연출이 시작되는 순간(장치가 일하는 순간).</summary>
    public event Action Performed;
    /// <summary>해금 연출이 시작되는 순간(거래는 이미 끝났다). 장치가 해금 동작을 재생한다.</summary>
    public event Action UnlockPerformed;

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
        secondaryMode = false;
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
    public bool OpenSecondaryFor(ArmamentOverviewPage owner, PlayerWeaponAbility weaponAbility, int slot, int focusBack)
    {
        if (weaponAbility == null || cards.Length == 0 || UIManager.Instance == null) return false;
        secondaryMode = true; secondaryShown.Clear();
        foreach (var candidate in weaponAbility.SecondaryWeapons)
            if (candidate != null && !secondaryShown.Contains(candidate)) secondaryShown.Add(candidate);
        if (secondaryShown.Count == 0 || secondaryShown.Count > cards.Length) return false;
        host = owner; ability = weaponAbility; setIndex = slot; returnIndex = focusBack;
        if (titleText != null) titleText.text = "보조 무기 교체";
        if (captionText != null) captionText.text = "무장 종류와 관계없이 이 자리의 보조 무기를 선택합니다.";
        RefreshCards(); LayoutCards(); ShowPage(); Open();
        // 처음 선택: 지금 장착한 카드(빈 자리면 첫 카드)
        FocusSilently(Mathf.Max(0, secondaryShown.IndexOf(weaponAbility.GetWeaponSet(slot)?.secondary)));
        return true;
    }

    public void ClosePage(bool instant)
    {
        StopEquipSequence();
        Close();
        if (pageGroup != null && (pageGroup.blocksRaycasts || pageGroup.alpha > 0f)) HidePage(instant);
        host = null;
        ability = null;
    }

    protected override bool IsNavigable(int index) => index < ShownCount && base.IsNavigable(index);

    private void RefreshCards()
    {
        if (secondaryMode)
        {
            var current = ability.GetWeaponSet(setIndex).secondary;
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].gameObject.SetActive(i < secondaryShown.Count);
                if (i < secondaryShown.Count)
                {
                    var weapon = secondaryShown[i];
                    cards[i].BindSecondary(weapon, current == weapon, !ArmamentUnlocks.IsWeaponUnlocked(weapon), PriceText(ArmamentUnlocks.PurchasableEntry(weapon)));
                }
            }
            if (equippedText != null) equippedText.text = "현재 보조 무기  " + (current != null ? current.DisplayName : "없음");
            return;
        }
        var equipped = ability != null ? ability.GetEquippedArmament(setIndex) : null;
        for (int i = 0; i < cards.Length; i++)
        {
            bool used = i < shown.Count;
            cards[i].gameObject.SetActive(used);
            if (used)
            {
                var armament = shown[i];
                cards[i].Bind(armament, armament == equipped, !ArmamentUnlocks.IsArmamentUnlocked(armament), PriceText(ArmamentUnlocks.PurchasableEntry(armament)));
            }
        }
        if (equippedText != null)
            equippedText.text = "현재 장착  " + (equipped != null ? equipped.DisplayName : "없음");
    }

    private void LayoutCards()
    {
        int count = ShownCount;
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
        if (!CanReceiveInput || ability == null || index < 0 || index >= ShownCount) return;
        // 해금하지 않은 카드: 장착 대신 훈련 필증 해금(확인 창 → 거래 → 연출)
        if (secondaryMode ? !ArmamentUnlocks.IsWeaponUnlocked(secondaryShown[index]) : !ArmamentUnlocks.IsArmamentUnlocked(shown[index]))
        {
            BeginUnlock(index);
            return;
        }
        if (secondaryMode)
        {
            if (!ability.TryEquipSecondaryWeapon(setIndex, secondaryShown[index])) { Warn("공격 중에는 보조 무기를 바꿀 수 없습니다.", index); return; }
            StartSequence(EquipSequence(index));
            return;
        }
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
        StartSequence(EquipSequence(index));
    }

    private void HandleCanceled()
    {
        if (CanReceiveInput) ReturnToOverview();
    }

    // 장착은 이미 끝났다. 바뀐 카드 표시와 짧은 연출을 보여 주는 동안만 입력을 막는다.
    // 예외가 나도 입력 차단이 남지 않게 바깥 finally에서 정리한다(남으면 메뉴를 닫는 입력도 받지 못한다).
    private IEnumerator EquipSequence(int index)
    {
        try
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
        }
        finally { EndSequence(false); }
        if (IsActive) ReturnToOverview();
    }

    // 연출 코루틴을 시작한다. 연출이 첫 단계에서 끝나면 끝난 코루틴을 equipRoutine에 남기지 않는다(남으면 다음 해금이 거부된다).
    // 게임 오브젝트가 꺼져 있어 시작하지 못하면 진행 표시를 바로 지운다.
    private void StartSequence(IEnumerator sequence)
    {
        sequenceRunning = true;
        Coroutine routine = gameObject.activeInHierarchy ? StartCoroutine(sequence) : null;
        if (routine == null && sequenceRunning) EndSequence(false);
        equipRoutine = sequenceRunning ? routine : null;
    }

    // 연출 상태 정리(정상 끝·예외 공통, 코루틴의 바깥 finally가 부른다). StopCoroutine으로 끊을 때는 finally가 돌지 않으므로 StopEquipSequence가 따로 정리한다.
    // aborted(예외로 끝남)이면 거래는 이미 끝났으므로 잠금 부품을 최종 자세로 두고 카드를 다시 그린다(그 단계의 예외는 기록만 한다).
    private void EndSequence(bool aborted)
    {
        DetachEngaged();
        int card = unlockingCard;
        unlockingCard = -1;
        equipRoutine = null;
        sequenceRunning = false;
        InputSuppressed = false;
        ReleaseInputBlock();
        if (!aborted) return;
        try
        {
            if (card >= 0 && card < cards.Length && cards[card] != null) cards[card].CompleteUnlock();
            if (ability != null && ability.GetWeaponSet(setIndex) != null) RefreshCards();
        }
        catch (Exception error) { Debug.LogException(error, this); }
    }

    private void StopEquipSequence()
    {
        var routine = equipRoutine;
        equipRoutine = null;
        sequenceRunning = false;
        if (routine != null) StopCoroutine(routine);
        // 해금 연출 도중에 끊기면(메뉴 닫힘 등) 거래는 이미 끝났으므로 잠금 부품을 최종 자세로 둔다(보조 무기는 장착하지 않는다).
        DetachEngaged();
        if (unlockingCard >= 0 && unlockingCard < cards.Length && cards[unlockingCard] != null) cards[unlockingCard].CompleteUnlock();
        unlockingCard = -1;
        InputSuppressed = false;
        ReleaseInputBlock();
    }

    #region Unlock
    // 잠긴 카드를 골랐다: 필증이 모자라면 확인 창 없이 경고, 아니면 구매 확인 창.
    private void BeginUnlock(int index)
    {
        var entry = UnlockEntry(index);
        if (entry == null) { Warn("해금 정보를 찾을 수 없습니다.", index, "무장 해금"); return; }
        var data = DataManager.Instance;
        if (data == null || data.TrainingCertificates < entry.price) { Warn(ArmamentUnlocks.ShortageMessage, index, "무장 해금"); return; }
        if (dialog == null)
        {
            Debug.LogError("업그레이드 구매 확인 창 연결이 필요합니다.", this);
            return;
        }
        ArmamentUnlockKind kind = entry.kind;
        string key = entry.Key;
        int price = entry.price;
        dialog.ShowConfirmation(entry.Title + " 해금", price, () => ConfirmUnlock(index, kind, key, price), RestoreFocus);
    }

    private ArmamentUnlockCatalogSO.Entry UnlockEntry(int index)
    {
        if (index < 0 || index >= ShownCount) return null;
        return secondaryMode ? ArmamentUnlocks.PurchasableEntry(secondaryShown[index]) : ArmamentUnlocks.PurchasableEntry(shown[index]);
    }

    // 확인 창 "예": 같은 프레임에 다시 확인하고 거래(필증 차감 + 해금 기록, 저장 1번)를 먼저 끝낸 뒤 연출한다.
    // 연출이 끊겨도 거래와 화면이 어긋나지 않는다(다시 열면 해금 상태로 그린다).
    private void ConfirmUnlock(int index, ArmamentUnlockKind kind, string key, int price)
    {
        if (!CanReceiveInput || sequenceRunning || ability == null) return;
        var entry = UnlockEntry(index);
        if (entry == null || entry.kind != kind || entry.Key != key || entry.price != price)
        {
            Warn("해금 정보가 변경되었습니다. 다시 확인해 주세요.", index, "무장 해금");
            return;
        }
        if (!ArmamentUnlocks.TryPurchase(entry, out string warning))
        {
            Warn(warning, index, "무장 해금");
            return;
        }
        if (host != null) host.RefreshCurrency();
        StartSequence(UnlockSequence(index));
    }

    // 해금 연출: 입력 차단 → 장치 해금 동작 → 시작 Feedback → 카드 잠금 부품이 풀림(체결 순간 완료 Feedback) → 카드 다시 그림.
    // 무장은 이 페이지에 머물고(다음 Z로 장착), 보조 무기는 입력 차단을 그대로 둔 채 그 자리에 장착하고 개요로 돌아간다.
    // UIManager.SetUIInputBlock은 횟수를 세지 않는 bool이라 중간에 풀었다 다시 걸지 않는다.
    // 시퀀스 전체를 바깥 try/finally로 감싸 예외가 나도 입력 차단·진행 표시가 남지 않게 한다(WeaponDetailPage.UnlockSequence와 같은 구조).
    // 페이지 이동(개요로 돌아가기·초점·경고)은 정리가 끝난 뒤에 한다(정리 전에 ClosePage가 불리면 이 코루틴을 스스로 멈추게 된다).
    private IEnumerator UnlockSequence(int index)
    {
        bool finished = false;
        bool equipped = false;
        try
        {
            var card = cards[index];
            InputSuppressed = true;
            if (UIManager.Instance != null)
            {
                ownsInputBlock = true;
                UIManager.Instance.SetUIInputBlock(true);
            }
            unlockingCard = index;
            RaiseUnlockPerformed();
            PlayFeedbackAt(unlockFeedback, card.FeedbackPoint);
            var overlay = card.LockOverlay;
            if (overlay != null)
            {
                DetachEngaged();
                engagedOverlay = overlay;
                engagedHandler = () => PlayFeedbackAt(completeFeedback, card.FeedbackPoint);
                overlay.Engaged += engagedHandler;
                try
                {
                    var routine = card.PlayUnlock(secondaryMode ? null : shown[index]);
                    while (routine.MoveNext()) yield return routine.Current;
                }
                finally { DetachEngaged(); }
            }
            else
            {
                // 잠금 부품이 없으면(적용 도구 실행 전) 연출 없이 Feedback 길이만큼 기다린다.
                float wait = unlockFeedback != null && FeedbackPlayer.Instance != null ? unlockFeedback.lifeTime : 0f;
                if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
                PlayFeedbackAt(completeFeedback, card.FeedbackPoint);
            }
            unlockingCard = -1;
            RefreshCards();

            if (secondaryMode)
            {
                equipped = ability != null && ability.TryEquipSecondaryWeapon(setIndex, secondaryShown[index]);
                RefreshCards();
                if (equipped)
                {
                    // 장착 연출만 보인다. 장치가 Upgrading을 한 번 더 돌지 않게 Performed는 내지 않는다.
                    cards[index].PlayEquipPulse();
                    PlayFeedbackAt(equipFeedback, cards[index].FeedbackPoint);
                    if (returnDelay > 0f) yield return new WaitForSecondsRealtime(returnDelay);
                }
            }
            finished = true;
        }
        finally { EndSequence(!finished); }

        if (!IsActive) yield break;
        if (!secondaryMode) FocusSilently(index);
        else if (equipped) ReturnToOverview();
        else
        {
            FocusSilently(index);
            Warn("해금했지만 지금은 장착하지 못했습니다. 다시 선택해 주세요.", index, "무장 해금");
        }
    }

    private void DetachEngaged()
    {
        if (engagedOverlay != null && engagedHandler != null) engagedOverlay.Engaged -= engagedHandler;
        engagedOverlay = null;
        engagedHandler = null;
    }

    private static string PriceText(ArmamentUnlockCatalogSO.Entry entry) => entry != null ? ArmamentUnlocks.PriceLabel(entry.price) : null;

    private void RaiseUnlockPerformed()
    {
        try { UnlockPerformed?.Invoke(); }
        catch (Exception error) { Debug.LogException(error, this); }
    }
    #endregion

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

    private void Warn(string message, int index, string title = "무장 교체")
    {
        if (index >= 0 && index < cards.Length) PlayFeedbackAt(failFeedback, cards[index].FeedbackPoint);
        if (dialog == null || !dialog.ShowWarning(message, RestoreFocus, title))
            Debug.LogWarning(message, this);
    }

    private void RestoreFocus()
    {
        if (IsActive) FocusSilently(currentIndex);
    }
}
