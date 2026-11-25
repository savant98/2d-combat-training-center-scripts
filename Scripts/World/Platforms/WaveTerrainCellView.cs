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
    [Tooltip("ownVisual을 끈 기본 지형 Prefab에서, 스타일 Tile이 이 Tile인 칸만 connectedSprites·connectedMaterial로 이웃과 이어 그린다. 다른 Tile(다른 테마)·투명 칸·생성 외형은 스타일 그림 그대로다.")]
    public TileBase connectedStyleTile;
    [Tooltip("connectedStyleTile 칸의 그림 재질. 비우면 스타일 재질을 쓴다.")]
    public Material connectedMaterial;
    [Tooltip("이 Prefab이 그리는 기본 지형(연결 그림 칸, 자기 그림 셀)의 덩어리 외곽선·패널 테두리·이음매·맞물림 섬광 재질. " +
        "비우면 스타일의 선 재질 또는 공통 규칙의 지형 선을 쓴다. 공통 규칙의 선은 Lobby 등 고정 Tilemap 외곽선과 함께 쓰므로 셀 Prefab 쪽에서 바꾼다.")]
    public Material connectedLineMaterial;
    [Tooltip("사방이 이어진 안쪽 칸(조합 255)을 2×2칸 강판으로 나눠 그리는 변형 그림. 변형마다 4조각(왼쪽 아래·오른쪽 아래·왼쪽 위·오른쪽 위) 순서이고 0번 변형은 민판이다. " +
        "블록 네 칸이 모두 안쪽 칸이면 블록 해시로 변형을 고르고, 일부만 안쪽이면 민판 조각을 쓴다. 비우면 조합 255 그림 하나를 쓴다.")]
    public Sprite[] interiorSprites;
    /// <summary>ownVisual을 끈 이 Prefab이 이 스타일의 칸을 이어진 그림으로 그리는지(스타일 조건부 연결 그림). 충돌은 그대로 스타일 Tile이다.</summary>
    public bool StyleConnects(WaveCellStyle style) => !ownVisual && connectedStyleTile != null && style != null && !style.generated && style.tile == connectedStyleTile
        && style.color.a > 0 && connectedSprites != null && connectedSprites.Length == 256;
    /// <summary>이 스타일의 칸이 이웃 조합에 맞춰 그림을 고르는지(전용 그림 셀이거나 스타일 조건부 연결 그림).</summary>
    public bool UsesConnection(WaveCellStyle style) => ownVisual || StyleConnects(style);
    /// <summary>
    /// 이 Prefab이 그리는 이 스타일 칸의 기본 지형 선 재질. 연결 그림 칸(공장형 기본 지형)과 자기 그림 셀(기본 외형 OneWay)에 connectedLineMaterial을 돌려주고,
    /// 정하지 않았거나 다른 테마 칸이면 null이다(호출 쪽이 공통 규칙 선을 쓴다). 스타일의 lineMaterial이 있으면 호출 쪽이 그것을 먼저 쓴다.
    /// </summary>
    public Material LineMaterialFor(WaveCellStyle style) => connectedLineMaterial != null && (ownVisual || StyleConnects(style)) ? connectedLineMaterial : null;
    /// <summary>실행 중 이 칸이 스타일 조건부 연결 그림으로 그려지는지. ApplyStyle이 정한다.</summary>
    public bool Connected { get; private set; }
    // 마지막으로 고른 그림의 열쇠(-1 = 아직 고르지 않음). 0~255는 이웃 조합, 256부터는 안쪽 강판 조각(256 + 변형 × 4 + 조각)이다.
    // 같은 열쇠면 그림을 다시 넣지 않는다. 고른 그림은 ApplyStyle이 다시 그릴 때 그대로 쓴다.
    private int connectionKey = -1;
    private Sprite connectionSprite;
    /// <summary>안쪽 강판 변형 한 벌의 조각 수(2×2칸).</summary>
    public const int InteriorPieces = 4;
    /// <summary>2×2칸 강판 블록이 정비 해치·통풍 그릴 변형을 고르는 비율(%). 나머지는 민판이다.</summary>
    public const int InteriorHatchPercent = 12, InteriorVentPercent = 8;
    /// <summary>이 Prefab이 가진 안쪽 강판 변형 수(0 = 변형 없음).</summary>
    public int InteriorVariantCount => interiorSprites != null ? interiorSprites.Length / InteriorPieces : 0;
    // 연결 그림을 선(덩어리 외곽선·패널 테두리)보다 카메라에서 멀리 두는 거리(Unit). 화면 위치에는 영향이 없다.
    public const float ConnectedDepth = .001f;

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
    /// <summary>
    /// 이 칸 위치와 같은 묶음 판정으로 고른 그림. 안쪽 칸(조합 255)은 2×2칸 강판 조각이고, 블록 전체가 안쪽이면 블록 해시로 고른 변형(해치·그릴 포함),
    /// 일부만 안쪽이면 민판 조각이다(해치가 잘려 보이지 않게). 그 밖의 칸은 이웃 조합 그림이다.
    /// 실행(WaveCellTerrain)과 회귀가 같은 결과를 내도록 이 함수 하나로 고른다.
    /// </summary>
    public Sprite ConnectedSprite(Vector2Int cell, System.Func<Vector2Int, bool> connected)
    {
        int key = ConnectionKey(cell, connected);
        return key >= 256 ? interiorSprites[key - 256] : ConnectedSprite(key);
    }
    /// <summary>이웃 조합에 맞춰 칸 그림을 바꾼다. 이어진 그림이 없는 Prefab은 그대로다.</summary>
    public void SetConnection(int mask) => Assign(mask & 255);
    /// <summary>칸 위치와 같은 묶음 판정으로 그림을 바꾼다(안쪽 강판 변형 포함). 이어진 그림이 없는 Prefab은 그대로다.</summary>
    public void SetConnection(Vector2Int cell, System.Func<Vector2Int, bool> connected)
    {
        if (!ownVisual && !Connected || image == null) return;
        Assign(ConnectionKey(cell, connected));
    }
    private void Assign(int key)
    {
        if (!ownVisual && !Connected || image == null || key == connectionKey) return;
        var sprite = key >= 256 ? interiorSprites[key - 256] : ConnectedSprite(key);
        if (sprite != null) { image.sprite = sprite; connectionKey = key; connectionSprite = sprite; }
    }
    // 그림 열쇠: 안쪽 강판 조각이면 256 + 변형 × 4 + 조각, 아니면 이웃 조합(0~255).
    private int ConnectionKey(Vector2Int cell, System.Func<Vector2Int, bool> connected)
    {
        int mask = ConnectionMask(cell, connected);
        int variants = InteriorVariantCount;
        if (mask != 255 || variants == 0) return mask;
        int variant = InteriorBlockComplete(cell, connected) ? InteriorVariant(InteriorBlock(cell), variants) : 0;
        int index = variant * InteriorPieces + InteriorPiece(cell);
        return interiorSprites[index] != null ? 256 + index : mask;
    }
    /// <summary>칸이 속한 2×2칸 강판 블록(칸 좌표를 2로 내림 나눈 값, 음수 좌표도 내림).</summary>
    public static Vector2Int InteriorBlock(Vector2Int cell) => new Vector2Int(cell.x >> 1, cell.y >> 1);
    /// <summary>칸이 2×2칸 강판 블록에서 차지하는 조각 번호(0 왼쪽 아래, 1 오른쪽 아래, 2 왼쪽 위, 3 오른쪽 위).</summary>
    public static int InteriorPiece(Vector2Int cell) => (cell.x & 1) | ((cell.y & 1) << 1);
    /// <summary>
    /// 칸이 속한 블록 네 칸이 모두 사방 이어진 안쪽 칸인지. 블록을 한 칸씩 넓힌 4×4 영역이 모두 같은 묶음이면 네 칸이 모두 조합 255다.
    /// 아니면 해치·그릴이 블록 밖 칸에서 잘려 보이므로 민판만 쓴다.
    /// </summary>
    public static bool InteriorBlockComplete(Vector2Int cell, System.Func<Vector2Int, bool> connected)
    {
        var block = InteriorBlock(cell);
        for (int y = block.y * 2 - 1; y <= block.y * 2 + 2; y++)
            for (int x = block.x * 2 - 1; x <= block.x * 2 + 2; x++)
                if (!connected(new Vector2Int(x, y))) return false;
        return true;
    }
    /// <summary>블록이 고르는 변형 번호(0 민판, 1 정비 해치, 2 통풍 그릴). 같은 블록은 늘 같은 값이다(결정적, 방 시드와 무관).</summary>
    public static int InteriorVariant(Vector2Int block, int variants)
    {
        if (variants <= 1) return 0;
        uint roll = InteriorHash(block) % 100u;
        int variant = roll < InteriorHatchPercent ? 1 : roll < InteriorHatchPercent + InteriorVentPercent ? 2 : 0;
        return variant < variants ? variant : 0;
    }
    private static uint InteriorHash(Vector2Int block)
    {
        unchecked
        {
            uint h = (uint)block.x * 0x9E3779B1u ^ ((uint)block.y * 0x85EBCA77u + 0x165667B1u);
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }
    // 아래 등장·소멸 곡선은 기계팔 패널로 움직이지 않는 칸에만 쓴다(공통 규칙에서 생성 발판·OneWay의 패널 연출을 껐을 때).
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
    private Color baseColor = Color.white;
    private bool panelOutline;
    // 패널 연출 동안 이 칸의 테두리로 쓰는 선과 그 선의 제 굵기.
    private LineRenderer panelLine;
    private float panelLineWidth;
    private LineRenderer surfaceLine;
    private LineRenderer SurfaceLine => surfaceLine != null ? surfaceLine : surfaceLine = System.Array.Find(edges, line => line.name == "SurfaceLine");
    /// <summary>패널 그림의 중심(칸 중심 기준)과 반 크기. OneWay는 칸 위쪽의 얇은 판이고, 그 밖에는 한 칸 전체다.</summary>
    public Vector2 PanelPivot { get; private set; }
    public Vector2 PanelHalf { get; private set; } = new Vector2(.5f, .5f);
    // 제 크기·제 색으로 고정된 상태. 이미 고정된 칸에 SetAmount(1)을 다시 불러도 아무것도 하지 않는다(수천 칸을 한꺼번에 되돌릴 때의 비용).
    private bool settled;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

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
        view.connectedStyleTile = connectedStyleTile; view.connectedMaterial = connectedMaterial;
        view.connectedLineMaterial = connectedLineMaterial; view.interiorSprites = interiorSprites;
        view.spawnAlpha = spawnAlpha; view.spawnWidth = spawnWidth; view.despawnWidth = despawnWidth; view.outlineWidth = outlineWidth; view.sourceDuration = sourceDuration;
        if (kind == WaveCellKind.OneWayPlatform && view.image != null && view.image.sprite != null)
        {
            // 그림(Sprite) 좌표를 이 칸의 좌표로 옮긴다. 방이 놓인 위치·방향과 관계없이 같은 값이 나온다.
            var toCell = node.transform.worldToLocalMatrix * view.image.transform.localToWorldMatrix;
            var bounds = view.image.sprite.bounds;
            Vector2 extents = toCell.MultiplyVector(bounds.extents);
            view.PanelPivot = toCell.MultiplyPoint3x4(bounds.center);
            view.PanelHalf = new Vector2(Mathf.Abs(extents.x), Mathf.Abs(extents.y));
        }
        return view;
    }
    public void ApplyStyle(WaveCellStyle style)
    {
        generated = style.generated;
        settled = false;
        Connected = false;
        if (image == null) return;
        if (StyleConnects(style))
        {
            // 스타일 조건부 연결 그림(공장형 기본 지형, 스타일 Tile = GreenP): 그림·재질은 이 Prefab의 이어진 그림 세트, 색·정렬은 스타일 값이다.
            // 그림은 칸 하나 = 1 Unit이라 배율·위치는 칸 그대로다. 이웃 조합·강판 변형은 WaveCellTerrain이 고르고, 그 전에는 지난 그림(처음은 외딴 칸)을 쓴다.
            // 덩어리 외곽선·패널 테두리와 정렬 층·순서·렌더 큐가 같으므로, 그림만 카메라에서 아주 조금 멀리 두어 선이 늘 위에 그려지게 한다
            // (2D 기본 투명 정렬은 직교 카메라에서 앞뒤 거리 순). 바깥 변 투명 여백과 함께 써서 이어진 변 위의 패널 테두리도 가리지 않는다.
            Connected = true;
            image.sprite = connectionSprite != null ? connectionSprite : ConnectedSprite(0);
            image.color = style.color;
            image.sharedMaterial = connectedMaterial != null ? connectedMaterial : style.material;
            image.sortingLayerID = style.sortingLayer;
            image.sortingOrder = style.sortingOrder;
            image.transform.localPosition = new Vector3(0f, 0f, ConnectedDepth);
            image.transform.localRotation = Quaternion.identity;
            image.transform.localScale = Vector3.one;
            tileRenderer = image;
            baseColor = image.color;
            SetOutline(0);
            return;
        }
        if (ownVisual)
        {
            // 전용 그림을 쓰는 셀은 등장 섬광(알파)만 받는다.
            tileRenderer = image;
            baseColor = image.color;
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
        baseColor = image.color;
        SetOutline(0);
    }
    public void SetOutline(int mask)
    {
        if (kind != WaveCellKind.Platform || edges == null) return;
        for (int i = 0; i < edges.Length; i++) edges[i].enabled = (mask & (1 << i)) != 0;
    }
    // 패널 연출 중 아직 나오지 않았거나 이미 수납된 칸을 숨긴다. SetAmount가 다시 보이게 한다.
    public void SetVisible(bool value)
    {
        if (!value) settled = false;
        if (image != null) image.enabled = value;
        if (!value && panelOutline) panelLine.enabled = false;
    }
    // 패널 연출: 기계팔이 든 한 칸의 자세. 칸 중심 위치·기울기·크기와 밝기(깊이 들어갈수록 어둡다)를 바꾼다.
    public void SetPanelPose(Vector3 localPosition, float angle, float scale, float brightness)
    {
        scale = Mathf.Max(.001f, scale);
        settled = false;
        transform.localPosition = localPosition;
        transform.localRotation = Quaternion.Euler(0, 0, angle);
        transform.localScale = new Vector3(scale, scale, 1);
        if (image == null) return;
        image.enabled = true;
        image.color = new Color(baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness, baseColor.a);
    }
    // 패널로 움직이는 동안의 한 칸 테두리. 고정된 뒤 덩어리 외곽선으로 바뀌어도 선이 그대로 이어지도록 같은 선을 한 칸 크기로 쓴다.
    // 일반 지형은 지형 선의 재질·안쪽 위치·굵기를 받고, 생성 외형은 자기 발광선을, OneWay는 윗면 선을 그대로 쓴다.
    public void ShowPanelOutline(Material material, float inset, float width, int sortingOrder)
    {
        if (edges == null || edges.Length == 0) return;
        if (panelOutline) HidePanelOutline();
        settled = false;
        if (kind == WaveCellKind.OneWayPlatform)
        {
            var surface = SurfaceLine;
            if (surface == null) return;
            panelLine = surface; panelLineWidth = surface.widthMultiplier;
            surface.positionCount = 2; surface.loop = false;
            surface.SetPosition(0, new Vector3(-.5f, .5f)); surface.SetPosition(1, new Vector3(.5f, .5f));
        }
        else
        {
            var line = edges[0];
            // 덩어리 외곽선으로 빌려 갔던 선이면 이 칸 아래로 되돌린다.
            if (line.transform.parent != transform) line.transform.SetParent(transform, false);
            line.transform.localPosition = Vector3.zero; line.transform.localRotation = Quaternion.identity; line.transform.localScale = Vector3.one;
            if (generated) { inset = 0f; width = outlineWidth; }
            else { if (material != null) line.sharedMaterial = material; line.sortingOrder = sortingOrder; }
            float half = .5f - inset;
            line.positionCount = 4; line.loop = true;
            line.SetPosition(0, new Vector3(-half, -half)); line.SetPosition(1, new Vector3(half, -half));
            line.SetPosition(2, new Vector3(half, half)); line.SetPosition(3, new Vector3(-half, half));
            panelLine = line; panelLineWidth = width;
        }
        panelOutline = true;
    }
    // amount: 테두리 굵기 배율(0 = 숨김, 1 = 제 굵기).
    public void SetPanelOutline(float amount)
    {
        if (!panelOutline) return;
        panelLine.widthMultiplier = panelLineWidth * amount;
        panelLine.enabled = amount > .0001f && (image == null || image.enabled);
    }
    public void HidePanelOutline()
    {
        if (!panelOutline) return;
        panelOutline = false;
        panelLine.widthMultiplier = panelLineWidth;
        panelLine.enabled = false;
    }
    public void SetAmount(float amount)
    {
        if (settled && amount == 1f) return;
        transform.localScale = new Vector3(1, Mathf.Max(.001f, amount), 1);
        transform.localRotation = Quaternion.identity;
        if (image != null) { image.enabled = true; image.color = baseColor; }
        SetAlpha(1);
        if (kind == WaveCellKind.Platform && edges != null) foreach (var edge in edges) edge.widthMultiplier = !generated && WaveRoomRulesSO.Default != null ? WaveRoomRulesSO.Default.terrainLineWidth : outlineWidth;
        settled = amount == 1f;
    }
    public void SetProgress(float progress, bool add, float elapsed = -1)
    {
        if (!generated && kind == WaveCellKind.Platform) { SetAmount(Mathf.SmoothStep(0, 1, add ? progress : 1 - progress)); return; }
        settled = false;
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
        properties.SetColor(ColorId, new Color(1, 1, 1, alpha));
        tileRenderer.SetPropertyBlock(properties);
    }
}
