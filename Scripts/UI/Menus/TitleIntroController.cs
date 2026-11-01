using System;
using UnityEngine;

public class TitleIntroController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TitleMenu titleMenu;
    [SerializeField] private Animator introUIAnimator;
    [SerializeField] private Animator playerAnimator;

    [Header("Scroll")]
    [Tooltip("TitleWorldInfiniteScrollLayer들이 들어있는 부모 오브젝트")]
    [SerializeField] private Transform scrollRoot;

    [Header("Animator Parameters")]
    [SerializeField] private string introTransitionTrigger = "Transition";
    [SerializeField] private string playerWarpParameter = "Warp";
    [SerializeField] private string introCompletedState = "TitleIntroTransition";

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO sparkBothFeedback;

    private TitleWorldInfiniteScrollLayer[] scrollLayers;
    private bool transitionStarted;
    private bool externalIntroActive;
    private bool transitionCompleted;
    public event Action PlayerRevealed;

    private void Awake()
    {
        transitionStarted = transitionCompleted = externalIntroActive = false;
        CacheScrollLayers();

        // Intro 중에는 Player가 화면에 나타나지 않는 상태로 둔다.
        playerAnimator.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (transitionStarted || externalIntroActive)
            return;

        if (!PressedAnyMappedButton())
            return;

        StartIntroTransition();
    }

    // 훈련 고지서 연출이 타이틀의 기존 입력·Animation Event 진입을 임시 소유한다.
    public void SetExternalIntroActive(bool active)
    {
        externalIntroActive = active;
    }

    public void ResumeAfterTrainingNotice()
    {
        externalIntroActive = false;
        if (!transitionStarted) StartIntroTransition();
    }
    // 새 연출은 배경 복구가 끝난 뒤 기존 UI의 완료 자세와 Player 등장을 같은 프레임에 연결한다.
    public void ResumeAfterTrainingNoticeImmediately()
    {
        externalIntroActive = false;
        transitionStarted = true;
        if (introUIAnimator != null && !string.IsNullOrEmpty(introCompletedState))
        {
            introUIAnimator.Play(introCompletedState, 0, 1f);
            introUIAnimator.Update(0f);
        }
        CompleteIntroTransition();
    }
    private void CacheScrollLayers()
    {
        if (scrollRoot == null)
        {
            scrollLayers = FindObjectsByType<TitleWorldInfiniteScrollLayer>(
                FindObjectsSortMode.None);

            return;
        }

        scrollLayers = scrollRoot.GetComponentsInChildren<TitleWorldInfiniteScrollLayer>(true);
    }

    private bool PressedAnyMappedButton()
    {
        return InputManager.Pressed(Button.Jump)
            || InputManager.Pressed(Button.Attack)
            || InputManager.Pressed(Button.Dash)
            || InputManager.Pressed(Button.Gear)
            || InputManager.Pressed(Button.SecondaryWeapon)
            || InputManager.Pressed(Button.Interact)
            || InputManager.Pressed(Button.SwapWeapon)
            || InputManager.Pressed(Button.Escape);
    }

    private void StartIntroTransition()
    {
        transitionStarted = true;

        if (introUIAnimator != null)
            introUIAnimator.SetTrigger(introTransitionTrigger);
        else
            CompleteIntroTransition();
    }

    /// <summary>
    /// Intro UI Animation의 마지막 프레임에서 Animation Event로 호출한다.
    /// </summary>
    public void CompleteIntroTransition()
    {
        if (externalIntroActive || transitionCompleted) return;
        transitionCompleted = true;
        if (playerAnimator != null)
        {
            playerAnimator.gameObject.SetActive(true);
            playerAnimator.SetTrigger(playerWarpParameter);
            FeedbackPlayer.Instance.Play(sparkBothFeedback, playerAnimator.gameObject.transform);
        }


        PlayerRevealed?.Invoke();

        if (titleMenu != null)
            titleMenu.Open();
    }
}