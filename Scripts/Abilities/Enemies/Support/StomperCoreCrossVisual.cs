using UnityEngine;

// 평상시에는 사각형의 자식 자세를 유지하고, ForwardDash의 선행 코어만 예외로 따른다.
[DefaultExecutionOrder(200)]
[ExecuteAlways]
public sealed class StomperCoreCrossVisual : MonoBehaviour
{
    [SerializeField] private SpriteRenderer core;
    [SerializeField] private SpriteRenderer[] arms;
    [SerializeField] private EnemyController owner;
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private float baseScale = 1.1f;
    private MaterialPropertyBlock properties;

    public bool FollowsLeadingCore { get; private set; }

    private void LateUpdate()
    {
        if (core == null || body == null || arms == null) return;
        bool coreVisible = core.enabled && core.color.a > .001f
            && Mathf.Min(Mathf.Abs(core.transform.lossyScale.x), Mathf.Abs(core.transform.lossyScale.y)) > .0001f;
        FollowsLeadingCore = coreVisible && owner != null && owner.Brain != null
            && owner.Brain.current is ForwardDashAbility dash && dash.IsRunning;
        bool visible = FollowsLeadingCore ? coreVisible : body.enabled && body.color.a > .001f
            && Mathf.Min(Mathf.Abs(body.transform.lossyScale.x), Mathf.Abs(body.transform.lossyScale.y)) > .0001f;
        // 기본 반경 .38을 본체 안쪽에 맞춘다. 장식 전체가 사각형과 같은 비율로 회전·이동한다.
        float size = Mathf.Min(baseScale, (Mathf.Min(body.sprite.bounds.extents.x, body.sprite.bounds.extents.y) - .04f) / .38f);
        if (FollowsLeadingCore)
        {
            transform.position = core.transform.position;
            transform.rotation = core.transform.rotation;
            float bodyX = Mathf.Max(.0001f, Mathf.Abs(body.transform.lossyScale.x));
            float bodyY = Mathf.Max(.0001f, Mathf.Abs(body.transform.lossyScale.y));
            transform.localScale = new Vector3(size * Mathf.Abs(core.transform.lossyScale.x) / bodyX,
                size * Mathf.Abs(core.transform.lossyScale.y) / bodyY, 1);
        }
        else
        {
            transform.localPosition = body.sprite.bounds.center;
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(size, size, 1);
        }
        if (properties == null) properties = new MaterialPropertyBlock();
        core.GetPropertyBlock(properties);
        foreach (var arm in arms)
        {
            if (arm == null) continue;
            arm.enabled = visible;
            Color color = core.color; color.a = FollowsLeadingCore ? core.color.a : body.color.a;
            arm.color = color;
            arm.maskInteraction = FollowsLeadingCore ? core.maskInteraction : body.maskInteraction;
            arm.SetPropertyBlock(properties);
        }
    }
}
