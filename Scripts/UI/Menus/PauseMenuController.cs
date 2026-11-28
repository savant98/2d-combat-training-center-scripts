using System;
using UnityEngine;

public class PauseMenuController : MonoBehaviour
{
    public static event Action OnOptionSelected;

    [SerializeField] private PauseMenu menu;
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
        EnsurePauseCanvasOrder();

        menu.OnSelected += HandleSelect;
        menu.OnCanceled += HandleCancel;
        menu.Open();
    }

    public void Hide()
    {
        if (!isOpen)
            return;

        isOpen = false;
        menu.OnSelected -= HandleSelect;
        menu.OnCanceled -= HandleCancel;
        menu.Close(() => root.SetActive(false));

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

    // 일시정지 창은 HUD·튜토리얼 안내(50)보다 앞, 옵션 창(200)보다 뒤에 오도록 Canvas 정렬 순서를 맞춘다.
    private void EnsurePauseCanvasOrder()
    {
        if (root == null) return;
        var parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null) return;
        var pauseCanvas = root.GetComponent<Canvas>();
        if (pauseCanvas == null) pauseCanvas = root.AddComponent<Canvas>();
        if (root.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        pauseCanvas.overrideSorting = true;
        pauseCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        pauseCanvas.worldCamera = parentCanvas.worldCamera;
        pauseCanvas.sortingOrder = Mathf.Max(190, parentCanvas.sortingOrder + 1);
    }

}