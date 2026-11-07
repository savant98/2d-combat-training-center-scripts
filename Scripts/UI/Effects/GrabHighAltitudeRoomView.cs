using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 원래 룸 카메라가 높은 곳을 비출 때 실제 후경을 이어 붙인다. UI·물리·공유 RT는 변경하지 않는다.
public sealed class GrabHighAltitudeRoomView : IDisposable
{
    private readonly Camera source;
    private readonly Transform backgroundOrigin;
    private readonly Vector2 fallbackOrigin;
    private readonly List<Renderer> ceilings = new List<Renderer>();
    private readonly List<Renderer> exterior = new List<Renderer>();
    private readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
    private GameObject root;
    private Mesh mesh;
    private Material material;
    private MeshRenderer renderer;
    private bool disposed;
    public float Altitude { get; private set; }
    public float Opacity { get; private set; }
    public int CeilingCount => ceilings.Count;
    public int ExteriorCount => exterior.Count;
    public int HiddenCeilingCount => hidden.Count;
    public bool IsReady => renderer != null && material != null;
    public bool IsVisible => IsReady && Opacity > .0001f;

    public GrabHighAltitudeRoomView(Camera source, GrabCinematicBackdropView backdrop, Bounds roomBounds, Vector2 landingFloor, Material template)
    {
        this.source = source;
        fallbackOrigin = new Vector2(roomBounds.center.x, landingFloor.y + 10);
        if (source == null || backdrop == null || backdrop.FarTexture == null) return;
        if (template == null) { Debug.LogError("잡기 고도 후경 Material이 연결되지 않았습니다."); return; }
        var original = GameObject.Find(backdrop.SourceName);
        if (original != null) backgroundOrigin = original.transform;
        root = new GameObject("GrabHighAltitudeRoomExtension");
        root.layer = VisibleLayer(source.cullingMask);
        mesh = new Mesh { name = "GrabHighAltitudeRoomQuad" };
        mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; mesh.RecalculateBounds();
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        renderer = root.AddComponent<MeshRenderer>(); renderer.sortingOrder = -30000;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        material = new Material(template) { name = "GrabHighAltitudeRoomMaterial" };
        material.SetTexture("_FarTex", backdrop.FarTexture);
        material.SetTexture("_MidTex", backdrop.MiddleTexture);
        material.SetTexture("_NearTex", backdrop.NearTexture);
        // RT 가운데 시설 부분만 연장해 원본 방의 바닥·천장 끝이 반복되지 않게 한다.
        material.SetVector("_VerticalCrop", new Vector4(.2f, .8f, 0, 0));
        material.SetColor("_Background", source.backgroundColor);
        renderer.sharedMaterial = material; renderer.enabled = false;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (candidate == renderer || !candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;
            var bounds = candidate.bounds;
            if (bounds.max.x < roomBounds.min.x || bounds.min.x > roomBounds.max.x) continue;
            // 다른 방은 보통 비활성화되지만 공용 벽·설비가 남아 있어도 고도 연출에 끼어들지 않게 한다.
            if (bounds.min.y > roomBounds.max.y + .05f) { exterior.Add(candidate); continue; }
            if (!HasCeilingName(candidate.transform) || bounds.max.y < roomBounds.center.y
                || bounds.min.y > roomBounds.max.y + 2) continue;
            ceilings.Add(candidate);
        }
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }
    public void Update(float altitude, float opacity)
    {
        if (disposed) return;
        Altitude = Mathf.Max(0, altitude); Opacity = Mathf.Clamp01(opacity);
        SyncView();
        if (renderer != null) renderer.enabled = false;
    }
    private void SyncView()
    {
        if (source == null || renderer == null) return;
        renderer.enabled = Opacity > .0001f;
        if (!renderer.enabled) return;
        float distance = Mathf.Max(source.nearClipPlane + .1f, Mathf.Min(200, source.farClipPlane - 1));
        float halfHeight = source.orthographic ? source.orthographicSize
            : Mathf.Tan(source.fieldOfView * .5f * Mathf.Deg2Rad) * distance;
        root.transform.SetPositionAndRotation(source.transform.position + source.transform.forward * distance, source.transform.rotation);
        root.transform.localScale = new Vector3(halfHeight * source.aspect * 2.01f, halfHeight * 2.01f, 1);
        Vector2 origin = backgroundOrigin != null ? (Vector2)backgroundOrigin.position : fallbackOrigin;
        Vector2 offset = ((Vector2)source.transform.position - origin) / 20;
        material.SetVector("_CameraOffset", new Vector4(offset.x, offset.y, 0, 0));
        material.SetFloat("_ViewScale", halfHeight / 10);
        material.SetFloat("_Aspect", source.aspect);
        material.SetFloat("_Opacity", Opacity);
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != source || disposed) return;
        // Cinemachine·PixelPerfect가 마지막에 조정한 실제 MainCamera에 맞춘다.
        SyncView(); RestoreCeilings();
        if (Altitude <= .001f && Opacity <= .001f) return;
        foreach (var ceiling in ceilings)
        {
            if (ceiling == null) continue;
            hidden[ceiling] = ceiling.forceRenderingOff; ceiling.forceRenderingOff = true;
        }
        foreach (var otherRoom in exterior)
        {
            if (otherRoom == null) continue;
            hidden[otherRoom] = otherRoom.forceRenderingOff; otherRoom.forceRenderingOff = true;
        }
    }
    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != source) return;
        RestoreCeilings(); if (renderer != null) renderer.enabled = false;
    }
    private void RestoreCeilings()
    {
        foreach (var pair in hidden) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
        hidden.Clear();
    }
    private static bool HasCeilingName(Transform transform)
    {
        for (int depth = 0; transform != null && depth < 4; depth++, transform = transform.parent)
            if (transform.name.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
    private static int VisibleLayer(int mask)
    {
        if ((mask & 1) != 0) return 0;
        for (int layer = 1; layer < 32; layer++) if ((mask & (1 << layer)) != 0) return layer;
        return 0;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        RestoreCeilings();
        if (renderer != null) renderer.enabled = false;
        if (root != null) UnityEngine.Object.Destroy(root);
        if (mesh != null) UnityEngine.Object.Destroy(mesh);
        if (material != null) UnityEngine.Object.Destroy(material);
        root = null; mesh = null; material = null; renderer = null;
    }
}
