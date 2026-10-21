using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerGearUI : MonoBehaviour
{
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Current Stage Weapon")]
    [SerializeField] private Image stageWeaponIcon;
    [SerializeField] private Image stageWeaponCircle;
    [SerializeField] private Image stageWeaponCooldownFill;
    [SerializeField] private CanvasGroup stageWeaponGroup;
    [SerializeField] private TextMeshProUGUI stageWeaponCooldownText;

    [Header("Current Stage Weapon Ready Visual")]
    [SerializeField] private Material weaponReadyMaterial;
    [SerializeField] private Material weaponPreparingMaterial;
    [SerializeField] private float readyPulseScale = 1.25f;
    [SerializeField] private float readyPulseDuration = 0.22f;
    [SerializeField] private float readyIconAlpha = 1f;
    [SerializeField] private float preparingIconAlpha = 0.35f;
    [SerializeField] private float stageWeaponCircleRotateSpeed = 25f;

    [Header("Gear Layout")]
    [Tooltip("큰 기어. 왼쪽 시작 지점 역할.")]
    [SerializeField] private RectTransform bigGear;

    [Tooltip("작은 기어. fill 값에 따라 큰 기어와 오른쪽 끝 사이를 이동.")]
    [SerializeField] private RectTransform smallGear;

    [Tooltip("작은 기어가 fill=0일 때 위치.")]
    [SerializeField] private RectTransform smallGearStartPoint;

    [Tooltip("작은 기어가 fill=1일 때 위치.")]
    [SerializeField] private RectTransform smallGearEndPoint;

    [Header("Line")]
    [Tooltip("큰 기어와 작은 기어 사이 직선 Fill 이미지.")]
    [SerializeField] private Image lineFillImage;

    [Header("Texts")]
    [SerializeField] private TextMeshProUGUI stageText;

    [Header("Emission Targets")]
    [Tooltip("큰 기어에 달린 Image. 없으면 bigGear의 Graphic을 자동 탐색.")]
    [SerializeField] private Graphic bigGearGraphic;

    [Tooltip("라인 Graphic. 없으면 lineFillImage 사용.")]
    [SerializeField] private Graphic lineGraphic;

    [Header("Emission Colors")]
    [SerializeField] private Color inactiveEmission = new Color(0.05f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color stage1Emission = new Color(0.25f, 0.85f, 1f, 1f);
    [SerializeField] private Color stage2Emission = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color stage3Emission = new Color(1f, 0.25f, 0.1f, 1f);
    [SerializeField] private Color flashEmission = Color.white;

    [Header("Emission Intensity")]
    [SerializeField] private float inactiveEmissionIntensity = 0.25f;
    [SerializeField] private float baseEmissionIntensity = 1.2f;
    [SerializeField] private float waitingEmissionIntensity = 1.6f;
    [SerializeField] private float flashEmissionIntensity = 3.5f;

    [Header("Motion")]
    [SerializeField] private float bigGearRotationSpeed = 240f;
    [SerializeField] private float smallGearRotationSpeed = 420f;
    [SerializeField] private float waitingRotationMultiplier = 0.65f;
    [SerializeField] private float moveLerpSpeed = 35f;
    [SerializeField] private bool instantMoveOnStageUp = true;

    [Header("Stage Punch")]
    [SerializeField] private float punchScalePower = 0.18f;
    [SerializeField] private float punchDuration = 0.18f;
    [SerializeField] private int punchVibrato = 8;
    [SerializeField] private float flashDuration = 0.12f;

    [Header("Feedback")]
    [Tooltip("단계가 확정되어 Waiting으로 들어갈 때 재생.")]
    [SerializeField] private FeedbackData stageUpFeedback;

    [Tooltip("기어가 돌아가는 동안 주기적으로 발생시키는 Spark Feedback.")]
    [SerializeField] private FeedbackData sparkFeedback;

    [SerializeField] private Transform bigGearSparkPoint;
    [SerializeField] private Transform smallGearSparkPoint;
    [SerializeField] private float sparkInterval = 0.08f;
    [SerializeField] private bool playSparkOnlyWhenActive = true;

    private bool isActive;
    private bool isWaiting;
    private int currentStage;
    private int lastStage;
    private float currentFill;
    private float targetFill;
    private bool stageWeaponReady;
    private bool hasStageWeaponReadyState;

    private Vector2 smallGearStartPos;
    private Vector2 smallGearEndPos;

    private float sparkTimer;

    private Material bigGearMaterial;
    private Material smallGearMaterial;
    private Material lineMaterial;

    private Tween bigGearPunchTween;
    private Tween bigEmissionTween;
    private Tween smallEmissionTween;
    private Tween lineEmissionTween;
    private Tween stageWeaponCirclePulseTween;

    private void Awake()
    {
        CacheReferences();
        CachePositions();
        CreateMaterialInstances();

        SetVisible(false);
        hasStageWeaponReadyState = false;
        stageWeaponReady = false;
        SetStageWeaponCooldown(null, 0f, 0f, false);
    }

    private void OnDestroy()
    {
        KillTweens();
        DestroyMaterialInstances();
    }

    private void LateUpdate()
    {
        if (!isActive)
            return;

        UpdateSmoothFill();
        UpdateSmallGearPosition();
        RotateGears();
        UpdateSpark();
        RotateStageWeaponCircle();
    }
    public void SetStageWeaponCooldown(Sprite icon, float remaining, float duration, bool ready)
    {
        bool hasIcon = icon != null;

        if (stageWeaponIcon != null)
        {
            stageWeaponIcon.gameObject.SetActive(hasIcon);
            stageWeaponIcon.sprite = icon;
        }

        if (stageWeaponCircle != null)
            stageWeaponCircle.gameObject.SetActive(hasIcon);

        if (!hasIcon)
        {
            SetStageWeaponReadyVisual(false, true);
            HideStageWeaponCooldown();

            // 다음에 다시 아이콘이 생겼을 때 Visual을 확실히 재적용
            hasStageWeaponReadyState = false;
            return;
        }

        SetStageWeaponReadyVisual(ready, false);

        float normalized = duration > 0f
            ? Mathf.Clamp01(remaining / duration)
            : 0f;

        bool coolingDown = remaining > 0.01f && duration > 0f;

        if (stageWeaponCooldownFill != null)
        {
            stageWeaponCooldownFill.gameObject.SetActive(coolingDown);
            stageWeaponCooldownFill.fillAmount = normalized;
        }

        if (stageWeaponCooldownText != null)
        {
            stageWeaponCooldownText.gameObject.SetActive(coolingDown);
            stageWeaponCooldownText.text = remaining > 1f
                ? Mathf.CeilToInt(remaining).ToString()
                : remaining.ToString("0.0");
        }
    }
    private void SetStageWeaponReadyVisual(bool ready, bool forceOff)
    {
        bool finalReady = ready && !forceOff;

        if (hasStageWeaponReadyState && stageWeaponReady == finalReady)
        {
            RefreshStageWeaponIconAlpha(finalReady);
            return;
        }

        hasStageWeaponReadyState = true;
        stageWeaponReady = finalReady;

        if (stageWeaponCircle != null)
        {
            stageWeaponCircle.material = finalReady ? weaponReadyMaterial : weaponPreparingMaterial;

            if (!finalReady)
            {
                KillStageWeaponCirclePulse();
            }
            else
            {
                if (stageWeaponCirclePulseTween == null || !stageWeaponCirclePulseTween.IsActive())
                {
                    stageWeaponCircle.rectTransform.localScale = Vector3.one;

                    stageWeaponCirclePulseTween = stageWeaponCircle.rectTransform
                        .DOScale(readyPulseScale, readyPulseDuration)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetEase(Ease.InOutSine)
                        .SetUpdate(true);
                }
            }
        }

        RefreshStageWeaponIconAlpha(finalReady);
    }
    private void RefreshStageWeaponIconAlpha(bool ready)
    {
        if (stageWeaponIcon == null)
            return;

        Color color = stageWeaponIcon.color;
        color.a = ready ? readyIconAlpha : preparingIconAlpha;
        stageWeaponIcon.color = color;
    }
    private void HideStageWeaponCooldown()
    {
        if (stageWeaponCooldownFill != null)
        {
            stageWeaponCooldownFill.fillAmount = 0f;
            stageWeaponCooldownFill.gameObject.SetActive(false);
        }

        if (stageWeaponCooldownText != null)
        {
            stageWeaponCooldownText.text = "";
            stageWeaponCooldownText.gameObject.SetActive(false);
        }
    }
    private void RotateStageWeaponCircle()
    {
        if (stageWeaponCircle == null)
            return;

        stageWeaponCircle.transform.Rotate(
            new Vector3(0f, 0f, stageWeaponCircleRotateSpeed * Time.unscaledDeltaTime));
    }
    public void SetGearUI(bool active, int stage, float fill, bool isWaiting)
    {
        fill = Mathf.Clamp01(fill);

        if (!active)
        {
            SetVisible(false);
            hasStageWeaponReadyState = false;
            stageWeaponReady = false;
            SetStageWeaponCooldown(null, 0f, 0f, false);
            return;
        }

        SetVisible(true);

        bool wasWaiting = this.isWaiting;
        int previousStage = currentStage;

        bool enteredWaiting = !wasWaiting && isWaiting;
        bool stageChanged = stage != previousStage && stage > 0;

        stageWeaponGroup.alpha = isWaiting ? 1f : 0f;

        // 핵심:
        // Charging 중에도 이미 targetStage를 표시하기 때문에
        // stageChanged만 보면 Waiting 진입을 못 잡는다.
        bool shouldPlayStageEffect = enteredWaiting && stage > 0;

        this.isWaiting = isWaiting;
        currentStage = stage;

        targetFill = fill;

        if (shouldPlayStageEffect && instantMoveOnStageUp)
        {
            currentFill = 1f;
            targetFill = 1f;
            ApplyLineFill(1f);
            UpdateSmallGearPositionImmediate(1f);
        }

        UpdateTexts();
        ApplyBaseEmission();

        if (shouldPlayStageEffect)
            PlayStageUpEffect();

        lastStage = currentStage;
    }
    public void SetVisible(bool visible)
    {
        isActive = visible;

        if (root != null)
            root.SetActive(visible);
        else
            gameObject.SetActive(visible);

        if (!visible)
        {
            currentStage = 0;
            lastStage = 0;
            currentFill = 0f;
            targetFill = 0f;
            isWaiting = false;
            sparkTimer = 0f;

            KillTweens();
            KillStageWeaponCirclePulse();

            ApplyInactiveEmission();
            ApplyLineFill(0f);
            UpdateSmallGearPositionImmediate(0f);
            SetStageWeaponCooldown(null, 0f, 0f, false);
        }
    }

    private void CacheReferences()
    {
        if (bigGearGraphic == null && bigGear != null)
            bigGearGraphic = bigGear.GetComponent<Graphic>();

        if (lineGraphic == null && lineFillImage != null)
            lineGraphic = lineFillImage;
    }

    private void CachePositions()
    {
        if (smallGearStartPoint != null)
            smallGearStartPos = smallGearStartPoint.anchoredPosition;
        else if (smallGear != null)
            smallGearStartPos = smallGear.anchoredPosition;

        if (smallGearEndPoint != null)
            smallGearEndPos = smallGearEndPoint.anchoredPosition;
        else if (smallGear != null)
            smallGearEndPos = smallGear.anchoredPosition;
    }

    private void CreateMaterialInstances()
    {
        bigGearMaterial = CreateGraphicMaterialInstance(bigGearGraphic);
        lineMaterial = CreateGraphicMaterialInstance(lineGraphic);

        ApplyInactiveEmission();
    }

    private Material CreateGraphicMaterialInstance(Graphic graphic)
    {
        if (graphic == null)
            return null;

        Material source = graphic.material;

        if (source == null)
            return null;

        Material instance = Instantiate(source);
        graphic.material = instance;

        return instance;
    }

    private void DestroyMaterialInstances()
    {
        DestroyMaterialInstance(bigGearMaterial);
        DestroyMaterialInstance(smallGearMaterial);
        DestroyMaterialInstance(lineMaterial);

        bigGearMaterial = null;
        smallGearMaterial = null;
        lineMaterial = null;
    }

    private void DestroyMaterialInstance(Material mat)
    {
        if (mat == null)
            return;

        if (Application.isPlaying)
            Destroy(mat);
        else
            DestroyImmediate(mat);
    }

    private void UpdateSmoothFill()
    {
        currentFill = Mathf.MoveTowards(
            currentFill,
            targetFill,
            moveLerpSpeed * Time.unscaledDeltaTime);

        ApplyLineFill(currentFill);
    }

    private void ApplyLineFill(float fill)
    {
        if (lineFillImage != null)
            lineFillImage.fillAmount = Mathf.Clamp01(fill);
    }

    private void UpdateSmallGearPosition()
    {
        UpdateSmallGearPositionImmediate(currentFill);
    }

    private void UpdateSmallGearPositionImmediate(float fill)
    {
        if (smallGear == null)
            return;

        smallGear.anchoredPosition = Vector2.Lerp(
            smallGearStartPos,
            smallGearEndPos,
            Mathf.Clamp01(fill));
    }

    private void RotateGears()
    {
        float direction = isWaiting ? -1f : 1f;
        float multiplier = isWaiting ? waitingRotationMultiplier : 1f;
        float dt = Time.unscaledDeltaTime;

        if (bigGear != null)
            bigGear.Rotate(0f, 0f, -direction * bigGearRotationSpeed * multiplier * dt);

        if (smallGear != null)
            smallGear.Rotate(0f, 0f, direction * smallGearRotationSpeed * multiplier * dt);
    }

    private void UpdateSpark()
    {
        if (playSparkOnlyWhenActive && (!isActive || currentStage <= 0))
            return;

        if (sparkFeedback == null)
            return;

        sparkTimer -= Time.unscaledDeltaTime;

        if (sparkTimer > 0f)
            return;

        sparkTimer = sparkInterval;

        FeedbackPlayer feedbackPlayer = GameManager.Instance != null
            ? FeedbackPlayer.Instance
            : null;

        if (feedbackPlayer == null)
            return;

        if (bigGearSparkPoint != null)
            feedbackPlayer.Play(sparkFeedback, bigGearSparkPoint);

        if (smallGearSparkPoint != null)
            feedbackPlayer.Play(sparkFeedback, smallGearSparkPoint);
    }

    private void UpdateTexts()
    {
        if (stageText != null)
        {
            stageText.text = currentStage <= 0 ? "0" : $"{currentStage}";
            stageText.color = GetTextColor();
        }
    }
    private Color GetTextColor()
    {
        return GetStageEmission(currentStage);
    }
    private void ApplyInactiveEmission()
    {
        Color emission = inactiveEmission * inactiveEmissionIntensity;

        SetEmission(bigGearMaterial, emission);
        SetEmission(smallGearMaterial, emission);
        SetEmission(lineMaterial, emission);
    }
    private void ApplyBaseEmission()
    {
        Color baseColor = GetStageEmission(currentStage);
        float intensity = isWaiting
            ? waitingEmissionIntensity
            : baseEmissionIntensity;

        Color emission = baseColor * intensity;

        SetEmission(bigGearMaterial, emission);
        SetEmission(smallGearMaterial, emission);
        SetEmission(lineMaterial, emission);
    }

    private Color GetStageEmission(int stage)
    {
        return stage switch
        {
            1 => stage1Emission,
            2 => stage2Emission,
            3 => stage3Emission,
            _ => inactiveEmission
        };
    }

    private void SetEmission(Material mat, Color emissionColor)
    {
        if (mat == null)
            return;

        if (!mat.HasProperty(EmissionColorID))
            return;

        mat.SetColor(EmissionColorID, emissionColor);
    }
    private void PlayStageUpEffect()
    {
        KillTweens();

        if (bigGear != null)
        {
            bigGear.DOKill();
            bigGear.localScale = Vector3.one;
            bigGearPunchTween = bigGear
                .DOPunchScale(
                    Vector3.one * punchScalePower,
                    punchDuration,
                    punchVibrato,
                    0.8f)
                .SetUpdate(true);
        }

        FlashMaterialEmission(bigGearMaterial, ref bigEmissionTween);
        FlashMaterialEmission(smallGearMaterial, ref smallEmissionTween);
        FlashMaterialEmission(lineMaterial, ref lineEmissionTween);

        FeedbackPlayer feedbackPlayer = GameManager.Instance != null
            ? FeedbackPlayer.Instance
            : null;

        if (feedbackPlayer != null && stageUpFeedback != null)
            feedbackPlayer.Play(stageUpFeedback, transform);
    }
    private void FlashMaterialEmission(Material mat, ref Tween tween)
    {
        if (mat == null)
            return;

        if (!mat.HasProperty(EmissionColorID))
            return;

        tween?.Kill();

        Color baseColor = GetStageEmission(currentStage);
        float baseIntensity = isWaiting
            ? waitingEmissionIntensity
            : baseEmissionIntensity;

        Color baseEmission = baseColor * baseIntensity;
        Color flashEmissionColor = flashEmission * flashEmissionIntensity;

        Sequence seq = DOTween.Sequence().SetUpdate(true);

        seq.Append(
            mat.DOColor(
                flashEmissionColor,
                EmissionColorID,
                flashDuration * 0.35f)
            .SetEase(Ease.OutQuad));

        seq.Append(
            mat.DOColor(
                baseEmission,
                EmissionColorID,
                flashDuration * 0.65f)
            .SetEase(Ease.OutQuad));

        tween = seq;
    }
    private void KillStageWeaponCirclePulse()
    {
        stageWeaponCirclePulseTween?.Kill();
        stageWeaponCirclePulseTween = null;

        if (stageWeaponCircle != null)
        {
            stageWeaponCircle.rectTransform.DOKill();
            stageWeaponCircle.rectTransform.localScale = Vector3.one;
        }
    }
    private void KillTweens()
    {
        bigGearPunchTween?.Kill();
        bigEmissionTween?.Kill();
        smallEmissionTween?.Kill();
        lineEmissionTween?.Kill();

        bigGearPunchTween = null;
        bigEmissionTween = null;
        smallEmissionTween = null;
        lineEmissionTween = null;

        if (stageText != null)
            stageText.transform.DOKill();

    }
}