using DG.Tweening;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class OptionMenu : SelectableMenuUI
{
    [Header("Root")]
    [SerializeField] private GameObject rootObject;

    [Header("Option Components")]
    [SerializeField] private CanvasGroup optionCanvas;
    [SerializeField] private TextMeshProUGUI languageText;
    [SerializeField] private TextMeshProUGUI resolutionText;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    private void Start()
    {
        OnSelected += HandleSelected;
        OnCanceled += HandleCanceled;

        optionCanvas.alpha = 0f;
        TitleMenu.OnOptionSelected += OpenMenu;
        PauseMenuController.OnOptionSelected += OpenMenu;

        rootObject.SetActive(false);
    }
    public void OpenMenu()
    {
        if (IsActive)
            return;

        optionCanvas.DOFade(1f, 0.5f).SetUpdate(true);
        rootObject.SetActive(true);
        Open();
    }
    private void OnDestroy()
    {
        OnSelected -= HandleSelected;
        OnCanceled -= HandleCanceled;

        TitleMenu.OnOptionSelected -= OpenMenu;
        PauseMenuController.OnOptionSelected -= OpenMenu;
    }

    protected override void ProcessMove(Vector2 dir)
    {
        int nextIndex = currentIndex;

        // 1. 상/하 입력으로 옵션 항목들 사이를 이동
        if (dir.y > 0.5f) nextIndex--;
        else if (dir.y < -0.5f) nextIndex++;

        // 2. 좌/우 입력으로 항목의 값 조절
        if (Mathf.Abs(dir.x) > 0.5f)
        {
            AdjustValue(currentIndex, dir.x);
            // 값을 조절하는 입력이므로 상하 이동은 취소

            PlayFeedback(moveFeedback);
            // 값 조절시에도 Feedback 재생
            return;
        }

        ChangeIndex(nextIndex);
        
    }

    private void AdjustValue(int index, float dirX)
    {
        int direction = dirX > 0 ? 1 : -1;

        switch (index)
        {
            case 0: // 언어
                break;
            case 1: // 해상도
                break;
            case 2: // 전체화면 토글
                break;
            case 3: // Bloom
                break;
            case 4: // 효과음
                break;
            case 5: // 배경음
                break;
            case 6: // 키 설정 버튼
                break;
        }
    }
    private void HandleSelected(int index)
    {
        switch (index)
        {
            case 0: // 언어
                break;
            case 1: // 해상도
                break;
            case 2: // 전체화면 토글
                break;
            case 3: // Bloom
                break;
            case 4: // 효과음
                break;
            case 5: // 배경음
                break;
            case 6: // 키 설정 버튼
                break;
        }
    }

    private void HandleCanceled()
    {
        // 나중에 QuitConfirmPopup을 열도록 확장 가능.
        optionCanvas.DOFade(0f, 0.5f).SetUpdate(true).OnComplete(() => rootObject.SetActive(false));
        Close();
    }
}