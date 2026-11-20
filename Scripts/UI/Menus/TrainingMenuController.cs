using DG.Tweening;
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
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
    [SerializeField, Tooltip("현재 훈련의 성적 조건입니다. 미흡을 제외하고 훈련 데이터에서 읽습니다.")] private TextMeshProUGUI gradeConditionsText;

    [SerializeField] private TrainingGradeConditionsView gradeConditionsView;

    [Header("Training List (Optional)")]
    [SerializeField] private TrainingMenu listMenu;
    [SerializeField] private GameObject listPanel;
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private TextMeshProUGUI listTitleText;
    [SerializeField] private TextMeshProUGUI listSummaryText;
    [SerializeField] private TextMeshProUGUI listCompletionText;
    [SerializeField, Tooltip("훈련 목록의 원본입니다. 비우면 Resources/Training/TrainingCatalog를 씁니다.")]
    private TrainingCatalogSO catalog;

    // 테스트 훈련(진행 순서에 들지 않는 항목)의 줄 앞에 번호 대신 붙이는 표시.
    private const string TestRowMark = "TEST";

    // 지금 목록 줄에 올라 있는 훈련. 줄의 순서가 이 목록의 순서다. 메뉴를 열 때마다 훈련 목록에서 다시 채운다.
    private readonly List<TrainingCatalogSO.Entry> shownEntries = new List<TrainingCatalogSO.Entry>();
    private readonly List<string> rowLabels = new List<string>();
    private TrainingCatalogSO Catalog => catalog != null ? catalog : TrainingCatalogSO.Load();

    [Header("Training Menu Animation")]
    [SerializeField] private RectTransform listScroll;
    [SerializeField] private CanvasGroup listPanelGroup;
    [SerializeField] private CanvasGroup detailPanelGroup;
    [SerializeField] private Image projectorRayLeft;
    [SerializeField] private Image projectorRayRight;

    [SerializeField] private TextMeshProUGUI detailTitleText;
    [SerializeField] private TextMeshProUGUI detailDescriptionText;
    // 목록에서 고른 훈련의 WaveData. 방과 도착 지점은 입구(RoomTrigger)가 정한다. 목록이 없는 패널에서는 null(방의 기본 데이터)이다.
    public WaveDataSO SelectedWaveData { get; private set; }
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
        if (HasList)
        {
            BuildList();
            ShowList();
        }
        else menu.Open();

        RefreshUI();
    }

    private void RefreshUI()
    {
        if (currentTraining == null) return;
        if (gradeConditionsView != null) gradeConditionsView.SetTraining(currentTraining);
        else if (gradeConditionsText != null) gradeConditionsText.text = TrainingRecordView.FormatConditions(currentTraining);
        TrainingRoomSaveData best = DataManager.Instance.GetTrainingRoomRecord(currentTraining.trainingId);
        if (DataManager.IsCleared(best))
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
            gradeText.text = "-";
            gradeText.color = Color.white;
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
        var pageMotion = listPanel.GetComponent<TrainingMenuPageMotion>();
        if (pageMotion != null) pageMotion.Replay();
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
        if (isTransitioning) return;
        // 줄에 올라 있는 훈련만 고를 수 있다(아직 열리지 않은 훈련은 줄이 없다).
        var entry = EntryAt(index);
        if (entry == null) return;
        currentTraining = entry.definition;
        SelectedWaveData = entry.waveData;
        if (detailTitleText != null) detailTitleText.text = entry.Title;
        if (detailDescriptionText != null) detailDescriptionText.text = entry.summary;
        RefreshUI();
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

    // 훈련 목록에서 지금 보일 훈련을 줄에 채운다: 훈련 메뉴에 나오는 항목 가운데 열린 것만, 진행 순서대로.
    // 아직 열리지 않은 훈련은 자리 표시 없이 줄 자체를 끈다(앞 훈련을 수료하면 다음에 열 때 나타난다).
    private void BuildList()
    {
        TrainingCatalogSO source = Catalog;
        shownEntries.Clear();
        rowLabels.Clear();
        if (source != null) source.CollectListed(DataManager.Instance, shownEntries);
        if (shownEntries.Count > listMenu.RowCount)
        {
            Debug.LogWarning($"[{name}] 훈련 {shownEntries.Count}개 가운데 목록 줄 {listMenu.RowCount}개만 표시합니다. Prefab의 줄을 늘려야 합니다.", this);
            shownEntries.RemoveRange(listMenu.RowCount, shownEntries.Count - listMenu.RowCount);
        }
        foreach (var entry in shownEntries)
        {
            // 번호는 진행 순서의 번호라서 테스트 훈련이 섞여도 바뀌지 않는다.
            int number = source.ProgressionNumber(entry);
            rowLabels.Add((number > 0 ? number.ToString("00") : TestRowMark) + "  " + entry.Title);
        }
        listMenu.SetRows(rowLabels);
    }

    private TrainingCatalogSO.Entry EntryAt(int index) => index >= 0 && index < shownEntries.Count ? shownEntries[index] : null;

    private void RefreshList(int index)
    {
        var entry = EntryAt(index);
        if (entry == null)
        {
            // 훈련 목록 에셋이 없거나 비어 있을 때만 여기에 온다(첫 훈련은 처음부터 열려 있다).
            if (listTitleText != null) listTitleText.text = "TRAINING";
            if (listSummaryText != null) listSummaryText.text = "지금 받을 수 있는 훈련이 없습니다.";
            if (listCompletionText != null) listCompletionText.text = "";
            return;
        }

        if (listTitleText != null) listTitleText.text = entry.Title;
        if (listSummaryText != null) listSummaryText.text = entry.summary + CertificateLine(entry);
        if (listCompletionText != null) listCompletionText.text = IsCompleted(entry.definition) ? "COMPLETED" : "NOT CLEARED";
    }

    // 요약 아래의 수료 보상 줄. 훈련 필증을 주는 훈련에서만, 아직 받지 않았으면 보상을, 받았으면 수령 완료를 적는다.
    private static string CertificateLine(TrainingCatalogSO.Entry entry)
    {
        if (!entry.grantsCertificate) return "";
        string certificate = TrainingCatalogSO.CertificateName;
        return DataManager.Instance.HasTrainingCertificate(entry.TrainingId)
            ? $"\n\n{certificate} 수령 완료"
            : $"\n\n수료 보상  {certificate} 1";
    }

    // 수료 판정은 훈련 목록의 열림 판정과 같은 기준(DataManager.IsCleared)을 쓴다.
    private static bool IsCompleted(TrainingDefinitionSO training) =>
        DataManager.IsCleared(DataManager.Instance.GetTrainingRoomRecord(training.trainingId));

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
