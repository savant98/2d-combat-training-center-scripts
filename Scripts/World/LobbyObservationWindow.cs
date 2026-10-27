using UnityEngine;

/// <summary>공유 재질을 변경하지 않고 통창 유리의 불투명도를 편집·실행 상태에서 조절한다.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class LobbyObservationWindow : MonoBehaviour
{
    [SerializeField, InspectorName("유리 Renderer")] private MeshRenderer glassRenderer;
    [SerializeField, Range(0f, 1f), InspectorName("불투명도")]
    private float opacity = .18f;

    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock originalProperties;
    private MaterialPropertyBlock properties;
    private MeshRenderer appliedRenderer;
    private float appliedOpacity = float.NaN;
    private bool refresh = true;

    public float Opacity
    {
        get => opacity;
        set
        {
            opacity = Mathf.Clamp01(value);
            ApplyOpacity();
        }
    }

    private void OnEnable() => ApplyOpacity();

    private void OnValidate()
    {
        opacity = Mathf.Clamp01(opacity);
        refresh = true;
    }

    private void Update()
    {
        if (refresh || appliedRenderer != glassRenderer || appliedOpacity != opacity)
            ApplyOpacity();
    }

    private void ApplyOpacity()
    {
        if (appliedRenderer != glassRenderer)
        {
            RestoreProperties();
            if (glassRenderer == null) return;
            originalProperties = new MaterialPropertyBlock();
            glassRenderer.GetPropertyBlock(originalProperties);
            appliedRenderer = glassRenderer;
        }
        if (appliedRenderer == null || appliedRenderer.sharedMaterial == null) return;
        properties ??= new MaterialPropertyBlock();
        appliedRenderer.GetPropertyBlock(properties);
        Color tint = appliedRenderer.sharedMaterial.GetColor(ColorID);
        tint.a = opacity;
        properties.SetColor(ColorID, tint);
        appliedRenderer.SetPropertyBlock(properties);
        appliedOpacity = opacity;
        refresh = false;
    }

    private void RestoreProperties()
    {
        if (appliedRenderer != null)
            appliedRenderer.SetPropertyBlock(originalProperties == null || originalProperties.isEmpty
                ? null : originalProperties);
        appliedRenderer = null;
        appliedOpacity = float.NaN;
    }

    private void OnDisable() => RestoreProperties();
    private void OnDestroy() => RestoreProperties();
}
