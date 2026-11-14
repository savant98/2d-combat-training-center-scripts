using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;

/// <summary>
/// 타이틀의 시작 연출.
/// 세이브가 하나도 없으면 어두운 방(TitleRoomStage)에서 모니터·휴대폰 고지서 연출과 서명 입력을 거친 뒤 타이틀로 들어가고,
/// 세이브가 있으면 방을 건너뛰고 등이 차례로 켜지는 연출부터 시작한다(아무 키로 건너뛸 수 있다).
/// 방의 자세·빛은 TitleRoomStage가, 흐름과 휴대폰 화면의 내용은 이 컴포넌트가 맡는다.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class TitleTrainingNoticeIntro : MonoBehaviour, IUIInputReceiver
{
    public enum IntroStage
    {
        Disabled, WaitingForInput, RevealingRoom, Notification, OpeningPhone,
        ReadingNotice, RefusalWarning, Signing, ReturningPhone, ReturningToTitle, Completed,
        RingingHold, WaitingForPhone, MissedCall, ReceiveMailPrompt, RestoringPlatform, RestoringBackground, RevealingTitle,
        MissedCallOnDesk, DeliveringMail, MissedCallReadDelay, ScreenTransition
    }

    /// <summary>이번 실행에서 재생하는 연출.</summary>
    public enum IntroMode { None, Room, LightsOnly }

    [Header("실행 조건")]
    [SerializeField] private bool enableFirstLaunchIntro = true;
    [SerializeField, Tooltip("켜면 세이브가 있어도 방 연출을 재생합니다(확인용). 저장 데이터는 변경하지 않습니다.")]
    private bool playIntroForTesting;
    [Header("기존 타이틀")]
    [SerializeField] private TitleIntroController titleIntro;
    [SerializeField] private TitleMenu titleMenu;
    [SerializeField] private Camera titleCamera;
    [SerializeField] private GameObject[] hiddenUntilIntroComplete = Array.Empty<GameObject>();
    [SerializeField] private GameObject platformRoot;
    [SerializeField] private GameObject[] backgroundRecoveryOrder = Array.Empty<GameObject>();
    [Header("방과 휴대폰")]
    [SerializeField] private CanvasGroup introGroup;
    [SerializeField, Tooltip("화면 전체를 덮어 뒤의 UI가 눌리지 않게 하는 면")] private UnityEngine.UI.Graphic inputBlocker;
    [SerializeField] private TitleRoomStage stage;
    [SerializeField, Tooltip("휴대폰 화면의 내용(World Space Canvas). 방 연출이 시작될 때 3D 휴대폰의 화면에 붙는다")] private Canvas phoneCanvas;
    [SerializeField] private CanvasGroup phoneScreenGroup;
    [SerializeField, Tooltip("세이브가 있을 때, 등이 켜지기 전의 흑백·어두운 화면을 만드는 후처리")] private Volume monochromeVolume;
    [SerializeField] private CanvasGroup notificationGroup, noticeGroup, refusalGroup, signatureGroup;
    [SerializeField] private CanvasGroup missedCallGroup, mailReceiveGroup;
    [SerializeField] private CanvasGroup incomingCallGroup, missedCallContinueHint;
    [SerializeField] private RectTransform incomingCallIcon, notificationMailIcon, receiveMailIcon;
    [SerializeField] private TextMeshProUGUI anyKeyPrompt, statusLabel;
    [Header("고지서")]
    [SerializeField] private UnityEngine.UI.ScrollRect noticeScroll;
    [SerializeField] private RectTransform signatureSection, signatureReveal;
    [SerializeField] private UnityEngine.UI.Button signButton, refuseButton, dismissRefusalButton;
    [SerializeField] private UnityEngine.UI.Button openPhoneButton, closeMissedCallButton, receiveMailButton;
    [Header("연출 시간")]
    [SerializeField, Min(.05f)] private float ringtoneDuration = 6f;
    [SerializeField, Min(0f), Tooltip("벨이 울리기 시작한 뒤 시선이 휴대폰 쪽으로 가기 전까지의 시간")] private float ringLeadInDuration = 3f;
    [SerializeField, Min(.05f), Tooltip("시선이 휴대폰 쪽으로 옮겨 가는 시간")] private float revealDuration = 3f;
    [SerializeField, Min(.05f)] private float phoneZoomDuration = 1.25f;
    [SerializeField, Min(.05f)] private float signatureDuration = 1.25f;
    [SerializeField, Min(0f)] private float signatureHold = .65f;
    [SerializeField, Min(.05f)] private float phoneLowerDuration = 1.1f;
    [SerializeField, Min(.05f), Tooltip("모니터가 시야를 채울 때까지 다가가는 시간")] private float returnDuration = 3f;
    [SerializeField, Min(.05f)] private float recoveryStepDuration = .65f;
    [SerializeField, Min(0f)] private float missedCallReadDuration = 2f;
    [SerializeField, Min(0f)] private float missedCallDeskDuration = 1.6f;
    [SerializeField, Min(.05f)] private float mailDeliveryDuration = .8f;
    [SerializeField, Min(.05f)] private float screenTransitionDuration = .32f;
    [SerializeField, Min(0f), Tooltip("세이브가 있을 때, 등이 켜지기 전에 어두운 화면을 보여 주는 시간")] private float lightsOnLeadIn = .5f;
    [SerializeField, Min(20f)] private float keyboardScrollStep = 150f;
    [SerializeField] private AnimationCurve transitionEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO notificationFeedback, confirmFeedback, refuseFeedback;
    [SerializeField] private FeedbackDataSO ringtoneFeedback, mailArrivalFeedback, recoveryFlashFeedback;
    [SerializeField] private FeedbackDataSO touchFeedback, signatureFeedback, vibrationFeedback;
    [SerializeField] private FeedbackDataSO recoveryLightFeedback, playerRevealFeedback;
    [SerializeField, Tooltip("플랫폼 → 원경 4 → 3 → 2 → 1 순서의 점등 음량 배율입니다.")]
    private float[] recoveryLightVolumeScales = { .25f, .35f, .5f, .7f, 1f };
    [SerializeField] private AudioClip titleBgmClip;
    [SerializeField, Range(0f, 1f)] private float titleBgmVolume = .38f;
    [SerializeField] private AudioClip titleBgmPreludeClip;
    [SerializeField, Range(0f, 1f)] private float titleBgmPreludeVolumeRatio = .35f;
    [SerializeField, Range(0f, 1f), Tooltip("최초 시작 세이브로 FinalRoom에 이어질 때 Title BGM을 남길 음량 배율입니다.")]
    private float distantTitleBgmVolumeRatio = .4f;
    [SerializeField, Range(200f, 22000f), Tooltip("FinalRoom에서 멀리서 들리게 남길 고음 상한(Hz)입니다. 낮을수록 먹먹하게 들립니다.")]
    private float distantTitleBgmCutoff = 1400f;

    private const string SkipHint = "아무 키나 누르면 건너뜁니다";
    // 등이 켜지기 전의 어두운 화면에서 한 프레임에 흐른 것으로 치는 최대 시간(긴 프레임 하나가 이 구간을 삼키지 않게 한다)
    private const float MaxLeadInStep = 1f / 30f;

    private bool running, initialized, legacyRestored, completed, inputRegistered, skippable;
    private bool[] legacyActive;
    private bool[] legacyRevealed, scrollWasEnabled;
    private TitleWorldInfiniteScrollLayer[] frozenScrollLayers;
    private Transform[] scrollingTargets;
    private Vector3[] scrollingRestPositions;
    private float signatureWidth, inputGuardUntil, phonePowerTarget;
    private int selectedSignatureIndex;
    private SoundHandle ringtoneHandle, vibrationHandle, signatureHandle;
    private bool ringtonePaused, vibrationPaused, signaturePaused, titleMusicStarted, missedCallReady;
    private bool preludeStarted, preludePaused;
    private bool closeButtonWasActive, blockerWasRaycastTarget;
    private float visualTime;
    private UiRectState[] ownedRectStates;
    private UiGroupState[] ownedGroupStates;

    private struct UiRectState
    {
        public RectTransform Rect;
        public Vector2 Position;
        public Vector3 Scale;
        public Quaternion Rotation;
    }
    private struct UiGroupState
    {
        public CanvasGroup Group;
        public float Alpha;
        public bool Interactable, BlocksRaycasts;
    }

    public IntroStage CurrentStage { get; private set; } = IntroStage.Disabled;
    public IntroMode Mode { get; private set; } = IntroMode.None;
    public bool IsActive => running && isActiveAndEnabled;
    public bool IsSigned { get; private set; }
    public int SelectedSignatureIndex => selectedSignatureIndex;
    public bool IsPlatformRestored { get; private set; }
    public int RecoveredBackgroundCount { get; private set; }
    public bool HasTitlePresentationStarted { get; private set; }
    /// <summary>아무 키로 건너뛸 수 있는 구간인가(세이브가 있을 때의 등 켜지는 연출).</summary>
    public bool CanSkip => running && skippable;
    public bool IsDocumentAtEnd => noticeScroll != null && noticeScroll.content != null && noticeScroll.viewport != null
        && (noticeScroll.content.rect.height <= noticeScroll.viewport.rect.height + 1f || noticeScroll.verticalNormalizedPosition <= .015f);
    private bool HasInputFocus => IsActive && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
        && UIManager.Instance.IsCurrentInputTarget(this);
    private bool CanAct => HasInputFocus && Time.unscaledTime >= inputGuardUntil;

    /// <summary>방 연출(최초 실행)을 재생하는 조건: 켜져 있고, 세이브가 하나도 없거나 확인용 강제 재생일 때.</summary>
    public static bool ShouldRunIntro(bool enabled, bool forceForTesting, bool hasSave)
        => enabled && (forceForTesting || !hasSave);

    private void Awake()
    {
        // 타이틀의 Update와 Animation Event가 새 연출을 건너뛰지 않게 먼저 진입을 보류한다.
        if (titleIntro != null) titleIntro.SetExternalIntroActive(true);
        if (signButton != null) signButton.onClick.AddListener(SignNotice);
        if (refuseButton != null) refuseButton.onClick.AddListener(RefuseNotice);
        if (dismissRefusalButton != null) dismissRefusalButton.onClick.AddListener(DismissRefusal);
        if (openPhoneButton != null) openPhoneButton.onClick.AddListener(OpenPhone);
        if (closeMissedCallButton != null) closeMissedCallButton.onClick.AddListener(CloseMissedCall);
        if (receiveMailButton != null) receiveMailButton.onClick.AddListener(ReceiveMail);
        if (titleIntro != null) titleIntro.PlayerRevealed += HandlePlayerRevealed;
        TitleMenu.OnTitleExitStarted += HandleTitleExit;
    }

    private void Start()
    {
        initialized = true;
        completed = legacyRestored = skippable = false;
        IsSigned = IsPlatformRestored = HasTitlePresentationStarted = false;
        RecoveredBackgroundCount = 0;
        titleMusicStarted = false;
        preludeStarted = preludePaused = false;
        missedCallReady = false;
        visualTime = 0f;
        phonePowerTarget = 0f;
        ownedRectStates = null;
        ownedGroupStates = null;
        legacyActive = legacyRevealed = null;
        frozenScrollLayers = null;
        scrollWasEnabled = null;
        scrollingTargets = null;
        CurrentStage = IntroStage.Disabled;
        SetMonochrome(false);

        // 세이브가 하나라도 있으면 방 연출을 이미 본 것으로 본다(서명 여부를 따로 저장하지 않는다).
        bool hasSave = DataManager.Instance.HasAnySave;
        Mode = !enableFirstLaunchIntro ? IntroMode.None
            : ShouldRunIntro(enableFirstLaunchIntro, playIntroForTesting, hasSave) ? IntroMode.Room : IntroMode.LightsOnly;
        if (Mode == IntroMode.None)
        {
            if (phoneCanvas != null) phoneCanvas.gameObject.SetActive(false);
            SetGroup(introGroup, false);
            if (titleIntro != null) titleIntro.SetExternalIntroActive(false);
            return;
        }
        if (!HasRequiredReferences())
        {
            Debug.LogError("타이틀 시작 연출의 필수 타이틀·방·고지서 참조가 누락되었습니다.", this);
            Mode = IntroMode.None;
            SetGroup(introGroup, false);
            if (titleIntro != null) titleIntro.SetExternalIntroActive(false);
            return;
        }
        if (Mode == IntroMode.Room)
        {
            // 휴대폰 화면은 방 안의 Canvas에 있다. 방을 켜고 배치를 한 번 계산한 뒤에 크기를 읽는다.
            stage.Begin(titleCamera);
            stage.AttachPhoneCanvas(phoneCanvas, phoneScreenGroup);
            Canvas.ForceUpdateCanvases();
        }
        else if (phoneCanvas != null) phoneCanvas.gameObject.SetActive(false);
        CachePresentation();
        running = true;
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopBGM(titleBgmClip);
            SoundManager.Instance.StopBGM(titleBgmPreludeClip);
        }
        if (titleMenu.IsActive) titleMenu.Close();
        UIManager.Instance.PushInputTarget(this, UIInputLayer.System);
        inputRegistered = true;
        HideLegacyObjects();
        noticeScroll.StopMovement();
        noticeScroll.verticalNormalizedPosition = 1f;
        SetGroup(introGroup, true);
        SetGroup(notificationGroup, false);
        SetGroup(noticeGroup, false);
        SetGroup(refusalGroup, false);
        SetGroup(signatureGroup, false);
        SetGroup(missedCallGroup, false);
        SetGroup(mailReceiveGroup, false);
        SetGroup(incomingCallGroup, false);
        SetGroup(missedCallContinueHint, false);
        if (closeMissedCallButton != null) closeMissedCallButton.gameObject.SetActive(false);
        SetSignatureFraction(0f);
        SetStatus("");
        inputGuardUntil = Time.unscaledTime + .2f;

        if (Mode == IntroMode.Room)
        {
            // 휴대폰 화면(방 안의 World Space Canvas)을 마우스로 누를 수 있도록 화면을 덮는 면의 입력을 푼다.
            if (inputBlocker != null) inputBlocker.raycastTarget = false;
            if (anyKeyPrompt != null) anyKeyPrompt.gameObject.SetActive(true);
            CurrentStage = IntroStage.WaitingForInput;
            RefreshControls();
        }
        else
        {
            if (anyKeyPrompt != null) anyKeyPrompt.gameObject.SetActive(false);
            StartCoroutine(LightsOnRoutine());
        }
    }

    private bool HasRequiredReferences() => titleIntro != null && titleMenu != null && titleCamera != null
        && UIManager.Instance != null && introGroup != null && stage != null
        && noticeGroup != null && refusalGroup != null && signatureGroup != null && noticeScroll != null && noticeScroll.content != null && noticeScroll.viewport != null
        && signButton != null && refuseButton != null && missedCallGroup != null && mailReceiveGroup != null
        && closeMissedCallButton != null && receiveMailButton != null && incomingCallGroup != null
        && missedCallContinueHint != null
        && incomingCallIcon != null && notificationMailIcon != null && receiveMailIcon != null;

    private void CachePresentation()
    {
        signatureWidth = signatureReveal != null ? signatureReveal.rect.width : 0f;
        closeButtonWasActive = closeMissedCallButton != null && closeMissedCallButton.gameObject.activeSelf;
        blockerWasRaycastTarget = inputBlocker != null && inputBlocker.raycastTarget;
        CacheOwnedUiPresentation();
        legacyActive = new bool[hiddenUntilIntroComplete.Length];
        legacyRevealed = new bool[hiddenUntilIntroComplete.Length];
        for (int i = 0; i < legacyActive.Length; i++)
            legacyActive[i] = hiddenUntilIntroComplete[i] != null && hiddenUntilIntroComplete[i].activeSelf;
        var layers = FindObjectsByType<TitleWorldInfiniteScrollLayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sceneLayers = new System.Collections.Generic.List<TitleWorldInfiniteScrollLayer>();
        foreach (var layer in layers) if (layer.gameObject.scene == gameObject.scene) sceneLayers.Add(layer);
        frozenScrollLayers = sceneLayers.ToArray();
        scrollWasEnabled = new bool[frozenScrollLayers.Length];
        var targets = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < frozenScrollLayers.Length; i++)
        {
            var layer = frozenScrollLayers[i];
            scrollWasEnabled[i] = layer.enabled;
            for (int child = 0; child < layer.transform.childCount; child++) targets.Add(layer.transform.GetChild(child));
            layer.StopScroll();
        }
        scrollingTargets = targets.ToArray();
        scrollingRestPositions = new Vector3[scrollingTargets.Length];
        for (int i = 0; i < scrollingTargets.Length; i++) scrollingRestPositions[i] = scrollingTargets[i].localPosition;
    }

    private void CacheOwnedUiPresentation()
    {
        var rects = new System.Collections.Generic.HashSet<RectTransform>();
        var groups = new System.Collections.Generic.HashSet<CanvasGroup>();
        foreach (var group in new[] { introGroup, notificationGroup, noticeGroup, refusalGroup, signatureGroup,
            missedCallGroup, mailReceiveGroup, incomingCallGroup, missedCallContinueHint })
        {
            if (group == null) continue;
            groups.Add(group);
            if (group.transform is RectTransform rect) rects.Add(rect);
        }
        foreach (var icon in new[] { incomingCallIcon, notificationMailIcon, receiveMailIcon })
        {
            if (icon == null) continue;
            rects.Add(icon);
            var group = icon.GetComponent<CanvasGroup>();
            if (group != null) groups.Add(group);
        }
        var poses = new System.Collections.Generic.List<UiRectState>();
        foreach (var rect in rects) poses.Add(new UiRectState {
            Rect = rect, Position = rect.anchoredPosition, Scale = rect.localScale, Rotation = rect.localRotation
        });
        ownedRectStates = poses.ToArray();
        var states = new System.Collections.Generic.List<UiGroupState>();
        foreach (var group in groups) states.Add(new UiGroupState {
            Group = group, Alpha = group.alpha, Interactable = group.interactable, BlocksRaycasts = group.blocksRaycasts
        });
        ownedGroupStates = states.ToArray();
    }

    private Vector3 RestScale(RectTransform rect)
    {
        if (ownedRectStates != null)
            foreach (var pose in ownedRectStates) if (pose.Rect == rect) return pose.Scale;
        return rect != null ? rect.localScale : Vector3.one;
    }

    private float RestAlpha(CanvasGroup group)
    {
        if (ownedGroupStates != null)
            foreach (var state in ownedGroupStates) if (state.Group == group) return state.Alpha;
        return 1f;
    }

    private void RestoreOwnedUiPresentation()
    {
        if (ownedGroupStates == null) return;
        if (ownedRectStates != null)
            foreach (var pose in ownedRectStates)
                if (pose.Rect != null)
                {
                    pose.Rect.anchoredPosition = pose.Position;
                    pose.Rect.localScale = pose.Scale;
                    pose.Rect.localRotation = pose.Rotation;
                }
        foreach (var state in ownedGroupStates)
            if (state.Group != null)
            {
                state.Group.alpha = state.Alpha;
                state.Group.interactable = state.Interactable;
                state.Group.blocksRaycasts = state.BlocksRaycasts;
            }
        if (closeMissedCallButton != null) closeMissedCallButton.gameObject.SetActive(closeButtonWasActive);
        if (inputBlocker != null) inputBlocker.raycastTarget = blockerWasRaycastTarget;
    }

    private void UpdatePhonePresentation()
    {
        if (HasInputFocus) visualTime += Time.unscaledDeltaTime;
        ResetPulse(incomingCallIcon);
        ResetPulse(notificationMailIcon);
        ResetPulse(receiveMailIcon);
        bool ringing = CurrentStage == IntroStage.RingingHold || CurrentStage == IntroStage.RevealingRoom;
        if (stage != null && stage.IsRunning)
        {
            // 벨이 울리는 동안의 진동과 휴대폰 화면의 켜짐은 방(3D 휴대폰)이 표현한다.
            stage.Ringing = ringing && HasInputFocus;
            stage.PhonePower = Mathf.MoveTowards(stage.PhonePower, phonePowerTarget, Time.unscaledDeltaTime * 5f);
        }
        if (ringing) Pulse(incomingCallIcon, visualTime * 7f);
        if (CurrentStage == IntroStage.WaitingForPhone) Pulse(notificationMailIcon, visualTime * 5f);
        if (CurrentStage == IntroStage.ReceiveMailPrompt) Pulse(receiveMailIcon, visualTime * 5f);
    }

    private void ResetPulse(RectTransform icon)
    {
        if (icon == null) return;
        icon.localScale = RestScale(icon);
        var group = icon.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = RestAlpha(group);
    }

    private void Pulse(RectTransform icon, float phase)
    {
        if (icon == null) return;
        float wave = .5f + .5f * Mathf.Sin(phase);
        icon.localScale = RestScale(icon) * (1f + .07f * wave);
        var group = icon.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = RestAlpha(group) * Mathf.Lerp(.35f, 1f, wave);
    }

    private void Update()
    {
        if (!running) return;
        RefreshControls();
        SyncRingtonePause();
        SyncLoopPause(signatureHandle, ref signaturePaused);
        SyncPreludePause();
        UpdatePhonePresentation();
        if (CurrentStage == IntroStage.WaitingForInput && CanAct && AnyKeyPressed()) BeginFromAnyKey();
        else if (skippable && CanAct && AnyKeyPressed()) SkipToTitle();
    }

    private void LateUpdate()
    {
        if (running && !legacyRestored) HideLegacyObjects();
        // 기존 타이틀의 Animation Event가 메뉴를 여는 순간에도 복귀 연출이 끝날 때까지 입력을 소유한다.
        // 다른 System 창은 가로채지 않고, 이번 연출이 직접 재개한 TitleMenu만 자기 아래로 되돌린다.
        if (running && CurrentStage == IntroStage.ReturningToTitle && titleMenu != null && titleMenu.IsActive
            && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
            && UIManager.Instance.IsCurrentInputTarget(titleMenu))
        {
            UIManager.Instance.PushInputTarget(this, UIInputLayer.System);
            RefreshControls();
        }
    }

    private static bool AnyKeyPressed()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
        if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame
            || Mouse.current.middleButton.wasPressedThisFrame)) return true;
        if (Gamepad.current != null)
            foreach (var control in Gamepad.current.allControls)
                if (control is ButtonControl button && button.wasPressedThisFrame) return true;
        return InputManager.PressedAnyButton();
    }

    #region 세이브가 있을 때: 등 켜지는 연출부터

    private IEnumerator LightsOnRoutine()
    {
        skippable = true;
        SetMonochrome(true);
        SetStatus(SkipHint);
        CurrentStage = IntroStage.ReturningToTitle;
        // Scene을 읽은 직후의 프레임은 길다. 그 시간이 연출 시간으로 흘러가 어두운 화면을 건너뛰지 않게, 화면이 그려지기 시작한 뒤부터 센다.
        yield return null;
        yield return null;
        // 전주는 방 연출의 길이(다가가는 시간 + 점등)에 맞춰 만든 곡이다. 그 끝이 타이틀 음악의 시작과 맞도록 뒷부분만 쓴다.
        float fullLength = returnDuration + RecoveryLength();
        StartPrelude(Mathf.Max(0f, fullLength - (lightsOnLeadIn + RecoveryLength())));
        float waited = 0f;
        while (waited < lightsOnLeadIn)
        {
            yield return null;
            if (HasInputFocus) waited += Mathf.Min(Time.unscaledDeltaTime, MaxLeadInStep);
        }
        yield return RecoverRoutine();
    }

    private float RecoveryLength() => recoveryStepDuration * (1 + (backgroundRecoveryOrder != null ? backgroundRecoveryOrder.Length : 0));

    /// <summary>등 켜지는 연출을 건너뛰고 타이틀을 바로 연다(세이브가 있을 때만).</summary>
    public void SkipToTitle()
    {
        if (!running || !skippable) return;
        StopAllCoroutines();
        SetMonochrome(false);
        IsPlatformRestored = true;
        RecoveredBackgroundCount = backgroundRecoveryOrder.Length;
        // 건너뛰는 데 쓴 키가 이어서 열리는 타이틀 메뉴까지 누르지 않게, 이번 프레임에 눌린 것으로 남아 있는 버튼을 지운다.
        if (InputManager.Instance != null) InputManager.Instance.ClearButtonStates();
        FinishToTitle();
    }

    #endregion

    #region 세이브가 없을 때: 방 → 전화 → 메일 → 서명

    public void BeginFromAnyKey()
    {
        if (!CanAct || CurrentStage != IntroStage.WaitingForInput) return;
        if (anyKeyPrompt != null) anyKeyPrompt.gameObject.SetActive(false);
        CurrentStage = IntroStage.RingingHold;
        StartCoroutine(OpenNoticeRoutine());
    }

    private IEnumerator OpenNoticeRoutine()
    {
        // 책상 위의 휴대폰이 켜지며 벨이 울리고, 잠시 뒤 시선이 휴대폰 쪽으로 간다.
        SetGroup(incomingCallGroup, true);
        phonePowerTarget = 1f;
        if (ringtoneFeedback != null && SoundManager.Instance != null)
            ringtoneHandle = SoundManager.Instance.PlayLoop(ringtoneFeedback.sound, Vector3.zero);
        if (vibrationFeedback != null && SoundManager.Instance != null)
            vibrationHandle = SoundManager.Instance.PlayLoop(vibrationFeedback.sound, Vector3.zero);
        float elapsed = 0f;
        while (elapsed < ringtoneDuration)
        {
            yield return null;
            if (!HasInputFocus) continue;
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < ringLeadInDuration) continue;
            CurrentStage = IntroStage.RevealingRoom;
            stage.Glance = Ease(Mathf.Clamp01((elapsed - ringLeadInDuration) / Mathf.Max(.01f, revealDuration)));
        }
        stage.Glance = 1f;
        StopRingtone();
        SetGroup(incomingCallGroup, false);
        ResetPulse(incomingCallIcon);
        CurrentStage = IntroStage.MissedCallOnDesk;
        SetGroup(missedCallGroup, true);
        yield return Hold(missedCallDeskDuration);

        // 메일 알림이 도착하면 화면이 알림으로 바뀐다.
        CurrentStage = IntroStage.DeliveringMail;
        PrepareScreenBlend(missedCallGroup, notificationGroup);
        yield return Animate(mailDeliveryDuration, t => {
            float blend = Mathf.Clamp01((t * mailDeliveryDuration - Mathf.Max(0f, mailDeliveryDuration - screenTransitionDuration))
                / Mathf.Max(.01f, screenTransitionDuration));
            BlendScreens(missedCallGroup, notificationGroup, Ease(blend));
        });
        FinishScreenBlend(missedCallGroup, notificationGroup);
        CurrentStage = IntroStage.WaitingForPhone;
        Play(mailArrivalFeedback != null ? mailArrivalFeedback : notificationFeedback);
        SetStatus("Z키를 눌러 휴대폰을 확인하세요");
        inputGuardUntil = Time.unscaledTime + .2f;
        RefreshControls();
    }

    public void OpenPhone()
    {
        if (!CanAct || CurrentStage != IntroStage.WaitingForPhone) return;
        Play(touchFeedback);
        CurrentStage = IntroStage.OpeningPhone;
        SetStatus("");
        StartCoroutine(OpenPhoneRoutine());
    }

    private IEnumerator OpenPhoneRoutine()
    {
        // 휴대폰을 눈앞으로 들어 올린다.
        PrepareScreenBlend(notificationGroup, missedCallGroup);
        yield return Animate(phoneZoomDuration, t => {
            stage.PhoneLift = Ease(t);
            BlendScreens(notificationGroup, missedCallGroup, Ease(Mathf.Clamp01(t * phoneZoomDuration / Mathf.Max(.01f, screenTransitionDuration))));
        });
        stage.PhoneLift = 1f;
        FinishScreenBlend(notificationGroup, missedCallGroup);
        CurrentStage = IntroStage.MissedCallReadDelay;
        missedCallReady = false;
        RefreshControls();
        yield return Hold(missedCallReadDuration);
        CurrentStage = IntroStage.MissedCall;
        missedCallReady = true;
        SetGroup(missedCallContinueHint, true);
        if (closeMissedCallButton != null) closeMissedCallButton.gameObject.SetActive(closeButtonWasActive);
        inputGuardUntil = Time.unscaledTime;
        RefreshControls();
    }

    public void CloseMissedCall()
    {
        if (!CanAct || CurrentStage != IntroStage.MissedCall || !missedCallReady) return;
        Play(touchFeedback);
        missedCallReady = false;
        SetGroup(missedCallContinueHint, false);
        BeginScreenTransition(missedCallGroup, mailReceiveGroup, IntroStage.ReceiveMailPrompt);
    }

    public void ReceiveMail()
    {
        if (!CanAct || CurrentStage != IntroStage.ReceiveMailPrompt) return;
        Play(touchFeedback);
        noticeScroll.StopMovement();
        noticeScroll.verticalNormalizedPosition = 1f;
        selectedSignatureIndex = 0;
        SetStatus("고지서 하단에서 서명을 완료해 주세요.");
        BeginScreenTransition(mailReceiveGroup, noticeGroup, IntroStage.ReadingNotice);
    }

    public void OnMove(Vector2 direction)
    {
        if (!CanAct) return;
        if (CurrentStage == IntroStage.WaitingForInput) { BeginFromAnyKey(); return; }
        if (CurrentStage != IntroStage.ReadingNotice) return;
        if (Mathf.Abs(direction.y) > .5f) ScrollDocument(direction.y * keyboardScrollStep);
        else if (IsDocumentAtEnd && Mathf.Abs(direction.x) > .5f)
        {
            selectedSignatureIndex = direction.x < 0f ? 0 : 1;
            RefreshControls();
        }
    }

    private void ScrollDocument(float pixels)
    {
        float distance = noticeScroll.content.rect.height - noticeScroll.viewport.rect.height;
        noticeScroll.StopMovement();
        if (distance > 0f) noticeScroll.verticalNormalizedPosition = Mathf.Clamp01(noticeScroll.verticalNormalizedPosition + pixels / distance);
        RefreshControls();
    }

    private void BeginScreenTransition(CanvasGroup outgoing, CanvasGroup incoming, IntroStage destination)
    {
        CurrentStage = IntroStage.ScreenTransition;
        inputGuardUntil = Time.unscaledTime + screenTransitionDuration;
        RefreshControls();
        StartCoroutine(TransitionScreens(outgoing, incoming, destination));
    }

    private IEnumerator TransitionScreens(CanvasGroup outgoing, CanvasGroup incoming, IntroStage destination)
    {
        PrepareScreenBlend(outgoing, incoming);
        yield return Animate(screenTransitionDuration, t => BlendScreens(outgoing, incoming, Ease(t)));
        FinishScreenBlend(outgoing, incoming);
        CurrentStage = destination;
        inputGuardUntil = Time.unscaledTime + .2f;
        RefreshControls();
    }

    private void PrepareScreenBlend(CanvasGroup outgoing, CanvasGroup incoming)
    {
        if (outgoing != null) { outgoing.alpha = 1f; outgoing.interactable = outgoing.blocksRaycasts = false; }
        if (incoming != null) { incoming.alpha = 0f; incoming.interactable = incoming.blocksRaycasts = false; }
        SetScreenScale(outgoing, 1f);
        SetScreenScale(incoming, .88f);
    }

    private void BlendScreens(CanvasGroup outgoing, CanvasGroup incoming, float progress)
    {
        if (outgoing != null) outgoing.alpha = 1f - progress;
        if (incoming != null) incoming.alpha = progress;
        SetScreenScale(outgoing, Mathf.Lerp(1f, .94f, progress));
        SetScreenScale(incoming, Mathf.Lerp(.88f, 1f, progress));
    }

    private void FinishScreenBlend(CanvasGroup outgoing, CanvasGroup incoming)
    {
        if (outgoing != null) { outgoing.alpha = 0f; outgoing.interactable = outgoing.blocksRaycasts = false; }
        if (incoming != null) incoming.alpha = 1f;
        SetScreenScale(outgoing, 1f);
        SetScreenScale(incoming, 1f);
    }

    private void SetScreenScale(CanvasGroup group, float multiplier)
    {
        if (group != null && group.transform is RectTransform rect) rect.localScale = RestScale(rect) * multiplier;
    }

    public void OnConfirm()
    {
        if (!CanAct) return;
        if (CurrentStage == IntroStage.WaitingForInput) BeginFromAnyKey();
        else if (CurrentStage == IntroStage.WaitingForPhone) OpenPhone();
        else if (CurrentStage == IntroStage.MissedCall) CloseMissedCall();
        else if (CurrentStage == IntroStage.ReceiveMailPrompt) ReceiveMail();
        else if (CurrentStage == IntroStage.RefusalWarning) DismissRefusal();
        else if (CurrentStage == IntroStage.ReadingNotice)
        {
            if (selectedSignatureIndex == 0) SignNotice(); else RefuseNotice();
        }
    }
    public void OnCancel()
    {
        if (!CanAct) return;
        if (CurrentStage == IntroStage.WaitingForInput) BeginFromAnyKey();
        else if (CurrentStage == IntroStage.RefusalWarning) DismissRefusal();
        else if (CurrentStage == IntroStage.ReadingNotice) RefuseNotice();
    }

    public void RefuseNotice()
    {
        if (!CanAct || CurrentStage != IntroStage.ReadingNotice) return;
        if (!IsDocumentAtEnd) { SetStatus("고지서 하단까지 확인해 주세요."); return; }
        Play(touchFeedback);
        selectedSignatureIndex = 1;
        Play(refuseFeedback);
        BeginScreenTransition(noticeGroup, refusalGroup, IntroStage.RefusalWarning);
    }
    public void DismissRefusal()
    {
        if (!CanAct || CurrentStage != IntroStage.RefusalWarning) return;
        Play(touchFeedback);
        selectedSignatureIndex = 0;
        BeginScreenTransition(refusalGroup, noticeGroup, IntroStage.ReadingNotice);
    }
    public void SignNotice()
    {
        if (!CanAct || CurrentStage != IntroStage.ReadingNotice) return;
        if (!IsDocumentAtEnd) { SetStatus("고지서 하단까지 확인해 주세요."); return; }
        Play(touchFeedback);
        selectedSignatureIndex = 0;
        CurrentStage = IntroStage.Signing;
        noticeScroll.StopMovement();
        RefreshControls();
        Play(confirmFeedback);
        StartCoroutine(SignAndReturnRoutine());
    }

    private IEnumerator SignAndReturnRoutine()
    {
        SetGroup(signatureGroup, true);
        if (signatureFeedback != null && SoundManager.Instance != null)
            signatureHandle = SoundManager.Instance.PlayLoop(signatureFeedback.sound, Vector3.zero);
        yield return Animate(signatureDuration, t => {
            float appearance = Ease(Mathf.Clamp01(t * signatureDuration / Mathf.Max(.01f, screenTransitionDuration)));
            signatureGroup.alpha = appearance;
            SetScreenScale(signatureGroup, Mathf.Lerp(.88f, 1f, appearance));
            SetSignatureFraction(t);
        });
        StopLoop(ref signatureHandle, ref signaturePaused);
        IsSigned = true;
        SetStatus("서명이 완료되었습니다.");
        yield return Hold(signatureHold);

        // 휴대폰을 책상에 내려놓고 화면을 끈다.
        CurrentStage = IntroStage.ReturningPhone;
        yield return Animate(phoneLowerDuration, t => {
            stage.PhoneLift = 1f - Ease(t);
            if (t > .6f) phonePowerTarget = 0f;
        });
        stage.PhoneLift = 0f;
        phonePowerTarget = 0f;

        // 모니터로 다가가 화면이 시야를 채우면, 그 그림 그대로 실제 타이틀 화면으로 넘어간다.
        CurrentStage = IntroStage.ReturningToTitle;
        StartPrelude(0f);
        SetStatus("");
        yield return Animate(returnDuration, t => {
            stage.MonitorZoom = Ease(t);
            stage.Glance = 1f - Ease(Mathf.Clamp01(t * 2.5f));
        });
        stage.MonitorZoom = 1f;
        yield return null;   // 화면이 꽉 찬 그림을 한 프레임 보여 준 뒤 바꾼다
        stage.End();
        yield return RecoverRoutine();
    }

    #endregion

    #region 등이 차례로 켜진다(두 경로 공통)

    private IEnumerator RecoverRoutine()
    {
        CurrentStage = IntroStage.RestoringPlatform;
        SetMonochrome(false);
        RestoreScrollUnder(platformRoot);
        IsPlatformRestored = true;
        Play(recoveryFlashFeedback);
        PlayRecoveryLight(0);
        yield return Hold(recoveryStepDuration);
        CurrentStage = IntroStage.RestoringBackground;
        foreach (var background in backgroundRecoveryOrder)
        {
            RevealLegacyObject(background);
            RestoreScrollUnder(background);
            RecoveredBackgroundCount++;
            Play(recoveryFlashFeedback);
            PlayRecoveryLight(RecoveredBackgroundCount);
            yield return Hold(recoveryStepDuration);
        }
        FinishToTitle();
    }

    private void FinishToTitle()
    {
        CurrentStage = IntroStage.RevealingTitle;
        skippable = false;
        RestoreLegacyObjects();
        RestoreAllScrolling();
        titleIntro.ResumeAfterTrainingNoticeImmediately();
        StopPrelude();
        completed = true;
        running = false;
        CurrentStage = IntroStage.Completed;
        if (stage != null) stage.End();
        SetStatus("");
        SetGroup(introGroup, false);
        RemoveOwnInput();
    }

    private void SetMonochrome(bool on)
    {
        if (monochromeVolume != null) monochromeVolume.weight = on ? 1f : 0f;
    }

    #endregion

    private IEnumerator Animate(float duration, Action<float> step)
    {
        float elapsed = 0f;
        step(0f);
        while (elapsed < Mathf.Max(.01f, duration))
        {
            yield return null;
            if (!HasInputFocus) continue;
            elapsed += Time.unscaledDeltaTime;
            step(Mathf.Clamp01(elapsed / Mathf.Max(.01f, duration)));
        }
        step(1f);
    }
    private IEnumerator Hold(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return null;
            if (HasInputFocus) elapsed += Time.unscaledDeltaTime;
        }
    }
    private float Ease(float t) => transitionEase != null ? transitionEase.Evaluate(Mathf.Clamp01(t)) : Mathf.SmoothStep(0f, 1f, t);

    private void StopRingtone()
    {
        StopLoop(ref ringtoneHandle, ref ringtonePaused);
        StopLoop(ref vibrationHandle, ref vibrationPaused);
    }

    private void SyncRingtonePause()
    {
        SyncLoopPause(ringtoneHandle, ref ringtonePaused);
        SyncLoopPause(vibrationHandle, ref vibrationPaused);
    }

    private void SyncLoopPause(SoundHandle handle, ref bool wasPaused)
    {
        if (handle == null || !handle.IsValid) return;
        var source = handle.GetSource();
        bool paused = !HasInputFocus;
        if (paused == wasPaused) return;
        if (paused) source.Pause(); else source.UnPause();
        wasPaused = paused;
    }

    private static void StopLoop(ref SoundHandle handle, ref bool wasPaused)
    {
        if (handle != null) handle.Stop();
        handle = null;
        wasPaused = false;
    }

    private void StartPrelude(float startSeconds)
    {
        if (preludeStarted || titleBgmPreludeClip == null || SoundManager.Instance == null) return;
        SoundManager.Instance.PlayBGM(titleBgmPreludeClip, titleBgmVolume * titleBgmPreludeVolumeRatio, false, startSeconds);
        preludeStarted = true;
        preludePaused = false;
    }

    private void SyncPreludePause()
    {
        if (!preludeStarted || SoundManager.Instance == null) return;
        bool paused = !HasInputFocus;
        if (paused == preludePaused) return;
        SoundManager.Instance.SetBGMPaused(titleBgmPreludeClip, paused);
        preludePaused = paused;
    }

    private void StopPrelude()
    {
        if (preludeStarted && SoundManager.Instance != null) SoundManager.Instance.StopBGM(titleBgmPreludeClip);
        preludeStarted = preludePaused = false;
    }

    private void HandlePlayerRevealed()
    {
        if (!isActiveAndEnabled || HasTitlePresentationStarted || (running && CurrentStage != IntroStage.RevealingTitle)) return;
        HasTitlePresentationStarted = true;
        StopPrelude();
        if (!titleMusicStarted && titleBgmClip != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayBGM(titleBgmClip, titleBgmVolume, true);
            titleMusicStarted = true;
        }
        Play(playerRevealFeedback);
    }

    private void HandleTitleExit()
    {
        StopPrelude();
        if (titleMusicStarted && SoundManager.Instance != null)
        {
            float duration = titleMenu != null ? titleMenu.PlayerDisappearTime : 1.0833334f;
            // 최초 시작 세이브로 FinalRoom에 이어지면 곡을 끊지 않고 멀리서 들리게 낮춰 보스 등장 전까지 잇는다.
            if (NightmareIntroSequence.IsStartQueued)
                SoundManager.Instance.SetBGMDistance(titleBgmClip, titleBgmVolume * distantTitleBgmVolumeRatio, distantTitleBgmCutoff, duration);
            else
                SoundManager.Instance.FadeOutBGM(titleBgmClip, duration);
        }
        titleMusicStarted = false;
    }

    private void HideLegacyObjects()
    {
        for (int i = 0; i < hiddenUntilIntroComplete.Length; i++)
        {
            var root = hiddenUntilIntroComplete[i];
            if (root != null && root.activeSelf && (legacyRevealed == null || !legacyRevealed[i])) root.SetActive(false);
        }
    }

    private void RevealLegacyObject(GameObject target)
    {
        if (target == null || legacyActive == null) return;
        for (int i = 0; i < hiddenUntilIntroComplete.Length; i++)
            if (hiddenUntilIntroComplete[i] == target)
            {
                legacyRevealed[i] = true;
                target.SetActive(legacyActive[i]);
                return;
            }
    }

    private void RestoreScrollUnder(GameObject root)
    {
        if (root == null || frozenScrollLayers == null) return;
        for (int i = 0; i < frozenScrollLayers.Length; i++)
            if (frozenScrollLayers[i] != null && frozenScrollLayers[i].transform.IsChildOf(root.transform))
                frozenScrollLayers[i].enabled = scrollWasEnabled[i];
    }

    private void RestoreAllScrolling()
    {
        if (frozenScrollLayers == null) return;
        for (int i = 0; i < frozenScrollLayers.Length; i++)
            if (frozenScrollLayers[i] != null) frozenScrollLayers[i].enabled = scrollWasEnabled[i];
    }
    private void RestoreLegacyObjects()
    {
        if (legacyRestored || legacyActive == null) return;
        legacyRestored = true;
        for (int i = 0; i < legacyActive.Length; i++)
            if (hiddenUntilIntroComplete[i] != null) hiddenUntilIntroComplete[i].SetActive(legacyActive[i]);
    }
    private void RefreshControls()
    {
        bool reading = CurrentStage == IntroStage.ReadingNotice && HasInputFocus;
        bool warning = CurrentStage == IntroStage.RefusalWarning && HasInputFocus;
        if (introGroup != null) { introGroup.interactable = HasInputFocus; introGroup.blocksRaycasts = HasInputFocus; }
        if (noticeGroup != null) { noticeGroup.interactable = reading; noticeGroup.blocksRaycasts = reading; }
        if (refusalGroup != null) { refusalGroup.interactable = warning; refusalGroup.blocksRaycasts = warning; }
        bool waitingPhone = CurrentStage == IntroStage.WaitingForPhone && HasInputFocus;
        bool missedCall = CurrentStage == IntroStage.MissedCall && missedCallReady && HasInputFocus;
        bool receiveMail = CurrentStage == IntroStage.ReceiveMailPrompt && HasInputFocus;
        if (notificationGroup != null) { notificationGroup.interactable = waitingPhone; notificationGroup.blocksRaycasts = waitingPhone; }
        if (missedCallGroup != null) { missedCallGroup.interactable = missedCall; missedCallGroup.blocksRaycasts = missedCall; }
        if (mailReceiveGroup != null) { mailReceiveGroup.interactable = receiveMail; mailReceiveGroup.blocksRaycasts = receiveMail; }
        if (openPhoneButton != null) openPhoneButton.interactable = waitingPhone;
        if (closeMissedCallButton != null)
        {
            closeMissedCallButton.interactable = missedCall;
            bool visible = CurrentStage == IntroStage.MissedCall && missedCallReady && closeButtonWasActive;
            if (closeMissedCallButton.gameObject.activeSelf != visible) closeMissedCallButton.gameObject.SetActive(visible);
        }
        if (receiveMailButton != null) receiveMailButton.interactable = receiveMail;
        if (missedCallContinueHint != null)
        {
            missedCallContinueHint.alpha = CurrentStage == IntroStage.MissedCall && missedCallReady ? 1f : 0f;
            missedCallContinueHint.interactable = missedCallContinueHint.blocksRaycasts = false;
        }
        if (incomingCallGroup != null) incomingCallGroup.interactable = incomingCallGroup.blocksRaycasts = false;
        if (signatureGroup != null) signatureGroup.interactable = signatureGroup.blocksRaycasts = false;
        bool footer = reading && IsDocumentAtEnd;
        if (signButton != null) signButton.interactable = footer;
        if (refuseButton != null) refuseButton.interactable = footer;
        Highlight(signButton, selectedSignatureIndex == 0);
        Highlight(refuseButton, selectedSignatureIndex == 1);
    }
    private static void Highlight(UnityEngine.UI.Button button, bool selected)
    {
        if (button == null || button.targetGraphic == null) return;
        button.targetGraphic.color = selected ? new Color(.12f, .43f, .61f, 1f) : new Color(.76f, .79f, .8f, 1f);
        foreach (var text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            text.color = selected ? Color.white : new Color(.13f, .16f, .19f, 1f);
    }
    private void SetSignatureFraction(float t)
    {
        if (signatureReveal != null) signatureReveal.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, signatureWidth * Mathf.Clamp01(t));
        else if (signatureGroup != null) signatureGroup.alpha = Mathf.Clamp01(t);
    }
    private void SetStatus(string message) { if (statusLabel != null) statusLabel.text = message; }
    private static void SetGroup(CanvasGroup group, bool visible)
    {
        if (group == null) return;
        group.alpha = visible ? 1f : 0f;
        group.interactable = group.blocksRaycasts = visible;
    }
    private void Play(FeedbackDataSO data)
    {
        if (data != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.PlayExact(data, Vector3.zero, Quaternion.identity);
    }
    private void PlayRecoveryLight(int recoveryIndex)
    {
        var source = recoveryLightFeedback != null ? recoveryLightFeedback.sound : null;
        if (source == null || source.clip == null || SoundManager.Instance == null) return;
        float scale = recoveryLightVolumeScales != null && recoveryLightVolumeScales.Length > 0
            ? recoveryLightVolumeScales[Mathf.Clamp(recoveryIndex, 0, recoveryLightVolumeScales.Length - 1)] : 1f;
        // 공유 Feedback의 설정을 바꾸지 않고 이번 점등에 사용할 사운드 옵션만 복사한다.
        var sound = new SoundOptions {
            bus = source.bus,
            clip = source.clip, volume = source.volume * Mathf.Max(0f, scale), pitch = source.pitch,
            spatialBlend = source.spatialBlend, randomizePitch = source.randomizePitch, pitchRange = source.pitchRange,
            minInterval = source.minInterval, maxSimultaneous = source.maxSimultaneous,
            crowdedVolumeMultiplier = source.crowdedVolumeMultiplier
        };
        SoundManager.Instance.PlaySound(sound, Vector3.zero);
    }
    private void RemoveOwnInput()
    {
        if (inputRegistered && UIManager.Instance != null) UIManager.Instance.RemoveInputTarget(this);
        inputRegistered = false;
    }
    private void OnDisable()
    {
        if (!initialized) { if (titleIntro != null) titleIntro.SetExternalIntroActive(false); return; }
        StopAllCoroutines();
        running = false;
        skippable = false;
        StopRingtone();
        StopLoop(ref signatureHandle, ref signaturePaused);
        StopPrelude();
        RemoveOwnInput();
        if (stage != null) stage.End();
        SetMonochrome(false);
        if (!completed)
        {
            RestoreLegacyObjects();
            CurrentStage = IntroStage.Disabled;
        }
        // Scene Reload가 꺼진 편집기에서도 다음 재생이 연출 도중의 자세를 기준으로 삼지 않게 한다.
        if (legacyActive != null)
        {
            SetSignatureFraction(1f);
            if (noticeScroll != null) { noticeScroll.StopMovement(); noticeScroll.verticalNormalizedPosition = 1f; }
            if (scrollingTargets != null)
                for (int i = 0; i < scrollingTargets.Length; i++)
                    if (scrollingTargets[i] != null) scrollingTargets[i].localPosition = scrollingRestPositions[i];
        }
        RestoreAllScrolling();
        SetGroup(notificationGroup, false);
        SetGroup(noticeGroup, false);
        SetGroup(refusalGroup, false);
        SetGroup(signatureGroup, false);
        SetGroup(missedCallGroup, false);
        SetGroup(mailReceiveGroup, false);
        SetGroup(incomingCallGroup, false);
        SetGroup(missedCallContinueHint, false);
        RestoreOwnedUiPresentation();
        SetGroup(introGroup, false);
        if (titleIntro != null) titleIntro.SetExternalIntroActive(false);
    }
    private void OnDestroy()
    {
        if (signButton != null) signButton.onClick.RemoveListener(SignNotice);
        if (refuseButton != null) refuseButton.onClick.RemoveListener(RefuseNotice);
        if (dismissRefusalButton != null) dismissRefusalButton.onClick.RemoveListener(DismissRefusal);
        if (openPhoneButton != null) openPhoneButton.onClick.RemoveListener(OpenPhone);
        if (closeMissedCallButton != null) closeMissedCallButton.onClick.RemoveListener(CloseMissedCall);
        if (receiveMailButton != null) receiveMailButton.onClick.RemoveListener(ReceiveMail);
        if (titleIntro != null) titleIntro.PlayerRevealed -= HandlePlayerRevealed;
        TitleMenu.OnTitleExitStarted -= HandleTitleExit;
    }
}
