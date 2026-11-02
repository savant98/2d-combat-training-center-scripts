using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering.Universal;

/// <summary>최초 실행의 모니터·휴대폰 고지서 연출과 서명 입력을 기존 타이틀 앞에 연결한다.</summary>
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

    [Header("실행 조건")]
    [SerializeField] private bool enableFirstLaunchIntro = true;
    [SerializeField, Tooltip("켜면 서명 기록과 관계없이 재생하며 저장 데이터는 변경하지 않습니다.")]
    private bool playIntroForTesting = true;
    [Header("기존 타이틀")]
    [SerializeField] private TitleIntroController titleIntro;
    [SerializeField] private TitleMenu titleMenu;
    [SerializeField] private Camera titleCamera;
    [SerializeField] private GameObject[] hiddenUntilIntroComplete = Array.Empty<GameObject>();
    [SerializeField] private GameObject platformRoot;
    [SerializeField] private GameObject[] backgroundRecoveryOrder = Array.Empty<GameObject>();
    [Header("방과 휴대폰")]
    [SerializeField] private CanvasGroup introGroup;
    [SerializeField] private RectTransform roomRoot;
    [SerializeField] private UnityEngine.UI.RawImage monitorScreen;
    [SerializeField] private RectTransform phoneRig;
    [SerializeField] private CanvasGroup notificationGroup, noticeGroup, refusalGroup, signatureGroup;
    [SerializeField] private CanvasGroup missedCallGroup, mailReceiveGroup;
    [SerializeField] private CanvasGroup incomingCallGroup, mailDeliveryGroup, phoneScreenGlow, missedCallContinueHint;
    [SerializeField] private RectTransform incomingCallIcon, mailDeliveryIcon, notificationMailIcon, receiveMailIcon;
    [SerializeField] private Material monitorIntroMaterial;
    [SerializeField] private TextMeshProUGUI anyKeyPrompt, statusLabel;
    [Header("고지서")]
    [SerializeField] private UnityEngine.UI.ScrollRect noticeScroll;
    [SerializeField] private RectTransform signatureSection, signatureReveal;
    [SerializeField] private UnityEngine.UI.Button signButton, refuseButton, dismissRefusalButton;
    [SerializeField] private UnityEngine.UI.Button openPhoneButton, closeMissedCallButton, receiveMailButton;
    [Header("연출 시간")]
    [SerializeField, Min(.05f)] private float ringtoneDuration = 6f;
    [SerializeField, Min(0f)] private float ringLeadInDuration = 3f;
    [SerializeField, Min(.05f)] private float revealDuration = 3f;
    [SerializeField, Min(.05f)] private float phoneZoomDuration = 1.25f;
    [SerializeField, Min(.05f)] private float signatureDuration = 1.25f;
    [SerializeField, Min(0f)] private float signatureHold = .65f;
    [SerializeField, Min(.05f)] private float phoneLowerDuration = 1.1f;
    [SerializeField, Min(.05f)] private float returnDuration = 3f;
    [SerializeField, Min(.05f)] private float recoveryStepDuration = .65f;
    [SerializeField, Min(0f)] private float missedCallReadDuration = 2f;
    [SerializeField, Min(0f)] private float missedCallDeskDuration = 1.6f;
    [SerializeField, Min(.05f)] private float mailDeliveryDuration = .8f;
    [SerializeField, Min(.05f)] private float screenTransitionDuration = .32f;
    [SerializeField, Min(.1f)] private float phoneFocusScale = .95f;
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

    private bool running, initialized, legacyRestored, completed, inputRegistered;
    private bool[] legacyActive;
    private bool[] legacyRevealed, scrollWasEnabled;
    private TitleWorldInfiniteScrollLayer[] frozenScrollLayers;
    private Transform[] scrollingTargets;
    private Vector3[] scrollingRestPositions;
    private Vector3 roomRestScale, phoneRestScale;
    private Vector2 roomRestPosition, phoneRestPosition;
    private Quaternion phoneRestRotation;
    private float signatureWidth, inputGuardUntil;
    private int selectedSignatureIndex;
    private RenderTexture liveScreen, originalCameraTarget;
    private Texture originalScreenTexture;
    private Material originalScreenMaterial, liveScreenMaterial;
    private Camera displayBackingCamera;
    private SoundHandle ringtoneHandle, vibrationHandle, signatureHandle;
    private bool ringtonePaused, vibrationPaused, signaturePaused, titleMusicStarted, missedCallReady;
    private bool preludeStarted, preludePaused;
    private bool closeButtonWasActive;
    private float visualTime;
    private UiRectState[] ownedRectStates;
    private UiGroupState[] ownedGroupStates;
    private float ringStartedAt = -1f, roomRevealStartedAt = -1f, ringStoppedAt = -1f;
    private Vector3 screenZoomScale;
    private Vector2 screenZoomPosition;

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
    public bool IsActive => running && isActiveAndEnabled;
    public bool IsSigned { get; private set; }
    public int SelectedSignatureIndex => selectedSignatureIndex;
    public bool IsPlatformRestored { get; private set; }
    public int RecoveredBackgroundCount { get; private set; }
    public bool HasTitlePresentationStarted { get; private set; }
    public bool IsDocumentAtEnd => noticeScroll != null && noticeScroll.content != null && noticeScroll.viewport != null
        && (noticeScroll.content.rect.height <= noticeScroll.viewport.rect.height + 1f || noticeScroll.verticalNormalizedPosition <= .015f);
    private bool HasInputFocus => IsActive && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked
        && UIManager.Instance.IsCurrentInputTarget(this);
    private bool CanAct => HasInputFocus && Time.unscaledTime >= inputGuardUntil;

    public static bool ShouldRunIntro(bool enabled, bool forceForTesting, bool noticeSigned)
        => enabled && (forceForTesting || !noticeSigned);

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
        completed = legacyRestored = false;
        IsSigned = IsPlatformRestored = HasTitlePresentationStarted = false;
        RecoveredBackgroundCount = 0;
        titleMusicStarted = false;
        preludeStarted = preludePaused = false;
        missedCallReady = false;
        visualTime = 0f;
        ownedRectStates = null;
        ownedGroupStates = null;
        ringStartedAt = roomRevealStartedAt = ringStoppedAt = -1f;
        legacyActive = legacyRevealed = null;
        frozenScrollLayers = null;
        scrollWasEnabled = null;
        scrollingTargets = null;
        CurrentStage = IntroStage.Disabled;
        bool signed = enableFirstLaunchIntro && DataManager.Instance.CurrentSaveData.trainingNoticeSigned;
        if (!ShouldRunIntro(enableFirstLaunchIntro, playIntroForTesting, signed))
        {
            SetGroup(introGroup, false);
            if (titleIntro != null) titleIntro.SetExternalIntroActive(false);
            return;
        }
        if (!HasRequiredReferences())
        {
            Debug.LogError("훈련 고지서 Intro의 필수 타이틀·화면·고지서 참조가 누락되었습니다.", this);
            SetGroup(introGroup, false);
            if (titleIntro != null) titleIntro.SetExternalIntroActive(false);
            return;
        }
        CachePresentation();
        running = true;
        titleMenu.MarkIntroPlayed();
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopBGM(titleBgmClip);
            SoundManager.Instance.StopBGM(titleBgmPreludeClip);
        }
        if (titleMenu.IsActive) titleMenu.Close();
        UIManager.Instance.PushInputTarget(this, UIInputLayer.System);
        inputRegistered = true;
        HideLegacyObjects();
        CreateLiveScreen();
        Canvas.ForceUpdateCanvases();
        CalculateScreenZoom();
        roomRoot.localScale = screenZoomScale;
        roomRoot.anchoredPosition = screenZoomPosition;
        phoneRig.localScale = phoneRestScale;
        phoneRig.anchoredPosition = phoneRestPosition;
        phoneRig.localRotation = phoneRestRotation;
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
        SetGroup(mailDeliveryGroup, false);
        SetGroup(phoneScreenGlow, false);
        SetGroup(missedCallContinueHint, false);
        if (closeMissedCallButton != null) closeMissedCallButton.gameObject.SetActive(false);
        SetSignatureFraction(0f);
        if (anyKeyPrompt != null) anyKeyPrompt.gameObject.SetActive(true);
        CurrentStage = IntroStage.WaitingForInput;
        inputGuardUntil = Time.unscaledTime + .2f;
        RefreshControls();
    }

    private bool HasRequiredReferences() => titleIntro != null && titleMenu != null && titleCamera != null
        && UIManager.Instance != null && introGroup != null && roomRoot != null && monitorScreen != null && phoneRig != null
        && noticeGroup != null && refusalGroup != null && signatureGroup != null && noticeScroll != null && noticeScroll.content != null && noticeScroll.viewport != null
        && signButton != null && refuseButton != null && missedCallGroup != null && mailReceiveGroup != null
        && closeMissedCallButton != null && receiveMailButton != null && incomingCallGroup != null
        && mailDeliveryGroup != null && phoneScreenGlow != null && missedCallContinueHint != null
        && incomingCallIcon != null && mailDeliveryIcon != null && notificationMailIcon != null && receiveMailIcon != null;

    private void CachePresentation()
    {
        roomRestScale = roomRoot.localScale;
        roomRestPosition = roomRoot.anchoredPosition;
        phoneRestScale = phoneRig.localScale;
        phoneRestPosition = phoneRig.anchoredPosition;
        phoneRestRotation = phoneRig.localRotation;
        signatureWidth = signatureReveal != null ? signatureReveal.rect.width : 0f;
        closeButtonWasActive = closeMissedCallButton != null && closeMissedCallButton.gameObject.activeSelf;
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
            missedCallGroup, mailReceiveGroup, incomingCallGroup, mailDeliveryGroup, phoneScreenGlow, missedCallContinueHint })
        {
            if (group == null) continue;
            groups.Add(group);
            if (group.transform is RectTransform rect) rects.Add(rect);
        }
        foreach (var icon in new[] { incomingCallIcon, mailDeliveryIcon, notificationMailIcon, receiveMailIcon })
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
        if (ownedGroupStates != null)
            foreach (var state in ownedGroupStates)
                if (state.Group != null)
                {
                    state.Group.alpha = state.Alpha;
                    state.Group.interactable = state.Interactable;
                    state.Group.blocksRaycasts = state.BlocksRaycasts;
                }
        if (closeMissedCallButton != null) closeMissedCallButton.gameObject.SetActive(closeButtonWasActive);
    }

    private void UpdatePhonePresentation()
    {
        if (HasInputFocus) visualTime += Time.unscaledDeltaTime;
        ResetPulse(incomingCallIcon);
        ResetPulse(notificationMailIcon);
        ResetPulse(receiveMailIcon);
        bool ringing = CurrentStage == IntroStage.RingingHold || CurrentStage == IntroStage.RevealingRoom;
        if (ringing)
        {
            float vibration = Mathf.Sin(visualTime * 43f);
            phoneRig.anchoredPosition = phoneRestPosition + new Vector2(vibration * 5f, Mathf.Sin(visualTime * 57f) * 2f);
            phoneRig.localRotation = phoneRestRotation * Quaternion.Euler(0f, 0f, vibration * 1.4f);
            Pulse(incomingCallIcon, visualTime * 7f);
            if (phoneScreenGlow != null) phoneScreenGlow.alpha = .25f + .6f * (.5f + .5f * Mathf.Sin(visualTime * 12f));
        }
        else if (CurrentStage != IntroStage.DeliveringMail && phoneScreenGlow != null) phoneScreenGlow.alpha = 0f;
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

    public void BeginFromAnyKey()
    {
        if (!CanAct || CurrentStage != IntroStage.WaitingForInput) return;
        if (anyKeyPrompt != null) anyKeyPrompt.gameObject.SetActive(false);
        CurrentStage = IntroStage.RingingHold;
        StartCoroutine(OpenNoticeRoutine());
    }

    private IEnumerator OpenNoticeRoutine()
    {
        ringStartedAt = Time.unscaledTime;
        SetGroup(incomingCallGroup, true);
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
            if (CurrentStage != IntroStage.RevealingRoom)
            {
                CurrentStage = IntroStage.RevealingRoom;
                roomRevealStartedAt = Time.unscaledTime;
            }
            float p = Ease(Mathf.Clamp01((elapsed - ringLeadInDuration) / Mathf.Max(.01f, revealDuration)));
            roomRoot.localScale = Vector3.LerpUnclamped(screenZoomScale, roomRestScale, p);
            roomRoot.anchoredPosition = Vector2.LerpUnclamped(screenZoomPosition, roomRestPosition, p);
        }
        roomRoot.localScale = roomRestScale;
        roomRoot.anchoredPosition = roomRestPosition;
        StopRingtone();
        SetGroup(incomingCallGroup, false);
        SetGroup(phoneScreenGlow, false);
        phoneRig.anchoredPosition = phoneRestPosition;
        phoneRig.localRotation = phoneRestRotation;
        ResetPulse(incomingCallIcon);
        CurrentStage = IntroStage.MissedCallOnDesk;
        SetGroup(missedCallGroup, true);
        yield return Hold(missedCallDeskDuration);
        CurrentStage = IntroStage.DeliveringMail;
        SetGroup(mailDeliveryGroup, true);
        PrepareScreenBlend(missedCallGroup, notificationGroup);
        var canvas = (RectTransform)introGroup.GetComponentInParent<Canvas>().transform;
        Vector3 destination = canvas.InverseTransformPoint(phoneRig.TransformPoint(phoneRig.rect.center));
        Vector3 origin = new Vector3(canvas.rect.xMax + mailDeliveryIcon.rect.width * 1.3f, destination.y + canvas.rect.height * .25f, destination.z);
        Vector3 envelopeScale = RestScale(mailDeliveryIcon);
        yield return Animate(mailDeliveryDuration, t => {
            float p = Ease(t);
            if (mailDeliveryIcon != null)
            {
                mailDeliveryIcon.position = canvas.TransformPoint(Vector3.LerpUnclamped(origin, destination, p));
                mailDeliveryIcon.localScale = envelopeScale * Mathf.Lerp(2.4f, .55f, p);
            }
            if (mailDeliveryGroup != null) mailDeliveryGroup.alpha = 1f - Mathf.Clamp01((t - .86f) / .14f);
            if (phoneScreenGlow != null) phoneScreenGlow.alpha = t > .7f ? Mathf.Sin(Mathf.Clamp01((t - .7f) / .3f) * Mathf.PI) : 0f;
            float blend = Mathf.Clamp01((t * mailDeliveryDuration - Mathf.Max(0f, mailDeliveryDuration - screenTransitionDuration))
                / Mathf.Max(.01f, screenTransitionDuration));
            BlendScreens(missedCallGroup, notificationGroup, Ease(blend));
        });
        FinishScreenBlend(missedCallGroup, notificationGroup);
        SetGroup(mailDeliveryGroup, false);
        SetGroup(phoneScreenGlow, false);
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
        PrepareScreenBlend(notificationGroup, missedCallGroup);
        yield return Animate(phoneZoomDuration, t => {
            float p = Ease(t);
            phoneRig.anchoredPosition = Vector2.LerpUnclamped(phoneRestPosition, Vector2.zero, p);
            phoneRig.localScale = Vector3.LerpUnclamped(phoneRestScale, Vector3.one * phoneFocusScale, p);
            phoneRig.localRotation = Quaternion.SlerpUnclamped(phoneRestRotation, Quaternion.identity, p);
            BlendScreens(notificationGroup, missedCallGroup, Ease(Mathf.Clamp01(t * phoneZoomDuration / Mathf.Max(.01f, screenTransitionDuration))));
        });
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
        // 강제 테스트는 다른 기능의 저장에도 섞이지 않도록 메모리의 마커도 변경하지 않는다.
        if (!playIntroForTesting)
        {
            DataManager.Instance.CurrentSaveData.trainingNoticeSigned = true;
            DataManager.Instance.SaveGame();
        }
        yield return Hold(signatureHold);
        CurrentStage = IntroStage.ReturningPhone;
        Vector2 fromPosition = phoneRig.anchoredPosition;
        Vector3 fromScale = phoneRig.localScale;
        Quaternion fromRotation = phoneRig.localRotation;
        yield return Animate(phoneLowerDuration, t => {
            float p = Ease(t);
            phoneRig.anchoredPosition = Vector2.LerpUnclamped(fromPosition, phoneRestPosition, p);
            phoneRig.localScale = Vector3.LerpUnclamped(fromScale, phoneRestScale, p);
            phoneRig.localRotation = Quaternion.SlerpUnclamped(fromRotation, phoneRestRotation, p);
        });
        CurrentStage = IntroStage.ReturningToTitle;
        StartPrelude();
        SetStatus("");
        // 화면 복구를 시작하기 전까지는 플랫폼과 모니터의 어두운 상태를 그대로 유지한다.
        yield return Animate(returnDuration, t => {
            roomRoot.localScale = Vector3.LerpUnclamped(roomRestScale, screenZoomScale, Ease(t));
            roomRoot.anchoredPosition = Vector2.LerpUnclamped(roomRestPosition, screenZoomPosition, Ease(t));
        });
        CurrentStage = IntroStage.RestoringPlatform;
        RestoreScreenMaterial();
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
        CurrentStage = IntroStage.RevealingTitle;
        RestoreLegacyObjects();
        RestoreAllScrolling();
        titleIntro.ResumeAfterTrainingNoticeImmediately();
        StopPrelude();
        completed = true;
        running = false;
        CurrentStage = IntroStage.Completed;
        ReleaseLiveScreen();
        SetGroup(introGroup, false);
        RemoveOwnInput();
    }

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

    private void CreateLiveScreen()
    {
        originalCameraTarget = titleCamera.targetTexture;
        originalScreenTexture = monitorScreen.texture;
        originalScreenMaterial = monitorScreen.material;
        int width = Mathf.Clamp(Screen.width, 640, 2560);
        int height = Mathf.Clamp(Screen.height, 360, 1440);
        liveScreen = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "TitleTrainingNoticeLiveScreen", hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear, antiAliasing = 1
        };
        liveScreen.Create();
        titleCamera.targetTexture = liveScreen;
        monitorScreen.texture = liveScreen;
        if (monitorIntroMaterial != null)
        {
            liveScreenMaterial = new Material(monitorIntroMaterial)
            {
                name = "TitleTrainingNoticeGrayInstance", hideFlags = HideFlags.DontSave
            };
            monitorScreen.material = liveScreenMaterial;
        }
        // 실제 Display 출력도 유지한다. 원래 카메라만 RT로 돌리면 Game View에는 출력 카메라가 없어진다.
        var displayObject = new GameObject("TitleTrainingNoticeDisplayCamera") { hideFlags = HideFlags.HideAndDontSave };
        displayBackingCamera = displayObject.AddComponent<Camera>();
        displayBackingCamera.targetDisplay = titleCamera.targetDisplay;
        displayBackingCamera.targetTexture = null;
        displayBackingCamera.cullingMask = 0;
        displayBackingCamera.clearFlags = CameraClearFlags.SolidColor;
        displayBackingCamera.backgroundColor = new Color(.005f, .008f, .012f, 1f);
        displayBackingCamera.depth = -1000f;
        displayBackingCamera.allowHDR = displayBackingCamera.allowMSAA = false;
        displayBackingCamera.useOcclusionCulling = false;
        displayBackingCamera.enabled = true;
        var additional = displayBackingCamera.GetUniversalAdditionalCameraData();
        additional.renderType = CameraRenderType.Base;
        additional.renderPostProcessing = additional.renderShadows = false;
        additional.requiresColorOption = CameraOverrideOption.Off;
        additional.requiresDepthOption = CameraOverrideOption.Off;
        additional.volumeLayerMask = 0;
    }
    private void CalculateScreenZoom()
    {
        var canvas = introGroup.GetComponentInParent<Canvas>();
        var canvasRect = (RectTransform)canvas.transform;
        var screen = monitorScreen.rectTransform;
        Vector3 relative = roomRoot.InverseTransformPoint(screen.TransformPoint(screen.rect.center));
        Vector3[] corners = new Vector3[4]; screen.GetWorldCorners(corners);
        Vector3 leftBottom = roomRoot.InverseTransformPoint(corners[0]);
        Vector3 rightTop = roomRoot.InverseTransformPoint(corners[2]);
        float width = Mathf.Abs(rightTop.x - leftBottom.x), height = Mathf.Abs(rightTop.y - leftBottom.y);
        float zoom = Mathf.Max(canvasRect.rect.width / Mathf.Max(1f, width), canvasRect.rect.height / Mathf.Max(1f, height));
        screenZoomScale = Vector3.one * zoom;
        screenZoomPosition = new Vector2(-relative.x * zoom, -relative.y * zoom);
    }
    private void ReleaseLiveScreen()
    {
        if (liveScreen != null)
        {
            if (titleCamera != null && titleCamera.targetTexture == liveScreen) titleCamera.targetTexture = originalCameraTarget;
            if (monitorScreen != null && monitorScreen.texture == liveScreen) monitorScreen.texture = originalScreenTexture;
        }
        RestoreScreenMaterial();
        if (displayBackingCamera != null)
        {
            displayBackingCamera.enabled = false;
            if (Application.isPlaying) Destroy(displayBackingCamera.gameObject); else DestroyImmediate(displayBackingCamera.gameObject);
            displayBackingCamera = null;
        }
        if (liveScreen != null)
        {
            liveScreen.Release();
            if (Application.isPlaying) Destroy(liveScreen); else DestroyImmediate(liveScreen);
            liveScreen = null;
        }
        if (liveScreenMaterial != null)
        {
            if (Application.isPlaying) Destroy(liveScreenMaterial); else DestroyImmediate(liveScreenMaterial);
            liveScreenMaterial = null;
        }
    }

    private void RestoreScreenMaterial()
    {
        if (monitorScreen != null && liveScreenMaterial != null && monitorScreen.material == liveScreenMaterial)
            monitorScreen.material = originalScreenMaterial;
    }

    private void StopRingtone()
    {
        StopLoop(ref ringtoneHandle, ref ringtonePaused);
        StopLoop(ref vibrationHandle, ref vibrationPaused);
        if (ringStartedAt >= 0f && ringStoppedAt < 0f) ringStoppedAt = Time.unscaledTime;
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

    private void StartPrelude()
    {
        if (preludeStarted || titleBgmPreludeClip == null || SoundManager.Instance == null) return;
        SoundManager.Instance.PlayBGM(titleBgmPreludeClip, titleBgmVolume * titleBgmPreludeVolumeRatio, false);
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
            SoundManager.Instance.FadeOutBGM(titleBgmClip, titleMenu != null ? titleMenu.PlayerDisappearTime : 1.0833334f);
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
        if (mailDeliveryGroup != null) mailDeliveryGroup.interactable = mailDeliveryGroup.blocksRaycasts = false;
        if (phoneScreenGlow != null) phoneScreenGlow.interactable = phoneScreenGlow.blocksRaycasts = false;
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
        StopRingtone();
        StopLoop(ref signatureHandle, ref signaturePaused);
        StopPrelude();
        RemoveOwnInput();
        ReleaseLiveScreen();
        if (!completed)
        {
            RestoreLegacyObjects();
            CurrentStage = IntroStage.Disabled;
        }
        // Scene Reload가 꺼진 편집기에서도 다음 재생이 확대된 자세를 기준으로 삼지 않게 한다.
        if (legacyActive != null)
        {
            if (roomRoot != null) { roomRoot.localScale = roomRestScale; roomRoot.anchoredPosition = roomRestPosition; }
            if (phoneRig != null) { phoneRig.localScale = phoneRestScale; phoneRig.anchoredPosition = phoneRestPosition; phoneRig.localRotation = phoneRestRotation; }
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
        SetGroup(mailDeliveryGroup, false);
        SetGroup(phoneScreenGlow, false);
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
