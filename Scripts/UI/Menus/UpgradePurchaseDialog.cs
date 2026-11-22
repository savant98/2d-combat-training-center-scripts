using System;
using TMPro;
using UnityEngine;

// 업그레이드 메뉴의 구매 확인과 실패 안내만 담당한다. 가격 계산·차감·저장은 소유 메뉴가 수행한다.
[DisallowMultipleComponent]
public sealed class UpgradePurchaseDialog : SelectableMenuUI
{
    [Header("Dialog")]
    [SerializeField] private GameObject rootObject;
    [SerializeField] private CanvasGroup dialogCanvas;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI contextText;
    [SerializeField] private TextMeshProUGUI certificateOwned;
    [SerializeField] private TextMeshProUGUI certificateCost;
    [SerializeField] private TextMeshProUGUI certificateResult;

    [Header("Choices")]
    [SerializeField] private RectTransform confirmButton;
    [SerializeField] private RectTransform cancelButton;
    [SerializeField] private UnityEngine.UI.Image confirmHighlight;
    [SerializeField] private UnityEngine.UI.Image cancelHighlight;
    [SerializeField] private TextMeshProUGUI confirmLabel;
    [SerializeField] private TextMeshProUGUI cancelLabel;

    [Header("Status")]
    [SerializeField] private UnityEngine.UI.Graphic[] accentGraphics = Array.Empty<UnityEngine.UI.Graphic>();
    [SerializeField] private Color normalTint = new Color(.2f, .85f, 1f, 1f);
    [SerializeField] private Color warningTint = new Color(1f, .18f, .2f, 1f);

    private Action accepted;
    private Action canceled;
    private bool warningOnly;
    private bool initialized;
    private Vector2 confirmHome;
    private Vector2 cancelHome;

    protected override void Awake()
    {
        if (rootObject == null || dialogCanvas == null || titleText == null || messageText == null || priceText == null
            || confirmButton == null || cancelButton == null || confirmHighlight == null || cancelHighlight == null
            || confirmLabel == null || cancelLabel == null)
        {
            Debug.LogError("업그레이드 확인 창의 표시·선택 참조가 필요합니다.", this);
            return;
        }
        InputBindingHint.Bind(rootObject.transform, "Window/InputHint", "{MenuDirections} 이동     {Confirm} 확인     {Cancel} 취소");
        InputBindingHint.Bind(rootObject.transform, "Window/Close/Label", "{Cancel}  닫기");
        confirmHome = confirmButton.anchoredPosition;
        cancelHome = cancelButton.anchoredPosition;
        ConfigureChoices(false);
        base.Awake();
        OnSelected += HandleSelected;
        OnCanceled += HandleCanceled;
        initialized = true;
        HideVisual();
    }

    public bool ShowConfirmation(string purchaseName, int price, Action onAccepted, Action onCanceled)
    {
        if (!CanShow()) return false;
        if (price < 0) return ShowWarning("구매 비용을 확인할 수 없습니다.", onCanceled);
        int owned = DataManager.Instance.TrainingCertificates;
        if (owned < price) return ShowWarning(CertificateShortage(owned, price), onCanceled);
        accepted = onAccepted;
        canceled = onCanceled;
        warningOnly = false;
        ConfigureChoices(false);
        titleText.text = "업그레이드 구매 확인";
        messageText.text = purchaseName;
        messageText.color = new Color32(211, 235, 242, 255);
        if (contextText != null)
        {
            contextText.text = TrainingCatalogSO.CertificateName;
            contextText.gameObject.SetActive(true);
        }
        UpdateQuote(owned, price);
        ApplyCertificateIcon();
        priceText.gameObject.SetActive(true);
        SetAccents(normalTint);
        ShowVisual();
        base.Open();
        return true;
    }

    public bool ShowWarning(string message, Action onClosed = null, string title = "업그레이드 안내")
    {
        if (!CanShow()) return false;
        accepted = null;
        canceled = onClosed;
        warningOnly = true;
        ConfigureChoices(true);
        titleText.text = title;
        messageText.text = message;
        messageText.color = warningTint;
        if (contextText != null) contextText.gameObject.SetActive(false);
        priceText.gameObject.SetActive(false);
        SetAccents(warningTint);
        ShowVisual();
        base.Open();
        return true;
    }

    private bool CanShow() => initialized && isActiveAndEnabled && !IsActive && UIManager.Instance != null;
    private bool CanReceiveInput => IsActive && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
        && UIManager.Instance.IsCurrentInputTarget(this);

    private void ConfigureChoices(bool warning)
    {
        confirmButton.anchoredPosition = warning ? new Vector2(0f, confirmHome.y) : confirmHome;
        cancelButton.anchoredPosition = cancelHome;
        confirmButton.localScale = cancelButton.localScale = Vector3.one;
        cancelButton.gameObject.SetActive(!warning);
        confirmLabel.text = warning ? "확인" : "구매";
        cancelLabel.text = "취소";
        // 구매는 취소를 기본 선택으로 두고 경고는 확인으로 닫는다.
        buttons = warning ? new[] { confirmButton } : new[] { cancelButton, confirmButton };
        images = warning ? new[] { confirmHighlight } : new[] { cancelHighlight, confirmHighlight };
        texts = warning ? new[] { confirmLabel } : new[] { cancelLabel, confirmLabel };
    }

    // 가격 줄의 재화 아이콘(Prefab의 CurrencyIcon, 옛 재화 그림)을 훈련 필증 아이콘으로 바꿔 끼운다.
    // 아이콘이 정해지지 않았으면 원래 그림을 그대로 둔다.
    private void ApplyCertificateIcon()
    {
        Sprite icon = TrainingCatalogSO.CertificateIcon;
        Transform slot = icon != null ? priceText.transform.Find("CurrencyIcon") : null;
        if (slot != null && slot.TryGetComponent(out UnityEngine.UI.Image image)) image.sprite = icon;
    }

    public static string CertificateShortage(int owned, int price) =>
        $"{TrainingCatalogSO.CertificateName}이 {Mathf.Max(0, price - owned):N0}개 부족합니다.\n보유 {owned:N0}개  ·  필요 {price:N0}개";

    private void UpdateQuote(int owned, int price)
    {
        int remaining = owned - price;
        if (certificateOwned == null || certificateCost == null || certificateResult == null)
        {
            priceText.text = $"{TrainingCatalogSO.CertificateName}  ·  보유 {owned:N0}개  ·  비용 {price:N0}개  ·  구매 후 {remaining:N0}개";
            return;
        }

        priceText.text = string.Empty;
        certificateOwned.text = Metric("보유", owned);
        certificateCost.text = Metric("비용", price);
        certificateResult.text = Metric("구매 후", remaining);
    }

    private static string Metric(string label, int value) =>
        $"<size=17><color=#9BBCC9>{label}</color></size>\n{value:N0}개";

    private void SetAccents(Color color)
    {
        foreach (var graphic in accentGraphics) if (graphic != null) graphic.color = color;
    }
    private void ShowVisual()
    {
        rootObject.SetActive(true);
        dialogCanvas.alpha = 1f;
        dialogCanvas.interactable = dialogCanvas.blocksRaycasts = true;
    }
    private void HideVisual()
    {
        if (dialogCanvas != null) {
            dialogCanvas.alpha = 0f;
            dialogCanvas.interactable = dialogCanvas.blocksRaycasts = false;
        }
        if (rootObject != null) rootObject.SetActive(false);
    }

    protected override void ProcessMove(Vector2 direction)
    {
        if (!CanReceiveInput || warningOnly) return;
        int step = direction.x > .5f || direction.y < -.5f ? 1 : direction.x < -.5f || direction.y > .5f ? -1 : 0;
        if (step != 0) ChangeIndex(Mathf.Clamp(currentIndex + step, 0, 1));
    }
    protected override void OnPointerEnter(int index)
    {
        if (!CanReceiveInput) return;
        // Awake의 두 버튼 EventTrigger 인덱스는 경고의 한 버튼 배열에 맞춰 변환한다.
        base.OnPointerEnter(warningOnly ? 0 : index);
    }
    protected override void OnPointerClick(int index)
    {
        if (!CanReceiveInput) return;
        ChangeIndex(warningOnly ? 0 : index);
        OnConfirm();
    }
    public override void OnConfirm()
    {
        if (CanReceiveInput) base.OnConfirm();
    }
    public override void OnCancel()
    {
        if (CanReceiveInput) base.OnCancel();
    }
    private void HandleSelected(int index) => Complete(!warningOnly && index == 1);
    private void HandleCanceled() => Complete(false);
    private void Complete(bool confirmed)
    {
        if (!CanReceiveInput) return;
        var callback = confirmed ? accepted : canceled;
        // 먼저 콜백과 입력 수신을 회수해 더블클릭이나 같은 프레임의 재확인이 거래를 반복하지 못하게 한다.
        Close();
        callback?.Invoke();
    }
    public override void Close()
    {
        accepted = canceled = null;
        if (IsActive && UIManager.Instance != null) base.Close();
        HideVisual();
    }
    private void OnDisable() => Close();
    private void OnDestroy()
    {
        OnSelected -= HandleSelected;
        OnCanceled -= HandleCanceled;
        Close();
    }
}
