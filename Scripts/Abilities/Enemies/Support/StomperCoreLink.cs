using UnityEngine;

/// <summary>선행 코어와 본체 사이만 잇는 Stomper 전용 돌진 예고선. 이동·공격 판정에는 관여하지 않는다.</summary>
[DisallowMultipleComponent]
public sealed class StomperCoreLink : MonoBehaviour
{
    [SerializeField] private EnemyController controller;
    [SerializeField] private ForwardDashAbility dash;
    [SerializeField] private Transform core;
    [SerializeField] private Transform center;
    [SerializeField] private Transform feedbackPoint;
    [SerializeField] private Material lineMaterial;
    [SerializeField] private SpriteRenderer sortingReference;
    [SerializeField, Min(0.01f)] private float width = 2.125f;

    [SerializeField, Min(0.1f)] private float blinkFrequency = 5f;
    private static readonly Color WarningRed = new Color(1f, 0f, 0f, 1f);
    private static readonly Color WarningYellow = new Color(1f, 0.72f, 0f, 1f);
    private float appearedAt;

    private LineRenderer line;
    private Material instanceMaterial;
    private Vector3 coreTarget;
    private int direction;
    private bool hasTarget;
    private float scroll;

    public LineRenderer ActiveLine => line;
    public float Length { get; private set; }
    public Vector3 Target => coreTarget;
    public float Width => width;
    public Color CurrentColor { get; private set; }
    public float BlinkFrequency => blinkFrequency;

    private void LateUpdate()
    {
        if (controller == null || dash == null || core == null || center == null) return;

        // FeedbackPlayer는 Transform의 회전을 사용하므로 좌향 돌진의 방출 방향도 함께 맞춘다.
        if (feedbackPoint != null)
            feedbackPoint.rotation = controller.Facing < 0 ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;

        if (dash.State == AbilityState.Windup)
        {
            direction = controller.Facing < 0 ? -1 : 1;
            coreTarget = core.position;
            hasTarget = (coreTarget.x - center.position.x) * direction > 0.01f;
        }
        else if (dash.State != AbilityState.Execute)
        {
            ClearLine();
            return;
        }

        // Execute에서는 원본 Animation이 코어를 숨긴다. 마지막 선행 위치를 유지하고 본체가 따라잡게 한다.
        float ahead = (coreTarget.x - center.position.x) * direction;
        if (!hasTarget || ahead <= 0.01f)
        {
            ClearLine();
            return;
        }

        EnsureLine();
        Vector3 origin = center.position;
        Vector3 target = coreTarget;
        target.z = origin.z;
        line.SetPosition(0, origin);
        line.SetPosition(1, target);
        Length = Vector3.Distance(origin, target);
        line.startWidth = line.endWidth = width;
        // 화살표 간격은 선이 늘어나도 일정하게 유지한다.
        line.textureScale = new Vector2(Length / Mathf.Max(width, 0.01f), 1);
        scroll -= Time.deltaTime * 2;
        instanceMaterial.SetVector("_ScrollOffset", new Vector4(scroll, 0, 0, 0));
        // 두 경고색을 번갈아 표시하며, 길이와 밝기는 코어 추적과 독립적으로 유지한다.
        bool yellow = (Mathf.FloorToInt((Time.time - appearedAt) * blinkFrequency * 2f) & 1) != 0;
        CurrentColor = yellow ? WarningYellow : WarningRed;
        instanceMaterial.SetColor("_EmissionColor", CurrentColor);
        instanceMaterial.SetColor("_Color", CurrentColor);
        line.startColor = line.endColor = CurrentColor;
    }

    private void EnsureLine()
    {
        if (line != null) return;
        var visual = new GameObject("StomperCorePrevline");
        visual.transform.SetParent(transform, false);
        line = visual.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        appearedAt = Time.time;
        scroll = 0;
        instanceMaterial = new Material(lineMaterial);
        line.sharedMaterial = instanceMaterial;
        if (sortingReference != null)
        {
            line.sortingLayerID = sortingReference.sortingLayerID;
            line.sortingOrder = sortingReference.sortingOrder - 2;
        }
    }

    private void ClearLine()
    {
        Length = 0;
        hasTarget = false;
        if (line != null)
        {
            line.SetPosition(1, line.GetPosition(0));
            line.enabled = false;
            Destroy(line.gameObject);
            line = null;
        }
        if (instanceMaterial != null) Destroy(instanceMaterial);
        instanceMaterial = null;
    }

    private void OnDisable() => ClearLine();
}
