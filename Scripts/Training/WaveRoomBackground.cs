using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 배경 선택은 카메라·통창·지형·진행 오브젝트와 별도로 적용한다. null은 원본 유지다.
public sealed class WaveRoomBackground : MonoBehaviour
{
    private readonly Dictionary<Renderer, bool> originalRenderers = new();
    private readonly Dictionary<Renderer, bool> observationGlass = new();
    private WaveDataSO data;
    private GameObject selectedPrefab;
    private GameObject instance;
    private Vector3 originalPosition, originalScale;
    private Bounds sourceBounds;
    private Rect roomBounds;
    private bool fitToRoom, hasSourceBounds, hasRoomBounds;
    public GameObject Instance => instance;
    public GameObject SelectedPrefab => selectedPrefab;
    public bool FitsRoom => fitToRoom;

    public static bool IsBackground(GameObject prefab)
        => prefab != null && prefab.GetComponentsInChildren<Renderer>(true).Length > 0
            && prefab.GetComponentsInChildren<Camera>(true).Length == 0
            && prefab.GetComponentsInChildren<CinemachineVirtualCameraBase>(true).Length == 0
            && prefab.GetComponentsInChildren<Collider2D>(true).Length == 0
            && prefab.GetComponentsInChildren<Room>(true).Length == 0
            && prefab.GetComponentsInChildren<WaveManager>(true).Length == 0;

    public void Configure(WaveDataSO source)
    {
        data = source;
        foreach (var map in GetComponentsInChildren<Tilemap>(true))
            if (map.GetComponent<TilemapCollider2D>() == null)
            {
                var renderer = map.GetComponent<TilemapRenderer>();
                if (renderer != null) originalRenderers[renderer] = renderer.enabled;
            }
        foreach (Transform child in transform)
            if (child.name.Contains("Backdrop") && child.name != "WaveBackdropTemplate")
                foreach (var renderer in child.GetComponentsInChildren<Renderer>(true)) originalRenderers[renderer] = renderer.enabled;
        var window = transform.Find("Furniture/TrainingObservationWindow");
        if (window != null)
            foreach (var renderer in window.GetComponentsInChildren<Renderer>(true))
                if (renderer.name == "LobbyWindow_Glass" || renderer.name == "LobbyWindow_Reflections")
                    observationGlass[renderer] = renderer.enabled;
        SetRoomBounds(data.room.bounds);
        ApplyWave(0);
    }
    public void ApplyWave(int index)
    {
        if (data == null) return;
        Apply(data.BackgroundAt(index), data.FitBackgroundAt(index));
        // 유리·반사만 선택적으로 숨긴다. 프레임·케이블과 통창의 공간 크기는 그대로 유지한다.
        foreach (var row in observationGlass)
            if (row.Key != null) row.Key.enabled = data.ShowObservationGlassAt(index) && row.Value;
    }
    public void Apply(GameObject prefab) => Apply(prefab, false);
    public void Apply(GameObject prefab, bool fit)
    {
        if (prefab != null && !IsBackground(prefab))
        {
            Debug.LogError("배경 Prefab에는 카메라·Collider·Room·WaveManager를 포함할 수 없습니다.", prefab);
            return;
        }
        GetComponent<WaveRoomBackdrop>()?.SetBackgroundVisible(prefab == null);
        fitToRoom = fit;
        if (prefab == selectedPrefab && (prefab == null || instance != null)) { Fit(); return; }
        if (instance != null)
        {
            instance.SetActive(false);
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }
        instance = null;
        hasSourceBounds = false;
        selectedPrefab = prefab;
        foreach (var row in originalRenderers) if (row.Key != null) row.Key.enabled = prefab == null && row.Value;
        if (prefab != null)
        {
            instance = Instantiate(prefab, transform, false);
            instance.name = "WaveBackground_" + prefab.name;
            originalPosition = instance.transform.localPosition;
            originalScale = instance.transform.localScale;
            MeasureSource();
            Fit();
        }
    }

    // 카메라·통창과 같은 연결 공간을 사용한다. 원본 배경 에셋의 Tile·Material·정렬 순서는 바꾸지 않는다.
    public void SetRoomBounds(Rect bounds)
    {
        if (bounds.width <= 0 || bounds.height <= 0) return;
        roomBounds = bounds;
        hasRoomBounds = true;
        Fit();
    }
    private void MeasureSource()
    {
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !VisibleInPrefab(renderer.transform)) continue;
            // 비활성 staging 안의 Tilemap Renderer 경계는 아직 비어 있을 수 있다. 저장된 Tile·Sprite에서 직접 잰다.
            if (renderer is TilemapRenderer && renderer.TryGetComponent<Tilemap>(out var map))
            {
                // 비활성 Tilemap은 아직 부모 Grid를 연결하지 않아 CellToLocal이 모든 칸을 원점으로 반환한다.
                // Grid 자체는 저장된 layout·size·gap을 이미 갖고 있으므로 staging에서도 같은 셀 위치를 얻는다.
                var grid = map.GetComponentInParent<Grid>(true);
                foreach (var cell in map.cellBounds.allPositionsWithin)
                {
                    var sprite = map.GetSprite(cell) ?? (map.GetTile(cell) as Tile)?.sprite;
                    if (sprite == null) continue;
                    Vector3 center = grid != null ? grid.CellToLocalInterpolated((Vector3)cell + map.tileAnchor) : map.GetCellCenterLocal(cell);
                    var matrix = transform.worldToLocalMatrix * map.transform.localToWorldMatrix
                        * Matrix4x4.Translate(center) * map.orientationMatrix * map.GetTransformMatrix(cell);
                    EncapsulateSource(sprite.bounds, matrix);
                }
                continue;
            }
            EncapsulateSource(renderer.localBounds, transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix);
        }
    }
    private void EncapsulateSource(Bounds local, Matrix4x4 matrix)
    {
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                        x == 0 ? local.min.x : local.max.x, y == 0 ? local.min.y : local.max.y, z == 0 ? local.min.z : local.max.z));
                    if (!hasSourceBounds) { sourceBounds = new Bounds(point, Vector3.zero); hasSourceBounds = true; }
                    else sourceBounds.Encapsulate(point);
                }
    }
    private bool VisibleInPrefab(Transform node)
    {
        for (; node != null && node != instance.transform.parent; node = node.parent)
            if (!node.gameObject.activeSelf) return false;
        return true;
    }
    private void Fit()
    {
        if (instance == null) return;
        if (!fitToRoom || !hasRoomBounds || !hasSourceBounds || sourceBounds.size.x < .001f || sourceBounds.size.y < .001f)
        {
            instance.transform.localPosition = originalPosition;
            instance.transform.localScale = originalScale;
            return;
        }
        // 실내 밖 카메라 여백과 하단 blinder 영역까지 덮는다. 가로·세로를 같은 비율로 확대해 무늬가 찌그러지지 않는다.
        Rect target = Rect.MinMaxRect(roomBounds.xMin - 1, roomBounds.yMin - 3, roomBounds.xMax + 1, roomBounds.yMax);
        float scale = Mathf.Max(target.width / sourceBounds.size.x, target.height / sourceBounds.size.y);
        Vector3 center = sourceBounds.center - originalPosition;
        instance.transform.localScale = new Vector3(originalScale.x * scale, originalScale.y * scale, originalScale.z);
        instance.transform.localPosition = new Vector3(target.center.x - center.x * scale, target.center.y - center.y * scale, originalPosition.z);
    }
}
