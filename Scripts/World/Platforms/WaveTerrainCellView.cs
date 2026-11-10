using UnityEngine;
using UnityEngine.Tilemaps;

// Prefab의 충돌은 독립 미리보기에 사용하고 런타임은 시각 부분과 공유 Composite를 조립한다.
public sealed class WaveTerrainCellView : MonoBehaviour
{
    public WaveCellKind kind;
    public GameObject visualRoot;
    public SpriteRenderer image;
    public LineRenderer[] edges;
    public AnimationCurve spawnScale;
    public AnimationCurve despawnScale;
    public AnimationCurve spawnAlpha;
    public AnimationCurve spawnWidth, despawnWidth;
    public float sourceDuration = .5f;
    private bool generated;
    private Renderer tileRenderer;
    private MaterialPropertyBlock properties;

    public WaveTerrainCellView CreateVisual(Transform parent)
    {
        var node = Instantiate(visualRoot, parent, false);
        var view = node.AddComponent<WaveTerrainCellView>();
        view.kind = kind; view.visualRoot = node;
        view.image = node.GetComponentInChildren<SpriteRenderer>(true);
        view.edges = node.GetComponentsInChildren<LineRenderer>(true);
        view.spawnScale = spawnScale; view.despawnScale = despawnScale;
        view.spawnAlpha = spawnAlpha; view.spawnWidth = spawnWidth; view.despawnWidth = despawnWidth; view.sourceDuration = sourceDuration;
        return view;
    }
    public void ApplyStyle(WaveCellStyle style)
    {
        generated = style.generated;
        if (image == null) return;
        var tile = style.tile as Tile;
        image.sprite = tile != null ? tile.sprite : null;
        image.color = style.color;
        image.sharedMaterial = style.material;
        image.sortingLayerID = style.sortingLayer;
        image.sortingOrder = style.sortingOrder;
        image.transform.localPosition = style.transform.GetColumn(3);
        image.transform.localRotation = style.transform.rotation;
        image.transform.localScale = style.transform.lossyScale;
        tileRenderer = image;
        SetOutline(0);
    }
    public void SetOutline(int mask)
    {
        if (kind != WaveCellKind.Platform || edges == null) return;
        for (int i = 0; i < edges.Length; i++) edges[i].enabled = (mask & (1 << i)) != 0;
    }
    public void SetAmount(float amount)
    {
        transform.localScale = new Vector3(1, Mathf.Max(.001f, amount), 1);
        SetAlpha(1);
        if (kind == WaveCellKind.Platform && edges != null) foreach (var edge in edges) edge.widthMultiplier = !generated && WaveRoomRulesSO.Default != null ? WaveRoomRulesSO.Default.terrainLineWidth : .05f;
    }
    public void SetProgress(float progress, bool add, float elapsed = -1)
    {
        if (!generated && kind == WaveCellKind.Platform) { SetAmount(Mathf.SmoothStep(0, 1, add ? progress : 1 - progress)); return; }
        float clipTime = elapsed >= 0 ? Mathf.Min(elapsed, sourceDuration) : progress * sourceDuration;
        var curve = add ? spawnScale : despawnScale;
        float value = curve != null && curve.length > 0 ? curve.Evaluate(clipTime) : (add ? progress : 1 - progress);
        transform.localScale = new Vector3(Mathf.Max(.001f, value), kind == WaveCellKind.OneWayPlatform ? 1 : Mathf.Max(.001f, value), 1);
        SetAlpha(add && spawnAlpha != null && spawnAlpha.length > 0 ? spawnAlpha.Evaluate(clipTime) : 1);
        if (kind == WaveCellKind.Platform && edges != null)
            foreach (var edge in edges)
                edge.widthMultiplier = (add ? spawnWidth : despawnWidth)?.Evaluate(clipTime) ?? .05f * (add ? clipTime / sourceDuration : 1 - clipTime / sourceDuration);
    }
    private void SetAlpha(float alpha)
    {
        if (tileRenderer == null) return;
        properties ??= new MaterialPropertyBlock();
        properties.SetColor("_Color", new Color(1, 1, 1, alpha));
        tileRenderer.SetPropertyBlock(properties);
    }
}
