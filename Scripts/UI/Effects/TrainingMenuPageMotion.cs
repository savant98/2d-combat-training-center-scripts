using UnityEngine;

/// <summary>훈련 화면의 순차 조립과 코어·주사선 대기를 실제 시간으로 재생한다.</summary>
[DisallowMultipleComponent]
public class TrainingMenuPageMotion : MonoBehaviour
{
    [SerializeField] private RectTransform[] sections = System.Array.Empty<RectTransform>();
    [SerializeField] private float[] delays = System.Array.Empty<float>();
    [SerializeField] private RectTransform coreRing;
    [SerializeField] private RectTransform counterRing;
    [SerializeField] private RectTransform scan;
    [SerializeField] private UnityEngine.UI.Graphic[] indicators = System.Array.Empty<UnityEngine.UI.Graphic>();

    private Vector2[] homes;
    private Vector3[] scales;
    private CanvasGroup[] groups;
    private float[] alphas;
    private bool[] settled;
    private bool[] fold;
    private Color[] colors;
    private Quaternion coreHome, counterHome;
    private Vector2 scanHome;
    private CanvasGroup scanGroup;
    private float scanAlpha;
    private float elapsed;
    private bool cached;

    private void Cache()
    {
        if (cached) return;
        cached = true;
        homes = new Vector2[sections.Length];
        scales = new Vector3[sections.Length];
        groups = new CanvasGroup[sections.Length];
        alphas = new float[sections.Length];
        settled = new bool[sections.Length];
        fold = new bool[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null) continue;
            homes[i] = sections[i].anchoredPosition;
            scales[i] = sections[i].localScale;
            groups[i] = sections[i].GetComponent<CanvasGroup>();
            alphas[i] = groups[i] == null ? 1f : groups[i].alpha;
            fold[i] = sections[i].GetComponent<UnityEngine.UI.Button>() == null;
        }
        colors = new Color[indicators.Length];
        for (int i = 0; i < indicators.Length; i++)
            if (indicators[i] != null) colors[i] = indicators[i].color;
        if (coreRing != null) coreHome = coreRing.localRotation;
        if (counterRing != null) counterHome = counterRing.localRotation;
        if (scan != null)
        {
            scanHome = scan.anchoredPosition;
            scanGroup = scan.GetComponent<CanvasGroup>();
            scanAlpha = scanGroup == null ? 1f : scanGroup.alpha;
        }
    }

    private void OnEnable()
    {
        Replay();
    }

    public void Replay()
    {
        if (!Application.isPlaying) return;
        Cache();
        System.Array.Clear(settled, 0, settled.Length);
        elapsed = 0f;
        Evaluate();
    }

    private void Update()
    {
        if (!cached) return;
        elapsed += Time.unscaledDeltaTime;
        Evaluate();
    }

    private void Evaluate()
    {
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null || settled[i]) continue;
            float delay = i < delays.Length ? delays[i] : .12f + i * .045f;
            float t = Mathf.Clamp01((elapsed - delay) / .26f);
            float smooth = t * t * (3f - 2f * t);
            sections[i].anchoredPosition = homes[i] + Vector2.down * (18f * (1f - smooth));
            // 가로선이 먼저 잡힌 뒤 판이 펼쳐지고 마지막에 정위치로 결합한다.
            // 선택 버튼의 확대는 SelectableMenuUI가 소유하므로 알파·위치만 조립한다.
            if (fold[i])
                sections[i].localScale = Vector3.Scale(scales[i], new Vector3(1f, Mathf.Lerp(.08f, 1f, smooth), 1f));
            if (groups[i] != null) groups[i].alpha = alphas[i] * smooth;
            if (t >= 1f) settled[i] = true;
        }
        if (coreRing != null) coreRing.localRotation = coreHome * Quaternion.Euler(0f, 0f, elapsed * -12f);
        if (counterRing != null) counterRing.localRotation = counterHome * Quaternion.Euler(0f, 0f, elapsed * 20f);
        for (int i = 0; i < indicators.Length; i++)
        {
            if (indicators[i] == null) continue;
            Color color = colors[i];
            color.a *= .72f + .28f * Mathf.Sin(elapsed * 1.7f + i * .8f);
            indicators[i].color = color;
        }
        if (scan != null)
        {
            // 스캔은 글자를 덮지 않는 낮은 알파로 한 번 지나간 뒤 다음 주기를 기다린다.
            float phase = Mathf.Repeat(elapsed / 6f, 1f);
            scan.anchoredPosition = scanHome + Vector2.up * Mathf.Lerp(302f, -302f, Mathf.Clamp01(phase / .8f));
            if (scanGroup != null) scanGroup.alpha = scanAlpha * (phase < .8f ? Mathf.Sin(phase / .8f * Mathf.PI) : 0f);
        }
    }

    private void OnDisable()
    {
        if (!cached) return;
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null) continue;
            sections[i].anchoredPosition = homes[i];
            sections[i].localScale = scales[i];
            if (groups[i] != null) groups[i].alpha = alphas[i];
        }
        for (int i = 0; i < indicators.Length; i++)
            if (indicators[i] != null) indicators[i].color = colors[i];
        if (coreRing != null) coreRing.localRotation = coreHome;
        if (counterRing != null) counterRing.localRotation = counterHome;
        if (scan != null) scan.anchoredPosition = scanHome;
        if (scanGroup != null) scanGroup.alpha = scanAlpha;
    }
}
