using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 안내선과 같은 UV 오프셋으로 후경 설비의 케이블을 흐르게 한다.</summary>
[DisallowMultipleComponent]
public class LobbyCableScroll : MonoBehaviour
{
    [SerializeField] private LineRenderer[] lineRenderers;
    [SerializeField] private float scrollSpeed = -1.25f;

    private static readonly int ScrollOffsetID = Shader.PropertyToID("_ScrollOffset");
    private Material[] originalMaterials;
    private Material[] assignedMaterials;
    private readonly List<Material> scrollingMaterials = new List<Material>();
    private float scrollOffset;

    private void OnEnable()
    {
        if (!Application.isPlaying || lineRenderers == null) return;

        // 같은 깊이의 케이블은 실행 중 Material 하나를 공유하며 원본 에셋은 수정하지 않는다.
        var instances = new Dictionary<Material, Material>();
        originalMaterials = new Material[lineRenderers.Length];
        assignedMaterials = new Material[lineRenderers.Length];
        for (int i = 0; i < lineRenderers.Length; i++)
        {
            var line = lineRenderers[i];
            if (line == null || line.sharedMaterial == null
                || !line.sharedMaterial.HasProperty(ScrollOffsetID)) continue;

            var source = line.sharedMaterial;
            if (!instances.TryGetValue(source, out var instance))
            {
                instance = new Material(source)
                {
                    name = source.name + " (LobbyScroll)",
                    hideFlags = HideFlags.DontSave
                };
                instances.Add(source, instance);
                scrollingMaterials.Add(instance);
            }
            originalMaterials[i] = source;
            assignedMaterials[i] = instance;
            line.sharedMaterial = instance;
        }
        ApplyOffset();
    }

    private void Update()
    {
        if (scrollingMaterials.Count == 0) return;
        scrollOffset = Mathf.Repeat(scrollOffset + scrollSpeed * Time.deltaTime, 1f);
        ApplyOffset();
    }

    private void ApplyOffset()
    {
        var offset = new Vector4(scrollOffset, 0, 0, 0);
        foreach (var material in scrollingMaterials)
            material.SetVector(ScrollOffsetID, offset);
    }

    private void OnDisable()
    {
        ReleaseMaterials();
    }

    private void OnDestroy()
    {
        ReleaseMaterials();
    }

    private void ReleaseMaterials()
    {
        if (originalMaterials == null) return;
        for (int i = 0; i < lineRenderers.Length; i++)
        {
            var line = lineRenderers[i];
            if (line != null && assignedMaterials[i] != null
                && line.sharedMaterial == assignedMaterials[i])
                line.sharedMaterial = originalMaterials[i];
        }
        foreach (var material in scrollingMaterials)
        {
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        scrollingMaterials.Clear();
        originalMaterials = null;
        assignedMaterials = null;
    }
}
