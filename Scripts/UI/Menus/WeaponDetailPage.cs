using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 상세 업그레이드 페이지(2026-11-18). 개요 페이지의 Gear 원에서 열려 그 무기 하나의 업그레이드(해금 · A · B)를 보여 주고 구매한다.
/// 구매 흐름: 조건 확인 → 구매 확인 창(UpgradePurchaseDialog) → 연출 → 다시 확인 → 거래(재화 차감 후 기록).
/// 업그레이드 데이터는 무기 SO의 unlockInfo / leftNodeInfo(A) / rightNodeInfo(B)이고, 기록 키는 weaponName이다.
/// 2026-11-18 2차: 옛 8칸 강화 격자를 지우고 구매는 이 페이지에서만 한다. 무기 원은 PlayerHUD 기어 모양(해금 전 = 단계 색 어두운 재질,
/// 해금 뒤 = 밝은 재질)이고, 'GEAR' 글자와 큰 단계 숫자로 몇 번째 Gear 기술인지 보인다.
/// 구매 연출을 시작할 때 ILobbyStationActivity.Performed를 내어 업그레이드 장치(LobbyStation)가 작업 동작을 재생하게 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponDetailPage : UpgradeMenuPage, ILobbyStationActivity
{
    private const int UnlockNode = 0, LeftNode = 1, RightNode = 2;

    [Header("Weapon")]
    [SerializeField] private Image iconImage;
    [Tooltip("무기 아이콘 뒤의 HUD 기어 모양 원(GlowMultiply 재질)")]
    [SerializeField] private Image iconRing;
    [SerializeField] private TextMeshProUGUI weaponNameText;
    [SerializeField] private TextMeshProUGUI armamentText;
    [Tooltip("큰 단계 숫자")]
    [SerializeField] private TextMeshProUGUI stageText;
    [Tooltip("큰 단계 숫자 위의 'GEAR' 글자")]
    [SerializeField] private TextMeshProUGUI stageMarkCaption;
    [SerializeField] private TextMeshProUGUI stageCaptionText;
    [SerializeField] private TextMeshProUGUI gaugeValueText;
    [SerializeField] private TextMeshProUGUI cooldownValueText;
    [SerializeField] private TextMeshProUGUI stateValueText;
    [Tooltip("Gear 1~3단계 숫자 색(HUD의 Gear 단계 색과 같게)")]
    [SerializeField] private Color[] stageColors = { new Color(.392f, .991f, .991f, 1f), new Color(1f, .196f, 0f, 1f), new Color(1f, .392f, 1f, 1f) };
    [SerializeField, Range(0f, 1f)] private float stageCaptionAlpha = .85f;
    [Tooltip("[0~2] Gear 1~3 단계 색 밝은 원 재질(해금 뒤)")]
    [SerializeField] private Material[] stageMaterials = new Material[3];
    [Tooltip("[0~2] Gear 1~3 단계 색 어두운 원 재질(해금 전)")]
    [SerializeField] private Material[] stageDimMaterials = new Material[3];

    [Header("Modules")]
    [Tooltip("[0] 무기 해금, [1] A 모듈(왼쪽 노드), [2] B 모듈(오른쪽 노드)")]
    [SerializeField] private WeaponModuleRow[] rows = new WeaponModuleRow[3];

    [Header("Purchase")]
    [SerializeField] private UpgradePurchaseDialog purchaseDialog;
    [SerializeField] private FeedbackDataSO unlockFeedback;
    [SerializeField] private FeedbackDataSO upgradeFeedback;
    [SerializeField] private FeedbackDataSO completeFeedback;
    [SerializeField] private FeedbackDataSO failFeedback;

    [Header("Colors")]
    [SerializeField] private Color activeColor = new Color(.455f, .812f, 1f, 1f);     // 구매 완료(기존 슬롯 #74CFFF)
    [SerializeField] private Color inactiveColor = new Color(.757f, .482f, .439f, 1f); // 미구매·재화 부족(기존 슬롯 #C17B70)
    [SerializeField] private Color readyColor = new Color(.659f, .906f, 1f, 1f);
    [SerializeField] private Color mutedColor = new Color(.51f, .62f, .71f, 1f);
    [SerializeField] private Color costColor = new Color(.914f, .71f, .42f, 1f);

    private readonly int[] rowNodes = new int[3];   // 선택 순서 → 노드 종류
    private ArmamentOverviewPage host;
    private WeaponBaseSO weapon;
    private int stage;
    private int returnIndex;
    private Coroutine purchaseRoutine;
    private bool purchaseInProgress;
    private bool ownsInputBlock;

    /// <summary>구매 연출이 시작되는 순간(장치가 일하는 순간). 거래는 연출 뒤에 확정된다.</summary>
    public event Action Performed;

    protected override void Awake()
    {
        var rects = new List<RectTransform>();
        var marks = new List<Image>();
        int count = 0;
        for (int i = 0; i < rows.Length && count < rowNodes.Length; i++)
        {
            var row = rows[i];
            if (row == null || row.HitArea == null || row.Highlight == null)
            {
                Debug.LogError($"[{name}] 구매 줄 {i}의 연결이 비어 있습니다.", this);
                continue;
            }
            rects.Add(row.HitArea);
            marks.Add(row.Highlight);
            rowNodes[count++] = i;
        }
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

    /// <summary>개요 페이지가 부른다. gearStage는 Gear 단계(1~3), armamentName·category는 머리글 표시용이다.</summary>
    public bool OpenFor(ArmamentOverviewPage owner, WeaponBaseSO target, int gearStage, string armamentName, ArmamentCategory category, int focusBack)
    {
        if (target == null || buttons.Length == 0 || UIManager.Instance == null) return false;
        host = owner;
        weapon = target;
        stage = gearStage;
        returnIndex = focusBack;

        if (iconImage != null)
        {
            iconImage.sprite = weapon.weaponSprite;
            iconImage.enabled = iconImage.sprite != null;
        }
        if (weaponNameText != null) weaponNameText.text = weapon.DisplayName;
        if (armamentText != null) armamentText.text = armamentName + " 무장  ·  " + CategoryName(category);
        Color color = StageColor();
        if (stageText != null)
        {
            stageText.text = stage.ToString(CultureInfo.InvariantCulture);
            stageText.color = color;
        }
        if (stageMarkCaption != null)
        {
            color.a *= stageCaptionAlpha;
            stageMarkCaption.color = color;
        }
        if (stageCaptionText != null) stageCaptionText.text = stage + "단계 기술";
        if (gaugeValueText != null) gaugeValueText.text = weapon.skillGauge.ToString("0.#", CultureInfo.InvariantCulture);
        if (cooldownValueText != null) cooldownValueText.text = weapon.cooldown.ToString("0.##", CultureInfo.InvariantCulture) + "초";
        Refresh();

        ShowPage();
        Open();
        FocusSilently(DefaultIndex());
        return true;
    }

    /// <summary>입력 스택에서 내리고 숨긴다. 구매 확인 창이나 진행 중인 구매 연출도 함께 정리한다.</summary>
    public void ClosePage(bool instant)
    {
        bool wasActive = IsActive;
        CancelPurchaseSequence();
        if (wasActive && purchaseDialog != null && purchaseDialog.IsActive) purchaseDialog.Close();
        Close();
        if (pageGroup != null && (pageGroup.blocksRaycasts || pageGroup.alpha > 0f)) HidePage(instant);
        host = null;
        weapon = null;
    }

    private void HandleCanceled()
    {
        if (!CanReceiveInput) return;
        var owner = host;
        int focus = returnIndex;
        ClosePage(false);
        if (owner != null) owner.ReturnFromChild(focus);
    }

    #region Purchase
    private void HandleSelected(int index)
    {
        if (!CanReceiveInput || purchaseInProgress || index < 0 || index >= buttons.Length) return;
        int nodeType = rowNodes[index];
        if (!TryResolvePurchase(nodeType, out string purchaseName, out int price, out string warning))
        {
            ShowPurchaseWarning(warning, nodeType);
            return;
        }
        if (purchaseDialog == null)
        {
            Debug.LogError("업그레이드 구매 확인 창 연결이 필요합니다.", this);
            return;
        }
        string weaponId = weapon.weaponName;
        purchaseDialog.ShowConfirmation(purchaseName, price, () => ConfirmPurchase(nodeType, weaponId, price), RestoreFocus);
    }

    private bool TryResolvePurchase(int nodeType, out string purchaseName, out int price, out string warning)
    {
        purchaseName = string.Empty;
        price = 0;
        warning = "구매 정보를 확인할 수 없습니다.";
        if (weapon == null || string.IsNullOrWhiteSpace(weapon.weaponName) || nodeType < UnlockNode || nodeType > RightNode) return false;
        var node = Node(nodeType);
        price = node.upgradeCost;
        if (price < 0) return false;
        purchaseName = weapon.DisplayName + " · " + NodeLabel(nodeType, node);
        var data = DataManager.Instance;
        string id = weapon.weaponName;
        bool unlocked = data.IsWeaponUnlocked(id);
        if (nodeType == UnlockNode ? unlocked : data.IsWeaponNodeUpgraded(id, nodeType == RightNode))
        { warning = "이미 구매한 업그레이드입니다"; return false; }
        if (nodeType != UnlockNode && !unlocked)
        { warning = "먼저 무기를 해금해 주세요."; return false; }
        if (data.CurrentGold < price)
        { warning = "재화가 부족합니다"; return false; }
        warning = null;
        return true;
    }

    private bool ValidateConfirmedPurchase(int nodeType, string weaponId, int quotedPrice, out string warning)
    {
        if (!TryResolvePurchase(nodeType, out _, out int currentPrice, out warning)) return false;
        if (weapon.weaponName != weaponId || currentPrice != quotedPrice)
        { warning = "구매 정보가 변경되었습니다. 다시 확인해 주세요."; return false; }
        return true;
    }

    private void ConfirmPurchase(int nodeType, string weaponId, int quotedPrice)
    {
        if (!CanReceiveInput) return;
        if (!ValidateConfirmedPurchase(nodeType, weaponId, quotedPrice, out string warning))
        { ShowPurchaseWarning(warning, nodeType); return; }
        purchaseInProgress = true;
        var routine = StartCoroutine(PurchaseSequence(nodeType, weaponId, quotedPrice));
        purchaseRoutine = purchaseInProgress ? routine : null;
    }

    private IEnumerator PurchaseSequence(int nodeType, string weaponId, int quotedPrice)
    {
        string warning = null;
        try
        {
            if (!ValidateConfirmedPurchase(nodeType, weaponId, quotedPrice, out warning)) yield break;
            ownsInputBlock = true;
            UIManager.Instance.SetUIInputBlock(true);
            RaisePerformed();
            Transform at = nodeType < rows.Length && rows[nodeType] != null ? rows[nodeType].FeedbackPoint : transform;
            FeedbackDataSO feedback = nodeType == UnlockNode ? unlockFeedback : upgradeFeedback;
            PlayFeedbackAt(feedback, at);
            float wait = feedback != null && FeedbackPlayer.Instance != null ? feedback.lifeTime : 0f;
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            PlayFeedbackAt(completeFeedback, at);

            // 연출 대기 중 가격·상태·재화가 바뀌었으면 다시 확인하게 하고 기존 거래는 수행하지 않는다.
            if (!ValidateConfirmedPurchase(nodeType, weaponId, quotedPrice, out warning)) yield break;
            Commit(nodeType);
            Refresh();
            if (host != null) host.RefreshGold();
            FocusSilently(DefaultIndex());
        }
        finally
        {
            purchaseRoutine = null;
            purchaseInProgress = false;
            ReleasePurchaseInputBlock();
            if (!string.IsNullOrEmpty(warning) && UIManager.Instance != null && UIManager.Instance.IsCurrentInputTarget(this))
                ShowPurchaseWarning(warning, nodeType);
        }
    }

    // 거래: 재화를 먼저 쓰고, 성공하면 기록을 남긴다(각각 저장된다).
    // (옛 강화 격자 WeaponUpgradeSlot.TryUnlockWeapon / TryUpgradeWeapon과 같은 순서)
    private void Commit(int nodeType)
    {
        var data = DataManager.Instance;
        string id = weapon.weaponName;
        if (!data.TrySpendGold(Node(nodeType).upgradeCost)) return;
        if (nodeType == UnlockNode) data.SetWeaponUnlockState(id, true);
        else data.SetWeaponNodeUpgradeState(id, nodeType == RightNode, true);
    }

    private void ShowPurchaseWarning(string message, int nodeType)
    {
        if (!IsActive || !isActiveAndEnabled || string.IsNullOrEmpty(message)) return;
        if (nodeType >= 0 && nodeType < rows.Length && rows[nodeType] != null) PlayFeedbackAt(failFeedback, rows[nodeType].FeedbackPoint);
        if (purchaseDialog != null) purchaseDialog.ShowWarning(message, RestoreFocus);
        else Debug.LogError("업그레이드 구매 확인 창 연결이 필요합니다: " + message, this);
    }

    private void RestoreFocus()
    {
        // 스택에 이 페이지를 다시 밀어 넣지 않는다. 위에 열린 Pause나 System 입력은 그대로 둔다.
        if (IsActive) FocusSilently(currentIndex);
    }

    // 구독자(장치) 쪽 예외가 구매 연출과 입력 차단 해제를 막지 않게 한다.
    private void RaisePerformed()
    {
        try { Performed?.Invoke(); }
        catch (Exception error) { Debug.LogException(error, this); }
    }

    private void ReleasePurchaseInputBlock()
    {
        if (!ownsInputBlock) return;
        ownsInputBlock = false;
        if (UIManager.Instance != null) UIManager.Instance.SetUIInputBlock(false);
    }

    private void CancelPurchaseSequence()
    {
        var routine = purchaseRoutine;
        purchaseRoutine = null;
        if (routine != null) StopCoroutine(routine);
        purchaseInProgress = false;
        ReleasePurchaseInputBlock();
    }
    #endregion

    #region Display
    private void Refresh()
    {
        if (weapon == null) return;
        var data = DataManager.Instance;
        string id = weapon.weaponName;
        bool hasId = !string.IsNullOrWhiteSpace(id);
        bool unlocked = hasId && data.IsWeaponUnlocked(id);
        int gold = data.CurrentGold;
        if (iconRing != null)
        {
            Material ring = StageMaterial(unlocked ? stageMaterials : stageDimMaterials);
            if (ring != null && iconRing.material != ring) iconRing.material = ring;
        }
        if (stateValueText != null)
        {
            stateValueText.text = UpgradeSummary(weapon);
            stateValueText.color = unlocked ? readyColor : mutedColor;
        }
        for (int nodeType = UnlockNode; nodeType <= RightNode; nodeType++)
        {
            var row = nodeType < rows.Length ? rows[nodeType] : null;
            if (row == null) continue;
            var node = Node(nodeType);
            bool purchased = hasId && (nodeType == UnlockNode ? unlocked : data.IsWeaponNodeUpgraded(id, nodeType == RightNode));
            string status;
            Color statusColor;
            if (purchased) { status = nodeType == UnlockNode ? "해금 완료" : "강화 완료"; statusColor = activeColor; }
            else if (nodeType != UnlockNode && !unlocked) { status = "해금 필요"; statusColor = mutedColor; }
            else if (gold < node.upgradeCost) { status = "재화 부족"; statusColor = inactiveColor; }
            else { status = "구매 가능"; statusColor = readyColor; }

            string description = !string.IsNullOrWhiteSpace(node.description) ? node.description
                : nodeType == UnlockNode ? "이 무기를 해금합니다. 해금한 뒤 A, B 모듈을 각각 강화할 수 있습니다."
                : "아직 정해진 강화 효과가 없습니다.";
            Color descriptionColor = string.IsNullOrWhiteSpace(node.description) && nodeType != UnlockNode ? mutedColor : readyColor;
            row.Show(Title(nodeType, node), EvenLines(description), descriptionColor,
                "x" + node.upgradeCost.ToString(CultureInfo.InvariantCulture), purchased ? mutedColor : costColor,
                status, statusColor, purchased ? activeColor : inactiveColor, purchased);
        }
    }

    // 해금 전이면 해금 줄, 해금 뒤에는 아직 사지 않은 첫 모듈(모두 샀으면 A)
    private int DefaultIndex()
    {
        if (weapon == null || string.IsNullOrWhiteSpace(weapon.weaponName)) return 0;
        var data = DataManager.Instance;
        string id = weapon.weaponName;
        int want = !data.IsWeaponUnlocked(id) ? UnlockNode
            : !data.IsWeaponNodeUpgraded(id, false) ? LeftNode
            : !data.IsWeaponNodeUpgraded(id, true) ? RightNode : LeftNode;
        for (int i = 0; i < buttons.Length; i++) if (rowNodes[i] == want) return i;
        return 0;
    }

    private Color StageColor() => stage >= 1 && stage <= stageColors.Length ? stageColors[stage - 1] : readyColor;

    private Material StageMaterial(Material[] materials) =>
        materials != null && stage >= 1 && stage <= materials.Length ? materials[stage - 1] : null;

    private WeaponBaseSO.UpgradeNodeInfo Node(int nodeType) =>
        nodeType == UnlockNode ? weapon.unlockInfo : nodeType == LeftNode ? weapon.leftNodeInfo : weapon.rightNodeInfo;

    private static string NodeLabel(int nodeType, WeaponBaseSO.UpgradeNodeInfo node)
    {
        if (nodeType == UnlockNode) return "해금";
        string module = nodeType == LeftNode ? "A 모듈" : "B 모듈";
        return string.IsNullOrWhiteSpace(node.nodeName) ? module : module + " " + node.nodeName;
    }

    private static string Title(int nodeType, WeaponBaseSO.UpgradeNodeInfo node)
    {
        if (nodeType == UnlockNode) return "무기 해금";
        string module = nodeType == LeftNode ? "A 모듈" : "B 모듈";
        return string.IsNullOrWhiteSpace(node.nodeName) ? module : module + "  ·  " + node.nodeName;
    }
    #endregion
}
