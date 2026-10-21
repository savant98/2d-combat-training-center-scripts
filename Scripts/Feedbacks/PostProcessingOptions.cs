using UnityEngine;

[System.Serializable]
public class PostProcessingOptions
{
    [Header("Flash")]
    public bool useFlash;
    public Color flashColor = Color.white;
    public float flashIntensity = 1f;
    public float flashDuration = 0.1f;

    [Header("Vignette")]
    public bool useVignette;
    public Color vignetteColor = new Color(1f, 0f, 1f);
    public float vignetteIntensity = 0.3f;
    public float vignetteDuration = 0.2f;

    [Header("ChromaticAberration")]
    public bool useChromaticAberration;
    public float chromaIntensity = 0.5f;
    public float chromaDuration = 0.2f;

    [Header("Bloom")]
    public bool useBloom;
    public float bloomIntensity = 50f;
    public float bloomDuration = 0.15f;

    [Header("Saturation")]
    public bool useSaturation;
    public float saturationIntensity = 0f;
    public float saturationDuration = 0.1f;
}
