using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
public class ScreenEffect : MonoBehaviour
{
    [Header("Fade")]
    [SerializeField] private CanvasGroup fadePanel;
    [SerializeField] private float defaultFadeValue = 0f;

    [Header("Break Effect")]
    [SerializeField] private GameObject shieldBreakPanel;
    [SerializeField] private GameObject breakPanel;

    [Header("Wireframe Effect")]
    [SerializeField] private Image wireframe;

    private Material wireframeInstanceMaterial;
    private Tween wireframeTween;
    /// <summary>암전 위에 겹쳐 그리는 것 가운데 가장 위에 있는 것. 로딩 표시처럼 암전 위에 보여야 하는 UI는 이 바로 다음 순서에 둔다.</summary>
    public Transform OverlayRoot => wireframe != null ? wireframe.transform : fadePanel != null ? fadePanel.transform : transform;
    private void Awake()
    {
        if (shieldBreakPanel != null)
            shieldBreakPanel.SetActive(false);

        if (breakPanel != null)
            breakPanel.SetActive(false);

        if (wireframe != null)
        {
            wireframeInstanceMaterial = Instantiate(wireframe.material);
            wireframe.material = wireframeInstanceMaterial;
        }

        if (fadePanel != null)
            fadePanel.alpha = defaultFadeValue;
    }

    public void OnShieldBreakTextEffect()
    {
        shieldBreakPanel.SetActive(false);
        shieldBreakPanel.SetActive(true);
    }
    public void OnBreakTextEffect()
    {
        breakPanel.SetActive(false);
        breakPanel.SetActive(true);
    }
    public void OnScreenFadeEffect(float value, float duration)
    {
        fadePanel.DOFade(value, duration);
    }
    public void OnWireframeEffect(float fadeValue, Color colorValue, float duration)
    {
        wireframeTween?.Kill();

        wireframeInstanceMaterial.SetColor("_WireColor", colorValue);
        wireframeTween = DOTween.Sequence().Append(DOTween.To(() => wireframeInstanceMaterial.GetFloat("_Fade"),
            x => wireframeInstanceMaterial.SetFloat("_Fade", x),
            fadeValue,
            duration))
            .Append(DOTween.To(() => wireframeInstanceMaterial.GetFloat("_TimeOffset"),
            x => wireframeInstanceMaterial.SetFloat("_TimeOffset", x),
            fadeValue * 3,
            duration));

    }
}