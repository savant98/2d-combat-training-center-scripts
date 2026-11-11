using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>설정 창을 열기 전부터 저장된 설정을 적용하고 Scene 전환 뒤 다시 연결한다.</summary>
public class OptionSettingsService : MonoBehaviour
{
    public static OptionSettingsService Instance { get; private set; }
    public OptionSettings Current { get; private set; }

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
        int width = settings.width > 0 ? settings.width : Screen.currentResolution.width;
        int height = settings.height > 0 ? settings.height : Screen.currentResolution.height;
        if (width <= 0 || height <= 0) { width = 1920; height = 1080; }
        // Editor GameView 크기와 사용자의 Editor 설정은 바꾸지 않는다.
        if (!Application.isEditor)
            Screen.SetResolution(width, height, settings.windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }
}
