using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Lobby 안내(2026-11-14). Nightmare에게 패배해 Lobby에 처음 온 훈련생에게 휴대용 단말기와 훈련 메뉴를 알려 준다.
/// - 순서(사용자 요청): 오른쪽 위 단말기 아이콘 → 환영 메일 도착(아이콘에 느낌표) → 단말기 여는 키 안내 → 메일 확인
///   → 단말기를 닫으면 화살표로 훈련 메뉴가 있는 곳을 가리킨다 → 훈련 메뉴를 열면 끝.
/// - 메일이 도착하고 그 메일을 확인할 때까지는 해야 할 한 가지만 남긴다(사용자 요청: "단말기만 누르도록 포커싱(나머지 주변 어둡게)해 주고
///   다른 키 입력 자체를 막으면 돼. 이후 메일 확인하는 것까지 이런 식으로"). 화면을 어둡게 덮고 눌러야 할 곳만 밝게 남기며(FocusDimGraphic),
///   그 밖의 입력은 받지 않는다(소리도 내지 않는다).
///     ① 단말기를 열기 전: 단말기 아이콘만 밝다. 이동·점프·공격·상호작용·일시정지를 받지 않고 단말기 여는 키만 받는다.
///     ② 단말기의 첫 화면: 메시지 수신함 카드만 밝다. 그 카드를 여는 조작만 받는다.
///     ③ 수신함: 환영 메일만 밝다. 그 메일을 여는 조작만 받는다. 메일이 열리면 어둠이 걷히고 무엇이든 할 수 있다.
/// - 진행 단계는 세이브(SaveData.onboardingStep)에 남아 다시 접속해도 이어진다. 도입부를 마치지 않은 세이브(isFirstStart)에서는 아무것도 하지 않는다.
/// - 단말기 아이콘은 안내가 끝난 뒤에도 Lobby에서 계속 보인다(단말기는 Lobby에서만 열린다).
/// - 표시는 실제 시간으로 움직인다(단말기가 열려 있으면 게임 시간이 멈춘다). 표시 전용이며 판정·이동과는 무관하다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyOnboarding : MonoBehaviour
{
    // 세이브에 남기는 단계(0 = 시작 전, SaveData.OnboardingDone = 끝)
    public const int StepMailArrived = 10;      // 환영 메일이 도착했다. 단말기를 열어 메일을 확인해야 한다
    public const int StepMailRead = 30;         // 메일을 확인했다. 훈련 메뉴로 안내한다
    // 환영 메일이 있는 자리(단말기의 수신함 탭, 맨 위 항목)
    public const int MailTab = 3, MailEntry = 0;

    [Header("연결")]
    [SerializeField] private InventoryTerminalMenu terminal;
    [SerializeField, Tooltip("훈련 메뉴의 입구. 비워 두면 Lobby에서 훈련 메뉴를 여는 입구를 찾는다")] private RoomTrigger trainingEntrance;

    [Header("단말기 아이콘(오른쪽 위)")]
    [SerializeField] private CanvasGroup iconGroup;
    [SerializeField] private RectTransform iconRoot;
    [SerializeField] private RectTransform badge;
    [SerializeField] private CanvasGroup toastGroup;
    [SerializeField] private RectTransform toastRoot;

    [Header("안내")]
    [SerializeField, Tooltip("단말기를 열라는 안내(단말기가 닫혀 있을 때)")] private CanvasGroup openPromptGroup;
    [SerializeField, Tooltip("안내 속의 키 그림(눌러 보라는 뜻으로 커졌다 작아진다)")] private RectTransform openPromptKey;
    [SerializeField, Tooltip("메일을 확인하라는 안내(단말기가 열려 있을 때)")] private CanvasGroup readPromptGroup;
    [SerializeField] private TextMeshProUGUI readPromptText;
    [SerializeField] private LobbyGuideArrow arrow;
    [SerializeField] private CanvasGroup arrowGroup;

    [Header("한 곳만 밝히기")]
    [SerializeField, Tooltip("화면을 어둡게 덮고 눌러야 할 곳만 남기는 막")] private FocusDimGraphic focusDim;
    [SerializeField, Tooltip("단말기를 열기 전에 밝게 남기는 곳(오른쪽 위의 단말기 아이콘)")] private RectTransform iconFocus;
    [SerializeField, Tooltip("단말기 첫 화면의 메시지 수신함 카드")] private RectTransform homeMailCard;
    [SerializeField, Tooltip("수신함의 환영 메일 카드")] private RectTransform mailCard;
    [SerializeField, Tooltip("밝게 남기는 곳의 둘레 여백(화면 단위)")] private Vector2 focusPadding = new Vector2(14f, 14f);

    [Header("수신함의 읽음 표시")]
    [SerializeField, Tooltip("환영 메일 카드의 상태 글자")] private TextMeshProUGUI mailStateLabel;
    [SerializeField, Tooltip("읽지 않은 메일이 있을 때만 켜는 표시")] private GameObject[] unreadMarks;
    [SerializeField, Tooltip("그 표시 가운데 커졌다 작아지며 눈길을 끄는 것(느낌표 배지)")] private RectTransform[] pulsingMarks;

    [Header("문구")]
    [SerializeField, TextArea] private string readPromptMessage = "메시지 수신함에서  <color=#FFB02E>입소 환영</color>  메일을 확인하십시오";
    [SerializeField, TextArea] private string readDoneMessage = "확인했습니다.  단말기를 닫으면 훈련 메뉴의 위치를 안내합니다";
    [SerializeField] private string unreadState = "새 통지";
    [SerializeField] private string readState = "확인함";

    [Header("연출")]
    [SerializeField, Min(0f), Tooltip("Lobby에 도착하고 메일이 오기까지의 시간(초)")] private float arrivalDelay = 1.2f;
    [SerializeField, Min(.1f), Tooltip("메일 도착 알림이 떠 있는 시간(초)")] private float toastTime = 3.2f;
    [SerializeField] private FeedbackDataSO mailArrivedFeedback;

    private float lobbyTime, arrivalAge = -1f;
    private float iconAlpha, openAlpha, readAlpha, arrowAlpha, toastAlpha;
    private Vector2 toastHome;
    private bool homesCached, subscribed, focusing;
    private UIManager focusOwner;
    private int shownStep = -1;

    /// <summary>세이브의 안내 단계. 안내를 걸 수 없는 상태(슬롯 없음·도입부 전)면 끝난 것으로 본다.</summary>
    public int Step
    {
        get
        {
            var data = DataManager.Instance;
            if (data == null || !data.HasSelectedSlot || data.CurrentSaveData.isFirstStart) return SaveData.OnboardingDone;
            return data.CurrentSaveData.onboardingStep;
        }
    }
    /// <summary>메일을 확인할 때까지 다른 길을 막고 있는가(Lobby의 입구도 그동안 잠긴다).</summary>
    public bool IsForcingMail => InLobby && Step < StepMailRead;
    /// <summary>지금 화면을 어둡게 덮고 한 곳만 남겨 둔 상태인가(그 밖의 입력을 받지 않는다).</summary>
    public bool IsFocusing => focusing;
    /// <summary>지금 밝게 남겨 둔 곳(없으면 null).</summary>
    public RectTransform FocusTarget => focusDim != null ? focusDim.Target : null;
    public FocusDimGraphic FocusDim => focusDim;
    public bool IsIconVisible => iconAlpha > .5f;
    public bool IsBadgeVisible => badge != null && badge.gameObject.activeSelf;
    public bool IsOpenPromptVisible => openAlpha > .5f;
    public bool IsReadPromptVisible => readAlpha > .5f;
    public bool IsArrowVisible => arrowAlpha > .5f;
    public RoomTrigger TrainingEntrance => FindEntrance();

    private bool InLobby
    {
        get
        {
            var game = GameManager.Instance;
            return game != null && game.LobbyRoom != null && RoomManager.Instance != null && RoomManager.Instance.CurrentRoom == game.LobbyRoom
                && RoomTransitionManager.Instance != null && !RoomTransitionManager.Instance.IsTransitioning;
        }
    }

    private void OnEnable()
    {
        CacheHomes();
        HideAll();
        if (subscribed) return;
        subscribed = true;
        LobbyInteractionLock.AddBlocker(IsLocked);
        RoomTrigger.Interacted += HandleInteracted;
        if (terminal != null)
        {
            terminal.ActionGuard += GuardTerminal;
            terminal.GuidedEntry += GuidedEntry;
            terminal.OpenWhileGameplayBlocked += OwnsGameplayBlock;
            terminal.EntryDetailsShown += HandleEntryShown;
            terminal.OpeningHomeIndex += OpeningHomeIndex;
        }
    }

    private void OnDisable()
    {
        if (subscribed)
        {
            subscribed = false;
            LobbyInteractionLock.RemoveBlocker(IsLocked);
            RoomTrigger.Interacted -= HandleInteracted;
            if (terminal != null)
            {
                terminal.ActionGuard -= GuardTerminal;
                terminal.GuidedEntry -= GuidedEntry;
                terminal.OpenWhileGameplayBlocked -= OwnsGameplayBlock;
                terminal.EntryDetailsShown -= HandleEntryShown;
                terminal.OpeningHomeIndex -= OpeningHomeIndex;
            }
        }
        SetFocusing(false);
        HideAll();
    }

    // ---- 잠금과 신호 ----

    private bool IsLocked(Component target) => IsForcingMail;

    // 메일을 확인할 때까지 단말기는 정해진 조작만 받는다: 열기(메일이 온 뒤), 수신함 카드 열기, 환영 메일 열기.
    private bool GuardTerminal(InventoryTerminalMenu.GuardedAction action, int index)
    {
        int step = Step;
        if (step >= StepMailRead) return true;
        switch (action)
        {
            case InventoryTerminalMenu.GuardedAction.Open: return step >= StepMailArrived;
            case InventoryTerminalMenu.GuardedAction.OpenCategory: return index == MailTab;
            case InventoryTerminalMenu.GuardedAction.SelectEntry:
            case InventoryTerminalMenu.GuardedAction.ShowDetails: return terminal.SelectedTab == MailTab && index == MailEntry;
            default: return false;
        }
    }

    // 수신함에 들어가면 환영 메일이 바로 골라져 있다(확인 한 번에 열린다).
    private int GuidedEntry(int tab) => Step < StepMailRead && tab == MailTab ? MailEntry : -1;

    private bool OwnsGameplayBlock() => focusOwner != null;

    // 메일을 확인해야 하는 동안에는 단말기를 열면 메시지 수신함이 먼저 골라져 있다.
    private int OpeningHomeIndex() => IsForcingMail ? MailTab : 0;

    private void HandleEntryShown(int tab, int entry)
    {
        if (tab != MailTab || entry != MailEntry) return;
        int step = Step;
        if (step >= StepMailRead) return;
        DataManager.Instance.SetOnboardingStep(StepMailRead);
    }

    private void HandleInteracted(RoomTrigger trigger)
    {
        int step = Step;
        if (step < StepMailRead || step >= SaveData.OnboardingDone || trigger != FindEntrance()) return;
        DataManager.Instance.SetOnboardingStep(SaveData.OnboardingDone);
    }

    private RoomTrigger FindEntrance()
    {
        if (trainingEntrance != null) return trainingEntrance;
        var game = GameManager.Instance;
        if (game == null || game.LobbyRoom == null) return null;
        // Lobby 안에서 훈련 메뉴를 여는 입구. 여러 개면 기본 훈련실(TrainingRoom)로 가는 것을 고른다.
        var candidates = game.LobbyRoom.GetComponentsInChildren<RoomTrigger>(true)
            .Where(t => t.OpensTrainingMenu && t.TargetRoom != null && t.TargetRoom.roomType == RoomType.Training).ToArray();
        trainingEntrance = candidates.FirstOrDefault(t => t.TargetRoom.name == "TrainingRoom") ?? candidates.FirstOrDefault();
        return trainingEntrance;
    }

    // 한 곳만 남기는 동안에는 게임 입력과 일시정지를 받지 않는다. 건 것은 반드시 같은 UIManager에서 푼다.
    private void SetFocusing(bool on)
    {
        if (on == focusing) return;
        focusing = on;
        if (on)
        {
            focusOwner = UIManager.Instance;
            if (focusOwner != null)
            {
                focusOwner.PushGameplayBlock();
                focusOwner.PauseInputBlocked = true;
            }
        }
        else
        {
            if (focusOwner != null)
            {
                focusOwner.PopGameplayBlock();
                focusOwner.PauseInputBlocked = false;
            }
            focusOwner = null;
            if (focusDim != null) focusDim.Clear();
        }
    }

    // ---- 진행과 표시 ----

    private void Update()
    {
        float delta = Time.unscaledDeltaTime;
        bool inLobby = InLobby;
        int step = Step;
        bool open = terminal != null && terminal.IsActive;
        var game = GameManager.Instance;
        var player = game != null ? game.Player : null;
        bool alive = player != null && player.gameObject.activeInHierarchy && player.Health.currentHP > 0f;
        bool busy = UIManager.Instance != null && (UIManager.Instance.IsDeathUIOpen || UIManager.Instance.IsCompleteBoardOpen);
        // 단말기가 아닌 메뉴(일시정지 등)가 떠 있는 동안에는 안내를 잠시 접는다.
        bool otherMenu = !open && UIManager.Instance != null && UIManager.Instance.HasActiveInputUI;

        lobbyTime = inLobby ? lobbyTime + delta : 0f;
        // Lobby에 도착하고 조금 뒤 환영 메일이 온다. 공중에서 입력이 끊기지 않도록 바닥에 선 뒤에 온다.
        if (inLobby && alive && !busy && !otherMenu && step < StepMailArrived && lobbyTime >= arrivalDelay && !open && player.IsGrounded)
        {
            DataManager.Instance.SetOnboardingStep(StepMailArrived);
            step = StepMailArrived;
            arrivalAge = 0f;
            if (mailArrivedFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.PlayExact(mailArrivedFeedback, Vector3.zero, Quaternion.identity);
        }
        if (arrivalAge >= 0f)
        {
            arrivalAge += delta;
            if (arrivalAge > toastTime || !inLobby) arrivalAge = -1f;
        }

        bool unread = step < StepMailRead;
        bool guiding = inLobby && alive && !busy && step >= StepMailArrived && step < SaveData.OnboardingDone;
        if (step != shownStep) ApplyMailState(step);

        // 메일이 온 뒤 확인할 때까지: 한 곳만 남기고 나머지 입력을 받지 않는다.
        // 다시 접속해 이어질 때도 바닥에 선 뒤에 시작한다(이미 시작했으면 그대로 간다).
        bool wantFocus = guiding && unread && !otherMenu && (focusing || open || player.IsGrounded);
        SetFocusing(wantFocus);
        if (focusing && focusDim != null)
        {
            RectTransform target = !open ? iconFocus
                : terminal.IsHomeVisible ? homeMailCard
                : terminal.SelectedTab == MailTab && !terminal.IsDetailOpen ? mailCard : null;
            if (target != null) focusDim.Focus(target, focusPadding);
            else focusDim.Cover();
        }

        // 목표 표시. 단말기든 훈련 메뉴든 화면을 쓰는 메뉴가 열려 있는 동안에는 아이콘과 길잡이를 치운다(그동안은 단말기도 열리지 않는다).
        bool menuOpen = open || otherMenu;
        bool showIcon = inLobby && alive && !busy && !menuOpen;
        bool showBadge = showIcon && unread && step >= StepMailArrived;
        bool showOpen = guiding && unread && !menuOpen;
        bool showRead = guiding && open && step < SaveData.OnboardingDone;
        var entrance = guiding && !unread && !menuOpen ? FindEntrance() : null;
        bool showArrow = entrance != null;

        iconAlpha = Mathf.MoveTowards(iconAlpha, showIcon ? 1f : 0f, delta * 4f);
        openAlpha = Mathf.MoveTowards(openAlpha, showOpen ? 1f : 0f, delta * 4f);
        readAlpha = Mathf.MoveTowards(readAlpha, showRead ? 1f : 0f, delta * 5f);
        arrowAlpha = Mathf.MoveTowards(arrowAlpha, showArrow ? 1f : 0f, delta * 4f);
        float toastTarget = arrivalAge >= 0f && arrivalAge < toastTime - .5f && showIcon ? 1f : 0f;
        toastAlpha = Mathf.MoveTowards(toastAlpha, toastTarget, delta * 4f);

        float time = Time.unscaledTime;
        if (iconGroup != null) iconGroup.alpha = iconAlpha;
        if (iconRoot != null)
        {
            // 메일이 도착하면 아이콘이 떨듯 흔들린다.
            float shake = arrivalAge >= 0f ? Mathf.Exp(-arrivalAge * 2.2f) * Mathf.Sin(arrivalAge * 38f) * 12f : 0f;
            iconRoot.localRotation = Quaternion.Euler(0f, 0f, shake);
        }
        float beat = 1f + .16f * Mathf.Sin(time * 6f);
        if (badge != null)
        {
            if (badge.gameObject.activeSelf != showBadge) badge.gameObject.SetActive(showBadge);
            badge.localScale = Vector3.one * beat;
        }
        if (pulsingMarks != null)
            foreach (var mark in pulsingMarks)
                if (mark != null && mark.gameObject.activeInHierarchy) mark.localScale = Vector3.one * beat;
        if (toastGroup != null) toastGroup.alpha = toastAlpha;
        if (toastRoot != null) toastRoot.anchoredPosition = toastHome + new Vector2((1f - toastAlpha) * 60f, 0f);
        // 안내 글은 늘 또렷하게 두고(뒤 배경이 비치면 읽기 어렵다) 키 그림만 눌러 보라는 듯 커졌다 작아진다.
        if (openPromptGroup != null) openPromptGroup.alpha = openAlpha;
        if (openPromptKey != null) openPromptKey.localScale = Vector3.one * (1f + .12f * Mathf.Max(0f, Mathf.Sin(time * 5f)));
        if (readPromptGroup != null) readPromptGroup.alpha = readAlpha;
        if (readPromptText != null)
        {
            string message = unread ? readPromptMessage : readDoneMessage;
            if (readPromptText.text != message) readPromptText.text = message;
        }
        if (arrow != null) arrow.Target = entrance != null ? entrance.transform : arrow.Target;
        if (arrowGroup != null) arrowGroup.alpha = arrowAlpha;
        if (arrow != null && arrowAlpha <= 0f) arrow.Target = null;
    }

    // 수신함의 환영 메일을 읽지 않음/읽음으로 보이게 한다.
    private void ApplyMailState(int step)
    {
        shownStep = step;
        bool unread = step < StepMailRead;
        if (mailStateLabel != null) mailStateLabel.text = unread ? unreadState : readState;
        if (unreadMarks != null)
            foreach (var mark in unreadMarks)
                if (mark != null && mark.activeSelf != unread) mark.SetActive(unread);
    }

    private void CacheHomes()
    {
        if (homesCached) return;
        homesCached = true;
        if (toastRoot != null) toastHome = toastRoot.anchoredPosition;
    }

    private void HideAll()
    {
        iconAlpha = openAlpha = readAlpha = arrowAlpha = toastAlpha = 0f;
        arrivalAge = -1f;
        lobbyTime = 0f;
        if (iconGroup != null) iconGroup.alpha = 0f;
        if (toastGroup != null) toastGroup.alpha = 0f;
        if (openPromptGroup != null) openPromptGroup.alpha = 0f;
        if (readPromptGroup != null) readPromptGroup.alpha = 0f;
        if (arrowGroup != null) arrowGroup.alpha = 0f;
        if (badge != null) badge.gameObject.SetActive(false);
        if (arrow != null) arrow.Target = null;
        if (focusDim != null) { focusDim.Clear(); focusDim.Snap(); }
    }
}
