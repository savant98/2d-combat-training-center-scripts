using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using DG.Tweening;

public class WeaponUpgradeSlot : MonoBehaviour
{
    [Header("Weapon Asset (SO)")]
    [SerializeField] private WeaponBaseSO weaponData; // 이 슬롯이 담당할 무기 데이터 원본

    [Header("Static UI Components")]
    [SerializeField] private Image weaponIconImage;
    [SerializeField] private TextMeshProUGUI weaponNameText;

    [Header("Dynamic Button Groups")]
    [SerializeField] private GameObject lockedGroup;   // 자물쇠 사각형 버튼이 포함된 부모 오브젝트
    [SerializeField] private GameObject unlockedGroup; // 두 개의 원형 업그레이드 버튼이 포함된 부모 오브젝트

    [Header("Selectable Buttons (RectTransform)")]
    [SerializeField] private RectTransform lockButton;          // index % 3 == 0 (자물쇠 버튼)
    [SerializeField] private RectTransform upgradeButtonLeft;   // index % 3 == 1 (왼쪽 원)
    [SerializeField] private RectTransform upgradeButtonRight;  // index % 3 == 2 (오른쪽 원)

    [Header("Unlock State Visuals")]
    [SerializeField] private TextMeshProUGUI unlockWeaponCostText;
    [SerializeField] private CanvasGroup unlockWeaponCostCanvasGroup;

    [Header("Upgrade State Visuals")]
    [SerializeField] private Image leftNodeImage;
    [SerializeField] private Image rightNodeImage;
    [SerializeField] private TextMeshProUGUI leftNodeDescText;
    [SerializeField] private TextMeshProUGUI leftNodeCostText;
    [SerializeField] private TextMeshProUGUI rightNodeDescText;
    [SerializeField] private TextMeshProUGUI rightNodeCostText;
    [SerializeField] private Color activeColor = Color.cyan;    // 청록색
    [SerializeField] private Color inactiveColor = Color.red;   // 빨간색

    [Header("Selectable Images (For SelectableMenuUI Highlight)")]
    [SerializeField] private Image lockButtonImage;
    [SerializeField] private Image leftNodeButtonImage;
    [SerializeField] private Image rightNodeButtonImage;

    [Header("Node Description UIs")]
    [SerializeField] private CanvasGroup leftDescCanvasGroup;
    [SerializeField] private CanvasGroup leftCostCanvasGroup;
    [SerializeField] private CanvasGroup rightDescCanvasGroup;
    [SerializeField] private CanvasGroup rightCostCanvasGroup;
    
    [SerializeField] private float fadeDuration = 0.2f;

    [Header("Feedback & Animations")]
    [SerializeField] private FeedbackDataSO unlockFeedback;
    [SerializeField] private FeedbackDataSO unlockCompleteFeedback;
    [SerializeField] private FeedbackDataSO upgradeFeedback;
    [SerializeField] private FeedbackDataSO failFeedback;

    // 외부(UpgradeMenu)에서 네비게이션 배열을 구축할 때 참조할 프로퍼티들
    public RectTransform LockButton => lockButton;
    public RectTransform UpgradeButtonLeft => upgradeButtonLeft;
    public RectTransform UpgradeButtonRight => upgradeButtonRight;
    public string WeaponID => weaponData != null ? weaponData.weaponName : string.Empty;
    public Image LockButtonImage => lockButtonImage;
    public Image LeftNodeButtonImage => leftNodeButtonImage;
    public Image RightNodeButtonImage => rightNodeButtonImage;

    private Tween leftFadeTween;
    private Tween rightFadeTween;
    private Tween unlockCostFadeTween;

    private void Start()
    {
        if (leftDescCanvasGroup != null)
            leftDescCanvasGroup.alpha = 0f;
        if (rightDescCanvasGroup != null)
            rightDescCanvasGroup.alpha = 0f;
        if (unlockWeaponCostCanvasGroup != null)
            unlockWeaponCostCanvasGroup.alpha = 0f;

        if (weaponData != null)
        {
            leftNodeDescText.text = weaponData.leftNodeInfo.description;
            leftNodeCostText.text = "x" + weaponData.leftNodeInfo.upgradeCost.ToString();

            rightNodeDescText.text = weaponData.rightNodeInfo.description;
            rightNodeCostText.text = "x" + weaponData.rightNodeInfo.upgradeCost.ToString();

            unlockWeaponCostText.text = "x" + weaponData.unlockInfo.upgradeCost.ToString();
        }

        // 게임이 시작되거나 UI가 켜질 때 현재 세이브 상태에 맞게 슬롯을 초기화합니다.
        RefreshSlotUI();
    }

    /// <summary>
    /// 현재 이 슬롯에 커서가 위치하고 있을 때, 포커스된 노드에 맞춰 설명창을 보여줌
    /// </summary>
    public void UpdateHoverDescription(bool isSelected, int currentCol)
    {
        // 무기 잠금 여부 체크
        bool isUnlocked = DataManager.Instance.IsWeaponUnlocked(WeaponID);

        if (!isUnlocked)
        {
            float targetUnlockAlpha = (isSelected && currentCol == 0) ? 1f : 0f;

            unlockCostFadeTween?.Kill();
            unlockCostFadeTween = unlockWeaponCostCanvasGroup.DOFade(targetUnlockAlpha, fadeDuration);

            if (DataManager.Instance.CurrentGold < weaponData.unlockInfo.upgradeCost)
                unlockWeaponCostText.color = inactiveColor;
            else
                unlockWeaponCostText.color = activeColor;

            return;
        }

        // 커서 목표 상태 계산
        float targetLeftAlpha = (isSelected && currentCol == 0) ? 1f : 0f;
        float targetRightAlpha = (isSelected && currentCol == 1) ? 1f : 0f;
        bool isRightNode = (isSelected && currentCol == 1) ? true : false;

        if (leftDescCanvasGroup != null)
        {
            leftFadeTween?.Kill();
            leftFadeTween = leftDescCanvasGroup.DOFade(targetLeftAlpha, fadeDuration);
        }

        if (rightDescCanvasGroup != null)
        {
            rightFadeTween?.Kill();
            rightFadeTween = rightDescCanvasGroup.DOFade(targetRightAlpha, fadeDuration);
        }

        if (!isRightNode)
        {
            rightCostCanvasGroup.alpha = 0f;

            // 업그레이드 확인
            if (DataManager.Instance.IsWeaponNodeUpgraded(WeaponID, isRightNode))
            {
                leftCostCanvasGroup.alpha = 0f;
                return;
            }

            leftCostCanvasGroup.alpha = 1f;

            // 구매 비용에 따른 색 변화
            if (DataManager.Instance.CurrentGold < weaponData.leftNodeInfo.upgradeCost)
                leftNodeCostText.color = inactiveColor;
            else
                leftNodeCostText.color = activeColor;
        }
        else
        {
            leftCostCanvasGroup.alpha = 0f;

            // 업그레이드 확인
            if (DataManager.Instance.IsWeaponNodeUpgraded(WeaponID, isRightNode))
            {
                rightCostCanvasGroup.alpha = 0f;
                return;
            }

            rightCostCanvasGroup.alpha = 1f;

            // 구매 비용에 따른 색 변화
            if (DataManager.Instance.CurrentGold < weaponData.rightNodeInfo.upgradeCost)
                rightNodeCostText.color = inactiveColor;
            else
                rightNodeCostText.color = activeColor;
        }
    }

    /// <summary>
    /// DataManager의 최신 세이브 데이터를 바탕으로 슬롯의 모든 비주얼을 새로고침합니다.
    /// </summary>
    public void RefreshSlotUI()
    {
        if (weaponData == null)
        {
            Debug.LogWarning($"[{name}] WeaponData(SO)가 연결되지 않았습니다.");
            return;
        }

        // 1. 고정 데이터 반영 (이름, 아이콘)
        weaponNameText.text = weaponData.weaponName;
        weaponIconImage.sprite = weaponData.weaponSprite;

        // 2. DataManager로부터 동적 데이터(해금 여부, 레벨) 조회
        bool isUnlocked = DataManager.Instance.IsWeaponUnlocked(WeaponID);

        // 3. 해금 여부에 따른 버튼 그룹 스위칭 (기획의 핵심!)
        if (isUnlocked)
        {
            bool leftActive = DataManager.Instance.IsWeaponNodeUpgraded(WeaponID, false);
            bool rightActive = DataManager.Instance.IsWeaponNodeUpgraded(WeaponID, true);

            lockedGroup.SetActive(false);
            unlockedGroup.SetActive(true);

            // 4. 업그레이드 진행도에 따른 원형 노드 색상 처리 (빨간색/청록색 예시)
            // 예시: 레벨 0 = 둘 다 빨강 / 레벨 1 = 왼쪽 청록, 오른쪽 빨강 / 레벨 2 = 둘 다 청록
            leftNodeImage.color = leftActive ? activeColor : inactiveColor;
            rightNodeImage.color = rightActive ? activeColor : inactiveColor;
            leftNodeDescText.color = leftActive ? activeColor : inactiveColor;
            rightNodeDescText.color = rightActive ? activeColor : inactiveColor;

            if (leftActive)
                leftCostCanvasGroup.alpha = 0f;

            if (rightActive)
                rightCostCanvasGroup.alpha = 0f;
        }
        else
        {
            lockedGroup.SetActive(true);
            unlockedGroup.SetActive(false);
        }
    }


    /// <summary>
    /// 자물쇠 해금 연출
    /// </summary>
    public void PlayUnlockAnimation(Action onComplete)
    {
        float waitTime = 0f;

        if (unlockFeedback != null && FeedbackPlayer.Instance != null)
        {
            // 1. FeedbackPlayer 재생
            FeedbackPlayer.Instance.Play(unlockFeedback, lockButtonImage.transform);

            // 2. FeedbackData에 세팅해둔 lifeTime만큼 기다림
            waitTime = unlockFeedback.lifeTime;
        }

        // 3. 지정된 시간(연출 끝나는 시간) 뒤에 완료 콜백 실행
        StartCoroutine(InvokeCallbackCo(waitTime, onComplete, lockButtonImage.transform));
    }

    /// <summary>
    /// 노드 업그레이드 연출
    /// </summary>
    public void PlayUpgradeAnimation(bool isRightNode, Action onComplete)
    {
        float waitTime = 0f;
        Transform spawnTr = rightNodeButtonImage.transform;

        if (upgradeFeedback != null && FeedbackPlayer.Instance != null)
        {
            spawnTr = isRightNode ? rightNodeButtonImage.transform : leftNodeButtonImage.transform;
            FeedbackPlayer.Instance.Play(upgradeFeedback, spawnTr);
            waitTime = upgradeFeedback.lifeTime;
        }

        StartCoroutine(InvokeCallbackCo(waitTime, onComplete, spawnTr));
    }

    private IEnumerator InvokeCallbackCo(float delay, Action callback, Transform completeFeedbackTr)
    {
        if (delay > 0)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        // 해금 피드백 재생
        if (unlockCompleteFeedback != null)
            FeedbackPlayer.Instance.Play(unlockCompleteFeedback, completeFeedbackTr);

        callback?.Invoke();
    }

    /// <summary>
    /// 무기 해금이 가능한 상태인지 체크 (재화 및 중복 해금 방지)
    /// </summary>
    public bool CanUnlockWeapon()
    {
        // 이미 해금된 경우 불가
        if (DataManager.Instance.IsWeaponUnlocked(WeaponID))
            return false;

        int cost = weaponData.unlockInfo.upgradeCost;

        if (DataManager.Instance.CurrentGold < cost)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 무기 업그레이드가 가능한 상태인지 체크 (선행 조건 및 재화 체크)
    /// </summary>
    public bool CanUpgradeWeapon(bool isRightNode)
    {
        // 1. 무기가 해금되지 않았다면 방어
        if (!DataManager.Instance.IsWeaponUnlocked(WeaponID))
        {
            Debug.LogWarning("무기가 해금되지 않았습니다.");
            return false;
        }

        // 2. 중복 업그레이드 방지: 이미 완료된 노드를 누르면 거절
        bool isAlreadyUpgraded = DataManager.Instance.IsWeaponNodeUpgraded(WeaponID, isRightNode);
        if (isAlreadyUpgraded)
        {
            Debug.LogWarning("이미 업그레이드된 노드입니다.");
            return false;
        }

        int cost = isRightNode ? weaponData.rightNodeInfo.upgradeCost : weaponData.leftNodeInfo.upgradeCost;

        if (DataManager.Instance.CurrentGold < cost)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 조건 미달로 실패했을 때 재생할 피드백
    /// </summary>
    public void PlayFailFeedback(bool isLock, bool isRightNode = false)
    {
        if (failFeedback != null && FeedbackPlayer.Instance != null)
        {
            Transform spawnTr = isLock ? lockButtonImage.transform : (isRightNode ? rightNodeButtonImage.transform : leftNodeButtonImage.transform);
            FeedbackPlayer.Instance.Play(failFeedback, spawnTr);
        }
    }

    /// <summary>
    /// 자물쇠 버튼을 눌렀을 때 실행될 해금 로직
    /// </summary>
    public void TryUnlockWeapon()
    {
        int cost = weaponData.unlockInfo.upgradeCost;

        if (DataManager.Instance.TrySpendGold(cost))
        {
            // 장부에 해금 상태 기록 (내부적으로 자동 SaveGame() 호출됨)
            DataManager.Instance.SetWeaponUnlockState(WeaponID, true);

            Debug.Log($"[WeaponUpgradeSlot] {WeaponID} 무기를 해금합니다.");

            // UI 즉시 새로고침 (자물쇠가 꺼지고 동그라미 2개가 켜짐)
            RefreshSlotUI();
        }
        
    }

    /// <summary>
    /// 원형 업그레이드 버튼을 눌렀을 때 실행될 성장 로직
    /// </summary>
    /// <param name="isRightNode">오른쪽 노드 클릭 여부</param>
    public void TryUpgradeWeapon(bool isRightNode)
    {
        int cost = isRightNode ? weaponData.rightNodeInfo.upgradeCost : weaponData.leftNodeInfo.upgradeCost;

        if (DataManager.Instance.TrySpendGold(cost))
        {
            // 실제 업그레이드 데이터 반영 필요
            Debug.Log($"[WeaponUpgradeSlot] {WeaponID} 무기 업그레이드 진행.");
            DataManager.Instance.SetWeaponNodeUpgradeState(WeaponID, isRightNode, true);

            RefreshSlotUI();

        }
    }
}