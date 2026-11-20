using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Unity.Cinemachine;

/// <summary>
/// 업그레이드 메뉴의 입구(2026-11-18 2차). 사용자 지시 "이제 이전 UpgradeMenu는 삭제해"로 옛 8칸 강화 격자와 구매 처리를 지우고,
/// 열기·닫기(Animator·카메라·HUD·능력 중단), 보드의 재화 글자, 구매 확인 창 정리만 맡는다.
/// 화면과 입력은 무장 개요 페이지(ArmamentOverviewPage)와 그 하위 페이지(교체·상세)가 맡는다. 이 메뉴는 입력 스택 맨 아래에 남아
/// 계속 열린 상태(IsActive)이므로 LobbyStation 장치도 펼쳐진 채로 있다.
/// buttons에는 그래픽이 없는 빈 선택 대상(MenuFocusAnchor) 하나만 둔다(SelectableMenuUI.Open이 buttons[0]을 강조하므로 비울 수 없다).
/// </summary>
public class UpgradeMenu : SelectableMenuUI
{
    [Header("UpgradeMenu Components")]
    [SerializeField] private Animator animator;
    [SerializeField] private CinemachineCamera virtualCamera;
    // 보드의 재화 칸(보유한 훈련 필증 수). UpgradePanel.prefab에는 옛 재화의 아이콘과 이름 글자가 남아 있어,
    // 열 때마다 훈련 목록(TrainingCatalogSO)의 아이콘과 이름으로 바꿔 끼운다.
    [FormerlySerializedAs("currentGoldDescription")]
    [SerializeField] private TextMeshProUGUI certificateCountText;
    [Tooltip("재화 아이콘. 비우면 보유 수 글자와 같은 칸의 CostImage를 찾는다")]
    [SerializeField] private Image certificateIconImage;
    [Tooltip("재화 이름 글자. 비우면 보유 수 글자와 같은 칸의 HologramCurrencyName을 찾는다")]
    [SerializeField] private TextMeshProUGUI certificateNameText;

    [Header("Purchase Confirmation")]
    // 상세·교체 페이지가 함께 쓰는 확인·경고 창. 메뉴가 닫힐 때 함께 닫는다.
    [SerializeField] private UpgradePurchaseDialog purchaseDialog;

    [Header("Armament Pages")]
    // 메뉴를 열면 보이는 무장 개요 페이지. 개요는 이 메뉴를 입력 스택 아래에 둔 채 위에 쌓인다.
    [SerializeField] private ArmamentOverviewPage overviewPage;

    private bool CanReceiveInput => IsActive && isActiveAndEnabled
        && (purchaseDialog == null || !purchaseDialog.IsActive)
        && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
        && UIManager.Instance.IsCurrentInputTarget(this);

    protected override void Awake()
    {
        base.Awake();
        if (purchaseDialog == null) purchaseDialog = GetComponentInChildren<UpgradePurchaseDialog>(true);
        if (buttons == null || buttons.Length == 0)
            Debug.LogError($"[{name}] 선택 대상(MenuFocusAnchor) 연결이 필요합니다. tools/Unity/ArmamentMenuSetup.Run을 실행하세요.", this);
    }
    private void Start()
    {
        OnCanceled += HandleCanceled;
    }
    private void OnDestroy()
    {
        OnCanceled -= HandleCanceled;
    }
    private void UpdateCertificateText()
    {
        if (certificateCountText != null)
        {
            certificateCountText.text = "x" + DataManager.Instance.TrainingCertificates;
        }
    }

    // 재화 칸의 아이콘과 이름 글자를 훈련 필증의 것으로 맞춘다. 아이콘이 정해지지 않았으면 원래 그림을 그대로 둔다.
    private void ApplyCertificateLabels()
    {
        Transform slot = certificateCountText != null ? certificateCountText.transform.parent : null;
        if (slot != null)
        {
            if (certificateIconImage == null)
            {
                Transform icon = slot.Find("CostImage");
                if (icon != null) certificateIconImage = icon.GetComponent<Image>();
            }
            if (certificateNameText == null)
            {
                Transform label = slot.Find("HologramCurrencyName");
                if (label != null) certificateNameText = label.GetComponent<TextMeshProUGUI>();
            }
        }
        Sprite sprite = TrainingCatalogSO.CertificateIcon;
        if (certificateIconImage != null && sprite != null) certificateIconImage.sprite = sprite;
        if (certificateNameText != null) certificateNameText.text = TrainingCatalogSO.CertificateName;
    }
    public void OpenMenu()
    {
        if (IsActive)
            return;

        animator.SetBool("Open", true);
        virtualCamera.Priority = 999;
        UIManager.Instance.HUDVisible(false);

        ApplyCertificateLabels();
        UpdateCertificateText();
        Open();

        // 무장 개요 페이지를 이 메뉴 위에 연다. 열 수 없으면 빈 보드에 갇히지 않도록 바로 닫는다.
        if (overviewPage == null || !overviewPage.OpenFromMenu(CloseMenu, UpdateCertificateText))
        {
            Debug.LogError($"[{name}] 무장 개요 페이지를 열 수 없어 업그레이드 메뉴를 닫습니다.", this);
            CloseMenu();
        }
    }

    // 선택 대상이 빈 앵커 하나뿐이라 이동·마우스·확인은 할 일이 없다. 입력은 위에 쌓인 페이지가 받는다.
    protected override void ProcessMove(Vector2 dir) { }
    protected override void OnPointerEnter(int index) { }
    protected override void OnPointerClick(int index) { }
    public override void OnConfirm() { }
    public override void OnCancel()
    {
        if (CanReceiveInput) base.OnCancel();
    }

    private void OnDisable()
    {
        if (overviewPage != null) overviewPage.CloseAllPages();
        if (purchaseDialog != null) purchaseDialog.Close();
        if (IsActive && UIManager.Instance != null) base.Close();
    }

    private void HandleCanceled()
    {
        if (!CanReceiveInput) return;
        CloseMenu();
    }

    // 기존 X 닫기 처리. 개요 페이지에서 X로 닫을 때도 이 경로를 쓴다.
    private void CloseMenu()
    {
        if (!IsActive) return;
        GameManager.Instance.Player.AbilityManager.InterruptAllAbilities();

        animator.SetBool("Open", false);
        virtualCamera.Priority = -1;
        UIManager.Instance.HUDVisible(true);
        // 나중에 QuitConfirmPopup을 열도록 확장 가능.

        Close();
    }

    public override void Close()
    {
        base.Close();
        if (overviewPage != null) overviewPage.CloseAllPages();
        if (purchaseDialog != null) purchaseDialog.Close();
    }
}