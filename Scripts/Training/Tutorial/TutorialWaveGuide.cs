using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Wave 설명에 포커스를 맞추고 지정 입력을 확인한 뒤 실제 실습 완료까지 목표를 잠급니다.
/// 이동·전투 상태는 관찰만 하며, 설명 중 입력 제한은 기존 UIManager에 맡깁니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialWaveGuide : MonoBehaviour, IUIInputReceiver
{
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private TextMeshProUGUI body;
    [SerializeField] private TextMeshProUGUI prompt;
    [SerializeField] private RectTransform panel;
    [SerializeField] private FocusDimGraphic focus;
    [SerializeField] private AudioClip practiceSuccessClip;

    private enum GuidePhase { Waiting, Explain, Release, Returning, Practice, Complete }
    private static TutorialWaveGuide instance;
    private WaveManager owner;
    private TutorialLesson lesson;
    private PlayerController player;
    private UIManager blockOwner;
    private bool previousPauseBlock;
    private GuidePhase phase;
    private float earliestInput, movedDistance;
    private Vector3 lastPosition;
    private int initialWeaponSet;
    private InputAction confirmAction;
    private WeaponObservation weapons;
    private TutorialFocusPresentation presentation;
    private GameTimeController pauseOwner;
    private int stepIndex;
    private float returningAt;
    private RectTransform currentFocus;
    private bool focusProjected;
    private float focusVisibleAt;
    private string pagePrompt;
    private readonly Vector3[] focusCorners = new Vector3[4];
    private readonly List<EnemyController> preparingEnemies = new();
    private EnemyShieldSystem practiceShield;
    private TerminalEquipmentGlyph stepIcon;
    private bool defeatPractice;

    // 기본 무장 소개 영상 언어 기반 런타임 모션그래픽 크롬(프리팹 직렬화 참조 보존)
    private bool chromeReady;
    private CanvasGroup panelGroup;
    private TutorialCardGraphic cardSurface;
    private TutorialCardGraphic iconTile;
    private TutorialCardGraphic stepPips;
    private TutorialCardGraphic statusPill;
    private TutorialCardGraphic statusDot;
    private TextMeshProUGUI statusBadgeText;
    private RectTransform headerDivider;
    private TutorialCardGraphic footerPill;
    private TutorialKeycapStrip keycapStrip;
    private TutorialGearDiagram gearDiagram;
    private TutorialCardGraphic focusBracket;
    private TutorialCardGraphic focusLeader;
    private Color currentAccent = TutorialGuideTheme.RifleCyan;
    private float cardRevealAt;
    private float pipPopAt;
    private bool canvasLayerSynced;

    // 실습 성공 시 화면에 확실하게 띄우는 완료 배너·카드 점등·효과음 상태
    private RectTransform successBanner;
    private CanvasGroup successBannerGroup;
    private TutorialCardGraphic successBannerCard;
    private TutorialCardGraphic successPulseRing;
    private TutorialCardGraphic successBadgeCircle;
    private TutorialCardGraphic successCheckmark;
    private TextMeshProUGUI successTitleText;
    private TextMeshProUGUI successSubText;
    private float successShownAt = -10f;
    private float successDuration = 1.9f;
    private bool successBannerActive;
    private string lastSuccessTitle;
    private bool swapMilestoneShown, breakMilestoneShown, storeMilestoneShown, storeUseMilestoneShown;

    private JumpAbility practiceJump;
    private PlayerWeaponAbility practiceStorage;
    private bool storedInPractice, usedInPractice;
    // 기어 빠른 장전 수업: 빠른 장전으로 도달한 2단계 이상 기술을 실제로 발동했는지, 마지막 빠른 장전의 목표 단계(안내 문구용).
    private PlayerWeaponAbility practiceGear;
    private bool quickSkillInPractice;
    private int quickChargeStage;
    private WaveTransitionPanelUI[] waveTransitions;

    public bool IsComplete => phase == GuidePhase.Complete;
    public bool IsWaiting => phase == GuidePhase.Waiting;
    public bool IsExplaining => phase == GuidePhase.Explain || phase == GuidePhase.Release || phase == GuidePhase.Returning;
    public bool IsActive => isActiveAndEnabled && owner != null && IsExplaining;
    public TutorialAction CurrentAction => lesson != null ? lesson.action : TutorialAction.None;
    public WaveManager Owner => owner;
    public int CurrentStepIndex => stepIndex;
    public int StepCount => lesson != null && lesson.steps != null && lesson.steps.Length > 0 ? lesson.steps.Length : 1;
    public TutorialFocusTarget CurrentFocusTarget => CurrentStep != null ? CurrentStep.target : TutorialFocusTarget.Panel;
    public RectTransform FocusTarget => currentFocus;
    public bool FocusProjected => focusProjected;
    public TutorialKeycapStrip KeycapStrip => keycapStrip;
    public TutorialGearDiagram GearDiagram => gearDiagram;
    public bool IsSuccessBannerVisible => successBannerActive && successBanner != null && successBanner.gameObject.activeInHierarchy;
    public string LastSuccessTitle => lastSuccessTitle;
    private TutorialFocusStep CurrentStep => lesson != null && lesson.steps != null && stepIndex >= 0 && stepIndex < lesson.steps.Length ? lesson.steps[stepIndex] : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;

    public static void Begin(WaveManager manager, TutorialLesson configuration)
    {
        if (instance != null) instance.Close();
        if (manager == null || configuration == null || !configuration.enabled) return;
        if (instance == null)
        {
            var prefab = Resources.Load<GameObject>("Training/TutorialGuide");
            var guide = prefab != null ? prefab.GetComponent<TutorialWaveGuide>() : null;
            if (guide == null)
            {
                Debug.LogError("[TutorialWaveGuide] Training/TutorialGuide Prefab을 찾을 수 없습니다.");
                return;
            }
            instance = Instantiate(guide);
        }
        instance.Open(manager, configuration);
    }

    public static bool CanComplete(WaveManager manager)
    {
        if (manager == null || manager.Data == null || manager.currentWaveIndex >= manager.Data.waves.Length) return true;
        var current = manager.Data.waves[manager.currentWaveIndex].tutorial;
        return current == null || !current.enabled || (instance != null && instance.owner == manager && instance.IsComplete);
    }

    private void Open(WaveManager manager, TutorialLesson configuration)
    {
        gameObject.SetActive(true);
        owner = manager;
        lesson = configuration;
        defeatPractice = manager.Data.waves[manager.currentWaveIndex].clearType == WaveClearType.DefeatAllTargets;
        owner.WaveEnded += Close;
        owner.WavesReset += Close;
        owner.EnemySpawned += TrackEnemySpawn;
        preparingEnemies.Clear();
        waveTransitions = FindObjectsByType<WaveTransitionPanelUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        phase = GuidePhase.Waiting;
        player = null;
        movedDistance = 0f;
        stepIndex = 0;
        storedInPractice = usedInPractice = false;
        quickSkillInPractice = false;
        quickChargeStage = 0;
        currentFocus = null;
        focusProjected = false;
        currentAccent = TutorialGuideTheme.AccentOf(lesson.action);
        canvasLayerSynced = false;
        EnsureCanvasLayer();
        if (presentation == null && focus != null) presentation = new TutorialFocusPresentation(focus.rectTransform.parent as RectTransform, body != null ? body.font : null);
        presentation?.SetAccent(currentAccent);
        EnsureChrome();
        HideFocusDecor();
        HideSuccessBanner();
        swapMilestoneShown = breakMilestoneShown = storeMilestoneShown = storeUseMilestoneShown = false;
        lastSuccessTitle = null;
        InputManager.BindingsChanged -= RefreshLiveBindings;
        InputManager.BindingsChanged += RefreshLiveBindings;
        if (panel != null) panel.gameObject.SetActive(false);
        if (focus != null) { focus.Clear(); focus.Snap(); }
    }

    private void LateUpdate()
    {
        if (owner == null || !owner.isActiveAndEnabled) { Close(); return; }
        if (player == null) player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || player.Health == null) return;
        if (!player.gameObject.activeInHierarchy || player.Health.IsDead)
        {
            Close();
            return;
        }
        if (!canvasLayerSynced) EnsureCanvasLayer();
        var ui = UIManager.Instance;
        if (ui != null && (ui.IsDeathUIOpen || ui.IsCompleteBoardOpen)) { Close(); return; }
        if (phase == GuidePhase.Waiting)
        {
            if (owner.IsPreparingWave || WavePlayerStart.IsHolding(player) || !player.IsGrounded
                || ui == null || ui.BlocksGameplayInput || ui.HasActiveInputUI || IsPresentationPreparing()) return;
            if (lesson.focusEnabled) Explain();
            else StartPractice();
            return;
        }
        if (phase == GuidePhase.Explain)
        {
            UpdateFocus();
            UpdateVisualMotion();
        }
        else if (phase == GuidePhase.Release)
        {
            UpdateFocus();
            UpdateVisualMotion();
            bool held = confirmAction != null && confirmAction.IsPressed();
            if (!held)
            {
                if (stepIndex + 1 < StepCount) { stepIndex++; ShowStep(); }
                else ReturnToPractice();
            }
        }
        else if (phase == GuidePhase.Returning)
        {
            UpdateVisualMotion();
            float elapsed = Time.unscaledTime - returningAt;
            if (elapsed >= .25f && (presentation == null || presentation.IsPlayerVisible(player) || elapsed >= 2f)) StartPractice();
        }
        else if (phase == GuidePhase.Practice)
        {
            UpdateVisualMotion();
            if (gearDiagram != null && gearDiagram.gameObject.activeInHierarchy && player.AbilityManager != null)
                gearDiagram.Refresh(player.AbilityManager.AttackAbility, lesson.action);
            if (ui != null && ui.BlocksGameplayInput) { lastPosition = player.transform.position; return; }
            ObservePractice();
        }
        else if (phase == GuidePhase.Complete)
        {
            UpdateVisualMotion();
        }
    }

    private void TrackEnemySpawn(EnemyController enemy)
    {
        if (phase != GuidePhase.Waiting || enemy == null) return;
        preparingEnemies.Add(enemy);
        if (!defeatPractice && lesson.action == TutorialAction.ShieldBreakGear)
            practiceShield = enemy.Health != null ? enemy.Health.enemyShieldSystem : null;
    }

    private bool IsPresentationPreparing()
    {
        // Wave 제목과 적의 등장 연출은 게임 시간을 쓰므로 모두 끝난 다음 설명을 일시정지합니다.
        foreach (var transition in waveTransitions)
            if (transition != null && transition.isActiveAndEnabled && transition.IsPlaying) return true;

        var pool = EnemyPool.Instance;
        for (int i = preparingEnemies.Count - 1; i >= 0; i--)
        {
            var enemy = preparingEnemies[i];
            if (enemy == null || !enemy.gameObject.activeInHierarchy || pool == null || !pool.IsSpawning(enemy))
                preparingEnemies.RemoveAt(i);
        }
        return preparingEnemies.Count > 0;
    }

    private void Explain()
    {
        confirmAction = InputManager.Instance != null ? InputManager.Instance.Actions.FindAction("UI/Submit") : null;
        blockOwner = UIManager.Instance;
        if (blockOwner != null)
        {
            previousPauseBlock = blockOwner.PauseInputBlocked;
            blockOwner.PushGameplayBlock();
            blockOwner.PauseInputBlocked = true;
            blockOwner.PushInputTarget(this, UIInputLayer.System);
        }
        pauseOwner = GameTimeController.Instance;
        if (pauseOwner != null) pauseOwner.PushPause();
        ShowStep();
        if (confirmAction == null)
        {
            if (body != null) body.text += "\n\n입력 설정을 찾을 수 없어 목표 지점으로 진행할 수 있습니다.";
            Complete();
        }
    }

    private void ShowStep()
    {
        EnsureChrome();
        phase = GuidePhase.Explain;
        earliestInput = Time.unscaledTime + .25f;
        focusProjected = false;
        focusVisibleAt = float.PositiveInfinity;
        cardRevealAt = Time.unscaledTime;
        pipPopAt = Time.unscaledTime;
        presentation?.BeginStep();
        var step = CurrentStep;
        currentAccent = TutorialGuideTheme.AccentOf(lesson.action);
        presentation?.SetAccent(currentAccent);
        // 단계마다 그 단계가 다루는 조작의 키를 보인다(StepKeys). 같은 글자를 LessonIcon 대상의 키 표시에도 쓴다.
        string keys = StepKeys(step);
        presentation?.SetBindingText(keys);
        ShowStepIcon(step != null ? step.icon : TutorialStepIcon.None);
        if (gearDiagram != null) gearDiagram.gameObject.SetActive(false);
        if (title != null)
        {
            title.color = Color.white;
            title.text = step != null && !string.IsNullOrEmpty(step.title) ? step.title : lesson.title;
        }
        if (body != null)
        {
            string rawDesc = step != null && !string.IsNullOrEmpty(step.description) ? step.description : lesson.description;
            body.color = TutorialGuideTheme.BodyColor;
            body.text = TutorialGuideTheme.HighlightBody(rawDesc);
        }
        ApplyCardTheme(currentAccent, false);
        UpdateHeaderChrome(true, false);
        SetPanelLayout(true);
        if (keycapStrip != null)
        {
            keycapStrip.gameObject.SetActive(true);
            keycapStrip.SetItems(TutorialGuideTheme.BuildStepKeys(lesson, step), currentAccent, height: 34f, textSize: 17f, availableWidth: panel.sizeDelta.x - 64f);
        }
        if (panel != null) { panel.gameObject.SetActive(true); panel.SetAsLastSibling(); }
        pagePrompt = InputBindingDisplay.Confirm + " 선택 · " + (stepIndex + 1) + " / " + StepCount + (stepIndex + 1 < StepCount ? "  다음 설명" : "  설명 마치기");
        SetPrompt(pagePrompt);
        UpdateFocus();
    }

    private void ShowStepIcon(TutorialStepIcon icon)
    {
        if (panel == null) return;
        EnsureChrome();
        if (stepIcon == null)
        {
            var node = new GameObject("TutorialStepIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(TerminalEquipmentGlyph));
            node.layer = panel.gameObject.layer;
            stepIcon = node.GetComponent<TerminalEquipmentGlyph>();
            var rect = stepIcon.rectTransform;
            rect.SetParent(iconTile != null ? iconTile.rectTransform : panel, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(28f, 28f);
        }
        var glyph = TutorialGuideTheme.StepGlyph(icon, lesson != null ? lesson.action : TutorialAction.Move);
        stepIcon.Configure(glyph, currentAccent, 3.4f);
        if (iconTile != null) iconTile.gameObject.SetActive(true);
        stepIcon.gameObject.SetActive(true);
    }

    public void OnMove(Vector2 direction) { }
    public void OnCancel()
    {
        if (phase == GuidePhase.Explain || phase == GuidePhase.Release) ReturnToPractice();
    }

    private void ReturnToPractice()
    {
        presentation?.RestoreFollow();
        focus?.Clear();
        HideFocusDecor();
        phase = GuidePhase.Returning;
        returningAt = Time.unscaledTime;
        SetPrompt("실습 위치로 돌아갑니다");
    }
    public void OnConfirm()
    {
        if (phase != GuidePhase.Explain || Time.unscaledTime < earliestInput) return;
        if (!focusProjected || Time.unscaledTime < focusVisibleAt + .2f)
        {
            SetPrompt("대상 위치를 보여 주고 있습니다 · 잠시 후 선택하십시오");
            return;
        }
        phase = GuidePhase.Release;
        SetPrompt("선택 확인 · 키를 놓으면 " + (stepIndex + 1 < StepCount ? "다음 설명으로 넘어갑니다" : "실습을 시작합니다"));
    }

    private void UpdateFocus()
    {
        if (focus == null || panel == null) return;
        var step = CurrentStep;
        if (step == null || step.target == TutorialFocusTarget.Panel)
        {
            currentFocus = panel;
            if (!focusProjected) focusVisibleAt = Time.unscaledTime;
            focusProjected = true;
            focus.Focus(panel, step != null ? step.padding : new Vector2(16f, 16f));
            HideFocusDecor();
            return;
        }
        bool before = focusProjected;
        focusProjected = presentation != null && presentation.TryTarget(step, owner, player, lesson.action, out currentFocus);
        if (!focusProjected)
        {
            currentFocus = null;
            focus.Cover();
            HideFocusDecor();
            DockExplanation(true);
            return;
        }
        if (!before) focusVisibleAt = Time.unscaledTime;
        if (phase == GuidePhase.Explain && Time.unscaledTime >= focusVisibleAt + .2f) SetPrompt(pagePrompt);
        focus.Focus(currentFocus, step.padding);
        LayoutExternalExplanation(step.padding);
        UpdateFocusDecor(step.padding);
    }

    private void LayoutExternalExplanation(Vector2 padding)
    {
        var root = panel.parent as RectTransform;
        if (root == null || currentFocus == null) return;
        currentFocus.GetWorldCorners(focusCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in focusCorners)
        {
            Vector2 local = root.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        min -= padding;
        max += padding;
        Rect full = root.rect;
        float above = full.yMax - 44f - max.y;
        float below = min.y - full.yMin - 44f;
        float height = Mathf.Clamp(Mathf.Max(above, below), 206f, 356f);
        float width = Mathf.Min(780f, full.width - 48f);
        float left = min.x - full.xMin - 44f, right = full.xMax - max.x - 44f;
        if (Mathf.Max(above, below) < 206f && Mathf.Max(left, right) >= 420f)
        {
            bool useRight = right >= left;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(useRight ? 1f : 0f, .5f);
            panel.anchoredPosition = new Vector2(useRight ? -24f : 24f, 0f);
            width = Mathf.Min(780f, Mathf.Max(left, right));
            height = 356f;
        }
        else DockExplanation(above >= below);
        float prevWidth = panel.sizeDelta.x;
        float prevHeight = panel.sizeDelta.y;
        ApplyCardLayout(true, width, height, false);
        if (keycapStrip != null && (!Mathf.Approximately(prevWidth, width) || !Mathf.Approximately(prevHeight, height)))
        {
            bool compact = height < 270f;
            keycapStrip.SetItems(TutorialGuideTheme.BuildStepKeys(lesson, CurrentStep), currentAccent,
                height: compact ? 28f : 34f, textSize: compact ? 14.5f : 17f, availableWidth: width - 64f);
        }
    }

    private void DockExplanation(bool top)
    {
        if (panel == null) return;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, top ? 1f : 0f);
        panel.anchoredPosition = new Vector2(0f, top ? -24f : 24f);
    }

    private void StartPractice()
    {
        EnsureChrome();
        ReleaseFocus();
        currentAccent = TutorialGuideTheme.AccentOf(lesson.action);
        cardRevealAt = Time.unscaledTime;
        ShowStepIcon(TutorialStepIcon.None);
        ApplyCardTheme(currentAccent, false);
        UpdateHeaderChrome(false, false);
        bool showDiagram = !lesson.wallInstructions && ShouldShowGearDiagram();
        if (gearDiagram != null) gearDiagram.gameObject.SetActive(showDiagram);
        SetPanelLayout(false);
        if (panel != null)
        {
            panel.gameObject.SetActive(!lesson.wallInstructions);
            if (!lesson.wallInstructions) panel.SetAsLastSibling();
        }
        if (title != null) { title.color = Color.white; title.text = lesson.title; }
        // 실습 중에도 상단 패널에 키워드 강조가 입힌 수업 설명과 실시간 3D 키캡 줄을 남긴다(벽면 수업은 패널을 숨기고 벽면 판이 키를 보여 준다).
        if (body != null)
        {
            body.color = TutorialGuideTheme.BodyColor;
            body.text = lesson.wallInstructions ? lesson.description : TutorialGuideTheme.HighlightBody(lesson.description);
        }
        if (keycapStrip != null)
        {
            keycapStrip.gameObject.SetActive(!lesson.wallInstructions);
            if (!lesson.wallInstructions)
                keycapStrip.SetItems(TutorialGuideTheme.BuildActionKeys(lesson.action), currentAccent, height: 34f, textSize: 17f, availableWidth: panel.sizeDelta.x - 64f);
        }
        if (showDiagram && gearDiagram != null && player != null && player.AbilityManager != null)
            gearDiagram.Refresh(player.AbilityManager.AttackAbility, lesson.action);
        phase = GuidePhase.Practice;
        lastPosition = player.transform.position;
        weapons = new WeaponObservation(player.AbilityManager.AttackAbility);
        initialWeaponSet = weapons.SetIndex;
        if (!defeatPractice && lesson.action == TutorialAction.SkillStorage)
        {
            practiceStorage = player.AbilityManager.AttackAbility;
            if (practiceStorage != null)
            {
                practiceStorage.SkillStorageFinished += OnPracticeStored;
                practiceStorage.StoredSkillUsed += OnPracticeStorageUsed;
            }
        }
        if (lesson.action == TutorialAction.GearQuickCharge)
        {
            practiceGear = player.AbilityManager.AttackAbility;
            if (practiceGear != null)
            {
                practiceGear.GearSkillStarted += OnPracticeGearSkill;
                practiceGear.GearQuickCharged += OnPracticeQuickCharged;
            }
        }
        if (lesson.action == TutorialAction.Jump || lesson.action == TutorialAction.DoubleJump)
        {
            practiceJump = player.AbilityManager.JumpAbility;
            if (practiceJump != null) practiceJump.Jumped += OnPracticeJump;
        }

        if (!lesson.RequiresPractice)
        {
            Complete();
            return;
        }
        // 보조 무기 수업은 '회복'을 가르친다(벽면/화면 UI 공통). 어느 세트에도 해금된 회복이 없으면 설명만 확인하고 진행한다.
        if (lesson.action == TutorialAction.SecondaryWeapon)
        {
            if (!weapons.HasUsableHealing)
            {
                if (body != null) body.text += "\n\n회복 보조 무기가 장착된 무장이 없습니다. 이번 단계는 설명을 확인하고 목표로 진행합니다.";
                Complete();
                return;
            }
        }
        else if (lesson.action == TutorialAction.SwapWeapon || lesson.action == TutorialAction.Gear)
        {
            string unavailable = weapons.UnavailableReason(lesson.action);
            if (!string.IsNullOrEmpty(unavailable))
            {
                if (body != null) body.text += "\n\n" + unavailable + " 이번 단계는 설명을 확인하고 목표로 진행합니다.";
                Complete();
                return;
            }
        }
        if (lesson.action == TutorialAction.GearQuickCharge && !weapons.HasQuickChargeSkillInAnySet)
        {
            if (body != null) body.text += "\n\n2단계 이상 기어 기술이 있는 무장이 없습니다. 이번 단계는 설명을 확인하고 목표로 진행합니다.";
            Complete();
            return;
        }
        SetPrompt(PracticePrompt());
    }

    private void ObservePractice()
    {
        var abilities = player.AbilityManager;
        bool success = false;
        switch (lesson.action)
        {
            case TutorialAction.Move:
                float distance = Mathf.Abs(player.transform.position.x - lastPosition.x);
                // 재배치·크레인 이동은 보행 실습 거리에 포함하지 않습니다.
                if (distance < 2f && Mathf.Abs(player.MoveInput.x) > .1f && !WavePlayerStart.IsHolding(player)) movedDistance += distance;
                success = movedDistance >= Mathf.Max(.1f, lesson.requiredDistance);
                break;
            case TutorialAction.Dash:
                success = abilities.DashAbility != null && abilities.DashAbility.IsActive && ReachedDashExit();
                break;
            case TutorialAction.ShieldBreakGear:
                success = !defeatPractice && practiceShield != null && practiceShield.SuccessfulBreakCount > 0;
                break;
            case TutorialAction.SkillStorage: success = !defeatPractice && storedInPractice && usedInPractice; break;
            case TutorialAction.SwapWeapon: success = !defeatPractice && weapons.SetIndex >= 0 && weapons.SetIndex != initialWeaponSet; break;
            // 보조 무기 수업은 회복(HealingWeaponSO)을 실제로 써야 완료한다(다른 보조 무기 사용은 완료로 보지 않는다).
            case TutorialAction.SecondaryWeapon: success = weapons.IsExecutingHealing; break;
            case TutorialAction.Gear: success = weapons.IsExecutingGear; break;
            case TutorialAction.GearQuickCharge: success = quickSkillInPractice; break;
        }
        lastPosition = player.transform.position;
        if (success) Complete();
        else
        {
            ObserveDefeatMilestones();
            if (defeatPractice || lesson.action == TutorialAction.Gear || lesson.action == TutorialAction.SecondaryWeapon
                || lesson.action == TutorialAction.GearQuickCharge) SetPrompt(PracticePrompt());
        }
    }

    // 표적 처치형 튜토리얼(무장 전환·브레이크·스킬 저장)에서도 핵심 조작을 정확히 해낸 순간 즉시 성공 피드백을 띄운다.
    private void ObserveDefeatMilestones()
    {
        if (!defeatPractice || lesson == null || player == null || player.AbilityManager == null) return;
        switch (lesson.action)
        {
            case TutorialAction.SwapWeapon:
                if (!swapMilestoneShown && lesson.practiceArmament != null)
                {
                    var ability = player.AbilityManager.AttackAbility;
                    if (ability != null && ability.GetEquippedArmament(ability.CurrentSetIndex) == lesson.practiceArmament)
                    {
                        swapMilestoneShown = true;
                        ShowSuccessFeedback("무장 전환 성공!", "낮은 틈 가까이에서 사격해 표적을 파괴하세요");
                    }
                }
                break;
            case TutorialAction.ShieldBreakGear:
                if (!breakMilestoneShown)
                {
                    foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                    {
                        var shield = enemy.Health != null ? enemy.Health.enemyShieldSystem : null;
                        if (shield != null && (shield.IsBreaking || shield.SuccessfulBreakCount > 0))
                        {
                            breakMilestoneShown = true;
                            ShowSuccessFeedback("브레이크 성공!", "브레이크 중 기본 공격으로 표적을 처치하세요");
                            break;
                        }
                    }
                }
                break;
            case TutorialAction.SkillStorage:
                if (!storeMilestoneShown && owner != null && owner.TutorialSkillStored)
                {
                    storeMilestoneShown = true;
                    ShowSuccessFeedback("스킬 저장 성공!", "기어 키를 놓은 채 저장 키로 발동하세요");
                }
                else if (!storeUseMilestoneShown && owner != null && owner.TutorialStoredSkillUsed)
                {
                    storeUseMilestoneShown = true;
                    ShowSuccessFeedback("저장 스킬 발동 성공!", "남은 표적을 모두 처치하세요");
                }
                break;
        }
    }

    private void OnPracticeJump(bool extraJump)
    {
        if (phase == GuidePhase.Practice && (lesson.action == TutorialAction.Jump || extraJump)) Complete();
    }

    private void OnPracticeStored(bool completed) => storedInPractice |= completed;
    private void OnPracticeStorageUsed() => usedInPractice |= storedInPractice;

    // 빠른 장전으로 도달한 2단계 이상의 기술을 실제로 발동해야 완료한다(누른 채 기다려 넘어간 단계·빈 단계는 제외).
    private void OnPracticeGearSkill(WeaponBaseSO weapon, int stage, bool quick)
    {
        if (phase == GuidePhase.Practice && weapon != null && stage >= 2 && quick) quickSkillInPractice = true;
        quickChargeStage = 0;
    }

    private void OnPracticeQuickCharged(int stage) => quickChargeStage = stage;

    private bool ReachedDashExit()
    {
        if (lesson.dashCrossingOffset <= 0f) return true;
        int index = owner.currentWaveIndex;
        var layout = WaveRoomState.LayoutAt(owner.Data, index);
        var start = WaveRoomState.PlayerStartAt(owner.Data, index) - layout.spawnOffset;
        float exit = owner.SpawnRoot.TransformPoint(start + Vector2.right * lesson.dashCrossingOffset).x;
        return lastPosition.x < exit && player.transform.position.x >= exit;
    }

    private string PracticePrompt()
    {
        if (defeatPractice)
        {
            string targets = $"남은 표적 {owner.aliveTargets}기";
            switch (lesson.action)
            {
                case TutorialAction.Attack:
                    return CurrentKeys() + " · " + targets;
                case TutorialAction.SwapWeapon:
                    var ability = player.AbilityManager.AttackAbility;
                    bool ready = ability != null && ability.GetEquippedArmament(ability.CurrentSetIndex) == lesson.practiceArmament;
                    return ready
                        ? "틈 가까이에서 [ " + BindingLabel("Attack") + " ] 사격 · " + targets
                        : "[ " + BindingLabel("SwapWeapon") + " ] 키로 라이플 무장 선택";
                case TutorialAction.ShieldBreakGear:
                    // 지금 세트의 1단계 기어가 비어 있으면(잠김 포함) 먼저 1단계 기술이 있는 무장으로 바꾸게 한다.
                    if (weapons != null && weapons.FirstGearEmpty) return FirstGearHint() + " · " + targets;
                    return "[ " + BindingLabel("Attack") + " ] 쉴드 0 → [ " + BindingLabel("Gear") + " ] 유지 + [ " + BindingLabel("Attack")
                        + " ] 기어 발동(브레이크) → 처치 · " + targets;
                case TutorialAction.SkillStorage:
                    if (owner.TutorialStoredSkillUsed) return "저장 스킬 사용 확인 · " + targets;
                    if (owner.TutorialSkillStored) return "기어 키 [ " + BindingLabel("Gear") + " ]를 놓은 채 [ " + InputBindingDisplay.StoreSkill + " ] 사용";
                    if (weapons != null && weapons.FirstGearEmpty) return FirstGearHint();
                    return "[ " + BindingLabel("Attack") + " ] 공격으로 게이지 충전 → [ " + BindingLabel("Gear") + " ] 유지로 기어 준비 → [ "
                        + InputBindingDisplay.StoreSkill + " ] 저장";
            }
        }
        if (lesson.action == TutorialAction.GearQuickCharge) return QuickChargePrompt();
        if (lesson.action == TutorialAction.SecondaryWeapon && weapons != null && weapons.HasUsableHealing && !weapons.CurrentSecondaryIsHealing)
            return "[ " + BindingLabel("SwapWeapon") + " ] 무장 전환 · 보조 무기가 '회복'인 무장을 선택하세요";
        if ((lesson.action == TutorialAction.Gear || lesson.action == TutorialAction.SecondaryWeapon) && weapons != null && weapons.NeedsGauge(lesson.action))
            return "게이지 충전: 훈련 표적을 [ " + BindingLabel("Attack") + " ]로 적중시키십시오 · " + weapons.GaugeLabel(lesson.action);
        switch (lesson.action)
        {
            case TutorialAction.Move: return "[ " + BindingLabel("Move") + " ] 이동한 뒤 목표 지점으로 가십시오";
            case TutorialAction.DoubleJump: return "[ " + BindingLabel("Jump") + " ] 점프한 뒤 공중에서 다시 누르십시오";
            case TutorialAction.Gear: return "[ " + BindingLabel("Gear") + " ] 기어 준비 → 단계가 켜지면 [ " + BindingLabel("Attack") + " ] 발동";
            case TutorialAction.SkillStorage: return CurrentKeys() + " · 저장 후 같은 입력으로 사용";
            default: return "[ " + BindingLabel(ActionName(lesson.action)) + " ] 실제로 사용하면 목표 지점이 열립니다";
        }
    }

    private string QuickChargePrompt()
    {
        string gear = BindingLabel("Gear"), attack = BindingLabel("Attack");
        var ability = player != null && player.AbilityManager != null ? player.AbilityManager.AttackAbility : null;
        if (ability == null || weapons == null) return CurrentKeys();
        if (weapons.QuickChargeSkill == null)
            return "[ " + BindingLabel("SwapWeapon") + " ] 무장 전환 · 2단계 기술이 있는 " + weapons.QuickChargeArmamentName + "무장을 선택하세요";
        if (weapons.NeedsQuickChargeGauge)
            return "게이지 충전: 훈련 표적을 [ " + attack + " ]로 적중 · " + weapons.QuickChargeGaugeLabel;
        if (ability.IsGearQuickCharged)
        {
            if (!ability.IsGearWaiting) return "빠른 장전 중 · 다음 단계가 켜질 때까지 [ " + gear + " ] 유지";
            // 잠겨 비어 있는 단계까지 넘어갔으면 발동할 기술이 없다(기어가 풀린 뒤 처음부터 다시).
            var stageWeapon = weapons.StageWeapon(ability.GearStage);
            if (stageWeapon == null) return ability.GearStage + "단계는 비어 있습니다 · 기어가 풀리면 [ " + gear + " ]부터 다시";
            float stageCooldown = ability.GetSkillCooldownRemaining(stageWeapon);
            if (stageCooldown > .05f) return ability.GearStage + "단계 재사용 대기 중 · " + stageCooldown.ToString("0.0") + "초 뒤 다시 시도하세요";
            if (ability.SkillGaugeValue < stageWeapon.skillGauge)
                return "게이지 부족 · 훈련 표적을 [ " + attack + " ]로 적중 · " + Mathf.FloorToInt(ability.SkillGaugeValue) + " / " + stageWeapon.skillGauge.ToString("0");
            return "빠른 장전 성공 · " + ability.GearStage + "단계가 켜져 있는 동안 [ " + attack + " ]로 발동";
        }
        float quickCooldown = weapons.QuickChargeCooldownRemaining;
        if (quickCooldown > .05f)
            return "재사용 대기 중 · " + quickCooldown.ToString("0.0") + "초 뒤 다시 시도하세요";
        if (quickChargeStage > 0 && !ability.IsGearCharging && !ability.IsGearWaiting)
            return "기어가 풀렸습니다 · 다시 누른 [ " + gear + " ]는 다음 단계가 켜질 때까지 유지하세요";
        return "[ " + gear + " ] → 1단계가 켜지면 떼었다가 다시 [ " + gear + " ] 유지 → 2단계에서 [ " + attack + " ] 발동";
    }

    private string FirstGearHint()
    {
        string armament = weapons != null ? weapons.FirstGearArmamentName : null;
        return "1단계 기어가 비어 있습니다 · [ " + BindingLabel("SwapWeapon") + " ]로 " + (string.IsNullOrEmpty(armament) ? "다른 무장" : armament) + " 선택";
    }

    // 설명·실습 패널의 키 줄은 머리말 '조작 · '을 붙인 노란 줄로 본문 끝에 둔다.
    private static string KeyLine(string keys) => string.IsNullOrEmpty(keys) ? "" : "\n<color=#FFD57B>조작 · " + keys + "</color>";

    // 설명 단계마다 그 단계가 다루는 조작의 키를 고른다(단계 아이콘을 먼저, 아이콘이 일반이면 강조 대상을 본다).
    // 빠른 장전 수업은 모든 단계에 전용 키 줄을 쓴다. 키 이름은 늘 현재 바인딩에서 읽는다(BindingLabel·InputBindingDisplay).
    private string StepKeys(TutorialFocusStep step)
    {
        if (lesson == null || step == null || lesson.action == TutorialAction.GearQuickCharge) return CurrentKeys();
        switch (step.icon)
        {
            case TutorialStepIcon.Swap: return SwapKeys();
            case TutorialStepIcon.Gear:
            case TutorialStepIcon.GearReady: return GearKeys();
            case TutorialStepIcon.Storage: return "기어 준비 중 [ " + InputBindingDisplay.StoreSkill + " ] 저장";
            case TutorialStepIcon.Gauge: return GaugeKeys();
            case TutorialStepIcon.Secondary: return SecondaryKeys();
        }
        switch (step.target)
        {
            case TutorialFocusTarget.AutoAim: return "자동 조준 전환 [ " + BindingLabel("ToggleAutoAim") + " ]";
            case TutorialFocusTarget.WeaponSet: return SwapKeys();
            case TutorialFocusTarget.GearOne:
            case TutorialFocusTarget.GearTwo:
            case TutorialFocusTarget.GearThree:
            case TutorialFocusTarget.GearStages: return GearKeys();
            case TutorialFocusTarget.SkillGauge: return GaugeKeys();
            case TutorialFocusTarget.SecondaryWeapon: return SecondaryKeys();
        }
        return CurrentKeys();
    }

    private static string SwapKeys() => "무장 전환 [ " + BindingLabel("SwapWeapon") + " ]";
    private static string GearKeys() => "기어 [ " + BindingLabel("Gear") + " ] 유지 → 단계가 켜지면 [ " + BindingLabel("Attack") + " ] 발동";
    private static string GaugeKeys() => "기본 공격 [ " + BindingLabel("Attack") + " ]로 게이지 충전";
    private static string SecondaryKeys() => "보조 무기 [ " + BindingLabel("SecondaryWeapon") + " ]";

    private string CurrentKeys()
    {
        if (lesson == null) return "";
        switch (lesson.action)
        {
            case TutorialAction.Move: return "이동 [ " + BindingLabel("Move") + " ]";
            case TutorialAction.Jump:
            case TutorialAction.DoubleJump: return "점프 [ " + BindingLabel("Jump") + " ]";
            case TutorialAction.Dash: return "대시 [ " + BindingLabel("Dash") + " ]";
            case TutorialAction.SwapWeapon: return SwapKeys();
            case TutorialAction.SecondaryWeapon: return "보조 무기 [ " + BindingLabel("SecondaryWeapon") + " ]";
            case TutorialAction.Attack:
            case TutorialAction.Shield: return "기본 공격 [ " + BindingLabel("Attack") + " ]";
            case TutorialAction.ShieldBreakGear: return "공격 [ " + BindingLabel("Attack") + " ] · 기어 [ " + BindingLabel("Gear") + " ] 유지 → [ " + BindingLabel("Attack") + " ] 발동";
            case TutorialAction.GearQuickCharge:
                return "기어 [ " + BindingLabel("Gear") + " ] → 떼었다가 다시 [ " + BindingLabel("Gear") + " ] 유지 → 발동 [ " + BindingLabel("Attack") + " ]";
            case TutorialAction.Break:
            case TutorialAction.Gear: return "기어 [ " + BindingLabel("Gear") + " ] → 발동 [ " + BindingLabel("Attack") + " ]";
            case TutorialAction.SkillStorage: return "기어 [ " + BindingLabel("Gear") + " ] → 저장 [ " + InputBindingDisplay.StoreSkill + " ]";
            case TutorialAction.Platforms:
            case TutorialAction.Trap: return "이동 [ " + BindingLabel("Move") + " ] · 점프 [ " + BindingLabel("Jump") + " ]";
            default: return "";
        }
    }

    private void Complete()
    {
        bool alreadyComplete = phase == GuidePhase.Complete;
        ReleaseFocus();
        phase = GuidePhase.Complete;
        owner?.RevealPracticeGoals();
        ApplyCardTheme(TutorialGuideTheme.CompleteGreen, true);
        UpdateHeaderChrome(false, true);
        SetPanelLayout(false);
        SetPrompt("실습 성공 · 빛나는 목표 지점으로 이동하십시오");
        if (!alreadyComplete)
            ShowSuccessFeedback("실습 성공!", "빛나는 목표 지점으로 이동하세요");
    }

    // 튜토리얼 UI Canvas가 MainScene의 UI Canvas(ScreenSpaceCamera)와 같은 카메라·레이어를 쓰되
    // HUD(1)보다 앞, 일시정지 창(190)·옵션 창(200)보다 뒤(50)에 그려지게 맞춘다.
    private void EnsureCanvasLayer()
    {
        var ownCanvas = GetComponent<Canvas>();
        if (ownCanvas == null) return;
        var hud = FindFirstObjectByType<PlayerHUD>(FindObjectsInactive.Include);
        var uiCanvas = hud != null ? hud.GetComponentInParent<Canvas>() : null;
        if (uiCanvas == null) return;

        if (uiCanvas.renderMode == RenderMode.ScreenSpaceCamera && uiCanvas.worldCamera != null)
        {
            ownCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            ownCanvas.worldCamera = uiCanvas.worldCamera;
            ownCanvas.planeDistance = uiCanvas.planeDistance;
            ownCanvas.sortingLayerID = uiCanvas.sortingLayerID;
            ownCanvas.sortingOrder = 50;
            SetLayerRecursively(gameObject, uiCanvas.gameObject.layer);
            canvasLayerSynced = true;
        }
        else
        {
            ownCanvas.sortingOrder = 50;
            canvasLayerSynced = true;
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null) return;
        target.layer = layer;
        var tr = target.transform;
        for (int i = 0; i < tr.childCount; i++)
            SetLayerRecursively(tr.GetChild(i).gameObject, layer);
    }

    private void ShowSuccessFeedback(string headline, string subtitle)
    {
        EnsureChrome();
        lastSuccessTitle = headline;
        successShownAt = Time.unscaledTime;
        successDuration = 2.0f;
        successBannerActive = true;
        cardRevealAt = Time.unscaledTime;

        if (successBanner != null)
        {
            successTitleText.text = headline;
            successSubText.text = subtitle;
            successBanner.gameObject.SetActive(true);
            successBanner.SetAsLastSibling();
            if (successBannerGroup != null) successBannerGroup.alpha = 1f;
            successBanner.localScale = new Vector3(0.76f, 0.76f, 1f);
        }

        if (practiceSuccessClip != null && SoundManager.Instance != null)
        {
            Vector3 pos = player != null ? player.transform.position : Vector3.zero;
            SoundManager.Instance.PlaySFX(new SoundOptions
            {
                bus = SoundBus.SFX,
                clip = practiceSuccessClip,
                volume = 0.62f,
                pitch = 1.06f,
                spatialBlend = 0f,
                minInterval = 0.08f,
                maxSimultaneous = 2,
            }, pos);
        }
    }

    private void HideSuccessBanner()
    {
        successBannerActive = false;
        if (successBanner != null) successBanner.gameObject.SetActive(false);
    }

    private void SetPrompt(string value) { if (prompt != null) prompt.text = value; }

    private bool ShouldShowGearDiagram() => lesson != null
        && (lesson.action == TutorialAction.ShieldBreakGear || lesson.action == TutorialAction.GearQuickCharge
            || lesson.action == TutorialAction.SkillStorage || lesson.action == TutorialAction.Gear);

    private void EnsureChrome()
    {
        if (chromeReady || panel == null) return;
        chromeReady = true;

        // 부드러운 차콜 스포트라이트(기본 무장 소개 영상 58% 어둠 + 넓은 페더, 외곽 사각 테두리는 둥근 괄호로 대체)
        if (focus != null)
            focus.ConfigureStyle(0.58f, new Color(0.012f, 0.016f, 0.024f, 1f), 44f, 0f, Color.clear);

        // 기존 평면 직각 Image와 상단 Accent 막대는 숨기고 둥근 카드 그래픽으로 대체한다.
        var legacyBg = panel.GetComponent<UnityEngine.UI.Image>();
        if (legacyBg != null) legacyBg.enabled = false;
        var legacyAccent = panel.Find("Accent");
        if (legacyAccent != null) legacyAccent.gameObject.SetActive(false);

        panelGroup = panel.GetComponent<CanvasGroup>();
        if (panelGroup == null) panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        panelGroup.blocksRaycasts = false;
        panelGroup.interactable = false;

        var cardRect = CreateChildRect(panel, "CardSurface", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        cardRect.SetAsFirstSibling();
        cardSurface = cardRect.gameObject.AddComponent<TutorialCardGraphic>();

        var tileRect = CreateChildRect(panel, "IconTile", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(52f, -40f), new Vector2(44f, 44f));
        iconTile = tileRect.gameObject.AddComponent<TutorialCardGraphic>();

        var pipsRect = CreateChildRect(panel, "StepPips", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(168f, -62f), new Vector2(160f, 14f));
        stepPips = pipsRect.gameObject.AddComponent<TutorialCardGraphic>();
        stepPips.gameObject.SetActive(false);

        var pillRect = CreateChildRect(panel, "StatusPill", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-101f, -34f), new Vector2(146f, 30f));
        statusPill = pillRect.gameObject.AddComponent<TutorialCardGraphic>();
        var dotRect = CreateChildRect(pillRect, "StatusDot", new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(18f, 0f), new Vector2(14f, 14f));
        statusDot = dotRect.gameObject.AddComponent<TutorialCardGraphic>();
        var badgeGo = new GameObject("StatusBadgeText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        badgeGo.layer = panel.gameObject.layer;
        var badgeRect = (RectTransform)badgeGo.transform;
        badgeRect.SetParent(pillRect, false);
        badgeRect.anchorMin = new Vector2(0f, .5f);
        badgeRect.anchorMax = new Vector2(1f, .5f);
        badgeRect.pivot = new Vector2(0f, .5f);
        badgeRect.offsetMin = new Vector2(30f, -14f);
        badgeRect.offsetMax = new Vector2(-8f, 14f);
        statusBadgeText = badgeGo.GetComponent<TextMeshProUGUI>();
        if (body != null && body.font != null) statusBadgeText.font = body.font;
        statusBadgeText.fontSize = 14.5f;
        statusBadgeText.fontStyle = FontStyles.Bold;
        statusBadgeText.alignment = TextAlignmentOptions.MidlineLeft;
        statusBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
        statusBadgeText.overflowMode = TextOverflowModes.Overflow;
        statusBadgeText.raycastTarget = false;

        headerDivider = CreateChildRect(panel, "HeaderDivider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -76f), new Vector2(-60f, 1.5f));
        var divImg = headerDivider.gameObject.AddComponent<UnityEngine.UI.Image>();
        divImg.color = TutorialGuideTheme.DividerColor;
        divImg.raycastTarget = false;

        var footerRect = CreateChildRect(panel, "FooterPill", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        footerPill = footerRect.gameObject.AddComponent<TutorialCardGraphic>();
        if (prompt != null) footerRect.SetSiblingIndex(prompt.transform.GetSiblingIndex());

        var diagramRect = CreateChildRect(panel, "GearDiagram", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        gearDiagram = diagramRect.gameObject.AddComponent<TutorialGearDiagram>();
        gearDiagram.Initialize(body != null ? body.font : null);
        gearDiagram.gameObject.SetActive(false);

        var stripRect = CreateChildRect(panel, "KeycapStrip", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        keycapStrip = stripRect.gameObject.AddComponent<TutorialKeycapStrip>();
        keycapStrip.Initialize(body != null ? body.font : null, "GuideKey", centered: false);
        keycapStrip.gameObject.SetActive(false);

        // 포커스 대상용 둥근 모서리 괄호·리더 라인(Canvas 루트 위, 패널 뒤)
        var root = panel.parent as RectTransform;
        if (root != null)
        {
            var leaderRect = CreateChildRect(root, "FocusLeader", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            leaderRect.pivot = root.pivot;
            leaderRect.offsetMin = Vector2.zero;
            leaderRect.offsetMax = Vector2.zero;
            focusLeader = leaderRect.gameObject.AddComponent<TutorialCardGraphic>();
            focusLeader.ConfigureLeaderLine(currentAccent, 3f);
            focusLeader.gameObject.SetActive(false);

            var bracketRect = CreateChildRect(root, "FocusBracket", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(100f, 100f));
            focusBracket = bracketRect.gameObject.AddComponent<TutorialCardGraphic>();
            focusBracket.ConfigureBracketBox(currentAccent, 4f, 26f);
            focusBracket.gameObject.SetActive(false);

            // 실습 성공 시 상단 카드 아래 중앙에 확실히 띄우는 성공 배너(패널 자식이 아니므로 패널 TMP 경계 검사에 간섭 없음)
            successBanner = CreateChildRect(root, "SuccessBanner", new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -342f), new Vector2(520f, 84f));
            successBannerGroup = successBanner.gameObject.AddComponent<CanvasGroup>();
            successBannerGroup.blocksRaycasts = false;
            successBannerGroup.interactable = false;

            var pulseRect = CreateChildRect(successBanner, "SuccessPulseRing", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            successPulseRing = pulseRect.gameObject.AddComponent<TutorialCardGraphic>();
            successPulseRing.ConfigureCard(22f, Color.clear, TutorialGuideTheme.CompleteGreen, 3.2f, TutorialGuideTheme.CompleteGreen, 0f, 0f, 0f, 0f);

            var bannerCardRect = CreateChildRect(successBanner, "SuccessCard", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            successBannerCard = bannerCardRect.gameObject.AddComponent<TutorialCardGraphic>();
            successBannerCard.ConfigureCard(18f, new Color(0.039f, 0.086f, 0.067f, 0.96f), TutorialGuideTheme.CompleteGreen, 2.4f, TutorialGuideTheme.CompleteGreen, 8f, 0.48f, 14f, 0.10f);

            var badgeCircleRect = CreateChildRect(successBanner, "SuccessCheckTile", new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(46f, 0f), new Vector2(46f, 46f));
            successBadgeCircle = badgeCircleRect.gameObject.AddComponent<TutorialCardGraphic>();
            successBadgeCircle.ConfigureCard(23f, new Color(0.11f, 0.34f, 0.21f, 0.95f), TutorialGuideTheme.CompleteGreen, 2.2f, TutorialGuideTheme.CompleteGreen, 0f, 0f, 0f, 0.06f);

            var checkRect = CreateChildRect(badgeCircleRect, "SuccessCheckGlyph", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(28f, 28f));
            successCheckmark = checkRect.gameObject.AddComponent<TutorialCardGraphic>();
            successCheckmark.ConfigureCheckmark(TutorialGuideTheme.CompleteGreen, 4f, 1f);

            var titleGo = new GameObject("SuccessHeadline", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            titleGo.layer = root.gameObject.layer;
            var tr = (RectTransform)titleGo.transform;
            tr.SetParent(successBanner, false);
            tr.anchorMin = new Vector2(0f, .5f);
            tr.anchorMax = new Vector2(1f, .5f);
            tr.pivot = new Vector2(0f, .5f);
            tr.offsetMin = new Vector2(84f, 2f);
            tr.offsetMax = new Vector2(-20f, 38f);
            successTitleText = titleGo.GetComponent<TextMeshProUGUI>();
            if (body != null && body.font != null) successTitleText.font = body.font;
            successTitleText.fontSize = 24f;
            successTitleText.fontStyle = FontStyles.Bold;
            successTitleText.color = TutorialGuideTheme.CompleteGreen;
            successTitleText.alignment = TextAlignmentOptions.MidlineLeft;
            successTitleText.textWrappingMode = TextWrappingModes.NoWrap;
            successTitleText.overflowMode = TextOverflowModes.Overflow;
            successTitleText.raycastTarget = false;

            var subGo = new GameObject("SuccessSubtitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            subGo.layer = root.gameObject.layer;
            var sr = (RectTransform)subGo.transform;
            sr.SetParent(successBanner, false);
            sr.anchorMin = new Vector2(0f, .5f);
            sr.anchorMax = new Vector2(1f, .5f);
            sr.pivot = new Vector2(0f, .5f);
            sr.offsetMin = new Vector2(84f, -34f);
            sr.offsetMax = new Vector2(-20f, 2f);
            successSubText = subGo.GetComponent<TextMeshProUGUI>();
            if (body != null && body.font != null) successSubText.font = body.font;
            successSubText.fontSize = 16.5f;
            successSubText.fontStyle = FontStyles.Normal;
            successSubText.color = new Color(0.86f, 0.97f, 0.90f, 1f);
            successSubText.alignment = TextAlignmentOptions.MidlineLeft;
            successSubText.textWrappingMode = TextWrappingModes.NoWrap;
            successSubText.overflowMode = TextOverflowModes.Overflow;
            successSubText.raycastTarget = false;

            successBanner.gameObject.SetActive(false);
        }

        if (title != null) title.alignment = TextAlignmentOptions.MidlineLeft;
        if (body != null) body.alignment = TextAlignmentOptions.TopLeft;
        if (prompt != null) prompt.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void ApplyCardTheme(Color accent, bool completed)
    {
        Color border = completed
            ? new Color(accent.r, accent.g, accent.b, 0.50f)
            : new Color(accent.r, accent.g, accent.b, 0.28f);
        if (cardSurface != null)
            cardSurface.ConfigureCard(20f, TutorialGuideTheme.InkFill, border, 1.8f, accent, 8f, 0.46f, 15f, 0.08f);
        if (iconTile != null)
            iconTile.ConfigureCard(12f, TutorialGuideTheme.TileFill, accent, 2.2f, accent, 0f, 0f, 0f, 0.06f);
        if (stepIcon != null)
            stepIcon.Configure(TutorialGuideTheme.StepGlyph(CurrentStep != null && IsExplaining ? CurrentStep.icon : TutorialStepIcon.None,
                lesson != null ? lesson.action : TutorialAction.Move), accent, 3.4f);
        if (statusPill != null)
            statusPill.ConfigureCard(15f, TutorialGuideTheme.TileFill, new Color(accent.r, accent.g, accent.b, 0.45f), 1.5f, accent, 0f, 0f, 0f, 0.04f);
        if (statusDot != null)
            statusDot.ConfigureDot(accent);
        if (footerPill != null)
        {
            Color footerBorder = completed
                ? new Color(accent.r, accent.g, accent.b, 0.42f)
                : new Color(1f, 1f, 1f, 0.10f);
            footerPill.ConfigureCard(12f, TutorialGuideTheme.FooterFill, footerBorder, 1.3f, accent, completed ? 4f : 0f, 0f, 0f, 0.04f);
        }
        if (prompt != null)
            prompt.color = completed ? TutorialGuideTheme.CompleteGreen : TutorialGuideTheme.PromptAmber;
    }

    private void UpdateHeaderChrome(bool explaining, bool completed)
    {
        if (statusBadgeText != null)
        {
            statusBadgeText.color = Color.white;
            statusBadgeText.text = completed
                ? "실습 성공"
                : explaining
                    ? "설명 " + (stepIndex + 1) + " / " + StepCount
                    : "실습 진행";
        }
        if (stepPips != null)
        {
            bool showPips = explaining && StepCount > 1;
            stepPips.gameObject.SetActive(showPips);
            if (showPips) stepPips.ConfigurePips(StepCount, stepIndex + 1, currentAccent, 0f);
        }
    }

    private void UpdateVisualMotion()
    {
        float sinceReveal = Time.unscaledTime - cardRevealAt;
        if (panelGroup != null)
            panelGroup.alpha = Mathf.Clamp01(0.35f + 0.65f * TutorialCardGraphic.OutCubic(sinceReveal / 0.18f));
        if (iconTile != null && iconTile.gameObject.activeSelf)
        {
            float s = Mathf.Lerp(0.84f, 1f, TutorialCardGraphic.OutBack(Mathf.Clamp01(sinceReveal / 0.28f), 1.35f));
            iconTile.rectTransform.localScale = new Vector3(s, s, 1f);
        }
        if (stepPips != null && stepPips.gameObject.activeSelf)
        {
            float pop = Mathf.Clamp01((Time.unscaledTime - pipPopAt) / 0.36f);
            stepPips.ConfigurePips(StepCount, stepIndex + 1, currentAccent, pop);
        }
        UpdateSuccessBannerMotion();
    }

    private void UpdateSuccessBannerMotion()
    {
        if (!successBannerActive || successBanner == null) return;
        float elapsed = Time.unscaledTime - successShownAt;
        if (elapsed >= successDuration)
        {
            HideSuccessBanner();
            return;
        }
        // 상단 카드 바로 아래에 배치해 카드와 겹치지 않게 한다.
        float panelBottom = panel != null && panel.gameObject.activeSelf ? (panel.sizeDelta.y + 38f) : 290f;
        successBanner.anchoredPosition = new Vector2(0f, -(panelBottom + 44f));

        float pop = TutorialCardGraphic.OutBack(Mathf.Clamp01(elapsed / 0.24f), 1.55f);
        float scale = Mathf.Lerp(0.76f, 1f, pop);
        successBanner.localScale = new Vector3(scale, scale, 1f);

        if (successCheckmark != null)
            successCheckmark.ConfigureCheckmark(TutorialGuideTheme.CompleteGreen, 4f, Mathf.Clamp01((elapsed - 0.04f) / 0.24f));

        if (successPulseRing != null)
        {
            float ringT = Mathf.Clamp01(elapsed / 0.48f);
            float ringScale = Mathf.Lerp(1f, 1.11f, TutorialCardGraphic.OutCubic(ringT));
            successPulseRing.rectTransform.localScale = new Vector3(ringScale, ringScale, 1f);
            Color ringCol = TutorialGuideTheme.CompleteGreen;
            ringCol.a = (1f - ringT) * 0.85f;
            successPulseRing.ConfigureCard(22f, Color.clear, ringCol, 3.2f, ringCol, 0f, 0f, 0f, 0f);
        }

        if (successBannerGroup != null)
        {
            float fadeStart = successDuration - 0.35f;
            successBannerGroup.alpha = elapsed < fadeStart ? 1f : Mathf.Clamp01((successDuration - elapsed) / 0.35f);
        }
    }

    private void UpdateFocusDecor(Vector2 padding)
    {
        if (focusBracket == null || focusLeader == null || panel == null || currentFocus == null) return;
        var root = panel.parent as RectTransform;
        if (root == null) return;

        currentFocus.GetWorldCorners(focusCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector2 pt = root.InverseTransformPoint(focusCorners[i]);
            min = Vector2.Min(min, pt);
            max = Vector2.Max(max, pt);
        }
        min -= padding;
        max += padding;
        Rect hole = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        float elapsed = Mathf.Max(0f, Time.unscaledTime - focusVisibleAt);

        var br = focusBracket.rectTransform;
        br.anchorMin = br.anchorMax = br.pivot = new Vector2(.5f, .5f);
        br.anchoredPosition = hole.center;
        br.sizeDelta = new Vector2(Mathf.Max(28f, hole.width + 8f), Mathf.Max(28f, hole.height + 8f));
        focusBracket.gameObject.SetActive(true);
        focusBracket.SetBracketState(Mathf.Clamp01(elapsed / 0.30f), currentAccent);
        if (focus != null) focusBracket.transform.SetSiblingIndex(focus.transform.GetSiblingIndex() + 1);

        // 패널과 포커스 구멍 사이를 잇는 리더 라인
        panel.GetWorldCorners(focusCorners);
        Vector2 pMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 pMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector2 pt = root.InverseTransformPoint(focusCorners[i]);
            pMin = Vector2.Min(pMin, pt);
            pMax = Vector2.Max(pMax, pt);
        }
        Rect pRect = Rect.MinMaxRect(pMin.x, pMin.y, pMax.x, pMax.y);
        Vector2 start, end;
        if (pRect.yMin >= hole.yMax - 2f)
        {
            start = new Vector2(hole.center.x, hole.yMax + 4f);
            end = new Vector2(Mathf.Clamp(hole.center.x, pRect.xMin + 44f, pRect.xMax - 44f), pRect.yMin);
        }
        else if (pRect.yMax <= hole.yMin + 2f)
        {
            start = new Vector2(hole.center.x, hole.yMin - 4f);
            end = new Vector2(Mathf.Clamp(hole.center.x, pRect.xMin + 44f, pRect.xMax - 44f), pRect.yMax);
        }
        else if (pRect.xMin >= hole.xMax - 2f)
        {
            start = new Vector2(hole.xMax + 4f, hole.center.y);
            end = new Vector2(pRect.xMin, Mathf.Clamp(hole.center.y, pRect.yMin + 36f, pRect.yMax - 36f));
        }
        else
        {
            start = new Vector2(hole.xMin - 4f, hole.center.y);
            end = new Vector2(pRect.xMax, Mathf.Clamp(hole.center.y, pRect.yMin + 36f, pRect.yMax - 36f));
        }

        if (Vector2.Distance(start, end) > 20f)
        {
            var lr = focusLeader.rectTransform;
            Vector2 sLocal = lr.InverseTransformPoint(root.TransformPoint(start));
            Vector2 eLocal = lr.InverseTransformPoint(root.TransformPoint(end));
            focusLeader.gameObject.SetActive(true);
            focusLeader.SetLeaderLine(sLocal, eLocal, Mathf.Clamp01(elapsed / 0.34f), currentAccent);
            if (focus != null) focusLeader.transform.SetSiblingIndex(focus.transform.GetSiblingIndex() + 1);
        }
        else
        {
            focusLeader.gameObject.SetActive(false);
        }
        panel.SetAsLastSibling();
    }

    private void HideFocusDecor()
    {
        if (focusBracket != null) focusBracket.gameObject.SetActive(false);
        if (focusLeader != null) focusLeader.gameObject.SetActive(false);
    }

    private void RefreshLiveBindings()
    {
        if (lesson == null || panel == null || !panel.gameObject.activeInHierarchy) return;
        if (IsExplaining)
        {
            var step = CurrentStep;
            string keys = StepKeys(step);
            presentation?.SetBindingText(keys);
            pagePrompt = InputBindingDisplay.Confirm + " 선택 · " + (stepIndex + 1) + " / " + StepCount + (stepIndex + 1 < StepCount ? "  다음 설명" : "  설명 마치기");
            if (phase == GuidePhase.Explain) SetPrompt(pagePrompt);
            if (keycapStrip != null)
            {
                bool compact = panel.sizeDelta.y < 270f;
                keycapStrip.SetItems(TutorialGuideTheme.BuildStepKeys(lesson, step), currentAccent,
                    height: compact ? 28f : 34f, textSize: compact ? 14.5f : 17f, availableWidth: panel.sizeDelta.x - 64f);
            }
        }
        else if (phase == GuidePhase.Practice)
        {
            if (keycapStrip != null && !lesson.wallInstructions)
                keycapStrip.SetItems(TutorialGuideTheme.BuildActionKeys(lesson.action), currentAccent,
                    height: 34f, textSize: 17f, availableWidth: panel.sizeDelta.x - 64f);
            SetPrompt(PracticePrompt());
        }
    }

    private void SetPanelLayout(bool explaining)
    {
        if (panel == null) return;
        EnsureChrome();
        panel.anchorMin = panel.anchorMax = explaining ? new Vector2(.5f, .5f) : new Vector2(.5f, 1f);
        panel.pivot = explaining ? new Vector2(.5f, .5f) : new Vector2(.5f, 1f);
        panel.anchoredPosition = explaining ? Vector2.zero : new Vector2(0f, -20f);
        if (!explaining && lesson != null && !lesson.focusEnabled && lesson.action == TutorialAction.Move)
        {
            ApplyCardLayout(false, 780f, 206f, false);
            return;
        }
        bool withDiagram = !explaining && !lesson.wallInstructions && ShouldShowGearDiagram();
        float width = withDiagram ? 812f : 780f;
        float height = explaining ? 356f : (withDiagram ? 332f : 266f);
        ApplyCardLayout(explaining, width, height, withDiagram);
    }

    private void ApplyCardLayout(bool explaining, float width, float height, bool withDiagram)
    {
        panel.sizeDelta = new Vector2(width, height);
        bool compact = height < 270f && explaining;
        bool showPips = explaining && StepCount > 1;

        float tileSize = compact ? 34f : (explaining ? 44f : 40f);
        float headerTop = compact ? 12f : (explaining ? 16f : 14f);
        if (iconTile != null)
        {
            var tr = iconTile.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0f, 1f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = new Vector2(tileSize, tileSize);
            tr.anchoredPosition = new Vector2(28f + tileSize * 0.5f, -(headerTop + tileSize * 0.5f));
        }
        if (stepIcon != null)
            stepIcon.rectTransform.sizeDelta = new Vector2(tileSize * 0.64f, tileSize * 0.64f);

        float pillHeight = compact ? 24f : 30f;
        float pillWidth = compact ? 132f : 146f;
        if (statusPill != null)
        {
            var pr = statusPill.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(1f, 1f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(pillWidth, pillHeight);
            pr.anchoredPosition = new Vector2(-(28f + pillWidth * 0.5f), -(headerTop + tileSize * 0.5f));
        }
        if (statusBadgeText != null) statusBadgeText.fontSize = compact ? 13f : 14.5f;

        float titleLeft = TitleLeftPadding(tileSize);
        float titleRight = 28f + pillWidth + 14f;
        float titleHeight = compact ? 28f : (explaining ? 36f : 34f);
        PlaceText(title, headerTop + (tileSize - titleHeight) * 0.5f, titleHeight, compact ? 15f : 18f, compact ? 22f : (explaining ? 28f : 25f), titleLeft, titleRight);

        float headerBottom = headerTop + tileSize;
        if (showPips && stepPips != null)
        {
            float pipsY = compact ? headerBottom + 4f : headerBottom + 8f;
            var pipR = stepPips.rectTransform;
            pipR.anchorMin = pipR.anchorMax = new Vector2(0f, 1f);
            pipR.pivot = new Vector2(0f, 1f);
            pipR.anchoredPosition = new Vector2(titleLeft, -pipsY);
            pipR.sizeDelta = new Vector2(160f, 12f);
            headerBottom = pipsY + 12f;
        }

        float dividerY = headerBottom + (compact ? 6f : 10f);
        if (headerDivider != null)
        {
            headerDivider.anchorMin = new Vector2(0f, 1f);
            headerDivider.anchorMax = new Vector2(1f, 1f);
            headerDivider.pivot = new Vector2(0.5f, 1f);
            headerDivider.offsetMin = new Vector2(28f, -dividerY - 1.5f);
            headerDivider.offsetMax = new Vector2(-28f, -dividerY);
        }

        float bottomPad = compact ? 10f : 14f;
        // 실습 안내(prompt)는 키 표기가 2줄이 되어도 TMP preferredHeight가 상자를 넘지 않게 높이와 최대 글자 크기를 맞춘다.
        float footerHeight = explaining ? (compact ? 38f : 50f) : 56f;
        float footerTop = height - bottomPad - footerHeight;
        if (footerPill != null)
        {
            var fr = footerPill.rectTransform;
            fr.anchorMin = new Vector2(0f, 1f);
            fr.anchorMax = new Vector2(1f, 1f);
            fr.pivot = new Vector2(0.5f, 1f);
            fr.offsetMin = new Vector2(26f, -footerTop - footerHeight);
            fr.offsetMax = new Vector2(-26f, -footerTop);
        }
        float promptHeight = explaining ? (compact ? 34f : 44f) : 54f;
        PlaceText(prompt, footerTop + (footerHeight - promptHeight) * 0.5f, promptHeight,
            compact ? 12.5f : 14f, explaining ? (compact ? 17.5f : 21f) : 19f, 40f, 40f);

        float stripHeight = compact ? 32f : 40f;
        float stripTop = footerTop - (compact ? 6f : 10f) - stripHeight;
        if (keycapStrip != null)
        {
            var sr = keycapStrip.transform as RectTransform;
            sr.anchorMin = new Vector2(0f, 1f);
            sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.offsetMin = new Vector2(32f, -stripTop - stripHeight);
            sr.offsetMax = new Vector2(-32f, -stripTop);
        }

        float bodyBottom = stripTop - (compact ? 4f : 8f);
        if (withDiagram && gearDiagram != null)
        {
            float diagramHeight = 64f;
            float diagramTop = stripTop - 8f - diagramHeight;
            var dr = gearDiagram.transform as RectTransform;
            dr.anchorMin = new Vector2(0f, 1f);
            dr.anchorMax = new Vector2(1f, 1f);
            dr.pivot = new Vector2(0.5f, 1f);
            dr.offsetMin = new Vector2(28f, -diagramTop - diagramHeight);
            dr.offsetMax = new Vector2(-28f, -diagramTop);
            bodyBottom = diagramTop - 6f;
        }

        float bodyTop = dividerY + (compact ? 6f : 10f);
        float bodyHeight = Mathf.Max(34f, bodyBottom - bodyTop);
        PlaceText(body, bodyTop, bodyHeight, compact ? 12.5f : 13.5f, explaining ? (compact ? 19f : 23f) : (withDiagram ? 19.5f : 20.5f), 32f, 32f);
    }

    private float TitleLeftPadding(float tileSize) => iconTile != null && iconTile.gameObject.activeSelf ? 28f + tileSize + 14f : 32f;

    private static RectTransform CreateChildRect(RectTransform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;
        return rect;
    }

    private static void PlaceText(TextMeshProUGUI text, float top, float height, float minimumFont, float maximumFont, float leftPadding = 32f, float rightPadding = 32f)
    {
        if (text == null) return;
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.offsetMin = new Vector2(leftPadding, -top - height);
        rect.offsetMax = new Vector2(-rightPadding, -top);
        text.enableAutoSizing = true;
        text.fontSizeMin = minimumFont;
        text.fontSizeMax = maximumFont;
    }

    private void ReleaseFocus()
    {
        presentation?.End();
        HideFocusDecor();
        if (pauseOwner != null) { pauseOwner.PopPause(); pauseOwner = null; }
        if (blockOwner != null)
        {
            blockOwner.RemoveInputTarget(this);
            blockOwner.PopGameplayBlock();
            blockOwner.PauseInputBlocked = previousPauseBlock;
            blockOwner = null;
        }
        if (focus != null) focus.Clear();
        currentFocus = null;
        focusProjected = false;
    }

    private void Close()
    {
        InputManager.BindingsChanged -= RefreshLiveBindings;
        HideSuccessBanner();
        ReleaseFocus();
        if (owner != null)
        {
            owner.WaveEnded -= Close;
            owner.WavesReset -= Close;
            owner.EnemySpawned -= TrackEnemySpawn;
        }
        preparingEnemies.Clear();
        practiceShield = null;
        if (practiceJump != null) practiceJump.Jumped -= OnPracticeJump;
        practiceJump = null;
        if (practiceGear != null)
        {
            practiceGear.GearSkillStarted -= OnPracticeGearSkill;
            practiceGear.GearQuickCharged -= OnPracticeQuickCharged;
            practiceGear = null;
        }
        if (practiceStorage != null)
        {
            practiceStorage.SkillStorageFinished -= OnPracticeStored;
            practiceStorage.StoredSkillUsed -= OnPracticeStorageUsed;
            practiceStorage = null;
        }
        waveTransitions = null;
        owner = null;
        lesson = null;
        player = null;
        if (panel != null) panel.gameObject.SetActive(false);
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void OnDisable() => Close();
    private void OnDestroy() { presentation?.Dispose(); if (instance == this) instance = null; }

    private static InputAction FindAction(string name) => InputManager.Instance != null ? InputManager.Instance.Actions.FindAction("Player/" + name) : null;

    internal static string ActionName(TutorialAction action)
    {
        switch (action)
        {
            case TutorialAction.Move: return "Move";
            case TutorialAction.Jump:
            case TutorialAction.DoubleJump: return "Jump";
            case TutorialAction.Dash: return "Dash";
            case TutorialAction.SwapWeapon: return "SwapWeapon";
            case TutorialAction.SecondaryWeapon: return "SecondaryWeapon";
            case TutorialAction.Attack: return "Attack";
            case TutorialAction.Gear:
            case TutorialAction.GearQuickCharge: return "Gear";
            case TutorialAction.SkillStorage: return "StoreSkill";
            default: return "Interact";
        }
    }

    internal static string BindingLabel(string name)
    {
        var action = FindAction(name);
        if (action == null) return name;
        int binding = OptionMenu.FindBinding(action, name == "Move" ? "left" : "");
        string result = binding >= 0 ? action.GetBindingDisplayString(binding) : name;
        if (name == "Move")
        {
            int right = OptionMenu.FindBinding(action, "right");
            if (right >= 0) result += " / " + action.GetBindingDisplayString(right);
        }
        return result;
    }

    // 전투 개편 중인 Ability를 수정하지 않는 임시 읽기 어댑터입니다.
    // 내부 필드가 바뀌면 입력 시도를 성공으로 간주하지 않고 설명 후 진행 가능한 상태로 처리합니다.
    // 2026-11-26: 리플렉션 대신 PlayerWeaponAbility의 읽기 전용 공개 값(CurrentSetIndex·GetWeaponSet·SelectedWeaponView·SkillGaugeValue)을 읽습니다.
    // 칸은 예전처럼 실행용 사본(잠긴 기술은 빈 칸)을 보므로 판정 동작은 같습니다.
    private sealed class WeaponObservation
    {
        private readonly PlayerWeaponAbility ability;
        public WeaponObservation(PlayerWeaponAbility target) { ability = target; }
        public int SetIndex => ability != null ? ability.CurrentSetIndex : -1;
        private WeaponSet CurrentSet => ability != null ? ability.GetWeaponSet(SetIndex) : null;
        private WeaponBaseSO SelectedWeapon => ability != null ? ability.SelectedWeaponView : null;
        private float CurrentGauge => ability != null ? ability.SkillGaugeValue : 0f;
        private WeaponBaseSO RequiredWeapon(TutorialAction action) => CurrentSet == null ? null : action == TutorialAction.Gear ? CurrentSet.gearStage1 : CurrentSet.secondary;
        public bool NeedsGauge(TutorialAction action) => RequiredWeapon(action) != null && CurrentGauge < RequiredWeapon(action).skillGauge;
        public string GaugeLabel(TutorialAction action) => RequiredWeapon(action) != null ? Mathf.FloorToInt(CurrentGauge) + " / " + RequiredWeapon(action).skillGauge.ToString("0") : "";
        public bool IsExecutingSecondary => ability != null && ability.AttackPhase == AbilityState.Execute && CurrentSet != null && SelectedWeapon != null && SelectedWeapon == CurrentSet.secondary;
        // 보조 무기 수업은 '회복'을 실제로 쓰는지 본다(지금 세트의 보조 무기가 회복이고 그 무기를 실행 중).
        public bool IsExecutingHealing => IsExecutingSecondary && SelectedWeapon is HealingWeaponSO;
        public bool CurrentSecondaryIsHealing => CurrentSet != null && CurrentSet.secondary is HealingWeaponSO;
        // 어느 세트든 해금된 회복 보조 무기가 있는지(예전 HasUsableSecondary를 회복 기준으로 좁힘). 없으면 보조 무기 수업은 설명만 보고 진행한다.
        public bool HasUsableHealing
        {
            get
            {
                if (ability == null) return false;
                for (int i = 0; i < ability.WeaponSetCount; i++)
                {
                    var auxiliary = ability.GetWeaponSet(i)?.secondary;
                    if (auxiliary is HealingWeaponSO && ArmamentUnlocks.IsWeaponUnlocked(auxiliary)) return true;
                }
                return false;
            }
        }

        // 기어 1단계가 빈(잠긴) 무장인지와 1단계 기술이 있는 다른 무장의 표시 이름(쉴드·브레이크·스킬 저장 수업 안내).
        public bool FirstGearEmpty => CurrentSet != null && CurrentSet.gearStage1 == null;
        public string FirstGearArmamentName => ArmamentNameWhere(set => set.gearStage1 != null);

        // 빠른 장전 수업: 지금 세트에서 빠른 장전으로 꺼낼 2단계 이상 기술(낮은 단계 먼저). 잠긴 칸은 실행용 사본에서 비어 있다.
        public WeaponBaseSO QuickChargeSkill => CurrentSet == null ? null : CurrentSet.gearStage2 != null ? CurrentSet.gearStage2 : CurrentSet.gearStage3;
        public WeaponBaseSO StageWeapon(int stage) => CurrentSet != null && stage >= 1 && stage <= 3 ? CurrentSet.GetGearWeapon(stage) : null;
        public bool HasQuickChargeSkillInAnySet => ArmamentNameWhere(HasQuickChargeSkill) != null;
        // 안내 문구에 끼울 무장 이름('해머 '처럼 뒤에 띄어쓰기 포함). 이름을 알 수 없으면 빈 문자열.
        public string QuickChargeArmamentName
        {
            get
            {
                string name = ArmamentNameWhere(HasQuickChargeSkill);
                return string.IsNullOrEmpty(name) ? "" : name + " ";
            }
        }
        // 지금 세트에서 재사용 대기가 끝난 2단계 이상 기술이 있으면 그것을 우선 본다(모두 대기 중이면 QuickChargeSkill).
        private WeaponBaseSO ReadyQuickChargeSkill
        {
            get
            {
                if (CurrentSet == null) return null;
                if (CurrentSet.gearStage2 != null && (ability == null || ability.GetSkillCooldownRemaining(CurrentSet.gearStage2) <= .05f)) return CurrentSet.gearStage2;
                if (CurrentSet.gearStage3 != null && (ability == null || ability.GetSkillCooldownRemaining(CurrentSet.gearStage3) <= .05f)) return CurrentSet.gearStage3;
                return QuickChargeSkill;
            }
        }
        public float QuickChargeCooldownRemaining => ReadyQuickChargeSkill != null && ability != null ? ability.GetSkillCooldownRemaining(ReadyQuickChargeSkill) : 0f;
        public bool NeedsQuickChargeGauge => QuickChargeSkill != null && CurrentGauge < QuickChargeSkill.skillGauge;
        public string QuickChargeGaugeLabel => QuickChargeSkill != null ? Mathf.FloorToInt(CurrentGauge) + " / " + QuickChargeSkill.skillGauge.ToString("0") : "";
        private static bool HasQuickChargeSkill(WeaponSet set) => set.gearStage2 != null || set.gearStage3 != null;

        // 조건에 맞는 세트(지금 든 세트를 뒤로 미룸)의 무장 표시 이름. 무장을 정하지 않은 자리면 빈 문자열, 맞는 세트가 없으면 null.
        private string ArmamentNameWhere(System.Func<WeaponSet, bool> match)
        {
            if (ability == null) return null;
            string found = null;
            for (int i = 0; i < ability.WeaponSetCount; i++)
            {
                var set = ability.GetWeaponSet(i);
                if (set == null || !match(set)) continue;
                string name = ability.GetEquippedArmament(i)?.DisplayName ?? "";
                if (i != SetIndex) return name;
                found ??= name;
            }
            return found;
        }
        public bool IsExecutingGear => ability != null && ability.AttackPhase == AbilityState.Execute && CurrentSet != null && SelectedWeapon != null
            && (SelectedWeapon == CurrentSet.gearStage1 || SelectedWeapon == CurrentSet.gearStage2 || SelectedWeapon == CurrentSet.gearStage3);

        public string UnavailableReason(TutorialAction action)
        {
            if (ability == null || CurrentSet == null)
                return "현재 무장 상태를 확인할 수 없습니다.";
            if (action == TutorialAction.SwapWeapon)
                return ability.WeaponSetCount >= 2 && ability.GetWeaponSet(0) != null && ability.GetWeaponSet(1) != null ? null : "전환할 두 번째 무장이 없습니다.";
            WeaponBaseSO weapon = action == TutorialAction.Gear ? CurrentSet.gearStage1 : CurrentSet.secondary;
            if (weapon == null) return "현재 무장에는 이 기능이 장착되어 있지 않습니다.";
            return null;
        }
    }
}
