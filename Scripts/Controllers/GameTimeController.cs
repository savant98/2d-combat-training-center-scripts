using System.Collections;
using UnityEngine;

public class GameTimeController : MonoBehaviour
{
    public static GameTimeController Instance { get; private set; }

    [Header("Default")]
    [SerializeField] private float defaultTimeScale = 1f;

    private float baseFixedDeltaTime;

    private int pauseCount;
    private Coroutine hitStopCoroutine;
    private float hitStopScale = 1f;
    private bool hitStopActive;

    public bool IsPaused => pauseCount > 0;
    public bool IsHitStopActive => hitStopActive;

    private void Awake()
    {
        Instance = this;

        baseFixedDeltaTime = Time.fixedDeltaTime;
        ApplyTimeScale(defaultTimeScale);
    }

    public void PushPause()
    {
        pauseCount++;
        RefreshTimeScale();
    }

    public void PopPause()
    {
        pauseCount = Mathf.Max(0, pauseCount - 1);
        RefreshTimeScale();
    }

    public void ClearPause()
    {
        pauseCount = 0;
        RefreshTimeScale();
    }

    public void PlayHitStop(float scale, float duration)
    {
        if (duration <= 0f)
            return;

        if (scale > Time.timeScale)
            return;

        if (hitStopCoroutine != null)
            StopCoroutine(hitStopCoroutine);

        hitStopCoroutine = StartCoroutine(HitStopRoutine(scale, duration));
    }

    private IEnumerator HitStopRoutine(float scale, float duration)
    {
        hitStopActive = true;
        hitStopScale = Mathf.Clamp(scale, 0.01f, 1f);

        RefreshTimeScale();

        yield return new WaitForSecondsRealtime(duration);

        hitStopActive = false;
        hitStopScale = 1f;
        hitStopCoroutine = null;

        RefreshTimeScale();
    }

    private void RefreshTimeScale()
    {
        if (pauseCount > 0)
        {
            ApplyTimeScale(0f);
            return;
        }

        if (hitStopActive)
        {
            ApplyTimeScale(hitStopScale);
            return;
        }

        ApplyTimeScale(defaultTimeScale);
    }

    private void ApplyTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = baseFixedDeltaTime * scale;
    }
}