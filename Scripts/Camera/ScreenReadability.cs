using UnityEngine;

// 방 화면 가독성 처리(Hollow Knight·Ori의 깊이 안개·주의 예산 방식). 리소스(Sprite·Material·정렬 순서)는 바꾸지 않고 실행 중에만
//  ① 배경 층(Default 정렬 층 음수 순서)과 플레이 층(발판 0 이상·Prop·적·Player) 사이에 화면을 덮는 반투명 안개 판을 끼워
//     배경의 명도 범위·채도·발광을 압축한다. 발판·Prop·적·Player·바닥 음영은 그대로라 배경과의 명도 차가 커진다.
//     안개 색은 배경을 플레이 층의 명도로 다가가게 하지 않는다. 기본은 어두운 안개, 발판보다 밝은 배경 Prefab은 설정의 배경별 안개(배경 중앙 명도의 무채색 안개)를 쓴다.
//  ② 처리가 켜진 방에서는 멀리 있는 상호작용 시설의 깜박임 최고치·빛 점을 줄인다(FacilityHighlight가 FacilityCalm을 묻는다).
// 적용 범위는 ScreenReadabilitySO(기본 모든 방 종류 — Lobby·던전·일반 Wave 방·자율훈련장). 안개 판은 카메라 계산(FixedMotionPresentation 99의 brain 갱신·RoomViewRules)이 끝난 뒤
// 가장 늦은 LateUpdate에서 화면에 맞춘다. 같은 정렬 순서 안에서는 카메라에 가장 가까워 가장 마지막에 그려진다(2D 정렬 기본 = 거리 순).
[DefaultExecutionOrder(32000)]
public sealed class ScreenReadability : MonoBehaviour
{
    private static ScreenReadability instance;
    private static bool active;
    private static bool? runtimeOverride;
    private static Vector2 playerPosition;
    private static GameObject activeBackdrop;

    private SpriteRenderer[] veils = System.Array.Empty<SpriteRenderer>();
    private Sprite veilSprite;
    private Material veilMaterial;
    private Room lastRoom;
    private DungeonManager dungeon;
    private TrainingRoomHost host;

    /// <summary>지금 방에 가독성 처리가 켜져 있는지.</summary>
    public static bool Active => active;

    /// <summary>지금 안개 층을 고른 배경 Prefab(배경별 안개를 쓰는 중일 때만, 기본 층이면 null).</summary>
    public static GameObject ActiveBackdrop => activeBackdrop;

    /// <summary>Play 중 비교용 켜기·끄기(저장하지 않는다). null이면 설정 에셋 값을 따른다.</summary>
    public static bool? RuntimeOverride
    {
        get => runtimeOverride;
        set { runtimeOverride = value; if (instance != null) instance.Refresh(); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        active = false;
        runtimeOverride = null;
        playerPosition = Vector2.zero;
        activeBackdrop = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null || ScreenReadabilitySO.Default == null) return;
        var node = new GameObject("ScreenReadability");
        DontDestroyOnLoad(node);
        instance = node.AddComponent<ScreenReadability>();
    }

    /// <summary>처리가 켜진 방에서 Player와 먼 시설일수록 1에 가까운 값(0 = 강조 그대로).</summary>
    public static float FacilityCalm(Vector3 position)
    {
        if (!active) return 0f;
        var settings = ScreenReadabilitySO.Default;
        if (settings == null) return 0f;
        float distance = Vector2.Distance(position, playerPosition);
        float t = Mathf.InverseLerp(settings.facilityNoticeNear, Mathf.Max(settings.facilityNoticeNear + .01f, settings.facilityNoticeFar), distance);
        return t * t * (3f - 2f * t);
    }

    /// <summary>멀리 있는 '쓸 수 있음' 시설의 깜박임 최고 세기.</summary>
    public static float DistantBlinkPeak => ScreenReadabilitySO.Default != null ? ScreenReadabilitySO.Default.distantBlinkPeak : 2.9f;

    /// <summary>멀리 있는 시설의 테두리 빛 점 세기 배율.</summary>
    public static float DistantCometScale => ScreenReadabilitySO.Default != null ? ScreenReadabilitySO.Default.distantCometScale : 1f;

    /// <summary>지금 상태를 바로 다시 계산해 안개 판에 반영한다(같은 프레임 비교·검증용).</summary>
    public static void RefreshNow()
    {
        if (instance != null) instance.Refresh();
    }

    private void LateUpdate() => Refresh();

    private void Refresh()
    {
        var settings = ScreenReadabilitySO.Default;
        var camera = Camera.main;
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player != null) playerPosition = player.transform.position;
        bool enabledByUser = runtimeOverride ?? (settings != null && settings.readabilityEnabled);
        active = enabledByUser && settings != null && camera != null && camera.orthographic && InScope(settings);
        var layers = System.Array.Empty<ScreenReadabilitySO.HazeLayer>();
        activeBackdrop = null;
        if (active)
        {
            var backdrop = CurrentBackdrop();
            layers = settings.LayersFor(backdrop) ?? layers;
            if (backdrop != null && layers != settings.hazeLayers) activeBackdrop = backdrop;
        }
        UpdateVeils(layers, camera);
    }

    // 지금 방에 켜진 배경 Prefab(배경별 안개를 고르는 열쇠). 던전 벽면 키트(방 덮어쓰기)는 null → 기본 층.
    // Wave별 배경(backgroundPrefab, WaveRoomBackground.SelectedPrefab) > 방 배경 교체(WaveDataSO.roomBackdrop) 순서다. Lobby 같은 Scene 배경은 null.
    private GameObject CurrentBackdrop()
    {
        if (lastRoom == null) return null;
        var content = host != null && host.Content != null ? host.Content.gameObject : lastRoom.gameObject;
        var background = content.GetComponent<WaveRoomBackground>();
        if (background != null)
        {
            if (background.HasRoomOverride) return null;
            if (background.SelectedPrefab != null) return background.SelectedPrefab;
        }
        return host != null && host.Content != null ? host.ContentBackdrop : null;
    }

    // 적용 범위: 현재 방의 종류가 roomTypes에 있으면 켠다. 검수 템플릿 목록이 있으면 던전 방은 그 템플릿 방에서만, 일반 Wave 방은 켜지 않는다.
    private bool InScope(ScreenReadabilitySO settings)
    {
        var room = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
        if (room == null) return false;
        if (room != lastRoom)
        {
            lastRoom = room;
            dungeon = room.WaveManager != null ? room.WaveManager.GetComponent<DungeonManager>() : null;
            host = room.GetComponent<TrainingRoomHost>();
        }
        if (settings.roomTypes == null || System.Array.IndexOf(settings.roomTypes, room.roomType) < 0) return false;
        bool limited = false;
        foreach (var review in settings.reviewTemplates) if (review != null) { limited = true; break; }
        if (!limited) return true;
        if (dungeon == null || !dungeon.HasFloor || dungeon.Floor == null) return false;
        var template = CurrentTemplate(dungeon);
        if (template == null) return false;
        foreach (var review in settings.reviewTemplates)
            if (review != null && (review == template || review.name == template.name)) return true;
        return false;
    }

    private static DungeonRoomTemplateSO CurrentTemplate(DungeonManager manager)
    {
        int id = manager.CurrentRoom;
        foreach (var data in manager.Floor.rooms)
            if (data != null && data.Id == id) return data.node != null ? data.node.template : null;
        return null;
    }

    private void UpdateVeils(ScreenReadabilitySO.HazeLayer[] layers, Camera camera)
    {
        int count = active ? layers.Length : 0;
        if (count > veils.Length) Build(count);
        for (int i = 0; i < veils.Length; i++)
        {
            var veil = veils[i];
            if (veil == null) continue;
            bool show = i < count && layers[i].alpha > 0f;
            if (!show) { if (veil.enabled) veil.enabled = false; continue; }
            var layer = layers[i];
            veil.sortingLayerID = SortingLayer.NameToID("Default");
            veil.sortingOrder = layer.sortingOrder;
            veil.color = new Color(layer.color.r, layer.color.g, layer.color.b, layer.alpha);
            // 화면 전체(여유 8%)를 덮고, 같은 정렬 순서 안에서는 가장 마지막에 그려지게 카메라 바로 앞에 둔다.
            float height = camera.orthographicSize * 2f * 1.08f, width = height * camera.aspect;
            Vector3 c = camera.transform.position;
            veil.transform.SetPositionAndRotation(new Vector3(c.x, c.y, c.z + camera.nearClipPlane + .5f + .01f * i), Quaternion.identity);
            veil.transform.localScale = new Vector3(width, height, 1f);
            if (!veil.enabled) veil.enabled = true;
        }
    }

    private void Build(int count)
    {
        if (veilSprite == null)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "ScreenReadabilityVeil", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            veilSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4f);
            veilSprite.hideFlags = HideFlags.HideAndDontSave;
        }
        if (veilMaterial == null)
        {
            var shader = Shader.Find("Sprites/Default");
            veilMaterial = new Material(shader) { name = "ScreenReadabilityVeil", hideFlags = HideFlags.HideAndDontSave };
        }
        var list = new System.Collections.Generic.List<SpriteRenderer>(veils);
        while (list.Count < count)
        {
            var node = new GameObject("HazeVeil_" + list.Count);
            node.transform.SetParent(transform, false);
            var renderer = node.AddComponent<SpriteRenderer>();
            renderer.sprite = veilSprite;
            renderer.sharedMaterial = veilMaterial;
            renderer.enabled = false;
            list.Add(renderer);
        }
        veils = list.ToArray();
    }

    private void OnDisable()
    {
        active = false;
        foreach (var veil in veils) if (veil != null) veil.enabled = false;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (veilMaterial != null) Destroy(veilMaterial);
        if (veilSprite != null) { Destroy(veilSprite.texture); Destroy(veilSprite); }
    }
}
