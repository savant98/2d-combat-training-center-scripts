using DG.Tweening;
using UnityEngine;
using System.Collections;
public class LaserPreviewLine : WorldEntityBase
{
    [Header("References")]
    [SerializeField] private LineRenderer lineRenderer;

    [Header("Warning Loop Sound")]
    [SerializeField] private SoundOptions warningLoopSound;

    [Header("Width Grow")]
    [SerializeField] private float appearDuration = 0.2f;

    [Header("Flash")]
    [SerializeField] private float flashLeadTime = 0.2f;
    [SerializeField] private float blinkSpeed = 18f;
    [SerializeField] private float baseEmissionIntensity = 2f;
    [SerializeField] private float flashEmissionIntensity = 5f;
    [SerializeField] private Color flashColor = Color.white;
   
    [Header("Caps")]
    [SerializeField] private Transform startCap;
    [SerializeField] private Transform endCap;
    [SerializeField, Min(0f)] private float startCapScaleMultiplier = 1.0f;
    [SerializeField, Min(0f)] private float endCapScaleMultiplier = 1.15f;
    [SerializeField, Min(0f)] private float endCapPulseRepeatSpeed = 8f;

    [Header("Scroll")]
    [SerializeField] private float baseScrollSpeed = 2f;
    [SerializeField] private float maxScrollSpeed = 5f;

    private Material lineMaterial;
    private Vector2 origin;
    private Vector2 direction;
    private float length;
    private float width;
    private LayerMask blockMask;
    private bool activePreview;

    private static readonly int ScrollOffsetID = Shader.PropertyToID("_ScrollOffset");
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private Color previewColor;
    private Vector2 scrollOffset;

    private Coroutine lifeRoutine;
    private Tween widthTween;
    private SoundHandle warningLoopHandle;

    protected override void Awake()
    {
        base.Awake();

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;

            lineMaterial = lineRenderer.material;
        }
    }

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        ResetPreviewState();
    }

    public override void ForceReturnToPool()
    {
        ResetPreviewState();
        base.ForceReturnToPool();
    }

    public void Show(
        Vector2 origin,
        Vector2 direction,
        float length,
        float width,
        LayerMask blockMask,
        Color color,
        float duration)
    {
        this.origin = origin;
        this.direction = direction.normalized;
        this.length = Mathf.Max(0f, length);
        this.width = Mathf.Max(0.01f, width);
        this.blockMask = blockMask;

        activePreview = true;

        StartWarningLoopSound();

        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
            lineRenderer.startWidth = this.width;
            lineRenderer.endWidth = this.width;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
        }
        previewColor = color;
        PlayWidthTween(width);

        StopLifeRoutine();

        if (duration <= 0f)
            return;

        lifeRoutine = StartCoroutine(CoReturnAfter(duration));
    }
    private void StopLifeRoutine()
    {
        if (lifeRoutine == null)
            return;

        StopCoroutine(lifeRoutine);
        lifeRoutine = null;
    }
    private IEnumerator CoReturnAfter(float duration)
    {
        if (duration > 0f)
            yield return new WaitForSeconds(duration);

        lifeRoutine = null;
        ForceReturnToPool();
    }
    private void PlayWidthTween(float targetWidth)
    {
        widthTween?.Kill();

        float currentWidth = 0f;

        widthTween = DOTween.To(
            () => currentWidth,
            x =>
            {
                currentWidth = x;

                if (lineRenderer != null)
                {
                    lineRenderer.startWidth = currentWidth;
                    lineRenderer.endWidth = currentWidth;
                }
            },
            targetWidth,
            appearDuration
        ).SetEase(Ease.OutCubic);
    }

    private void LateUpdate()
    {
        if (!activePreview)
            return;

        RefreshLine();
    }
    public void UpdatePreviewVisual(float remaining, float totalTime)
    {
        if (!activePreview)
            return;

        UpdateEmissionFlash(remaining);
        UpdateScroll(remaining, totalTime);
    }
    private void UpdateEmissionFlash(float remainingTime)
    {
        if (lineMaterial == null)
            return;

        Color emissionColor;

        if (remainingTime > flashLeadTime)
        {
            emissionColor = previewColor * baseEmissionIntensity;
        }
        else
        {
            bool isWhite = Mathf.FloorToInt(Time.time * blinkSpeed) % 2 == 0;

            emissionColor = isWhite
                ? flashColor * flashEmissionIntensity
                : previewColor * baseEmissionIntensity;
        }

        lineMaterial.SetColor(EmissionColorID, emissionColor);
    }
    private void UpdateScroll(float remainingTime, float totalWindupTime)
    {
        if (lineMaterial == null)
            return;

        float chargeT = 1f - Mathf.Clamp01(remainingTime / totalWindupTime);

        float currentScrollSpeed = Mathf.Lerp(baseScrollSpeed, maxScrollSpeed, chargeT);

        scrollOffset.x += currentScrollSpeed * Time.deltaTime;

        lineMaterial.SetVector(ScrollOffsetID, scrollOffset);
    }

    private void RefreshLine()
    {
        if (lineRenderer == null)
            return;

        RaycastHit2D hit = LaserLineUtility.GetBlockHit(
            origin,
            direction,
            length,
            width * 0.5f,
            blockMask);

        float distance = hit.collider != null ? hit.distance + width * 0.5f : length;
        Vector2 endPoint = origin + direction * distance;

        UpdateWarningLoopSoundPosition(endPoint);

        lineRenderer.SetPosition(0, origin);
        lineRenderer.SetPosition(1, endPoint);

        LaserLineUtility.UpdateCaps(
            startCap,
            endCap,
            origin,
            endPoint,
            direction,
            width,
            startCapScaleMultiplier,
            endCapScaleMultiplier);

        float pulse = 1f + Mathf.Sin(Time.time * endCapPulseRepeatSpeed) * 0.25f;
        endCap.localScale = Vector3.one * width * endCapScaleMultiplier * pulse;
    }

    public void SetOrigin(Vector2 newOrigin)
    {
        origin = newOrigin;
    }

    public void SetDirection(Vector2 newDirection)
    {
        if (newDirection.sqrMagnitude <= 0.0001f)
            return;

        direction = newDirection.normalized;
    }
    private void OnDisable()
    {
        StopWarningLoopSound();
    }
    private void StartWarningLoopSound()
    {
        StopWarningLoopSound();

        if (warningLoopSound == null || warningLoopSound.clip == null)
            return;

        if (SoundManager.Instance == null)
            return;

        warningLoopHandle = SoundManager.Instance.PlayLoop(
            warningLoopSound,
            origin,
            null);
    }

    private void StopWarningLoopSound()
    {
        warningLoopHandle?.Stop();
        warningLoopHandle = null;
    }

    private void UpdateWarningLoopSoundPosition(Vector2 endPoint)
    {
        if (warningLoopHandle == null || !warningLoopHandle.IsValid)
            return;

        Vector2 center = (origin + endPoint) * 0.5f;
        warningLoopHandle.SetPosition(center);
    }
    private void ResetPreviewState()
    {
        StopWarningLoopSound();

        activePreview = false;
        widthTween?.Kill();
        StopLifeRoutine();
        origin = Vector2.zero;
        direction = Vector2.right;
        length = 0f;
        width = 0.01f;

        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
            lineRenderer.startWidth = 0f;
            lineRenderer.endWidth = 0f;
            lineRenderer.SetPosition(0, Vector3.zero);
            lineRenderer.SetPosition(1, Vector3.zero);
        }
    }
}