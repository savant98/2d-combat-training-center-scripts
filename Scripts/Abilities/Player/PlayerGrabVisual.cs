using UnityEngine;

// 물리 루트와 분리된 몸체 Clip·포박 전선을 같은 세션에서 복구한다.
[DefaultExecutionOrder(150)]
public sealed class PlayerGrabVisual : MonoBehaviour
{
    private PlayerController player;
    private Transform body;
    private Vector3 position, scale;
    private Quaternion rotation;
    private bool animatorEnabled, playing;
    private AnimationClip clip;
    private float startedAt, heldRotation;
    private LineRenderer[] wires;
    private MaterialPropertyBlock properties;
    public bool IsPlaying => playing;
    public void Initialize(PlayerController controller)
    {
        player = controller; body = controller.model.transform;
        properties = new MaterialPropertyBlock();
        position = body.localPosition; rotation = body.localRotation; scale = body.localScale;
    }
    public void Begin(PlayerGrabDataSO data)
    {
        End(); clip = data.restrainedLoop; startedAt = Time.time; heldRotation = 0;
        animatorEnabled = player.animator.enabled;
        player.animator.enabled = false; playing = true;
        wires = new LineRenderer[3];
        for (int i = 0; i < wires.Length; i++)
        {
            var go = new GameObject("GrabRestraint" + i); go.transform.SetParent(body, false);
            var line = go.AddComponent<LineRenderer>(); wires[i] = line;
            line.useWorldSpace = false; line.loop = true; line.positionCount = 32;
            line.sharedMaterial = data.wireMaterial; line.widthMultiplier = .028f;
            line.numCapVertices = line.numCornerVertices = 2;
            var source = body.GetComponentInChildren<SpriteRenderer>();
            line.sortingLayerID = source.sortingLayerID; line.sortingOrder = source.sortingOrder + 12;
        }
        UpdatePose();
    }
    public void SetRotation(float degrees) => heldRotation = degrees;
    private void LateUpdate() { if (playing) UpdatePose(); }
    private void UpdatePose()
    {
        if (clip != null) clip.SampleAnimation(body.gameObject, Mathf.Repeat(Time.time - startedAt, clip.length));
        float parentSign = body.parent != null && body.parent.localToWorldMatrix.determinant < 0 ? -1 : 1;
        body.localRotation = Quaternion.Euler(0, 0, heldRotation * parentSign) * body.localRotation;
        for (int band = 0; band < wires.Length; band++)
        {
            var line = wires[band];
            float tilt = band == 0 ? 38 : band == 1 ? -38 : 0;
            var turn = Quaternion.Euler(0, 0, tilt);
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / line.positionCount;
                float spark = Mathf.Sin(Time.time * 38 + i * 2.7f + band) * .018f;
                var point = new Vector3(Mathf.Cos(angle) * (.7f + spark), Mathf.Sin(angle) * .23f, 0);
                line.SetPosition(i, turn * point + Vector3.up * (band - 1) * .16f);
            }
            properties.SetColor("_Color", new Color(2.8f + Mathf.Sin(Time.time * 17) * .6f, .015f, .01f, 1));
            line.SetPropertyBlock(properties);
        }
    }
    public void End()
    {
        if (!playing) return;
        playing = false;
        if (wires != null) foreach (var wire in wires) if (wire != null) Destroy(wire.gameObject);
        wires = null;
        if (body != null) { body.localPosition = position; body.localRotation = rotation; body.localScale = scale; }
        if (player != null && player.animator != null)
        {
            player.animator.enabled = animatorEnabled;
            if (animatorEnabled && player.gameObject.activeInHierarchy) { player.animator.Rebind(); player.animator.Update(0); }
        }
    }
    private void OnDisable() => End();
}
