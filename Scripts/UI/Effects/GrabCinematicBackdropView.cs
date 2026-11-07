using UnityEngine;
using UnityEngine.Rendering.Universal;

// 실제 TrainingBackdrop의 표시 요소를 한 번 렌더하고, 세 후경 영상을 독립적으로 스크롤한다.
public sealed class GrabCinematicBackdropView
{
    private readonly UnityEngine.UI.RawImage display;
    private readonly Material material;
    private readonly RenderTexture[] textures = new RenderTexture[3];
    private GameObject captureStage;
    private readonly int captureFrame;
    private float travel, visibility, displayWeight=1;
    public int CopiedRendererCount { get; private set; }
    public RenderTexture FarTexture => textures[0];
    public RenderTexture MiddleTexture => textures[1];
    public RenderTexture NearTexture => textures[2];
    public float Altitude { get; private set; }
    public float Travel => travel;
    public float Opacity => display != null ? display.color.a : 0;
    public bool IsBaked => captureStage == null;
    public string SourceName { get; }
    public GrabCinematicBackdropView(Transform canvas, int siblingIndex, GameObject prefab, Material template, Camera sourceCamera)
    {
        if (prefab == null || template == null) return;
        var source = GameObject.Find("TrainingBackdrop");
        if (source == null) source = prefab;
        SourceName = source.name;
        var go = new GameObject("GrabTravelBackground", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        go.transform.SetParent(canvas, false); go.transform.SetSiblingIndex(siblingIndex);
        display = go.GetComponent<UnityEngine.UI.RawImage>(); display.raycastTarget = false;
        var rect = display.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero;
        material = new Material(template); display.material = material; display.color = Color.clear;
        captureStage = new GameObject("GrabBackdropCaptureStage"); captureFrame = Time.frameCount;
        string[] groups = { "FarStructure", "TrainingChambers", "RecoveryMachinery" };
        for (int i = 0; i < groups.Length; i++)
        {
            var group = source.transform.Find(groups[i]);
            var origin = new Vector3(20000 + i * 300, 20000, 0);
            var root = new GameObject(groups[i]).transform; root.SetParent(captureStage.transform, false); root.position = origin;
            textures[i] = new RenderTexture(1024, 1024, 16, RenderTextureFormat.ARGBHalf)
            { name = "GrabTrainingBackdrop" + i, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            textures[i].Create(); material.SetTexture(i == 0 ? "_FarTex" : i == 1 ? "_MidTex" : "_NearTex", textures[i]);
            if (group != null)
                {
                    foreach (var renderer in group.GetComponentsInChildren<Renderer>(true)) CopyRenderer(renderer, root, source.transform);
                    foreach (var mask in group.GetComponentsInChildren<SpriteMask>(true))
                    {
                        if (!mask.enabled || !ActiveToRoot(mask.transform, source.transform)) continue;
                        var copy = new GameObject(mask.name).AddComponent<SpriteMask>(); copy.gameObject.layer = LayerMask.NameToLayer("UI");
                        copy.transform.SetParent(root, false); copy.transform.localPosition = mask.transform.position - source.transform.position;
                        copy.transform.localRotation = mask.transform.rotation; copy.transform.localScale = mask.transform.lossyScale;
                        copy.sprite = mask.sprite; copy.alphaCutoff = mask.alphaCutoff; copy.isCustomRangeActive = mask.isCustomRangeActive;
                        copy.frontSortingLayerID = mask.frontSortingLayerID; copy.frontSortingOrder = mask.frontSortingOrder;
                        copy.backSortingLayerID = mask.backSortingLayerID; copy.backSortingOrder = mask.backSortingOrder;
                    }
                }
            var camera = new GameObject("GrabBackdropBakeCamera" + i).AddComponent<Camera>();
            camera.transform.SetParent(captureStage.transform, false);
            Vector3 centre = Vector3.zero;
            camera.transform.position = origin + new Vector3(centre.x, centre.y, -30);
            camera.orthographic = true; camera.orthographicSize = 10;
            camera.nearClipPlane = .1f; camera.farClipPlane = 120;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << LayerMask.NameToLayer("UI"); camera.allowHDR = true; camera.allowMSAA = false;
            camera.targetTexture = textures[i]; camera.depth = -20;
            var extra = camera.GetUniversalAdditionalCameraData(); extra.renderPostProcessing = false; extra.renderShadows = false;
        }
    }
    private static bool ActiveToRoot(Transform target, Transform root)
    {
        for (var ancestor = target; ancestor != null; ancestor = ancestor.parent)
        { if (!ancestor.gameObject.activeSelf) return false; if (ancestor == root) break; }
        return true;
    }
    private void CopyRenderer(Renderer source, Transform parent, Transform sourceRoot)
    {
        if (!source.enabled || source.forceRenderingOff) return;
        if (!ActiveToRoot(source.transform, sourceRoot)) return;
        var go = new GameObject(source.name); go.layer = LayerMask.NameToLayer("UI"); go.transform.SetParent(parent, false);
        go.transform.localPosition = source.transform.position - sourceRoot.position;
        go.transform.localRotation = source.transform.rotation; go.transform.localScale = source.transform.lossyScale;
        Renderer copy = null;
        if (source is SpriteRenderer sprite)
        {
            var target = go.AddComponent<SpriteRenderer>(); target.sprite = sprite.sprite; target.color = sprite.color;
            target.maskInteraction = sprite.maskInteraction; target.flipX = sprite.flipX; target.flipY = sprite.flipY; target.drawMode = sprite.drawMode; target.size = sprite.size;
            copy = target;
        }
        else if (source is LineRenderer line)
        {
            var target = go.AddComponent<LineRenderer>(); target.useWorldSpace = false; target.loop = line.loop;
            target.widthCurve = line.widthCurve; target.widthMultiplier = line.widthMultiplier; target.colorGradient = line.colorGradient;
            target.textureMode = line.textureMode; target.numCapVertices = line.numCapVertices; target.numCornerVertices = line.numCornerVertices;
            target.positionCount = line.positionCount;
            for (int i = 0; i < line.positionCount; i++)
                target.SetPosition(i, line.useWorldSpace ? source.transform.InverseTransformPoint(line.GetPosition(i)) : line.GetPosition(i));
            copy = target;
        }
        else if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter))
        { go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh; copy = go.AddComponent<MeshRenderer>(); }
        if (copy == null) { Object.Destroy(go); return; }
        copy.sharedMaterials = source.sharedMaterials; copy.sortingLayerID = source.sortingLayerID; copy.sortingOrder = source.sortingOrder;
        var properties = new MaterialPropertyBlock(); source.GetPropertyBlock(properties); copy.SetPropertyBlock(properties);
        CopiedRendererCount++;
    }
    public void SetTravel(float speed, float altitude, bool rising, float deltaTime)
    {
        if (display == null) return;
        Altitude = Mathf.Clamp01(altitude); travel += speed * deltaTime / 20;
        if (rising) visibility = Mathf.Max(visibility, Mathf.SmoothStep(0, 1, altitude / .12f));
        display.color = new Color(1, 1, 1, visibility*displayWeight);
        material.SetFloat("_Travel", travel);
    }
    public void Update(float weight, float aspect,float viewSize,Vector2 cameraOffset)
    {
        if (display == null) return;
        displayWeight=weight;
        material.SetFloat("_Aspect", aspect); material.SetFloat("_ViewScale",viewSize/10); material.SetVector("_CameraOffset",new Vector4(cameraOffset.x/20,cameraOffset.y/20,0,0)); display.color = new Color(1, 1, 1, visibility * displayWeight);
        if (captureStage != null && Time.frameCount > captureFrame + 3)
        {
            foreach (var camera in captureStage.GetComponentsInChildren<Camera>()) { camera.enabled = false; camera.targetTexture = null; }
            Object.Destroy(captureStage); captureStage = null;
        }
    }
    public void Dispose()
    {
        if (captureStage != null)
        {
            foreach (var camera in captureStage.GetComponentsInChildren<Camera>()) { camera.enabled = false; camera.targetTexture = null; }
            Object.Destroy(captureStage); captureStage = null;
        }
        foreach (var texture in textures) if (texture != null) { texture.Release(); Object.Destroy(texture); }
        if (display != null) Object.Destroy(display.gameObject);
        if (material != null) Object.Destroy(material);
    }
}
