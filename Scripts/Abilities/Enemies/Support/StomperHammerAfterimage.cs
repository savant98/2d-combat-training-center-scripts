using UnityEngine;

// 타격 자세에서 만든 독립 해머 잔상. 소유자나 준비용 해머를 따라가지 않고 스스로 사라진다.
[DisallowMultipleComponent]
public sealed class StomperHammerAfterimage : MonoBehaviour
{
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    SpriteRenderer[] sprites;
    MaterialPropertyBlock[] properties;
    float[] baselineIntensities;
    Color[] baselineColors;

    public float Alpha { get; private set; }
    public float Duration { get; private set; }
    public float SpawnTime { get; private set; }
    public int SpawnFrame { get; private set; }
    public Vector3 SpawnPosition { get; private set; }
    public Quaternion SpawnRotation { get; private set; }
    public Vector3 SpawnScale { get; private set; }
    public float Elapsed => Mathf.Max(0f, Time.time - SpawnTime);
    public float Remaining => Mathf.Max(0f, Duration - Elapsed);
    public bool IsVisible => Alpha > 0f && gameObject.activeInHierarchy;
    public int RendererCount => sprites != null ? sprites.Length : 0;

    public static StomperHammerAfterimage Spawn(Transform source, float duration)
    {
        if (source == null) return null;
        var copy = Instantiate(source.gameObject, source.position, source.rotation);
        copy.name = "StomperHammerImpactAfterimage";
        copy.transform.SetParent(null, true);
        copy.transform.localScale = source.lossyScale;
        var fade = copy.AddComponent<StomperHammerAfterimage>();
        fade.Initialize(source, duration);
        copy.SetActive(true);
        return fade;
    }

    void Initialize(Transform source, float duration)
    {
        Duration = Mathf.Max(0.01f, duration);
        SpawnTime = Time.time;
        SpawnFrame = Time.frameCount;
        SpawnPosition = transform.position;
        SpawnRotation = transform.rotation;
        SpawnScale = transform.localScale;
        var originals = source.GetComponentsInChildren<SpriteRenderer>(true);
        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        properties = new MaterialPropertyBlock[sprites.Length];
        baselineIntensities = new float[sprites.Length];
        baselineColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++)
        {
            var original = originals[i];
            var block = new MaterialPropertyBlock();
            original.GetPropertyBlock(block);
            properties[i] = block;
            // 준비용 Renderer의 기존 override를 우선하고 없으면 공유 Material의 원래 발광값을 읽는다.
            var material = original.sharedMaterial;
            baselineIntensities[i] = block.HasProperty(IntensityId) ? block.GetFloat(IntensityId)
                : material != null && material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 1f;
            baselineColors[i] = original.color;
            sprites[i].SetPropertyBlock(block);
        }
        ApplyAlpha(1f);
    }

    void Update()
    {
        if (sprites == null) return;
        float alpha = Elapsed >= Duration - 0.00001f ? 0f : 1f - Mathf.Clamp01(Elapsed / Duration);
        ApplyAlpha(alpha);
        if (alpha <= 0f) Destroy(gameObject);
    }

    void ApplyAlpha(float alpha)
    {
        Alpha = alpha;
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] == null) continue;
            var color = baselineColors[i];
            color.a *= alpha;
            sprites[i].color = color;
            // Stomper의 Additive GlowMultiply는 Sprite alpha를 쓰지 않으므로 실제 출력도 함께 줄인다.
            properties[i].SetFloat(IntensityId, baselineIntensities[i] * alpha);
            sprites[i].SetPropertyBlock(properties[i]);
        }
    }

    public float BaselineIntensity(int index) => baselineIntensities[index];
    public float CurrentIntensity(int index)
    {
        if (sprites == null || index < 0 || index >= sprites.Length || sprites[index] == null) return 0f;
        var block = new MaterialPropertyBlock();
        sprites[index].GetPropertyBlock(block);
        return block.GetFloat(IntensityId);
    }
}
