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

    [Header("Feedback")]
    [SerializeField] private FeedbackData sparkBothFeedback;

    private TitleWorldInfiniteScrollLayer[] scrollLayers;
    private bool transitionStarted;

    private void Awake()
    {
        CacheScrollLayers();

        // Intro 중에는 Player가 화면에 나타나지 않는 상태로 둔다.
        playerAnimator.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (transitionStarted)
            return;

        if (!PressedAnyMappedButton())
            return;

        StartIntroTransition();
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
        if (playerAnimator != null)
        {
            playerAnimator.gameObject.SetActive(true);
            playerAnimator.SetTrigger(playerWarpParameter);
            FeedbackPlayer.Instance.Play(sparkBothFeedback, playerAnimator.gameObject.transform);
        }


        if (titleMenu != null)
            titleMenu.Open();
    }
}