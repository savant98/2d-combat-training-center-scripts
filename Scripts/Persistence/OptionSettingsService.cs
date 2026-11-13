using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>설정 창을 열기 전부터 저장된 설정을 적용하고 Scene 전환 뒤 다시 연결한다.</summary>
public class OptionSettingsService : MonoBehaviour
{
    public static OptionSettingsService Instance { get; private set; }
    public OptionSettings Current { get; private set; }
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
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(ApplyAfterStart());

    private IEnumerator ApplyAfterStart()
    {
        // AudioMixer는 Start 이후 값을 설정해야 초기화 값으로 덮이지 않는다.
        yield return null;
        ApplyPreview(Current);
        ApplyTiming(Current);
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
                var mode = settings.windowed ? FullScreenMode.Windowed : settings.exclusiveFullscreen ?
                    FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
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
