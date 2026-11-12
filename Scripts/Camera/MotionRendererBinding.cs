using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

// 물리 요소가 없는 가지는 렌더 때만 보정한다. 그림과 Collider가 같은 노드이면 그림만 복제한다.
internal sealed class MotionRendererBinding : IDisposable
{
    private readonly Transform node, owner;
    private readonly Renderer source, proxy;
    private readonly MaterialPropertyBlock properties = new();
    private readonly List<Material> materials = new();
    private Vector3 savedPosition, savedScale;
    private Quaternion savedRotation;
    private bool applied, savedHidden;
    private Tilemap originalTiles, displayedTiles;
    private readonly TrailRenderer worldTrail;
    private Vector3 savedTrailTip;
    private int trailCount;
    private readonly HashSet<Vector3Int> dirtyTiles = new();

    private MotionRendererBinding(Transform visual, Transform root) { node = visual; owner = root; }
    private MotionRendererBinding(TrailRenderer trail, Transform root) { node = trail.transform; owner = root; worldTrail = trail; }
    private MotionRendererBinding(Renderer renderer, Transform root)
    {
        source = renderer; owner = root;
        var clone = new GameObject("__MotionPresentation_" + renderer.GetType().Name);
        clone.transform.SetParent(renderer.transform, false);
        clone.hideFlags = HideFlags.DontSave;
        node = clone.transform;
        if (renderer is SpriteRenderer) proxy = clone.AddComponent<SpriteRenderer>();
        else if (renderer is MeshRenderer)
        {
            clone.AddComponent<MeshFilter>();
            proxy = clone.AddComponent<MeshRenderer>();
        }
        else if (renderer is TilemapRenderer)
        {
            originalTiles = renderer.GetComponent<Tilemap>();
            displayedTiles = clone.AddComponent<Tilemap>();
            proxy = clone.AddComponent<TilemapRenderer>();
            foreach (var cell in originalTiles.cellBounds.allPositionsWithin) CopyTile(cell);
            Tilemap.tilemapTileChanged += TilesChanged;
        }
        proxy.forceRenderingOff = true;
    }
    internal static void Collect(FixedMotionBody body, List<MotionRendererBinding> result)
    {
        var root = body.transform;
        var physics = new HashSet<Transform> { root };
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (!(component is Collider2D) && !(component is Rigidbody2D) && !(component is FixedMotionBody && component.transform != root)) continue;
            for (var node = component.transform; node != null && node.IsChildOf(root); node = node.parent) physics.Add(node);
        }
        var branches = new HashSet<Transform>();
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer.name.StartsWith("__MotionPresentation_", StringComparison.Ordinal)) continue;
            if (renderer.GetComponentInParent<FixedMotionBody>() != body) continue;
            if (renderer is TrailRenderer trail) { result.Add(new MotionRendererBinding(trail, root)); continue; }
            var node = renderer.transform;
            if (physics.Contains(node))
            {
                if (renderer is SpriteRenderer || renderer is MeshRenderer || renderer is TilemapRenderer)
                    result.Add(new MotionRendererBinding(renderer, root));
                continue;
            }
            while (node.parent != null && node.parent != root && !physics.Contains(node.parent)) node = node.parent;
            branches.Add(node);
        }
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.renderMode != RenderMode.WorldSpace || canvas.GetComponentInParent<FixedMotionBody>() != body || physics.Contains(canvas.transform)) continue;
            branches.Add(canvas.transform);
        }
        foreach (var node in branches)
            if (!branches.Any(other => node != other && node.IsChildOf(other))) result.Add(new MotionRendererBinding(node, root));
    }
    private void CopyTile(Vector3Int cell)
    {
        displayedTiles.SetTile(cell, originalTiles.GetTile(cell));
        displayedTiles.SetTileFlags(cell, TileFlags.None);
        displayedTiles.SetColor(cell, originalTiles.GetColor(cell));
        displayedTiles.SetTransformMatrix(cell, originalTiles.GetTransformMatrix(cell));
    }
    private void TilesChanged(Tilemap map, Tilemap.SyncTile[] changes)
    {
        if (map == originalTiles) foreach (var tile in changes) dirtyTiles.Add(tile.position);
    }
    internal void Apply(Matrix4x4 delta, Quaternion rotation)
    {
        if (node == null || owner == null || !node.IsChildOf(owner)) return;
        if (worldTrail != null)
        {
            trailCount = worldTrail.positionCount;
            if (trailCount == 0) return;
            savedTrailTip = worldTrail.GetPosition(trailCount - 1);
            // 지난 경로는 유지하고 최신 끝점만 물체의 표시 위치에 맞춘다. 원래 점과 수명은 렌더 뒤 복구한다.
            worldTrail.SetPosition(trailCount - 1, delta.MultiplyPoint3x4(node.position));
            applied = true;
            return;
        }
        if (source != null)
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || source.forceRenderingOff) return;
            savedHidden = source.forceRenderingOff;
            SyncRenderer();
            source.forceRenderingOff = true;
            proxy.forceRenderingOff = false;
        }
        savedPosition = node.localPosition; savedRotation = node.localRotation; savedScale = node.localScale;
        Vector3 position = node.position;
        var horizontal = delta.MultiplyVector(node.right).magnitude;
        var vertical = delta.MultiplyVector(node.up).magnitude;
        var depth = delta.MultiplyVector(node.forward).magnitude;
        // 방향 반전과 Animator의 자식 자세는 유지하고 root의 물리 차이만 적용한다.
        node.SetPositionAndRotation(delta.MultiplyPoint3x4(position), rotation * node.rotation);
        node.localScale = Vector3.Scale(savedScale, new Vector3(horizontal, vertical, depth));
        applied = true;
    }
    private void SyncRenderer()
    {
        proxy.gameObject.layer = source.gameObject.layer;
        source.GetSharedMaterials(materials); proxy.SetSharedMaterials(materials);
        proxy.sortingLayerID = source.sortingLayerID; proxy.sortingOrder = source.sortingOrder;
        proxy.renderingLayerMask = source.renderingLayerMask;
        proxy.shadowCastingMode = source.shadowCastingMode; proxy.receiveShadows = source.receiveShadows;
        source.GetPropertyBlock(properties); proxy.SetPropertyBlock(properties);
        if (source is SpriteRenderer sprite && proxy is SpriteRenderer copy)
        {
            copy.sprite = sprite.sprite; copy.color = sprite.color;
            copy.flipX = sprite.flipX; copy.flipY = sprite.flipY;
            copy.drawMode = sprite.drawMode; copy.size = sprite.size;
            copy.tileMode = sprite.tileMode; copy.adaptiveModeThreshold = sprite.adaptiveModeThreshold;
            copy.maskInteraction = sprite.maskInteraction; copy.spriteSortPoint = sprite.spriteSortPoint;
        }
        else if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var mesh))
            proxy.GetComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
        else if (source is TilemapRenderer tiles && proxy is TilemapRenderer shown)
        {
            displayedTiles.color = originalTiles.color; displayedTiles.tileAnchor = originalTiles.tileAnchor;
            displayedTiles.orientation = originalTiles.orientation; displayedTiles.orientationMatrix = originalTiles.orientationMatrix;
            displayedTiles.animationFrameRate = originalTiles.animationFrameRate;
            shown.mode = tiles.mode; shown.sortOrder = tiles.sortOrder; shown.maskInteraction = tiles.maskInteraction;
            foreach (var cell in dirtyTiles) CopyTile(cell);
            dirtyTiles.Clear();
        }
    }
    internal void Restore()
    {
        if (!applied) return;
        applied = false;
        if (worldTrail != null)
        {
            if (worldTrail.positionCount >= trailCount) worldTrail.SetPosition(trailCount - 1, savedTrailTip);
            return;
        }
        if (node != null) { node.SetLocalPositionAndRotation(savedPosition, savedRotation); node.localScale = savedScale; }
        if (source != null) source.forceRenderingOff = savedHidden;
        if (proxy != null) proxy.forceRenderingOff = true;
    }
    public void Dispose()
    {
        Restore();
        Tilemap.tilemapTileChanged -= TilesChanged;
        if (proxy != null) UnityEngine.Object.Destroy(proxy.gameObject);
    }
}
