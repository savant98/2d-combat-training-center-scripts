using UnityEngine;
using UnityEngine.Tilemaps;

// Prefab의 충돌은 독립 미리보기에 사용하고 런타임은 시각 부분과 공유 Composite를 조립한다.
public sealed class WaveTerrainCellView : MonoBehaviour
{
    public WaveCellKind kind;
    public GameObject visualRoot;
    public SpriteRenderer image;
    public LineRenderer[] edges;
    [Tooltip("켜면 스타일의 Tile·재질·색으로 그림을 덮어쓰지 않고 이 Prefab의 그림을 그대로 쓴다.")]
    public bool ownVisual;
    [Tooltip("이웃 칸 조합(8방향 비트, ConnectionMask)마다 쓸 그림 256개. 비우면 Prefab 그림 하나를 쓴다. RuleTile처럼 놓기만 하면 이어진다.")]
    public Sprite[] connectedSprites;
    [Tooltip("이 Prefab으로 그린 칸의 충돌 Tile. 비우면 스타일 Tile을 쓴다. Grid 충돌 Tile이면 충돌이 셀 경계와 정확히 맞는다.")]
    public TileBase collisionTile;

    // 이웃 칸 비트. 위·오른쪽·아래·왼쪽, 그다음 대각선(오른쪽 위부터 시계 방향).
    public const int Up = 1, Right = 2, Down = 4, Left = 8, UpRight = 16, DownRight = 32, DownLeft = 64, UpLeft = 128;
    /// <summary>같은 묶음으로 이어지는 이웃 칸을 8방향 비트로 모은다.</summary>
    public static int ConnectionMask(Vector2Int cell, System.Func<Vector2Int, bool> connected)
    {
        int mask = 0;
        if (connected(cell + Vector2Int.up)) mask |= Up;
        if (connected(cell + Vector2Int.right)) mask |= Right;
        if (connected(cell + Vector2Int.down)) mask |= Down;
        if (connected(cell + Vector2Int.left)) mask |= Left;
        if (connected(cell + new Vector2Int(1, 1))) mask |= UpRight;
        if (connected(cell + new Vector2Int(1, -1))) mask |= DownRight;
        if (connected(cell + new Vector2Int(-1, -1))) mask |= DownLeft;
        if (connected(cell + new Vector2Int(-1, 1))) mask |= UpLeft;
        return mask;
    }
    /// <summary>이웃 조합에 맞는 그림. 이어진 그림이 없으면 null.</summary>
    public Sprite ConnectedSprite(int mask) => connectedSprites != null && connectedSprites.Length == 256 ? connectedSprites[mask & 255] : null;
    /// <summary>이웃 조합에 맞춰 칸 그림을 바꾼다. 이어진 그림이 없는 Prefab은 그대로다.</summary>
    public void SetConnection(int mask)
    {
        if (!ownVisual || image == null) return;
        var sprite = ConnectedSprite(mask);
        if (sprite != null) image.sprite = sprite;
    }
    public AnimationCurve spawnScale;
    public AnimationCurve despawnScale;
    [Tooltip("비워 두면 세로도 spawnScale·despawnScale을 따른다(OneWay는 세로 1). 채우면 가로가 먼저 뻗고 세로가 펼쳐지는 식으로 따로 움직인다.")]
    public AnimationCurve spawnScaleY, despawnScaleY;
    public AnimationCurve spawnAlpha;
    public AnimationCurve spawnWidth, despawnWidth;
    [Tooltip("생성 외형 덩어리 외곽선의 완성 굵기(칸). 넓은 발광선은 셰이더가 가운데 밝은 선에서 바깥으로 옅어지게 그린다.")]
    public float outlineWidth = .05f;
    public float sourceDuration = .5f;
    private bool generated;
    private Renderer tileRenderer;
    private MaterialPropertyBlock properties;

    public WaveTerrainCellView CreateVisual(Transform parent)
    {
        var node = Instantiate(visualRoot, parent, false);
        var view = node.AddComponent<WaveTerrainCellView>();
        view.kind = kind; view.visualRoot = node; view.ownVisual = ownVisual;
        view.image = node.GetComponentInChildren<SpriteRenderer>(true);
        view.edges = node.GetComponentsInChildren<LineRenderer>(true);
        view.spawnScale = spawnScale; view.despawnScale = despawnScale;
        view.spawnScaleY = spawnScaleY; view.despawnScaleY = despawnScaleY;
        view.connectedSprites = connectedSprites; view.collisionTile = collisionTile;
        view.spawnAlpha = spawnAlpha; view.spawnWidth = spawnWidth; view.despawnWidth = despawnWidth; view.outlineWidth = outlineWidth; view.sourceDuration = sourceDuration;
        return view;
    }
    public void ApplyStyle(WaveCellStyle style)
    {
        generated = style.generated;
        if (image == null) return;
        if (ownVisual)
        {
            // 전용 그림을 쓰는 셀은 등장 섬광(알파)만 받는다.
            tileRenderer = image;
            SetOutline(0);
            return;
        }
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
        if (kind == WaveCellKind.Platform && edges != null) foreach (var edge in edges) edge.widthMultiplier = !generated && WaveRoomRulesSO.Default != null ? WaveRoomRulesSO.Default.terrainLineWidth : outlineWidth;
    }
    public void SetProgress(float progress, bool add, float elapsed = -1)
    {
        if (!generated && kind == WaveCellKind.Platform) { SetAmount(Mathf.SmoothStep(0, 1, add ? progress : 1 - progress)); return; }
        float clipTime = elapsed >= 0 ? Mathf.Min(elapsed, sourceDuration) : progress * sourceDuration;
        var curve = add ? spawnScale : despawnScale;
        float value = curve != null && curve.length > 0 ? curve.Evaluate(clipTime) : (add ? progress : 1 - progress);
        var curveY = add ? spawnScaleY : despawnScaleY;
        bool separateY = curveY != null && curveY.length > 0;
        float valueY = separateY ? curveY.Evaluate(clipTime) : kind == WaveCellKind.OneWayPlatform ? 1 : value;
        transform.localScale = new Vector3(Mathf.Max(.001f, value), Mathf.Max(.001f, valueY), 1);
        SetAlpha(add && spawnAlpha != null && spawnAlpha.length > 0 ? spawnAlpha.Evaluate(clipTime) : 1);
        if (kind == WaveCellKind.Platform && edges != null)
            foreach (var edge in edges)
                edge.widthMultiplier = (add ? spawnWidth : despawnWidth)?.Evaluate(clipTime) ?? outlineWidth * (add ? clipTime / sourceDuration : 1 - clipTime / sourceDuration);
    }
    private void SetAlpha(float alpha)
    {
        if (tileRenderer == null) return;
        properties ??= new MaterialPropertyBlock();
        properties.SetColor("_Color", new Color(1, 1, 1, alpha));
        tileRenderer.SetPropertyBlock(properties);
    }
}
