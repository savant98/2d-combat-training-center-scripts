using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>설정 창을 열기 전부터 저장된 설정을 적용하고 Scene 전환 뒤 다시 연결한다.</summary>
public class OptionSettingsService : MonoBehaviour
{
    public static OptionSettingsService Instance { get; private set; }
    public OptionSettings Current { get; private set; }
    /// <summary>전환 키로 자동 조준을 바꿨을 때 알린다(켜짐 여부, settings.json 저장 성공 여부). 화면 알림은 UI가 구독한다.</summary>
    public event System.Action<bool, bool> RangedAutoAimToggled;
    private OptionSettings pendingDisplay;
    private Coroutine displayRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (Instance != null) return;
        new GameObject("OptionSettingsService").AddComponent<OptionSettingsService>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Current = DataManager.Instance.Options.Copy();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyDisplay(Current);
        // 첫 Scene에는 sceneLoaded가 오지 않을 수 있다(편집기에서 Scene 다시 읽기를 끈 Play). 처음 한 번은 직접 적용한다.
        // 그러지 않으면 타이틀에서는 믹서의 기본값으로 들리다가 다음 Scene에 들어가는 순간 음량이 바뀐다.
        StartCoroutine(ApplyAfterStart());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(ApplyAfterStart());

    private IEnumerator ApplyAfterStart()
    {
        // AudioMixer는 Start 이후 값을 설정해야 초기화 값으로 덮이지 않는다.
        yield return null;
        // Scene에 새로 연결된 믹서와 피드백에는 저장한 값을 적용한다.
        var saved = DataManager.Instance.Options;
        ApplyPreview(saved);
        ApplyTiming(saved);
    }

    public void ApplyPreview(OptionSettings settings)
    {
        Current = settings.Copy();
        Current.Sanitize();
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.SetMasterVolume(Current.masterVolume);
            SoundManager.Instance.SetBGMVolume(Current.bgmVolume);
            SoundManager.Instance.SetSFXVolume(Current.sfxVolume);
        }
        if (FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.ApplyAccessibility(Current.bloom, Current.flash);
    }

    /// <summary>
    /// 자동 조준 켜기·끄기를 뒤집어 바로 적용하고 settings.json에도 저장한다(설정 창의 자동 조준 행과 같은 값).
    /// 저장에 실패해도 이번 실행에는 적용하고, 다음 Scene 전환 때 저장값으로 돌아간다. 새 값을 돌려준다.
    /// </summary>
    public bool ToggleRangedAutoAim()
    {
        var stored = DataManager.Instance.Options.Copy();
        bool enabled = !(Current != null ? Current.rangedAutoAim : stored.rangedAutoAim);
        stored.rangedAutoAim = enabled;
        bool saved = DataManager.Instance.SaveOptions(stored);
        var applied = Current != null ? Current.Copy() : stored.Copy();
        applied.rangedAutoAim = enabled;
        Current = applied;
        RangedAutoAimToggled?.Invoke(enabled, saved);
        return enabled;
    }

    public static void ApplyTiming(OptionSettings settings)
    {
        QualitySettings.vSyncCount = settings.vSync ? 1 : 0;
        Application.targetFrameRate = settings.vSync ? -1 : settings.frameRate;
    }

    public static void ApplyDisplay(OptionSettings settings)
    {
        ApplyTiming(settings);
        // Editor GameView 크기와 사용자의 Editor 설정은 바꾸지 않는다.
        if (Application.isEditor || Instance == null) return;
        Instance.pendingDisplay = settings.Copy();
        DisplayOptions.NormalizeMonitor(Instance.pendingDisplay);
        if (Instance.displayRoutine == null) Instance.displayRoutine = Instance.StartCoroutine(Instance.ApplyDisplayRoutine());
    }

    private IEnumerator ApplyDisplayRoutine()
    {
        // 이동 작업은 취소할 수 없으므로 직렬화한다. 확인 시간 초과 복구가 가장 마지막에 적용된다.
        yield return null;
        try
        {
            while (pendingDisplay != null)
            {
                var settings = pendingDisplay;
                pendingDisplay = null;
                var displays = DisplayOptions.Layout();
                int index = DisplayOptions.Monitor(settings, displays);
                if (index >= 0 && !Screen.mainWindowDisplayInfo.Equals(displays[index]))
                {
                    if (Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen)
                    {
                        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                        yield return null;
                    }
                    var display = displays[index];
                    var position = settings.windowed ? display.workArea.position + new Vector2Int(Mathf.Max(0, (display.workArea.width - DisplayOptions.Width(settings)) / 2), Mathf.Max(0, (display.workArea.height - DisplayOptions.Height(settings)) / 2)) : Vector2Int.zero;
                    AsyncOperation move = null;
                    try { move = Screen.MoveMainWindowTo(display, position); }
                    catch (System.Exception error)
                    {
                        // 이동 중 연결이 끊겨도 복구 요청을 계속 처리할 수 있게 현재 화면을 유지한다.
                        Debug.LogWarning("출력 모니터 이동 실패: " + error.Message);
                    }
                    if (move != null) yield return move;
                    if (pendingDisplay != null) continue;
                }
                var mode = DisplayOptions.Mode(settings);
                try
                {
                    Screen.SetResolution(DisplayOptions.Width(settings), DisplayOptions.Height(settings), mode,
                        DisplayOptions.CanChangeRefresh(settings) ? DisplayOptions.Rate(settings) : DisplayOptions.AutoRate);
                }
                catch (System.Exception error) { Debug.LogWarning("화면 설정 적용 실패: " + error.Message); }
                yield return null;
            }
        }
        finally { displayRoutine = null; }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }
}
