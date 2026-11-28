using UnityEngine;

/// <summary>투사판 안에서 목록과 상세의 본문을 왼쪽부터 차례로 표시한다.</summary>
[DisallowMultipleComponent]
public class TrainingMenuPageMotion : MonoBehaviour
{
    [SerializeField] private RectTransform[] sections = System.Array.Empty<RectTransform>();
    [SerializeField] private float[] delays = System.Array.Empty<float>();

    private Vector2[] homes;
    private CanvasGroup[] groups;
    private float[] alphas;
    private float elapsed;
    private float duration;
    private const float FadeDuration = .26f;

    private void OnEnable() => Replay();

    public void Replay()
    {
        if (!Application.isPlaying) return;
        if (homes == null)
        {
            homes = new Vector2[sections.Length];
            groups = new CanvasGroup[sections.Length];
            alphas = new float[sections.Length];
            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i] == null) continue;
                homes[i] = sections[i].anchoredPosition;
                groups[i] = sections[i].GetComponent<CanvasGroup>();
                alphas[i] = groups[i] == null ? 1f : groups[i].alpha;
                duration = Mathf.Max(duration, Delay(i) + FadeDuration);
            }
        }
        elapsed = 0f;
        Evaluate();
    }

    private float Delay(int index) => index < delays.Length ? delays[index] : index * .04f;

    private void Update()
    {
        if (homes == null || elapsed >= duration) return;
        elapsed += Time.unscaledDeltaTime;
        Evaluate();
    }

    private void Evaluate()
    {
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null) continue;
            float delay = Delay(i);
            float t = TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(elapsed, delay, delay + FadeDuration));
            sections[i].anchoredPosition = homes[i] + Vector2.left * (24f * (1f - t));
            if (groups[i] != null) groups[i].alpha = alphas[i] * t;
        }
    }

    private void OnDisable()
    {
        if (homes == null) return;
        elapsed = duration;
        Evaluate();
    }
}
