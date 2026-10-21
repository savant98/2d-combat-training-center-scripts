using DG.Tweening;
using System;
using UnityEngine;

public class PauseMenuController : MonoBehaviour
{
    public static event Action OnOptionSelected;

    [SerializeField] private SelectableMenuUI menu;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private GameObject root;

    private bool isOpen;

    public bool IsOpen => isOpen;

    private void Start()
    {
        root.SetActive(false);
    }
    public void Show()
    {
        if (isOpen)
            return;

        isOpen = true;

        if (GameTimeController.Instance != null)
            GameTimeController.Instance.PushPause();

        root.SetActive(true);

        menu.OnSelected += HandleSelect;
        menu.OnCanceled += HandleCancel;
        menu.Open();

        canvasGroup.DOKill();
        canvasGroup.alpha = 0f;
        canvasGroup.DOFade(1f, 0.15f).SetUpdate(true);
    }

    public void Hide()
    {
        if (!isOpen)
            return;

        Cleanup();

        canvasGroup.DOKill();
        canvasGroup.DOFade(0f, 0.12f)
            .SetUpdate(true)
            .OnComplete(() => root.SetActive(false));

        if (GameTimeController.Instance != null)
            GameTimeController.Instance.PopPause();
    }

    private void HandleSelect(int index)
    {
        switch (index)
        {
            case 0: // Resume
                Hide();
                break;

            case 1: // Option
                // OptionMenuController.Show();
                // OptionMenu도 SelectableMenuUI라면 menu.Open() 하는 순간
                // UIManager 입력 Stack 최상단으로 올라간다.
                OnOptionSelected?.Invoke();
                break;

            case 2: // Retry
                ExitGame();
                break;
        }
    }
    private void ExitGame()
    {
        if (GameTimeController.Instance != null)
            GameTimeController.Instance.PopPause();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void HandleCancel()
    {
        Hide();
    }

    private void Cleanup()
    {
        isOpen = false;

        menu.OnSelected -= HandleSelect;
        menu.OnCanceled -= HandleCancel;
        menu.Close();
    }
}