using System.Collections.Generic;
using System.Reflection;
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
    private JumpAbility practiceJump;
    private PlayerWeaponAbility practiceStorage;
    private bool storedInPractice, usedInPractice;
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
        currentFocus = null;
        focusProjected = false;
        if (presentation == null && focus != null) presentation = new TutorialFocusPresentation(focus.rectTransform.parent as RectTransform, body != null ? body.font : null);
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
        }
        else if (phase == GuidePhase.Release)
        {
            UpdateFocus();
            bool held = confirmAction != null && confirmAction.IsPressed();
            if (!held)
            {
                if (stepIndex + 1 < StepCount) { stepIndex++; ShowStep(); }
                else ReturnToPractice();
            }
        }
        else if (phase == GuidePhase.Returning)
        {
            float elapsed = Time.unscaledTime - returningAt;
            if (elapsed >= .25f && (presentation == null || presentation.IsPlayerVisible(player) || elapsed >= 2f)) StartPractice();
        }
        else if (phase == GuidePhase.Practice)
        {
            if (ui != null && ui.BlocksGameplayInput) { lastPosition = player.transform.position; return; }
            ObservePractice();
        }
    }

    private void TrackEnemySpawn(EnemyController enemy)
    {
        if (phase != GuidePhase.Waiting || enemy == null) return;
        preparingEnemies.Add(enemy);
        if (lesson.action == TutorialAction.ShieldBreakGear && enemy.Health is TrainingDummyHealth dummy
            && dummy.enemyShieldSystem != null)
        {
            practiceShield = dummy.enemyShieldSystem;
            practiceShield.ConfigureTrainingCapacity(195f);
        }
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
        phase = GuidePhase.Explain;
        earliestInput = Time.unscaledTime + .25f;
        focusProjected = false;
        focusVisibleAt = float.PositiveInfinity;
        presentation?.BeginStep();
        presentation?.SetBindingText(CurrentKeys());
        var step = CurrentStep;
        if (title != null) title.text = step != null && !string.IsNullOrEmpty(step.title) ? step.title : lesson.title;
        if (body != null)
        {
            body.text = step != null && !string.IsNullOrEmpty(step.description) ? step.description : lesson.description;
            string keys = CurrentKeys();
            if (!string.IsNullOrEmpty(keys)) body.text += "\n<color=#FFD57B>" + keys + "</color>";
        }
        SetPanelLayout(true);
        if (panel != null) { panel.gameObject.SetActive(true); panel.SetAsLastSibling(); }
        pagePrompt = InputBindingDisplay.Confirm + " 선택 · " + (stepIndex + 1) + " / " + StepCount + (stepIndex + 1 < StepCount ? "  다음 설명" : "  설명 마치기");
        SetPrompt(pagePrompt);
        UpdateFocus();
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
            return;
        }
        bool before = focusProjected;
        focusProjected = presentation != null && presentation.TryTarget(step, owner, player, lesson.action, out currentFocus);
        if (!focusProjected)
        {
            currentFocus = null;
            focus.Cover();
            DockExplanation(true);
            return;
        }
        if (!before) focusVisibleAt = Time.unscaledTime;
        if (phase == GuidePhase.Explain && Time.unscaledTime >= focusVisibleAt + .2f) SetPrompt(pagePrompt);
        focus.Focus(currentFocus, step.padding);
        LayoutExternalExplanation(step.padding);
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
        float height = Mathf.Clamp(Mathf.Max(above, below), 206f, 340f);
        float width = Mathf.Min(780f, full.width - 48f);
        float left = min.x - full.xMin - 44f, right = full.xMax - max.x - 44f;
        if (Mathf.Max(above, below) < 206f && Mathf.Max(left, right) >= 420f)
        {
            bool useRight = right >= left;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(useRight ? 1f : 0f, .5f);
            panel.anchoredPosition = new Vector2(useRight ? -24f : 24f, 0f);
            width = Mathf.Min(780f, Mathf.Max(left, right));
            height = 340f;
        }
        else DockExplanation(above >= below);
        panel.sizeDelta = new Vector2(width, height);
        PlaceText(title, 18f, 38f, 18f, 28f);
        PlaceText(body, 68f, height - 142f, 14f, 24f);
        PlaceText(prompt, height - 62f, 44f, 15f, 22f);
    }

    private void DockExplanation(bool top)
    {
        if (panel == null) return;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, top ? 1f : 0f);
        panel.anchoredPosition = new Vector2(0f, top ? -24f : 24f);
    }

    private void StartPractice()
    {
        ReleaseFocus();
        SetPanelLayout(false);
        if (panel != null) panel.gameObject.SetActive(!lesson.wallInstructions);
        if (title != null) title.text = lesson.title;
        if (body != null) body.text = lesson.description;
        phase = GuidePhase.Practice;
        lastPosition = player.transform.position;
        weapons = new WeaponObservation(player.AbilityManager.AttackAbility);
        initialWeaponSet = weapons.SetIndex;
        if (lesson.action == TutorialAction.SkillStorage)
        {
            practiceStorage = player.AbilityManager.AttackAbility;
            if (practiceStorage != null)
            {
                practiceStorage.SkillStorageFinished += OnPracticeStored;
                practiceStorage.StoredSkillUsed += OnPracticeStorageUsed;
            }
        }
        if (lesson.action == TutorialAction.Jump || lesson.action == TutorialAction.DoubleJump)
        {
            practiceJump = player.AbilityManager.JumpAbility;
            if (practiceJump != null) practiceJump.Jumped += OnPracticeJump;
        }

        if (lesson.action == TutorialAction.None || lesson.action == TutorialAction.Shield || lesson.action == TutorialAction.Break
            || lesson.action == TutorialAction.Platforms || lesson.action == TutorialAction.Trap)
        {
            Complete();
            return;
        }
        if (lesson.wallInstructions && lesson.action == TutorialAction.SecondaryWeapon && !weapons.HasUsableSecondary)
        {
            Complete();
            return;
        }
        if (lesson.action == TutorialAction.SwapWeapon || lesson.action == TutorialAction.Gear
            || (lesson.action == TutorialAction.SecondaryWeapon && !lesson.wallInstructions))
        {
            string unavailable = weapons.UnavailableReason(lesson.action);
            if (!string.IsNullOrEmpty(unavailable))
            {
                if (body != null) body.text += "\n\n" + unavailable + " 이번 단계는 설명을 확인하고 목표로 진행합니다.";
                Complete();
                return;
            }
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
                success = practiceShield != null && practiceShield.SuccessfulBreakCount > 0;
                break;
            case TutorialAction.SkillStorage: success = storedInPractice && usedInPractice; break;
            case TutorialAction.SwapWeapon: success = weapons.SetIndex >= 0 && weapons.SetIndex != initialWeaponSet; break;
            case TutorialAction.SecondaryWeapon: success = weapons.IsExecutingSecondary; break;
            case TutorialAction.Gear: success = weapons.IsExecutingGear; break;
        }
        lastPosition = player.transform.position;
        if (success) Complete();
        else if (lesson.action == TutorialAction.Gear || lesson.action == TutorialAction.SecondaryWeapon) SetPrompt(PracticePrompt());
    }

    private void OnPracticeJump(bool extraJump)
    {
        if (phase == GuidePhase.Practice && (lesson.action == TutorialAction.Jump || extraJump)) Complete();
    }

    private void OnPracticeStored(bool completed) => storedInPractice |= completed;
    private void OnPracticeStorageUsed() => usedInPractice |= storedInPractice;

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

    private string CurrentKeys()
    {
        if (lesson == null) return "";
        switch (lesson.action)
        {
            case TutorialAction.Move: return "이동 [ " + BindingLabel("Move") + " ]";
            case TutorialAction.Jump:
            case TutorialAction.DoubleJump: return "점프 [ " + BindingLabel("Jump") + " ]";
            case TutorialAction.Dash: return "대시 [ " + BindingLabel("Dash") + " ]";
            case TutorialAction.SwapWeapon: return "무기 전환 [ " + BindingLabel("SwapWeapon") + " ]";
            case TutorialAction.SecondaryWeapon: return "보조 무기 [ " + BindingLabel("SecondaryWeapon") + " ]";
            case TutorialAction.Shield: return "표적 적중 [ " + BindingLabel("Attack") + " ]";
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
        ReleaseFocus();
        SetPanelLayout(false);
        phase = GuidePhase.Complete;
        SetPrompt("확인 완료 · 빛나는 목표 지점으로 이동하십시오");
    }

    private void SetPrompt(string value) { if (prompt != null) prompt.text = value; }

    private void SetPanelLayout(bool explaining)
    {
        if (panel == null) return;
        panel.anchorMin = panel.anchorMax = explaining ? new Vector2(.5f, .5f) : new Vector2(.5f, 1f);
        panel.pivot = explaining ? new Vector2(.5f, .5f) : new Vector2(.5f, 1f);
        panel.anchoredPosition = explaining ? Vector2.zero : new Vector2(0f, -22f);
        if (!explaining && lesson != null && !lesson.focusEnabled && lesson.action == TutorialAction.Move)
        {
            panel.sizeDelta = new Vector2(780f, 170f);
            PlaceText(title, 14f, 32f, 18f, 24f);
            PlaceText(body, 52f, 48f, 14f, 20f);
            PlaceText(prompt, 110f, 44f, 16f, 21f);
            return;
        }
        panel.sizeDelta = explaining ? new Vector2(780f, 340f) : new Vector2(780f, 240f);
        PlaceText(title, explaining ? 24f : 18f, explaining ? 44f : 38f, 18f, explaining ? 30f : 26f);
        PlaceText(body, explaining ? 82f : 64f, explaining ? 158f : 102f, 14f, explaining ? 25f : 21f);
        PlaceText(prompt, explaining ? 254f : 176f, explaining ? 62f : 46f, 16f, explaining ? 24f : 22f);
    }

    private static void PlaceText(TextMeshProUGUI text, float top, float height, float minimumFont, float maximumFont)
    {
        if (text == null) return;
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.offsetMin = new Vector2(32f, -top - height);
        rect.offsetMax = new Vector2(-32f, -top);
        text.enableAutoSizing = true;
        text.fontSizeMin = minimumFont;
        text.fontSizeMax = maximumFont;
    }

    private void ReleaseFocus()
    {
        presentation?.End();
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
            case TutorialAction.Gear: return "Gear";
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
    private sealed class WeaponObservation
    {
        private readonly PlayerWeaponAbility ability;
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo Set = typeof(PlayerWeaponAbility).GetField("currentSetIndex", Flags);
        private static readonly FieldInfo Selected = typeof(PlayerWeaponAbility).GetField("selectedWeapon", Flags);
        private static readonly FieldInfo Gauge = typeof(PlayerWeaponAbility).GetField("skillGauge", Flags);
        private static readonly FieldInfo Data = typeof(PlayerWeaponAbility).GetField("weaponData", Flags);
        public WeaponObservation(PlayerWeaponAbility target) { ability = target; }
        public int SetIndex => ability != null && Set != null ? (int)Set.GetValue(ability) : -1;
        private PlayerWeaponDataSO WeaponData => ability != null && Data != null ? Data.GetValue(ability) as PlayerWeaponDataSO : null;
        private WeaponSet CurrentSet => WeaponData != null && SetIndex >= 0 && SetIndex < WeaponData.weaponSets.Length ? WeaponData.weaponSets[SetIndex] : null;
        private WeaponBaseSO SelectedWeapon => ability != null && Selected != null ? Selected.GetValue(ability) as WeaponBaseSO : null;
        private float CurrentGauge => ability != null && Gauge != null ? (float)Gauge.GetValue(ability) : 0f;
        private WeaponBaseSO RequiredWeapon(TutorialAction action) => CurrentSet == null ? null : action == TutorialAction.Gear ? CurrentSet.gearStage1 : CurrentSet.secondary;
        public bool NeedsGauge(TutorialAction action) => RequiredWeapon(action) != null && CurrentGauge < RequiredWeapon(action).skillGauge;
        public string GaugeLabel(TutorialAction action) => RequiredWeapon(action) != null ? Mathf.FloorToInt(CurrentGauge) + " / " + RequiredWeapon(action).skillGauge.ToString("0") : "";
        public bool IsExecutingSecondary => ability != null && ability.AttackPhase == AbilityState.Execute && CurrentSet != null && SelectedWeapon != null && SelectedWeapon == CurrentSet.secondary;
        public bool HasUsableSecondary
        {
            get
            {
                if (ability == null) return false;
                for (int i = 0; i < ability.WeaponSetCount; i++)
                {
                    var auxiliary = ability.GetWeaponSet(i)?.secondary;
                    if (auxiliary != null && ArmamentUnlocks.IsWeaponUnlocked(auxiliary)) return true;
                }
                return false;
            }
        }
        public bool IsExecutingGear => ability != null && ability.AttackPhase == AbilityState.Execute && CurrentSet != null && SelectedWeapon != null
            && (SelectedWeapon == CurrentSet.gearStage1 || SelectedWeapon == CurrentSet.gearStage2 || SelectedWeapon == CurrentSet.gearStage3);

        public string UnavailableReason(TutorialAction action)
        {
            if (ability == null || Set == null || Selected == null || Gauge == null || Data == null || CurrentSet == null)
                return "현재 무장 상태를 확인할 수 없습니다.";
            if (action == TutorialAction.SwapWeapon)
                return WeaponData.weaponSets.Length >= 2 && WeaponData.weaponSets[0] != null && WeaponData.weaponSets[1] != null ? null : "전환할 두 번째 무장이 없습니다.";
            WeaponBaseSO weapon = action == TutorialAction.Gear ? CurrentSet.gearStage1 : CurrentSet.secondary;
            if (weapon == null) return "현재 무장에는 이 기능이 장착되어 있지 않습니다.";
            return null;
        }
    }
}
