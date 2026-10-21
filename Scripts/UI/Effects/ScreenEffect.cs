using DG.Tweening;
using TMPro;
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

    [Header("Wave Effect")]
    [SerializeField] private GameObject wavePanel;
    [SerializeField] private GameObject trainingCompletePanel;
    [SerializeField] private TextMeshProUGUI waveText;

    [Header("Wireframe Effect")]
    [SerializeField] private Image wireframe;

    private Material wireframeInstanceMaterial;
    private Tween wireframeTween;
    private void Awake()
    {
        if (shieldBreakPanel != null)
            shieldBreakPanel.SetActive(false);

        if (breakPanel != null)
            breakPanel.SetActive(false);

        if (wavePanel != null)
            wavePanel.SetActive(false);

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
    public void OnWavePanelEffect(int value)
    {
        wavePanel.SetActive(false);
        wavePanel.SetActive(true);

        waveText.text = "훈련 : " + value.ToString();
    }
    public void OnTrainingCompletePanel()
    {
        trainingCompletePanel.SetActive(false);
        trainingCompletePanel.SetActive(true);
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