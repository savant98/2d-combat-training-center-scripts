using DG.Tweening;
using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.UI;
using Unity.Cinemachine;

public class UpgradeMenu : SelectableMenuUI
{
    [Header("UpgradeMenu Components")]
    [SerializeField] private Animator animator;
    [SerializeField] private CinemachineCamera virtualCamera;
    [SerializeField] private TextMeshProUGUI currentGoldDescription;

    [Header("Weapon Slots Setup")]
    // [0,1,2,3] -> 왼쪽 그룹 1~4열 / [4,5,6,7] -> 오른쪽 그룹 1~4열
    [SerializeField] private WeaponUpgradeSlot[] weaponSlots;

    [Header("Purchase Confirmation")]
    [SerializeField] private UpgradePurchaseDialog purchaseDialog;
    private Coroutine purchaseRoutine;
    private bool purchaseInProgress;
    private bool ownsPurchaseInputBlock;

    public bool IsPurchasePending => purchaseInProgress || (purchaseDialog != null && purchaseDialog.IsActive);
    private bool CanReceiveInput => IsActive && isActiveAndEnabled && !IsPurchasePending
        && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
        && UIManager.Instance.IsCurrentInputTarget(this);

    private int currentRow = 0; // 세로 (무기 인덱스)
    private bool isRightGroup = false; // 현재 오른쪽 그룹(4~7번 슬롯들)에 있는지 여부
    private int currentCol = 0; // 가로 (0: 왼쪽 원, 1: 오른쪽 원)

    private const int ROW_COUNT = 4; // 한 그룹당 세로 줄 개수

    // 현재 포커스된 무기의 실제 weaponSlots 배열 인덱스를 반환하는 헬퍼 프로퍼티
    private int CurrentSlotIndex => isRightGroup ? (currentRow + ROW_COUNT) : currentRow;

    protected override void Awake()
    {
        if (weaponSlots == null || weaponSlots.Length == 0)
        {
            Debug.LogError($"[{name} weaponSlots가 인스펙터에 할당되지 않았습니다!]");
            return;
        }

        // 총 8개의 무기 * 3개씩 = 24개의 버튼과 이미지를 부모 시스템에 동적 바인딩
        int totalButtons = weaponSlots.Length * 3;
        buttons = new RectTransform[totalButtons];
        images = new Image[totalButtons];

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            buttons[i * 3 + 0] = weaponSlots[i].LockButton;
            buttons[i * 3 + 1] = weaponSlots[i].UpgradeButtonLeft;
            buttons[i * 3 + 2] = weaponSlots[i].UpgradeButtonRight;

            images[i * 3 + 0] = weaponSlots[i].LockButtonImage;
            images[i * 3 + 1] = weaponSlots[i].LeftNodeButtonImage;
            images[i * 3 + 2] = weaponSlots[i].RightNodeButtonImage;
        }

        // Bind는 버튼 조립 이후
        base.Awake();
        if (purchaseDialog == null) purchaseDialog = GetComponentInChildren<UpgradePurchaseDialog>(true);
    }
    private void Start()
    {
        OnSelected += HandleSelected;
        OnCanceled += HandleCanceled;
    }
    private void UpdateGoldText()
    {
        if (currentGoldDescription != null)
        {
            currentGoldDescription.text = "x" + DataManager.Instance.CurrentGold;
        }
    }
    public void OpenMenu()
    {
        if (IsActive)
            return;

        animator.SetBool("Open", true);
        virtualCamera.Priority = 999;
        UIManager.Instance.HUDVisible(false);

        // 첫 무기 슬롯으로 초기화
        currentRow = 0;
        currentCol = 0;
        isRightGroup = false;

        UpdateGoldText();

        // 메뉴가 열릴 때 세이브 상태를 체크해 올바른 초기 인덱스 잡기
        Open();
        SyncIndexToRowCol();
        
    }
    private void OnDestroy()
    {
        OnSelected -= HandleSelected;
        OnCanceled -= HandleCanceled;
    }

    /// <summary>
    /// 2단 Grid(좌 4줄 우 4줄) 구조에 맞춘 상하좌우 이동 공식 재정의
    /// </summary>
    protected override void ProcessMove(Vector2 dir)
    {
        if (!CanReceiveInput) return;
        int nextRow = currentRow;
        int nextCol = currentCol;
        bool nextGroup = isRightGroup;

        // 1. 상하 이동 (무기 변경)
        if (dir.y > 0.5f)
            nextRow--;
        if (dir.y < -0.5f)
            nextRow++;
        nextRow = Mathf.Clamp(nextRow, 0, ROW_COUNT - 1);

        int currentSlotIdx = nextGroup ? (nextRow + ROW_COUNT) : nextRow;
        // 이동할 목적지 무기의 해금 상태를 세이브 데이터 매니저에서 조회
        string currentWeaponId = weaponSlots[currentSlotIdx].WeaponID;
        bool isCurrentLocked = !DataManager.Instance.IsWeaponUnlocked(currentWeaponId);

        if (dir.x > 0.5f) // 오른쪽 입력
        {
            if (isCurrentLocked)
            {
                // 잠긴 상태라면 자물쇠(0) 하나뿐이므로, 오른쪽 입력 시 무조건 오른쪽 그룹으로 점프 시도
                if (!nextGroup)
                {
                    nextGroup = true;
                    nextCol = 0; // 오른쪽 그룹의 자물쇠 혹은 첫 노드로
                }
            }
            else
            {
                // 해금된 상태라면 0(왼쪽 원) -> 1(오른쪽 원) 순차 이동
                nextCol++;
                if (nextCol > 1) // 오른쪽 원을 넘어선 경우
                {
                    if (!nextGroup) // 왼쪽 그룹이었다면 오른쪽 그룹의 같은 줄로 점프!
                    {
                        nextGroup = true;
                        nextCol = 0; // 오른쪽 그룹의 첫 번째 원(0)에 안착
                    }
                    else
                    {
                        nextCol = 1; // 이미 오른쪽 그룹 끝이면 방어
                    }
                }
            }
        }
        else if (dir.x < -0.5f) // 왼쪽 입력
        {
            nextCol--;
            if (nextCol < 0) // 범위를 벗어난 경우
            {
                if (nextGroup) // 오른쪽 그룹에 있었다면 왼쪽 그룹의 같은 줄로 점프!
                {
                    nextGroup = false;

                    // 왼쪽 그룹 무기의 해금 상태를 확인하여 커서 위치 조정
                    string leftWeaponId = weaponSlots[nextRow].WeaponID;
                    bool isLeftLocked = !DataManager.Instance.IsWeaponUnlocked(leftWeaponId);

                    // 해금되어 있다면 가장 우측 노드(1: 오른쪽 원)에 커서를 대주고, 잠겨있다면 자물쇠(0)에 대줌
                    nextCol = isLeftLocked ? 0 : 1;
                }
                else
                {
                    nextCol = 0; // 이미 왼쪽 그룹 끝이면 방어
                }
            }
        }

        // 3. 상하 이동으로 인해 무기 줄이 바뀔 때의 가로 칸 예외 처리
        int finalSlotIdx = nextGroup ? (nextRow + ROW_COUNT) : nextRow;
        string finalWeaponId = weaponSlots[finalSlotIdx].WeaponID;
        bool isFinalLocked = !DataManager.Instance.IsWeaponUnlocked(finalWeaponId);

        if (isFinalLocked)
        {
            nextCol = 0; // 잠긴 무기로 가 가 가버리면 가로칸은 무조건 자물쇠(0) 고정
        }

        // 최종 상태 반영
        currentRow = nextRow;
        currentCol = nextCol;
        isRightGroup = nextGroup;

        SyncIndexToRowCol();
    }

    /// <summary>
    /// 마우스가 UI 위에 올라왔을 때(Hover) 1차원 인덱스를 2D 좌우 그룹 좌표로 역산
    /// </summary>
    protected override void OnPointerEnter(int index)
    {
        if (!CanReceiveInput)
            return;

        // UIManager에서 UI 입력이 막혀있다면 마우스 호버(Hover) 완벽 무시!
        if (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked)
            return;

        int slotIndex = index / 3; // 0 ~ 7
        int nodeType = index % 3;  // 0: 자물쇠, 1: 왼원, 2: 오른원

        // 슬롯 번호가 4 이상이면 오른쪽 그룹입니다.
        isRightGroup = slotIndex >= ROW_COUNT;
        currentRow = isRightGroup ? (slotIndex - ROW_COUNT) : slotIndex;

        if (nodeType == 0)
        {
            currentCol = 0; // 자물쇠
        }
        else
        {
            currentCol = nodeType - 1; // 1이면 0(왼쪽 원), 2면 1(오른쪽 원)
        }

        SyncIndexToRowCol();
    }

    /// <summary>
    /// 2D 좌표 및 그룹 정보를 계산하여 부모의 1차원 ChangeIndex 시스템에 전달
    /// </summary>
    private void SyncIndexToRowCol()
    {
        if (weaponSlots == null || weaponSlots.Length == 0)
            return;

        int slotIdx = CurrentSlotIndex;
        string weaponId = weaponSlots[slotIdx].WeaponID;
        bool isLocked = !DataManager.Instance.IsWeaponUnlocked(weaponId);

        int targetIndex;
        if (isLocked)
        {
            targetIndex = (slotIdx * 3) + 0; // 자물쇠 버튼 인덱스
        }
        else
        {
            targetIndex = (slotIdx * 3) + 1 + currentCol; // 업그레이드 원 버튼 인덱스
        }

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            if (weaponSlots[i] == null)
                continue;

            // i번째 슬롯이 현재 유저가 커서를 올려둔(포커스한) 슬롯인지 확인
            bool isCurrentSlot = (i == CurrentSlotIndex);

            // 설명창 띄우기
            weaponSlots[i].UpdateHoverDescription(isCurrentSlot, currentCol);
        }

        // 부모 SelectableMenuUI의 인덱스 변경 및 피드백 재생 함수 호출
        ChangeIndex(targetIndex);
    }
    protected override void OnPointerClick(int index)
    {
        if (!CanReceiveInput) return;
        OnPointerEnter(index);
        base.OnPointerClick(index);
    }
    public override void OnConfirm()
    {
        if (CanReceiveInput) base.OnConfirm();
    }
    public override void OnCancel()
    {
        if (CanReceiveInput) base.OnCancel();
    }

    private void HandleSelected(int index)
    {
        if (!CanReceiveInput) return;
        if (!TryResolvePurchase(index, out var slot, out var nodeType, out var purchaseName, out var price, out var warning))
        {
            ShowPurchaseWarning(warning, slot, nodeType);
            return;
        }
        if (purchaseDialog == null)
        {
            Debug.LogError("업그레이드 구매 확인 창 연결이 필요합니다.", this);
            return;
        }
        string weaponId = slot.WeaponID;
        purchaseDialog.ShowConfirmation(purchaseName, price,
            () => ConfirmPurchase(index, weaponId, price), RestorePurchaseFocus);
    }

    private bool TryResolvePurchase(int index, out WeaponUpgradeSlot slot, out int nodeType,
        out string purchaseName, out int price, out string warning)
    {
        slot = null; nodeType = index % 3; purchaseName = string.Empty; price = 0;
        warning = "구매 정보를 확인할 수 없습니다.";
        if (index < 0 || weaponSlots == null || index / 3 >= weaponSlots.Length) return false;
        slot = weaponSlots[index / 3];
        if (slot == null || !slot.TryGetPurchaseInfo(nodeType, out purchaseName, out price)) return false;
        var manager = DataManager.Instance;
        bool unlocked = manager.IsWeaponUnlocked(slot.WeaponID);
        if (nodeType == 0 ? unlocked : manager.IsWeaponNodeUpgraded(slot.WeaponID, nodeType == 2))
        { warning = "이미 구매한 업그레이드입니다"; return false; }
        if (nodeType != 0 && !unlocked)
        { warning = "먼저 무기를 해금해 주세요."; return false; }
        if (manager.CurrentGold < price)
        { warning = "재화가 부족합니다"; return false; }
        warning = null;
        return true;
    }
    private bool ValidateConfirmedPurchase(int index, string weaponId, int quotedPrice, out WeaponUpgradeSlot slot,
        out int nodeType, out string warning)
    {
        if (!TryResolvePurchase(index, out slot, out nodeType, out _, out int currentPrice, out warning)) return false;
        if (slot.WeaponID != weaponId || currentPrice != quotedPrice)
        { warning = "구매 정보가 변경되었습니다. 다시 확인해 주세요."; return false; }
        return true;
    }
    private void ShowPurchaseWarning(string message, WeaponUpgradeSlot slot = null, int nodeType = 0)
    {
        if (!IsActive || !isActiveAndEnabled || string.IsNullOrEmpty(message)) return;
        if (slot != null) slot.PlayFailFeedback(nodeType == 0, nodeType == 2);
        if (purchaseDialog != null) purchaseDialog.ShowWarning(message, RestorePurchaseFocus);
        else Debug.LogError("업그레이드 구매 확인 창 연결이 필요합니다: " + message, this);
    }
    private void RestorePurchaseFocus()
    {
        // 스택에 소유 메뉴를 다시 밀어 넣지 않는다. 위에 열린 Pause나 System 입력은 그대로 둔다.
        if (IsActive) SyncIndexToRowCol();
    }
    private void ConfirmPurchase(int index, string weaponId, int quotedPrice)
    {
        if (!CanReceiveInput) return;
        if (!ValidateConfirmedPurchase(index, weaponId, quotedPrice, out var slot, out var nodeType, out var warning))
        { ShowPurchaseWarning(warning, slot, nodeType); return; }
        purchaseInProgress = true;
        var routine = StartCoroutine(UpgradeSequenceCo(index, weaponId, quotedPrice));
        purchaseRoutine = purchaseInProgress ? routine : null;
    }

    private IEnumerator UpgradeSequenceCo(int index, string weaponId, int quotedPrice)
    {
        string warning = null;
        WeaponUpgradeSlot slot = null;
        int nodeType = index % 3;
        try
        {
            if (!ValidateConfirmedPurchase(index, weaponId, quotedPrice, out slot, out nodeType, out warning)) yield break;
            ownsPurchaseInputBlock = true;
            UIManager.Instance.SetUIInputBlock(true);
            bool animationFinished = false;
            if (nodeType == 0) slot.PlayUnlockAnimation(() => animationFinished = true);
            else slot.PlayUpgradeAnimation(nodeType == 2, () => animationFinished = true);
            yield return new WaitUntil(() => animationFinished);

            // 연출 대기 중 가격·상태·재화가 바뀌었으면 다시 확인하게 하고 기존 거래는 수행하지 않는다.
            if (!ValidateConfirmedPurchase(index, weaponId, quotedPrice, out slot, out nodeType, out warning)) yield break;
            if (nodeType == 0)
            {
                slot.TryUnlockWeapon();
                int slotIndex = index / 3;
                isRightGroup = slotIndex >= ROW_COUNT;
                currentRow = isRightGroup ? slotIndex - ROW_COUNT : slotIndex;
                currentCol = 0;
            }
            else slot.TryUpgradeWeapon(nodeType == 2);
            UpdateGoldText();
            SyncIndexToRowCol();
        }
        finally
        {
            purchaseRoutine = null;
            purchaseInProgress = false;
            ReleasePurchaseInputBlock();
            if (!string.IsNullOrEmpty(warning) && UIManager.Instance != null && UIManager.Instance.IsCurrentInputTarget(this))
                ShowPurchaseWarning(warning, slot, nodeType);
        }
    }
    private void ReleasePurchaseInputBlock()
    {
        if (!ownsPurchaseInputBlock) return;
        ownsPurchaseInputBlock = false;
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
    private void OnDisable()
    {
        if (purchaseDialog != null) purchaseDialog.Close();
        CancelPurchaseSequence();
        if (IsActive && UIManager.Instance != null) base.Close();
    }

    private void HandleCanceled()
    {
        if (!CanReceiveInput) return;
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
        if (purchaseDialog != null) purchaseDialog.Close();
        CancelPurchaseSequence();

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            if (weaponSlots[i] != null)
            {
                weaponSlots[i].UpdateHoverDescription(false, 0);
            }
        }
    }
}