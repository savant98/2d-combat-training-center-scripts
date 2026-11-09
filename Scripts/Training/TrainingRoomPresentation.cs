using System;
using UnityEngine;

// 실내 치수와 외곽 마감을 맞춘다. 설비는 고정 크기로 반복하고 바닥·천장 판의 끝점만 옮긴다.
[DisallowMultipleComponent]
public sealed class TrainingRoomPresentation : MonoBehaviour
{
    [Serializable]
    private struct Surface
    {
        public MeshFilter filter;
        public bool followsCeiling;
        public bool isWindow;
    }

    [Serializable]
    private struct DepthLayer
    {
        public Transform root;
        [Range(0f, .4f)] public float parallax;
    }

    [SerializeField] private SpriteRenderer floorShadow;
    [SerializeField] private SpriteRenderer leftShadow;
    [SerializeField] private SpriteRenderer rightShadow;
    [SerializeField] private LineRenderer boundaryLine;
    [SerializeField] private Surface[] surfaces = Array.Empty<Surface>();
    [SerializeField] private DepthLayer[] depthLayers = Array.Empty<DepthLayer>();
    [SerializeField] private Transform serviceRail;
    [SerializeField] private LineRenderer windowCable;

    private Mesh[] originals;
    private Mesh[] copies;
    private Vector3[][] vertices;
    private Vector3[][] working;
    private Vector3[] surfaceHome;
    private Vector3[] depthHome;
    private Vector3 railHome;
    private Camera viewCamera;
    private Vector3[] windowCableHome;

    private void Prepare()
    {
        if (originals != null && originals.Length == surfaces.Length && depthHome.Length == depthLayers.Length) return;
        originals = new Mesh[surfaces.Length];
        copies = new Mesh[surfaces.Length];
        vertices = new Vector3[surfaces.Length][];
        working = new Vector3[surfaces.Length][];
        surfaceHome = new Vector3[surfaces.Length];
        depthHome = new Vector3[depthLayers.Length];
        for (int i = 0; i < surfaces.Length; i++)
        {
            var filter = surfaces[i].filter;
            originals[i] = filter.sharedMesh;
            copies[i] = Instantiate(originals[i]);
            copies[i].name = originals[i].name + " (EvaluationPanel)";
            copies[i].hideFlags = HideFlags.DontSave;
            vertices[i] = originals[i].vertices;
            working[i] = new Vector3[vertices[i].Length];
            surfaceHome[i] = filter.transform.localPosition;
            filter.sharedMesh = copies[i];
        }
        for (int i = 0; i < depthLayers.Length; i++) depthHome[i] = depthLayers[i].root.localPosition;
        railHome = serviceRail.localPosition;
        if (windowCable != null)
        {
            windowCableHome = new Vector3[windowCable.positionCount];
            windowCable.GetPositions(windowCableHome);
        }
    }

    public void Apply(float halfWidth, float height)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        Prepare();
        ResizeShadow(floorShadow, new Vector3(0, -1.5f, 0), new Vector2(halfWidth * 2f + 2f, 3f));
        ResizeShadow(leftShadow, new Vector3(-halfWidth - .5f, (height - 3f) * .5f, 0), new Vector2(1f, height + 3f));
        ResizeShadow(rightShadow, new Vector3(halfWidth + .5f, (height - 3f) * .5f, 0), new Vector2(1f, height + 3f));
        boundaryLine.positionCount = 4;
        boundaryLine.SetPositions(new[] { new Vector3(-halfWidth, 0), new Vector3(halfWidth, 0),
            new Vector3(halfWidth, height), new Vector3(-halfWidth, height) });

        for (int i = 0; i < surfaces.Length; i++)
        {
            // 원본의 선형 판은 반폭 25다. 중앙 이음새와 두께는 유지하고 양 끝을 이동한다.
            for (int v = 0; v < vertices[i].Length; v++)
            {
                Vector3 point = vertices[i][v];
                float reach = halfWidth + 2f;
                if (surfaces[i].isWindow)
                {
                    point.x += Mathf.Sign(point.x) * (halfWidth - 13f);
                    if (point.y > 8.46f) point.y += height - 17f;
                }
                else point.x = Mathf.Abs(point.x) > 24f ? Mathf.Sign(point.x) * reach
                    : Mathf.Clamp(point.x, -reach, reach);
                working[i][v] = point;
            }
            copies[i].vertices = working[i];
            copies[i].RecalculateBounds();
            surfaces[i].filter.transform.localPosition = surfaceHome[i]
                + (surfaces[i].followsCeiling ? Vector3.up * (height - 17f) : Vector3.zero);
        }
        serviceRail.localPosition = railHome + Vector3.up * (height - 17f);
        if (windowCable != null)
            for (int i = 0; i < windowCableHome.Length; i++)
            {
                Vector3 point = windowCableHome[i];
                point.x += Mathf.Sign(point.x) * (halfWidth - 13f);
                if (point.y > 8.46f) point.y += height - 17f;
                windowCable.SetPosition(i, point);
            }
    }

    private static void ResizeShadow(SpriteRenderer sprite, Vector3 center, Vector2 size)
    {
        Vector3 horizontal = sprite.transform.localRotation * Vector3.right;
        bool xIsHorizontal = Mathf.Abs(horizontal.x) > .5f;
        Vector2 sourceSize = sprite.sprite.bounds.size;
        sprite.transform.localScale = new Vector3((xIsHorizontal ? size.x : size.y) / sourceSize.x,
            (xIsHorizontal ? size.y : size.x) / sourceSize.y, 1f);
        sprite.transform.localPosition = center;
    }

    private void LateUpdate()
    {
        if (depthHome == null) return;
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return;
        // 기준은 Room의 고정 좌표다. 카메라 워프나 방 크기 변화가 설비의 배율에 영향을 주지 않는다.
        Vector3 delta = viewCamera.transform.position - transform.TransformPoint(new Vector3(0, 7f));
        delta.z = 0f;
        for (int i = 0; i < depthLayers.Length; i++)
            depthLayers[i].root.localPosition = depthHome[i] + delta * depthLayers[i].parallax;
    }

    private void OnDisable()
    {
        if (originals == null) return;
        for (int i = 0; i < originals.Length; i++)
        {
            surfaces[i].filter.sharedMesh = originals[i];
            surfaces[i].filter.transform.localPosition = surfaceHome[i];
            Destroy(copies[i]);
        }
        for (int i = 0; i < depthHome.Length; i++) depthLayers[i].root.localPosition = depthHome[i];
        serviceRail.localPosition = railHome;
        if (windowCable != null && windowCableHome != null) windowCable.SetPositions(windowCableHome);
        originals = null;
    }
}
