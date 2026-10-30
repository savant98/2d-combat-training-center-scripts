using System.Collections.Generic;
using UnityEngine;
public class UIManager : MonoBehaviour
{
    private class UIInputEntry
    {
        public IUIInputReceiver receiver;
        public UIInputLayer layer;

        public UIInputEntry(IUIInputReceiver receiver, UIInputLayer layer)
        {
            this.receiver = receiver;
            this.layer = layer;
        }
    }

    public static UIManager Instance { get; private set; }

    [Header("HUD")]
    [SerializeField] private PlayerHUD hud;
    [SerializeField] private StageHUD stageHud;
    [SerializeField] private TrainingScoreboardUI trainingScoreboardUI;
    [SerializeField] private TrainingCompleteBoardUI completeScoreBoardUI;

    [Header("DeathUI")]
    [SerializeField] private DeathMenuController deathMenuController;

    [Header("PauseUI")]
    [SerializeField] private PauseMenuController pauseMenuController;

    [Header("Screen Effect")]
    [SerializeField] private ScreenEffect screenEffect;

    [Header("UI Input")]
    [SerializeField] private float inputLockDuration = 0.12f;
    [SerializeField] private float moveRepeatStartDelay = 0.35f;
    [SerializeField] private float moveRepeatInterval = 0.12f;
    public bool IsUIInputBlocked { get; set; }
    public bool IsCompleteBoardOpen => completeScoreBoardUI != null && completeScoreBoardUI.IsOpen;

    private readonly List<UIInputEntry> inputStack = new();
    private Vector2 lastMoveInput;
    private Vector2 heldMoveDir;
    private float nextMoveRepeatTime;
    private float inputLockedUntil;
    private float gamePlayerInputLockedUntil;
    private int gameplayBlockCount;
    public bool HasActiveInputUI => GetCurrentEntry() != null;
    public bool BlocksGameplayInput
    {
        get
        {
            if (gameplayBlockCount > 0)
                return true;

            if (Time.unscaledTime < gamePlayerInputLockedUntil)
                return true;

            CleanupInvalidEntries();

            for (int i = 0; i < inputStack.Count; i++)
            {
                UIInputLayer layer = inputStack[i].layer;

                if (layer == UIInputLayer.Gameplay ||
                    layer == UIInputLayer.Pause ||
                    layer == UIInputLayer.System)
                    return true;
            }

            return false;
        }
    }
    private void Awake()
    {
        Instance = this;
    }
    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
    public void Update()
    {
        // UI 입력 차단
        if (IsUIInputBlocked)
            return;

        HandlePauseInput();
        HandleUIInput();
    }
    private void HandlePauseInput()
    {
        if (!InputManager.Pressed(Button.Escape))
            return;

        if (IsInputLocked())
            return;

        UIInputEntry current = GetCurrentEntry();

        // System UI가 최상단이면 Pause 입력을 먹지 않는다.
        // 예: DeathUI, LoadingUI, 강제 ConfirmPopup 등
        if (current != null && current.layer == UIInputLayer.System)
            return;

        // Pause 계층이 이미 열려 있으면,
        // ESC는 Pause 계층의 최상단 UI를 닫는다.
        if (HasPauseLayer())
        {
            UIInputEntry topPause = GetTopEntryOfLayer(UIInputLayer.Pause);

            if (topPause != null)
            {
                topPause.receiver.OnCancel();
                LockInput();
            }

            return;
        }

        // Pause 계층이 없으면,
        // Gameplay UI가 열려 있어도 그 위에 Pause를 연다.
        if (pauseMenuController != null)
        {
            pauseMenuController.Show();
            LockInput();
        }
    }
    private void HandleUIInput()
    {
        UIInputEntry current = GetCurrentEntry();

        if (current == null)
            return;

        IUIInputReceiver receiver = current.receiver;

        Vector2 move = InputManager.Instance.Move;

        bool hasMoveInput = move.sqrMagnitude > 0.1f;
        bool hadMoveInput = lastMoveInput.sqrMagnitude > 0.1f;

        if (!IsInputLocked())
        {
            if (hasMoveInput)
            {
                Vector2 moveDir = GetDominantMoveDir(move);

                bool isNewMoveInput = !hadMoveInput || moveDir != heldMoveDir;
                bool canRepeat = Time.unscaledTime >= nextMoveRepeatTime;

                if (isNewMoveInput || canRepeat)
                {
                    receiver.OnMove(moveDir);
                    LockInput();

                    heldMoveDir = moveDir;

                    nextMoveRepeatTime = Time.unscaledTime +
                        (isNewMoveInput ? moveRepeatStartDelay : moveRepeatInterval);
                }
            }
            else
            {
                heldMoveDir = Vector2.zero;
            }

            if (InputManager.Pressed(Button.Jump))
            {
                receiver.OnConfirm();
                LockInput();
            }

            if (InputManager.Pressed(Button.Attack))
            {
                receiver.OnCancel();
                LockInput();
            }
        }
        else
        {
            // Lock 중에도 입력이 풀렸는지 기록
            if (!hasMoveInput)
                heldMoveDir = Vector2.zero;
        }

        // 매 프레임 마지막 입력 상태 갱신
        lastMoveInput = move;
    }
    private Vector2 GetDominantMoveDir(Vector2 move)
    {
        if (Mathf.Abs(move.x) >= Mathf.Abs(move.y))
            return new Vector2(Mathf.Sign(move.x), 0f);

        return new Vector2(0f, Mathf.Sign(move.y));
    }
    private void LockGameplayInputBriefly()
    {
        gamePlayerInputLockedUntil = Time.unscaledTime + inputLockDuration;
    }

    private UIInputEntry GetCurrentEntry()
    {
        CleanupInvalidEntries();

        if (inputStack.Count <= 0)
            return null;

        return inputStack[inputStack.Count - 1];
    }

    private UIInputEntry GetTopEntryOfLayer(UIInputLayer layer)
    {
        CleanupInvalidEntries();

        for (int i = inputStack.Count - 1; i >= 0; i--)
        {
            if (inputStack[i].layer == layer)
                return inputStack[i];
        }

        return null;
    }
    private bool HasPauseLayer()
    {
        return GetTopEntryOfLayer(UIInputLayer.Pause) != null;
    }

    private void CleanupInvalidEntries()
    {
        for (int i = inputStack.Count - 1; i >= 0; i--)
        {
            IUIInputReceiver receiver = inputStack[i].receiver;

            if (receiver == null || !receiver.IsActive)
                inputStack.RemoveAt(i);
        }
    }
    private bool IsInputLocked()
    {
        return Time.unscaledTime < inputLockedUntil;
    }

    private void LockInput()
    {
        inputLockedUntil = Time.unscaledTime + inputLockDuration;
    }
    /// <summary>
    /// UI 연출용 입력 제한 (true: 모든 UI 입력 차단, false: 해제)
    /// </summary>
    public void SetUIInputBlock(bool block)
    {
        IsUIInputBlocked = block;

        // 입력을 해제하는 순간 혹시 남아있을지 모를 가상 입력 값을 초기화.
        if (!block)
        {
            lastMoveInput = Vector2.zero;
            heldMoveDir = Vector2.zero;
        }
    }
    public void PushGameplayBlock()
    {
        gameplayBlockCount++;
    }

    public void PopGameplayBlock()
    {
        gameplayBlockCount = Mathf.Max(0, gameplayBlockCount - 1);
    }

    public void ClearGameplayBlocks()
    {
        gameplayBlockCount = 0;
    }
    // 포인터와 직접 호출도 실제 최상단 입력 수신자만 처리하게 한다. 스택 순서는 바꾸지 않는다.
    public bool IsCurrentInputTarget(IUIInputReceiver receiver)
    {
        var current = GetCurrentEntry();
        return current != null && object.ReferenceEquals(current.receiver, receiver);
    }

    public void SetInputTarget(IUIInputReceiver ui)
    {
        PushInputTarget(ui, UIInputLayer.Gameplay);
    }
    public void SetInputTarget(IUIInputReceiver ui, UIInputLayer layer)
    {
        PushInputTarget(ui, layer);
    }
    public void ClearInputTarget(IUIInputReceiver ui)
    {
        RemoveInputTarget(ui);
    }
    public void PushInputTarget(IUIInputReceiver ui, UIInputLayer layer)
    {
        if (ui == null)
            return;

        RemoveInputTarget(ui);
        inputStack.Add(new UIInputEntry(ui, layer));

        LockInput();
    }

    public void RemoveInputTarget(IUIInputReceiver ui)
    {
        if (ui == null)
            return;

        for (int i = inputStack.Count - 1; i >= 0; i--)
        {
            if (inputStack[i].receiver == ui)
                inputStack.RemoveAt(i);
        }

        LockInput();
        LockGameplayInputBriefly();
    }
    public void HUDVisible(bool isVisible)
    {
        hud.HUDVisible(isVisible);
    }
    public void UpdateHP(float currentHP, float maxHP)
    {
        hud.SetHP(currentHP, maxHP);
    }

    public void UpdateBossHP(float normalized)
    {
        stageHud.UpdateBossHP(normalized);
    }
    public void UpdateBossShield(float normalized)
    {
        stageHud.UpdateBossShield(normalized);
    }
    public void UpdateSkillGauge(float gauge, float maxGauge, WeaponSet weaponSet)
    {
        hud.SetSkillGauge(gauge, maxGauge);
        hud.UpdateWeaponSetImage(weaponSet, gauge);
    }
    public void UpdateWeaponCooldowns(
    float normalRemaining,
    float normalDuration,
    float gear1Remaining,
    float gear1Duration,
    float gear2Remaining,
    float gear2Duration,
    float gear3Remaining,
    float gear3Duration,
    float secondaryRemaining,
    float secondaryDuration)
    {
        hud.UpdateWeaponCooldowns(
            normalRemaining,
            normalDuration,
            gear1Remaining,
            gear1Duration,
            gear2Remaining,
            gear2Duration,
            gear3Remaining,
            gear3Duration,
            secondaryRemaining,
            secondaryDuration);
    }
    public void StartShieldBreakEffect()
    {
        screenEffect.OnShieldBreakTextEffect();
    }
    public void StartBreakEffect()
    {
        screenEffect.OnBreakTextEffect();
    }
    public void StartScreenFade(float value, float duration)
    {
        screenEffect.OnScreenFadeEffect(value, duration);
    }
    public void StartWireframeEffect(float fadeValue, Color colorValue, float duration)
    {
        screenEffect.OnWireframeEffect(fadeValue, colorValue, duration);
    }
    public void StartWavePanelEffect(int value)
    {
        screenEffect.OnWavePanelEffect(value);
    }
    public void StartTrainingCompletePanelEffect()
    {
        screenEffect.OnTrainingCompletePanel();
    }
    public void StartWeaponSwap(WeaponSet weaponSet, float value)
    {
        hud.SwapWeaponSetImage(weaponSet, value);
    }
    public void ShowBossHP(bool value)
    {
        if (value)
        {
            stageHud.ShowBossHP();
        }
        else
        {
            stageHud.HideBossHP();
        }  
    }
    public void ShowWarningLine()
    {
        stageHud.ShowWarningLine();
    }
    public void ShowDeathUI(bool value)
    {
        if (value)
        {
            deathMenuController.Show();
        }
        else
        {
            deathMenuController.Hide();
        }
    }
    public void ShowTrainingScoreBoard(bool value, TrainingRunTracker tracker)
    {
        if (value)
        {
            trainingScoreboardUI.ShowTrainingScoreboard(tracker);
        }
        else
        {
            trainingScoreboardUI.HideTrainingScoreboard();
        }
    }
    public void ShowCompleteScoreBoard(bool value, RunResult result = default)
    {
        if (value)
        {
            completeScoreBoardUI.ShowResult(result);
        }
        else
        {
            completeScoreBoardUI.Hide();
        }
    }
}
