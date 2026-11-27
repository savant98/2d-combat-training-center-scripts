using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 배경 선택은 카메라·통창·지형·진행 오브젝트와 별도로 적용한다. null은 원본 유지다.
// 던전 정적 벽면 방(IDungeonRoomBackdrop)은 그 위에 '방 덮어쓰기' 한 겹을 더한다: 방 덮어쓰기(벽면 키트) > Wave 배경 > 시차 원본.
// 덮어쓰기는 DungeonRoomStage가 셀을 바꾼 직후(관 통과 덮개 아래) 켜고, ApplyWave(다음 Wave 준비·초기화)가 스스로 푼다.
// 덮어쓰기를 한 번도 쓰지 않으면(던전이 아닌 훈련) 지금까지와 같은 경로만 탄다.
public sealed class WaveRoomBackground : MonoBehaviour, IDungeonRoomBackdrop
{
    private readonly Dictionary<Renderer, bool> originalRenderers = new();
    private readonly Dictionary<Renderer, bool> observationGlass = new();
    // 통창 전체 숨김(WaveDataSO.hideObservationWindow)에 쓰는 통창 루트와 Room 원본의 켜짐 상태.
    private GameObject observationWindow;
    private bool observationWindowActive;
    private WaveDataSO data;
    private GameObject selectedPrefab;
    private GameObject instance;
    private Vector3 originalPosition, originalScale;
    private Bounds sourceBounds;
    private Rect roomBounds;
    private bool fitToRoom, hasSourceBounds, hasRoomBounds;
    // 방 덮어쓰기(정적 벽면 키트): 키트 Prefab마다 한 번 만든 인스턴스를 꺼 두었다가 다시 쓴다(방을 오갈 때 새로 만들지 않는다).
    private readonly Dictionary<GameObject, RoomWallBackdrop> roomWalls = new();
    private RoomWallBackdrop roomWall;
    private Transform roomWallStaging;
    // 덮어쓰기가 Wave 배경 인스턴스를 꺼 두었는지(풀 때 이것만 다시 켠다).
    private bool roomHidInstance;
    public GameObject Instance => instance;
    /// <summary>지금 방 덮어쓰기로 켠 벽면 키트(없으면 null).</summary>
    public RoomWallBackdrop RoomWall => roomWall;
    public bool HasRoomOverride => roomWall != null;
    public GameObject SelectedPrefab => selectedPrefab;
    public bool FitsRoom => fitToRoom;

    /// <summary>Play 중 통창 비교용(저장하지 않는다). null이면 Wave 데이터(hideObservationWindow)·벽면 방 규칙을 따르고, false면 시차 배경 방에서도 통창을 통째로 숨긴다.</summary>
    public static bool? ObservationWindowOverride { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOverride() => ObservationWindowOverride = null;

    /// <summary>통창 비교 값을 바꾸고 지금 켜진 모든 방 배경에 바로 반영한다.</summary>
    public static void SetObservationWindowOverride(bool? value)
    {
        ObservationWindowOverride = value;
        foreach (var background in FindObjectsByType<WaveRoomBackground>(FindObjectsSortMode.None)) background.RefreshRoomWindow();
    }

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
        {
            observationWindow = window.gameObject;
            observationWindowActive = observationWindow.activeSelf;
            foreach (var renderer in window.GetComponentsInChildren<Renderer>(true))
                if (renderer.name == "LobbyWindow_Glass" || renderer.name == "LobbyWindow_Reflections")
                    observationGlass[renderer] = renderer.enabled;
        }
        SetRoomBounds(data.room.bounds);
        ApplyWave(0);
    }
    public void ApplyWave(int index)
    {
        if (data == null) return;
        // 던전 방 덮어쓰기는 Wave 준비·초기화에서 푼다(다음 Wave·재도전은 시차 배경 위에서 시작한다). 덮어쓰기가 없으면 아무것도 하지 않는다.
        ClearRoom();
        Apply(data.BackgroundAt(index), data.FitBackgroundAt(index));
        // 유리·반사만 선택적으로 숨긴다. 프레임·케이블과 통창의 공간 크기는 그대로 유지한다.
        foreach (var row in observationGlass)
            if (row.Key != null) row.Key.enabled = data.ShowObservationGlassAt(index) && row.Value;
        // 통창 전체 숨김: 프레임·테두리 빛·케이블까지 통째로 끈다(개체를 꺼서 케이블 흐름 같은 갱신도 돌지 않는다).
        // 통창의 위치·크기는 WaveRoomBackdrop이 꺼진 동안에도 맞춰 두고, 그동안 건너뛴 둘레 케이블은 다시 켤 때 맞춘다.
        if (observationWindow != null)
        {
            bool show = observationWindowActive && (ObservationWindowOverride ?? !data.hideObservationWindow);
            if (observationWindow.activeSelf != show)
            {
                observationWindow.SetActive(show);
                if (show) GetComponent<WaveRoomBackdrop>()?.RefreshWindow();
            }
        }
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
        roomHidInstance = false;
        if (prefab != null)
        {
            instance = Instantiate(prefab, transform, false);
            instance.name = "WaveBackground_" + prefab.name;
            originalPosition = instance.transform.localPosition;
            originalScale = instance.transform.localScale;
            MeasureSource();
            Fit();
            // 방 덮어쓰기 중에 바뀐 Wave 배경은 덮어쓰기를 풀 때 보인다.
            if (roomWall != null) { instance.SetActive(false); roomHidInstance = true; }
        }
    }

    /// <summary>
    /// 정적 벽면 방: 시차 배경을 감추고(WaveRoomBackdrop.SetSuppressed), 통창을 통째로 숨기고, 키트 인스턴스(Prefab마다 하나, 재사용)를 실내에 맞춰 켠다.
    /// 키트 Prefab이 없으면 ClearRoom과 같다. 실내는 지금 연결 공간(SetRoomBounds — ShowRoom이 방금 맞춘 값)을 우선하고, 아직 없으면 요청의 bounds를 쓴다.
    /// 뒤이어 연결 공간이 바뀌면(SetRoomBounds) 키트가 LateUpdate에서 다시 맞춘다.
    /// </summary>
    public void ApplyRoom(DungeonRoomBackdropRequest request)
    {
        var prefab = request.kitPrefab;
        if (prefab == null) { ClearRoom(); return; }
        var wall = RoomWallFor(prefab);
        if (wall == null) { ClearRoom(); return; }
        if (roomWall != null && roomWall != wall) roomWall.gameObject.SetActive(false);
        roomWall = wall;
        var node = wall.transform;
        node.localPosition = Vector3.zero;
        node.localRotation = Quaternion.identity;
        node.localScale = Vector3.one;
        // 켜기 전에 맞춘다(입자·케이블·설비가 첫 프레임부터 이 방 크기다).
        Rect interior = hasRoomBounds ? roomBounds : request.bounds;
        if (interior.width > 0 && interior.height > 0) wall.Fit(interior);
        if (!wall.gameObject.activeSelf) wall.gameObject.SetActive(true);
        GetComponent<WaveRoomBackdrop>()?.SetSuppressed(true);
        if (instance != null && instance.activeSelf) { instance.SetActive(false); roomHidInstance = true; }
        RefreshRoomWindow();
    }

    /// <summary>방 덮어쓰기를 푼다: 키트를 끄고, 시차 배경·Wave 배경·통창을 원래대로 돌린다. 덮어쓰기가 없으면 아무것도 하지 않는다.</summary>
    public void ClearRoom()
    {
        if (roomWall == null) return;
        roomWall.gameObject.SetActive(false);
        roomWall = null;
        GetComponent<WaveRoomBackdrop>()?.SetSuppressed(false);
        if (roomHidInstance && instance != null && !instance.activeSelf) instance.SetActive(true);
        roomHidInstance = false;
        RefreshRoomWindow();
    }

    /// <summary>입장 로딩에서 부를 수 있다: 키트 인스턴스를 만들어 꺼 둔 채 이 실내의 합친 Mesh를 미리 만든다(화면은 바꾸지 않는다).</summary>
    public void PrewarmRoom(DungeonRoomBackdropRequest request)
    {
        if (request.kitPrefab == null || request.bounds.width <= 0 || request.bounds.height <= 0) return;
        var wall = RoomWallFor(request.kitPrefab);
        if (wall != null) wall.Prewarm(request.bounds);
    }

    // 키트 인스턴스는 꺼진 staging 아래에서 복제한 뒤 꺼서 옮긴다(Awake·OnEnable·소리가 켜기 전에 돌지 않는다).
    private RoomWallBackdrop RoomWallFor(GameObject prefab)
    {
        if (roomWalls.TryGetValue(prefab, out var cached) && cached != null) return cached;
        if (!IsBackground(prefab) || prefab.GetComponent<RoomWallBackdrop>() == null)
        {
            Debug.LogError("방 벽면 키트에는 RoomWallBackdrop이 있어야 하고 카메라·Collider·Room·WaveManager를 포함할 수 없습니다.", prefab);
            return null;
        }
        if (roomWallStaging == null)
        {
            var staging = new GameObject("RoomWallStaging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            roomWallStaging = staging.transform;
        }
        var copy = Instantiate(prefab, roomWallStaging, false);
        // 이름에 'Backdrop'을 두지 않는다(Configure가 이름으로 원본 배경을 수집하고, 잡기 연출이 이름으로 후경을 찾는다).
        copy.name = prefab.name.IndexOf("Backdrop", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "RoomWall" : prefab.name;
        copy.SetActive(false);
        copy.transform.SetParent(transform, false);
        var wall = copy.GetComponent<RoomWallBackdrop>();
        roomWalls[prefab] = wall;
        return wall;
    }

    // 통창은 '큰 시설을 들여다보는 창'이라 정적 벽면 방에서는 통째로 숨긴다. 덮어쓰기가 없으면 ApplyWave와 같은 판정이다.
    private void RefreshRoomWindow()
    {
        if (observationWindow == null) return;
        bool show = roomWall == null && observationWindowActive && (ObservationWindowOverride ?? (data == null || !data.hideObservationWindow));
        if (observationWindow.activeSelf == show) return;
        observationWindow.SetActive(show);
        if (show) GetComponent<WaveRoomBackdrop>()?.RefreshWindow();
    }

    // 카메라·통창과 같은 연결 공간을 사용한다. 원본 배경 에셋의 Tile·Material·정렬 순서는 바꾸지 않는다.
    public void SetRoomBounds(Rect bounds)
    {
        if (bounds.width <= 0 || bounds.height <= 0) return;
        roomBounds = bounds;
        hasRoomBounds = true;
        Fit();
        // 덮어쓰기 중이면 벽면도 이 공간에 맞춘다. LateUpdate로 미뤄 같은 프레임에 덮어쓰기가 풀리면(다음 Wave 준비) 쓸데없이 합치지 않는다.
        if (roomWall != null) roomWall.RequestFit(bounds);
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
