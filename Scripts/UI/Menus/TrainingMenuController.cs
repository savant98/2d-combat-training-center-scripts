using DG.Tweening;
using System;
using TMPro;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class TrainingMenuController : MonoBehaviour
{
    [Header("TrainingMenu Settings")]
    [SerializeField] private SelectableMenuUI menu;
    [SerializeField] private Animator animator;
    [SerializeField] private CinemachineCamera virtualCamera;

    [Header("TrainingMenu Data")]
    [SerializeField] private TrainingDefinitionSO currentTraining;
    [SerializeField] private TextMeshProUGUI hitCountText;
    [SerializeField] private TextMeshProUGUI timeTakenText;
    [SerializeField] private TextMeshProUGUI gradeText;

    [Header("Training List (Optional)")]
    [SerializeField] private TrainingMenu listMenu;
    [SerializeField] private GameObject listPanel;
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private TextMeshProUGUI listTitleText;
    [SerializeField] private TextMeshProUGUI listSummaryText;
    [SerializeField] private TextMeshProUGUI listCompletionText;
    [SerializeField, TextArea] private string listDescription = "근거리·원거리 적과 방어막 목표를 상대합니다.";

    [Header("Training Menu Animation")]
    [SerializeField] private RectTransform listScroll;
    [SerializeField] private CanvasGroup listPanelGroup;
    [SerializeField] private CanvasGroup detailPanelGroup;
    [SerializeField] private Image projectorRayLeft;
    [SerializeField] private Image projectorRayRight;

    public event Action OnConfirmEnterRoom;

    private bool isOpen;
    private bool isTransitioning;
    private DG.Tweening.Sequence presentation;
    private float leftRayAlpha;
    private float rightRayAlpha;
    private Vector3 leftRayScale;
    private Vector3 rightRayScale;
    private bool leftRayCached;
    private bool rightRayCached;
    private RectTransform listScreen;
    private RectTransform detailScreen;
    private Vector2 listHome;
    private Vector2 detailHome;
    private Vector3 listScreenScale;
    private Vector3 detailScreenScale;
    private bool screenLayoutCached;

    private bool HasPresentation => HasList && listScroll != null && listPanelGroup != null && detailPanelGroup != null && screenLayoutCached;

    private void Awake()
    {
        CacheProjectorRays();
        CacheScreenLayout();
    }

    private void CacheScreenLayout()
    {
        if (screenLayoutCached || !HasList) return;
        listScreen = listPanel.transform as RectTransform;
        detailScreen = detailPanel.transform as RectTransform;
        if (listScreen == null || detailScreen == null) return;

        listHome = listScreen.anchoredPosition;
        detailHome = detailScreen.anchoredPosition;
        listScreenScale = listScreen.localScale;
        detailScreenScale = detailScreen.localScale;
        screenLayoutCached = true;
    }

    private float SlideDistance()
    {
        var viewport = listScreen.parent as RectTransform;
        float width = viewport == null ? 0f : viewport.rect.width;
        return Mathf.Max(width, listScreen.rect.width, detailScreen.rect.width) + 24f;
    }

    private void RestoreScreenLayout()
    {
        if (!screenLayoutCached) return;
        listScreen.anchoredPosition = listHome;
        detailScreen.anchoredPosition = detailHome;
        listScreen.localScale = listScreenScale;
        detailScreen.localScale = detailScreenScale;
        if (listScroll != null) listScroll.localScale = Vector3.one;
    }

    private void CacheProjectorRays()
    {
        if (!leftRayCached && projectorRayLeft != null)
        {
            leftRayAlpha = projectorRayLeft.color.a;
            leftRayScale = projectorRayLeft.rectTransform.localScale;
            leftRayCached = true;
        }
        if (!rightRayCached && projectorRayRight != null)
        {
            rightRayAlpha = projectorRayRight.color.a;
            rightRayScale = projectorRayRight.rectTransform.localScale;
            rightRayCached = true;
        }
    }

    private void OnDisable()
    {
        if (!isOpen) return;
        presentation?.Kill();
        FinishCleanup();
    }
    public void Show()
    {
        if (isOpen)
            return;

        CacheProjectorRays();
        CacheScreenLayout();
        isOpen = true;

        menu.OnSelected += HandleSelect;
        menu.OnCanceled += HandleCancel;

        if (HasList)
        {
            listMenu.OnSelected += HandleListSelect;
            listMenu.OnCanceled += HandleListCancel;
            listMenu.OnSelectionChanged += RefreshList;
        }

        animator.SetBool("Open", true);
        virtualCamera.Priority = 999;
        UIManager.Instance.HUDVisible(false);
        if (HasList) ShowList();
        else menu.Open();

        if (currentTraining == null)
            return;

        TrainingRoomSaveData best = DataManager.Instance.GetTrainingRoomRecord(currentTraining.trainingId);
        if (best != null)
        {
            hitCountText.text = best.bestHitCount == int.MaxValue ? "-" : best.bestHitCount.ToString();
            timeTakenText.text = best.bestTimeSeconds == float.MaxValue ? "-" : TrainingScoreFormatHelper.FormatTime(best.bestTimeSeconds);

            TrainingScoreFormatHelper.GradeToText(best.bestGrade, gradeText);
            if (HasList && (best.bestHitCount == int.MaxValue || best.bestTimeSeconds == float.MaxValue))
                gradeText.text = "-";
        }
        else
        {
            hitCountText.text = "-";
            timeTakenText.text = "-";
            TrainingScoreFormatHelper.GradeToText(default, gradeText);
        }
        
    }
    private void HandleSelect(int index)
    {
        if (isTransitioning) return;
        switch (index)
        {
            case 0:
                HandleCancel();
                break;
            case 1:

                OnConfirmEnterRoom?.Invoke();
                Cleanup();
                break;
        }
    }
    private void HandleCancel()
    {
        if (isTransitioning) return;
        if (HasList && menu.IsActive) ShowList();
        else Cleanup();
    }

    private bool HasList => listMenu != null && listPanel != null && detailPanel != null;

    private void ShowList()
    {
        if (!HasPresentation)
        {
            if (menu.IsActive) menu.Close();
            detailPanel.SetActive(false);
            listPanel.SetActive(true);
            listMenu.Open();
            return;
        }

        if (menu.IsActive)
        {
            TransitionToList();
            return;
        }

        isTransitioning = true;
        RestoreScreenLayout();
        listPanel.SetActive(true);
        detailPanel.SetActive(false);
        listPanelGroup.alpha = 0f;
        listScreen.localScale = new Vector3(listScreenScale.x, listScreenScale.y * .015f, listScreenScale.z);
        listScreen.anchoredPosition = listHome + Vector2.down * 12f;
        listMenu.Open();
        listMenu.InputSuppressed = true;
        SetProjectorStart(projectorRayLeft, leftRayScale);
        SetProjectorStart(projectorRayRight, rightRayScale);
        presentation = DOTween.Sequence().SetUpdate(true);
        AddProjectorTween(presentation, projectorRayLeft, leftRayAlpha, leftRayScale, true);
        AddProjectorTween(presentation, projectorRayRight, rightRayAlpha, rightRayScale, true);
        // 빔이 먼저 연결되고 그 위에서 화면 전체가 얇은 선부터 펼쳐진다.
        presentation.Insert(.18f, listPanelGroup.DOFade(1f, .2f));
        presentation.Insert(.18f, listScreen.DOScaleY(listScreenScale.y, .42f).SetEase(Ease.OutCubic));
        presentation.Insert(.18f, listScreen.DOAnchorPosY(listHome.y, .42f).SetEase(Ease.OutCubic));
        presentation.OnComplete(() => { isTransitioning = false; listMenu.InputSuppressed = false; });
    }

    private void HandleListSelect(int index)
    {
        if (isTransitioning || index != 0 || currentTraining == null) return;
        if (!HasPresentation)
        {
            listMenu.Close(); listPanel.SetActive(false); detailPanel.SetActive(true); menu.Open();
            return;
        }

        isTransitioning = true;
        listMenu.InputSuppressed = true;
        RestoreScreenLayout();
        detailPanel.SetActive(true);
        listPanelGroup.alpha = 1f;
        detailPanelGroup.alpha = 1f;
        float distance = SlideDistance();
        detailScreen.anchoredPosition = detailHome + Vector2.right * distance;
        presentation = DOTween.Sequence().SetUpdate(true);
        // 제목·미리보기를 포함한 한 장의 투사 화면을 왼쪽으로 넘긴다.
        presentation.Join(listScreen.DOAnchorPos(listHome + Vector2.left * distance, .52f).SetEase(Ease.InOutCubic));
        presentation.Join(detailScreen.DOAnchorPos(detailHome, .52f).SetEase(Ease.InOutCubic));
        presentation.OnComplete(() =>
        {
            listMenu.Close(); listPanel.SetActive(false); listMenu.InputSuppressed = false;
            listScreen.anchoredPosition = listHome;
            menu.Open(); isTransitioning = false;
        });
    }

    private void TransitionToList()
    {
        isTransitioning = true;
        if (menu is TrainingMenu detailMenu) detailMenu.InputSuppressed = true;
        RestoreScreenLayout();
        listPanel.SetActive(true);
        listPanelGroup.alpha = 1f;
        detailPanelGroup.alpha = 1f;
        float distance = SlideDistance();
        listScreen.anchoredPosition = listHome + Vector2.left * distance;
        presentation = DOTween.Sequence().SetUpdate(true);
        presentation.Join(detailScreen.DOAnchorPos(detailHome + Vector2.right * distance, .52f).SetEase(Ease.InOutCubic));
        presentation.Join(listScreen.DOAnchorPos(listHome, .52f).SetEase(Ease.InOutCubic));
        presentation.OnComplete(() =>
        {
            menu.Close();
            if (menu is TrainingMenu detailMenu) detailMenu.InputSuppressed = false;
            detailPanel.SetActive(false); detailScreen.anchoredPosition = detailHome;
            listMenu.Open(); isTransitioning = false;
        });
    }

    private static void SetProjectorStart(Image ray, Vector3 originalScale)
    {
        if (ray == null) return;
        var color = ray.color; color.a = 0f; ray.color = color;
        ray.rectTransform.localScale = new Vector3(originalScale.x, originalScale.y * .04f, originalScale.z);
    }

    private static void AddProjectorTween(DG.Tweening.Sequence sequence, Image ray, float alpha, Vector3 originalScale, bool opening, float delay = 0f)
    {
        if (ray == null) return;
        var target = opening ? originalScale : new Vector3(originalScale.x, originalScale.y * .04f, originalScale.z);
        sequence.Insert(delay, ray.DOFade(opening ? alpha : 0f, opening ? .24f : .32f));
        sequence.Insert(delay, ray.rectTransform.DOScale(target, opening ? .38f : .4f).SetEase(Ease.InOutSine));
    }

    private void HandleListCancel()
    {
        if (!isTransitioning) Cleanup();
    }

    private void RefreshList(int index)
    {
        if (index > 0)
        {
            if (listTitleText != null) listTitleText.text = $"TRAINING {index + 1:00}";
            if (listSummaryText != null) listSummaryText.text = "준비 중인 훈련입니다.";
            if (listCompletionText != null) listCompletionText.text = "LOCKED";
            return;
        }

        if (index != 0 || currentTraining == null) return;
        TrainingRoomSaveData record = DataManager.Instance.GetTrainingRoomRecord(currentTraining.trainingId);
        bool completed = record != null && record.bestHitCount != int.MaxValue && record.bestTimeSeconds != float.MaxValue;
        if (listTitleText != null) listTitleText.text = currentTraining.displayName;
        if (listSummaryText != null) listSummaryText.text = $"{listDescription}\n\nREWARD  {currentTraining.baseRewardGold} G";
        if (listCompletionText != null) listCompletionText.text = completed ? "COMPLETED" : "NOT CLEARED";
    }

    private void Cleanup()
    {
        if (!isOpen || isTransitioning) return;
        if (!HasPresentation)
        {
            animator.SetBool("Open", false);
            FinishCleanup();
            return;
        }

        isTransitioning = true;
        if (listMenu.IsActive) listMenu.InputSuppressed = true;
        if (menu is TrainingMenu detailMenu && menu.IsActive) detailMenu.InputSuppressed = true;
        animator.SetBool("Open", false);
        presentation = DOTween.Sequence().SetUpdate(true);
        var screen = listPanel.activeSelf ? listScreen : detailScreen;
        var group = listPanel.activeSelf ? listPanelGroup : detailPanelGroup;
        float height = listPanel.activeSelf ? listScreenScale.y : detailScreenScale.y;
        presentation.Insert(0f, group.DOFade(0f, .18f));
        presentation.Insert(0f, screen.DOScaleY(height * .015f, .2f).SetEase(Ease.InCubic));
        // 화면을 먼저 거둔 뒤 단말로 연결되는 빔을 끈다.
        AddProjectorTween(presentation, projectorRayLeft, leftRayAlpha, leftRayScale, false, .16f);
        AddProjectorTween(presentation, projectorRayRight, rightRayAlpha, rightRayScale, false, .16f);
        presentation.OnComplete(FinishCleanup);
    }

    private void FinishCleanup()
    {
        presentation?.Kill();
        presentation = null;
        isOpen = false;
        isTransitioning = false;
        if (listMenu != null) listMenu.InputSuppressed = false;
        if (menu is TrainingMenu detailMenu) detailMenu.InputSuppressed = false;
        RestoreScreenLayout();

        menu.OnSelected -= HandleSelect;
        menu.OnCanceled -= HandleCancel;

        if (HasList)
        {
            listMenu.OnSelected -= HandleListSelect;
            listMenu.OnCanceled -= HandleListCancel;
            listMenu.OnSelectionChanged -= RefreshList;
            if (listMenu.IsActive && UIManager.Instance != null) listMenu.Close();
        }

        if (animator != null) animator.SetBool("Open", false);
        if (virtualCamera != null) virtualCamera.Priority = -1;
        if (UIManager.Instance != null) UIManager.Instance.HUDVisible(true);
        if (menu.IsActive && UIManager.Instance != null) menu.Close();

        if (GameManager.Instance != null && GameManager.Instance.Player != null)
            GameManager.Instance.Player.AbilityManager.InterruptAllAbilities();
    }
}
