using DG.Tweening;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleMenu : SelectableMenuUI
{
    public static event Action OnTitleExitStarted;
    public static event Action OnOptionSelected;

    [Header("Scene")]
    [SerializeField] private string mainSceneName = "MainScene";

    [Header("StartGame Transition Effect")]
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private Animator titleAnimator;
    [SerializeField] private FeedbackDataSO startFeedback;
    [SerializeField] private FeedbackDataSO warpFeedback;

    [Header("Save Select")]
    [SerializeField] private TitleSaveSelectUI saveSelect;
    [SerializeField] private TitlePanelMotion panelMotion;
    [SerializeField] private TitleSaveStatusView saveStatus;

    protected override void ProcessMove(Vector2 direction)
    {
        int previous = currentIndex;
        base.ProcessMove(direction);
        if (previous != currentIndex) SettleHighlight();
    }

    protected override void OnPointerEnter(int index)
    {
        int previous = currentIndex;
        base.OnPointerEnter(index);
        if (previous != currentIndex) SettleHighlight();
    }

    protected override void OnPointerClick(int index)
    {
        if (!IsActive || (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked)) return;
        int previous = currentIndex;
        ChangeIndex(index);
        if (previous != currentIndex) SettleHighlight();
        base.OnPointerClick(index);
    }

    private void SettleHighlight()
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].DOKill();
            buttons[i].DOScale(i == currentIndex ? 1.15f : 1f, .16f)
                .SetEase(Ease.OutCubic).SetUpdate(true);
        }
    }

    private bool isTransitioning;
    private UIManager exitInputOwner;
    public float PlayerDisappearTime => 1.0833334f;

    private void Start()
    {
        OnSelected += HandleSelected;
        OnCanceled += HandleCanceled;
        if (saveSelect != null)
        {
            saveSelect.SlotChosen += HandleSlotChosen;
            saveSelect.Canceled += HandleSaveSelectCanceled;
        }

        UIManager.Instance.StartScreenFade(0f, 1f);
    }
    public void OpenMenu()
    {
        if (isTransitioning || IsActive)
            return;

        Open();
    }

    // 메뉴 판은 열릴 때 펼쳐지고 닫힐 때 접힌다. 위쪽의 세이브 현황은 열 때마다 파일을 다시 읽는다.
    public override void Open()
    {
        base.Open();
        if (saveStatus != null) saveStatus.Refresh();
        if (panelMotion != null) panelMotion.Open();
    }

    public override void Close()
    {
        base.Close();
        if (panelMotion != null) panelMotion.Close();
    }

    private void OnDestroy()
    {
        if (exitInputOwner != null) exitInputOwner.SetUIInputBlock(false);
        OnSelected -= HandleSelected;
        OnCanceled -= HandleCanceled;
        if (saveSelect != null)
        {
            saveSelect.SlotChosen -= HandleSlotChosen;
            saveSelect.Canceled -= HandleSaveSelectCanceled;
        }
    }

    private void HandleSelected(int index)
    {
        if (isTransitioning)
            return;

        switch (index)
        {
            case 0:
                OpenSaveSelect();
                break;

            case 1:
                OpenOption();
                break;

            case 2:
                QuitGame();
                break;
        }
    }

    private void HandleCanceled()
    {
        // Title 최상위 메뉴에서는 우선 아무 처리도 하지 않는다.
        // 나중에 QuitConfirmPopup을 열도록 확장 가능.
    }

    // 게임 시작은 세이브 파일을 고른 뒤에 한다. 메뉴 판을 접고 세이브 선택 화면을 펼친다.
    private void OpenSaveSelect()
    {
        if (saveSelect == null)
        {
            // 세이브 선택 화면이 연결되지 않은 구성에서는 1번 슬롯으로 바로 시작한다.
            HandleSlotChosen(0);
            return;
        }

        Close();
        saveSelect.Open();
    }

    private void HandleSlotChosen(int slot)
    {
        if (isTransitioning)
            return;

        // 고른 칸의 파일을 읽거나 새로 만들지 못하면 메뉴로 돌아온다.
        if (!DataManager.Instance.SelectSlot(slot))
        {
            if (!IsActive) Open();
            return;
        }

        StartGame(DataManager.Instance.CurrentSaveData.isFirstStart);
    }

    private void HandleSaveSelectCanceled()
    {
        if (isTransitioning || IsActive)
            return;

        Open();
    }

    private void StartGame(bool firstStart)
    {
        isTransitioning = true;
        // 처음 만든 세이브(게임 최초 시작)는 Nightmare 도입부로, 그 밖에는 Lobby로 들어간다.
        NightmareIntroSequence.QueueNextStart(firstStart);
        if (IsActive) Close();

        exitInputOwner = UIManager.Instance;
        if (exitInputOwner != null) exitInputOwner.SetUIInputBlock(true);
        OnTitleExitStarted?.Invoke();

        StartCoroutine(StartGameRoutine());
    }

    private IEnumerator StartGameRoutine()
    {
        titleAnimator.SetTrigger("StartGameTransition");

        Transform playerTransform = playerAnimator.gameObject.transform;

        FeedbackPlayer.Instance.Play(startFeedback, playerAnimator.gameObject.transform);
        playerAnimator.SetBool("Warp", true);
        yield return new WaitForSeconds(.6f);
        UIManager.Instance.StartScreenFade(1f, PlayerDisappearTime - .6f);
        yield return new WaitForSeconds(.5f);

        FeedbackPlayer.Instance.Play(warpFeedback, new Vector3(playerTransform.position.x, playerTransform.position.y + 1.5f, 0));

        // 화면이 다 어두워진 지금부터 MainScene을 뒤에서 불러온다. 남은 전환 연출이 재생되는 시간도 불러오는 데 쓰고,
        // 그동안 암전 위에 로딩 표시를 띄운다(한 번에 불러오면 그동안 화면이 멈춘다).
        var loading = SceneLoadingSequence.Begin(mainSceneName);

        UIManager.Instance.StartWireframeEffect(3f, Color.red, 0.2f);

        // 암전 시작
        yield return new WaitForSeconds(0.2f);
        UIManager.Instance.StartWireframeEffect(0f, Color.red, 0.5f);

        yield return new WaitForSeconds(0.5f);

        // 이동 완료
        yield return loading.Finish();
    }

    private void OpenOption()
    {
        OnOptionSelected?.Invoke();
    }

    private void QuitGame()
    {
        isTransitioning = true;
        Close();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}