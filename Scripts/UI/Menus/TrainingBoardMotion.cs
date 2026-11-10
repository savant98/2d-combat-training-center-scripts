using UnityEngine;

// 훈련 상황판·Wave 전환·클리어 연출이 쓰는 시간표 보간(2026-11-10 TrainingRoom 상황판 리메이크).
// 연출은 매 프레임 "시간 t의 상태"를 계산해 적용한다. 같은 t면 같은 모습이라 검증과 조정이 쉽다.
public static class TrainingBoardMotion
{
    // t가 a~b 사이에서 0→1로 가는 진행도.
    public static float Seg(float t, float a, float b)
    {
        if (b <= a) return t >= b ? 1f : 0f;
        return Mathf.Clamp01((t - a) / (b - a));
    }

    public static float Smooth(float x) => x * x * (3f - 2f * x);

    public static float OutCubic(float x)
    {
        x = 1f - Mathf.Clamp01(x);
        return 1f - x * x * x;
    }

    public static float OutBack(float x)
    {
        const float s = 1.70158f;
        x = Mathf.Clamp01(x) - 1f;
        return 1f + (s + 1f) * x * x * x + s * x * x;
    }

    // 옛 UI Animation Clip(WavePanelStart·TrainingCompleteStart)과 같은 Hermite 보간.
    // WavePanelStart의 키는 모두 기울기 0이라 키 사이가 smoothstep이다. TrainingCompleteStart 회전은 0.583초 키만 -24(°/초)라
    // 그 곡선은 키별 기울기(slopes, 값/초)를 함께 넘긴다.
    public static float Keys(float t, float[] times, float[] values, float[] slopes = null)
    {
        if (t <= times[0]) return values[0];
        for (int i = 1; i < times.Length; i++)
        {
            if (t > times[i]) continue;
            float dt = times[i] - times[i - 1];
            float s = (t - times[i - 1]) / dt, s2 = s * s, s3 = s2 * s;
            float m0 = slopes != null ? slopes[i - 1] * dt : 0f, m1 = slopes != null ? slopes[i] * dt : 0f;
            return (2f * s3 - 3f * s2 + 1f) * values[i - 1] + (s3 - 2f * s2 + s) * m0 + (-2f * s3 + 3f * s2) * values[i] + (s3 - s2) * m1;
        }
        return values[values.Length - 1];
    }

    // 시간 t의 한 번 지나가는 빛(0→1→0, a에서 b까지).
    public static float Pulse(float t, float a, float b)
    {
        float k = Seg(t, a, b);
        return k <= 0f || k >= 1f ? 0f : Mathf.Sin(k * Mathf.PI);
    }
}
