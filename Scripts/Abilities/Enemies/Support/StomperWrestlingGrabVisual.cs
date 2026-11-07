using UnityEngine;

[DefaultExecutionOrder(250)]
public sealed class StomperWrestlingGrabVisual : MonoBehaviour
{
    private EnemyController owner;
    private Transform model, body, core, cross, head, port, stem;
    private Vector3 headPosition, headScale, stemPosition, stemScale;
    private Quaternion headRotation, stemRotation;
    private LineRenderer range;
    private SpriteRenderer fill;
    private StomperWrestlingGrabDataSO settings;
    private bool owns, preview;
    private float startedAt, extension;
    private Vector3 target;
    private Transform trackedTarget;
    public void TrackTarget(Transform value) => trackedTarget=value;
    public Vector3 CorePosition => core != null ? core.position : transform.position;
    public Transform CoreTransform => core;
    public Vector3 ArmTipPosition => port != null ? port.position : CorePosition;
    public float ArmReach => Vector3.Distance(CorePosition, ArmTipPosition);
    public LineRenderer Range => range;
    public bool ArmVisible => owns && extension > .001f;
    public void Initialize(EnemyController controller)
    {
        owner = controller; model = controller.model.transform;
        body = model.Find("ModelBorderMask"); core = model.Find("ModelMask");
        cross = body.Find("CoreCross"); head = model.Find("MineLauncher"); port = head.Find("Port"); stem = model.Find("MineLauncherStem");
        headPosition = head.localPosition; headScale = head.localScale; headRotation = head.localRotation;
        stemPosition = stem.localPosition; stemScale = stem.localScale; stemRotation = stem.localRotation;
    }
    public void Begin(StomperWrestlingGrabDataSO data)
    {
        if (owner == null) Initialize(GetComponent<EnemyController>());
        trackedTarget=null; settings = data; owns = preview = true; extension = 0; startedAt = Time.time;
        if (range == null)
        {
            range = new GameObject("GrabRangeOutline").AddComponent<LineRenderer>(); range.transform.SetParent(transform, false);
            range.loop = true; range.useWorldSpace = true; range.positionCount = 72; range.widthMultiplier = .045f;
            range.numCornerVertices = 2; range.sortingLayerID = body.GetComponent<SpriteRenderer>().sortingLayerID; range.sortingOrder = 50;
            fill = new GameObject("GrabRangeFill").AddComponent<SpriteRenderer>(); fill.transform.SetParent(transform, false);
            fill.sortingLayerID = range.sortingLayerID; fill.sortingOrder = -5;
        }
        range.sharedMaterial = data.previewMaterial; fill.sprite = data.previewFill;
        fill.color = new Color(1, .17f, .015f, .09f);
    }
    public void SetPreview(bool visible) => preview = visible;
    public void PoseArm(Vector3 playerPosition, float progress)
    { target = playerPosition; extension = Mathf.Clamp01(progress); if (owns) UpdateArm(); }
    private void LateUpdate()
    {
        if (!owns) return;
        range.enabled = fill.enabled = preview;
        if (preview)
        {
            Vector3 center = CorePosition; float radius = settings.grabRadius;
            for (int i = 0; i < range.positionCount; i++)
            {
                float theta = i * Mathf.PI * 2 / range.positionCount;
                range.SetPosition(i, center + new Vector3(Mathf.Cos(theta), Mathf.Sin(theta)) * radius);
            }
            fill.transform.position = center; fill.transform.localScale = Vector3.one * radius * 2;
        }
        // 외곽은 정지하고 준비 중 X자만 코어와 함께 공회전한다.
        var ability = GetComponent<StomperWrestlingGrabAbility>();
        if (ability.State == AbilityState.Windup)
            cross.localRotation = Quaternion.Euler(0, 0, -720 * (Time.time - startedAt) / settings.windupTime);
        if (ability.State == AbilityState.Execute)
        { body.localRotation = core.localRotation = cross.localRotation = Quaternion.identity; }
        if(trackedTarget!=null) target=trackedTarget.position;
        UpdateArm();
    }
    private void UpdateArm()
    {
        bool active = extension > .001f;
        head.gameObject.SetActive(active); stem.gameObject.SetActive(active);
        if (!active) return;
        Vector3 origin = CorePosition;
        Vector3 end = origin + Vector3.ClampMagnitude(target - origin, settings.grabRadius) * extension;
        Vector3 a = model.InverseTransformPoint(origin), b = model.InverseTransformPoint(end);
        Vector3 delta = b - a;
        float rotation = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90;
        // Port를 실제 손끝으로 사용해 최대 반경과 팔 끝점이 정확히 일치한다.
        b -= Quaternion.Euler(0, 0, rotation) * (port.localPosition * .32f);
        delta = b - a;
        stem.localPosition = (a + b) * .5f; stem.localRotation = Quaternion.Euler(0, 0, rotation);
        stem.localScale = new Vector3(.22f, delta.magnitude, 1);
        head.localPosition = b; head.localRotation = Quaternion.Euler(0, 0, rotation); head.localScale = Vector3.one * .32f;
    }
    public void End()
    {
        trackedTarget=null; if (!owns) return; owns = false;
        if (range != null) range.enabled = false; if (fill != null) fill.enabled = false;
        body.localRotation = core.localRotation = Quaternion.identity;
        head.localPosition = headPosition; head.localRotation = headRotation; head.localScale = headScale;
        stem.localPosition = stemPosition; stem.localRotation = stemRotation; stem.localScale = stemScale;
        head.gameObject.SetActive(false); stem.gameObject.SetActive(false);
    }
    private void OnDisable() => End();
}
