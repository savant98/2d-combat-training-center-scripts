using System.Collections.Generic;
using UnityEngine;

/// <summary>훈련 메뉴 프레임의 기어와 신호 케이블을 CanvasGroup의 표시 수명에 맞춘다.</summary>
[DisallowMultipleComponent]
public class TrainingMenuMechanism : MonoBehaviour
{
    [SerializeField] private RectTransform[] gears;
    [SerializeField] private float[] gearSpeeds;
    [SerializeField] private LineRenderer[] cables;
    [SerializeField] private SpriteRenderer[] gearRenderers = System.Array.Empty<SpriteRenderer>();
    [SerializeField] private float scrollSpeed = -1.25f;

    private static readonly int ScrollOffsetID = Shader.PropertyToID("_ScrollOffset");
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private readonly Dictionary<Material, Material> instances = new Dictionary<Material, Material>();
    private readonly Dictionary<Material, float> intensities = new Dictionary<Material, float>();
    private Material[] originalMaterials;
    private bool[] originalEnabled;
    private Material[] originalGearMaterials;
    private bool[] originalGearEnabled;
    private Quaternion[] originalRotations;
    private CanvasGroup[] visibilityGroups;
    private float scrollOffset;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        gears = gears ?? System.Array.Empty<RectTransform>();
        gearSpeeds = gearSpeeds ?? System.Array.Empty<float>();
        cables = cables ?? System.Array.Empty<LineRenderer>();
        gearRenderers = gearRenderers ?? System.Array.Empty<SpriteRenderer>();
        visibilityGroups = GetComponentsInParent<CanvasGroup>(true);
        originalRotations = new Quaternion[gears.Length];
        for (int i = 0; i < gears.Length; i++)
            if (gears[i] != null) originalRotations[i] = gears[i].localRotation;

        originalMaterials = new Material[cables.Length];
        originalEnabled = new bool[cables.Length];
        for (int i = 0; i < cables.Length; i++)
        {
            var line = cables[i];
            if (line == null) continue;
            originalMaterials[i] = line.sharedMaterial;
            originalEnabled[i] = line.enabled;
            var source = line.sharedMaterial;
            if (source != null && source.HasProperty(ScrollOffsetID))
                line.sharedMaterial = GetRuntimeMaterial(source);
        }
        originalGearMaterials = new Material[gearRenderers.Length];
        originalGearEnabled = new bool[gearRenderers.Length];
        for (int i = 0; i < gearRenderers.Length; i++)
        {
            var renderer = gearRenderers[i];
            if (renderer == null) continue;
            originalGearMaterials[i] = renderer.sharedMaterial;
            originalGearEnabled[i] = renderer.enabled;
            renderer.sharedMaterial = GetRuntimeMaterial(renderer.sharedMaterial);
        }
        UpdateVisibility(0f);
    }

    private Material GetRuntimeMaterial(Material source)
    {
        if (source == null || !source.HasProperty(IntensityID)) return source;
        // 환경 기어와 TutorialLineGlow를 재사용하되 원본 에셋에는 밝기·오프셋을 쓰지 않는다.
        if (!instances.TryGetValue(source, out var instance))
        {
            instance = new Material(source)
            {
                name = source.name + " (TrainingMenu)",
                hideFlags = HideFlags.DontSave
            };
            instances.Add(source, instance);
            intensities.Add(instance, source.GetFloat(IntensityID));
        }
        return instance;
    }

    private void LateUpdate()
    {
        if (originalMaterials == null) return;

        float alpha = 1f;
        foreach (var group in visibilityGroups)
        {
            if (group == null || !group.isActiveAndEnabled) continue;
            alpha *= group.alpha;
            if (group.ignoreParentGroups) break;
        }
        UpdateVisibility(alpha);
        if (alpha <= .001f) return;

        float delta = Time.unscaledDeltaTime;
        for (int i = 0; i < gears.Length && i < gearSpeeds.Length; i++)
            if (gears[i] != null) gears[i].Rotate(0f, 0f, gearSpeeds[i] * delta);

        scrollOffset = Mathf.Repeat(scrollOffset + scrollSpeed * delta, 1f);
        var offset = new Vector4(scrollOffset, 0f, 0f, 0f);
        foreach (var material in instances.Values)
            if (material.HasProperty(ScrollOffsetID)) material.SetVector(ScrollOffsetID, offset);
    }

    private void UpdateVisibility(float alpha)
    {
        foreach (var line in cables)
            if (line != null) line.enabled = alpha > .001f;
        foreach (var renderer in gearRenderers)
            if (renderer != null) renderer.enabled = alpha > .001f;
        // 일반 Renderer는 CanvasGroup을 따르지 않으므로 같은 알파를 발광 강도에 반영한다.
        foreach (var pair in intensities) pair.Key.SetFloat(IntensityID, pair.Value * alpha);
    }

    private void OnDisable()
    {
        if (originalMaterials == null) return;
        for (int i = 0; i < cables.Length; i++)
        {
            if (cables[i] == null) continue;
            if (originalMaterials[i] != null && instances.TryGetValue(originalMaterials[i], out var assigned)
                && cables[i].sharedMaterial == assigned)
                cables[i].sharedMaterial = originalMaterials[i];
            cables[i].enabled = originalEnabled[i];
        }
        for (int i = 0; i < gearRenderers.Length; i++)
        {
            var renderer = gearRenderers[i];
            if (renderer == null) continue;
            if (originalGearMaterials[i] != null && instances.TryGetValue(originalGearMaterials[i], out var assigned)
                && renderer.sharedMaterial == assigned)
                renderer.sharedMaterial = originalGearMaterials[i];
            renderer.enabled = originalGearEnabled[i];
        }
        for (int i = 0; i < gears.Length; i++)
            if (gears[i] != null) gears[i].localRotation = originalRotations[i];
        foreach (var material in instances.Values) Destroy(material);
        instances.Clear();
        intensities.Clear();
        originalMaterials = null;
        originalEnabled = null;
        originalGearMaterials = null;
        originalGearEnabled = null;
        originalRotations = null;
    }
}
