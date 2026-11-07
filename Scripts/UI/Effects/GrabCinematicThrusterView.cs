using UnityEngine;

// 표시용 분사기를 실제 하단 테두리에 고정하고 회전 시작 시 무대 안에서 분리한다.
public sealed class GrabCinematicThrusterView
{
    private readonly Transform[] roots = new Transform[2], heads = new Transform[2], pistons = new Transform[2];
    private readonly ParticleSystem[][] flames = new ParticleSystem[2][];
    private readonly SpriteRenderer[][] surfaces = new SpriteRenderer[2][];
    private readonly Color[][] surfaceColors = new Color[2][];
    private readonly Vector3[] detachedPositions = new Vector3[2];
    private readonly Quaternion[] detachedRotations = new Quaternion[2];
    private readonly Transform border, stage;
    private readonly SpriteRenderer frame;
    private bool firing;
    private float detachedAge;
    private const float DetachedLifetime = 1.2f;
    public float Deployment { get; private set; }
    public bool IsDetached { get; private set; }
    public int DetachedCount { get; private set; }
    public float MaximumDrop { get; private set; }
    public bool IsEmitting => flames[0] != null && flames[0].Length > 0 && flames[0][0] != null && flames[0][0].isEmitting;
    public float AttachedGap { get; private set; }
    public int ParticleCount
    {
        get { int count = 0; foreach (var group in flames) if (group != null) foreach (var flame in group) if (flame != null) count += flame.particleCount; return count; }
    }
    public GrabCinematicThrusterView(Transform model, GameObject prefab)
    {
        border = model.Find("ModelBorderMask");
        if (border == null) border = model;
        frame = border.GetComponent<SpriteRenderer>(); stage = model.root;
        if (prefab == null) return;
        for (int i = 0; i < roots.Length; i++)
        {
            roots[i] = Object.Instantiate(prefab, border).transform; roots[i].name = i == 0 ? "GrabThrusterLeft" : "GrabThrusterRight";
            roots[i].localPosition = Vector3.zero; roots[i].localRotation = Quaternion.identity;
            heads[i] = roots[i].Find("Head"); pistons[i] = roots[i].Find("Piston");
            var socket = roots[i].Find("Socket")?.GetComponent<SpriteRenderer>();
            if (socket != null)
            {
                // 테두리 안까지 물리는 두 고정턱과 가로 플랜지가 노즐의 하중을 받는다.
                Bracket(roots[i], socket, "MountFlange", new Vector2(0, .02f), new Vector2(.3f, .07f));
                Bracket(roots[i], socket, "MountJawLeft", new Vector2(-.11f, .04f), new Vector2(.045f, .18f));
                Bracket(roots[i], socket, "MountJawRight", new Vector2(.11f, .04f), new Vector2(.045f, .18f));
            }
            flames[i] = roots[i].GetComponentsInChildren<ParticleSystem>(true);
            foreach (var flame in flames[i]) flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            surfaces[i] = roots[i].GetComponentsInChildren<SpriteRenderer>(true); surfaceColors[i] = new Color[surfaces[i].Length];
            for (int j = 0; j < surfaces[i].Length; j++) surfaceColors[i][j] = surfaces[i][j].color;
            SetLayer(roots[i], LayerMask.NameToLayer("UI")); roots[i].gameObject.SetActive(false);
        }
    }
    public void SetFiring(bool value) => firing = value && !IsDetached;
    /// <summary>현재 월드 자세를 보존한 채 분사를 즉시 끄고 표시 장치를 아래로 떨어뜨린다.</summary>
    public void Detach()
    {
        if (IsDetached) return;
        IsDetached = true; firing = false; detachedAge = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            foreach (var flame in flames[i]) flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            roots[i].SetParent(stage, true);
            detachedPositions[i] = roots[i].position; detachedRotations[i] = roots[i].rotation;
            roots[i].gameObject.SetActive(true); DetachedCount++;
        }
    }
    public void Update(float deltaTime)
    {
        if (IsDetached) { UpdateDetached(deltaTime); return; }
        Deployment = Mathf.MoveTowards(Deployment, firing ? 1 : 0, deltaTime / .2f);
        float extension = Mathf.SmoothStep(0, 1, Deployment);
        // 프록시 Sprite는 생성 직후 아직 동기화되지 않았으므로 실제 갱신 시 bounds를 읽는다.
        Bounds frameBounds = frame != null && frame.sprite != null ? frame.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
        AttachedGap = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            roots[i].localPosition = new Vector3(frameBounds.center.x + (i == 0 ? -1 : 1) * frameBounds.extents.x * .55f, frameBounds.min.y + .025f, 0);
            roots[i].gameObject.SetActive(Deployment > 0);
            if (heads[i] != null) heads[i].localPosition = new Vector3(0, -.08f - .42f * extension, 0);
            if (pistons[i] != null)
            {
                pistons[i].localPosition = new Vector3(0, -.21f * extension, 0);
                pistons[i].localScale = new Vector3(.17f, .12f + .42f * extension, 1);
            }
            float upperEdge = roots[i].localPosition.y + .13f;
            AttachedGap = Mathf.Max(AttachedGap, Mathf.Max(0, frameBounds.min.y - upperEdge) * border.TransformVector(Vector3.up).magnitude);
            foreach (var flame in flames[i])
            {
                bool emit = firing && Deployment > .45f;
                if (emit && !flame.isEmitting) flame.Play();
                else if (!emit && flame.isEmitting) flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
    private void UpdateDetached(float deltaTime)
    {
        detachedAge += Mathf.Max(0, deltaTime);
        float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, DetachedLifetime, detachedAge));
        DetachedCount = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            if (detachedAge >= DetachedLifetime)
            {
                Object.Destroy(roots[i].gameObject); roots[i] = null; continue;
            }
            float side = i == 0 ? -1 : 1;
            float drop = 1.5f * detachedAge + 9 * detachedAge * detachedAge;
            roots[i].position = detachedPositions[i] + new Vector3(side * 1.5f * detachedAge, -drop, 0);
            roots[i].rotation = detachedRotations[i] * Quaternion.Euler(0, 0, side * 75 * detachedAge);
            MaximumDrop = Mathf.Max(MaximumDrop, drop); DetachedCount++;
            for (int j = 0; j < surfaces[i].Length; j++)
            {
                if (surfaces[i][j] == null) continue;
                Color color = surfaceColors[i][j]; color.a *= fade; surfaces[i][j].color = color;
            }
        }
        if (DetachedCount == 0) Deployment = 0;
    }
    private static void Bracket(Transform parent, SpriteRenderer source, string name, Vector2 position, Vector2 size)
    {
        if (source.sprite == null) return;
        var part = new GameObject(name).AddComponent<SpriteRenderer>(); part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        Vector2 original = source.sprite.bounds.size;
        part.transform.localScale = new Vector3(size.x / Mathf.Max(.001f, original.x), size.y / Mathf.Max(.001f, original.y), 1);
        part.sprite = source.sprite; part.sharedMaterial = source.sharedMaterial; part.color = source.color;
        part.sortingLayerID = source.sortingLayerID; part.sortingOrder = source.sortingOrder + 1;
    }
    private static void SetLayer(Transform target, int layer)
    { target.gameObject.layer = layer; foreach (Transform child in target) SetLayer(child, layer); }
}
