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
    [SerializeField] private FeedbackData startFeedback;
    [SerializeField] private FeedbackData warpFeedback;

    private bool isTransitioning;

    private void Start()
    {
        OnSelected += HandleSelected;
        OnCanceled += HandleCanceled;

        UIManager.Instance.StartScreenFade(0f, 1f);
    }
    public void OpenMenu()
    {
        if (isTransitioning || IsActive)
            return;

        Open();
    }
    private void OnDestroy()
    {
        OnSelected -= HandleSelected;
        OnCanceled -= HandleCanceled;
    }

    private void HandleSelected(int index)
    {
        if (isTransitioning)
            return;

        switch (index)
        {
            case 0:
                StartGame();
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

    private void StartGame()
    {
        isTransitioning = true;
        Close();

        OnTitleExitStarted?.Invoke();

        StartCoroutine(StartGameRoutine());
    }

    private IEnumerator StartGameRoutine()
    {
        titleAnimator.SetTrigger("StartGameTransition");

        Transform playerTransform = playerAnimator.gameObject.transform;

        FeedbackPlayer.Instance.Play(startFeedback, playerAnimator.gameObject.transform);
        playerAnimator.SetBool("Warp", true);
        yield return new WaitForSeconds(1.1f);

        FeedbackPlayer.Instance.Play(warpFeedback, new Vector3(playerTransform.position.x, playerTransform.position.y + 1.5f, 0));

        UIManager.Instance.StartScreenFade(1f, 0.2f);
        UIManager.Instance.StartWireframeEffect(3f, Color.red, 0.2f);

        // 암전 시작
        yield return new WaitForSeconds(0.2f);
        UIManager.Instance.StartWireframeEffect(0f, Color.red, 0.5f);

        yield return new WaitForSeconds(0.5f);

        // 이동 완료
        SceneManager.LoadScene(mainSceneName);
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