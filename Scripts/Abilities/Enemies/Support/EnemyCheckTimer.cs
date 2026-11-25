using UnityEngine;

// 적이 매 프레임 하던 확인(공격 후보 평가·감지 규칙·비행음 순위·쉴드 표시)을 정한 간격으로 줄이는 개체별 타이머다.
// - Time.time 기준이라 프레임률과 무관하다. 간격이 0 이하이면 매번 확인한다(줄이기 전과 같은 동작).
// - 위상 분산: 개체마다 생성 순번에 황금비를 곱한 소수부(0~1)를 위상으로 쓴다. StartStaggered로 첫 확인을 간격 × 위상만큼
//   늦추면 같은 프레임에 생성된 무리의 확인이 한 프레임에 몰리지 않는다. UnityEngine.Random을 쓰지 않아 전역 난수 순서를 바꾸지 않는다.
// - 구조체라 클래스 필드에 그대로 두고 그 필드에서 직접 호출해야 한다(readonly 필드·속성 복사본에서 부르면 상태가 남지 않는다).
public struct EnemyCheckTimer
{
    private const double GoldenRatioFraction = 0.6180339887498949;
    private static int sequence;

    private float nextTime;
    private float phase;
    private bool hasPhase;

    // 이 개체의 위상(0~1). 처음 읽을 때 생성 순번으로 정한다.
    public float Phase
    {
        get
        {
            if (!hasPhase)
            {
                hasPhase = true;
                phase = (float)((++sequence * GoldenRatioFraction) % 1.0);
            }
            return phase;
        }
    }

    // 다음 Due를 바로 참으로 만든다(유지 시간이 끝난 첫 프레임처럼 즉시 한 번 확인해야 할 때).
    public void Reset() => nextTime = float.NegativeInfinity;

    // 첫 확인을 지금부터 간격 × 위상 뒤로 미룬다(개체마다 다른 시각).
    public void StartStaggered(float interval) => nextTime = Time.time + Mathf.Max(0f, interval) * Phase;

    // 다른 이유(칸 수 변화 등)로 방금 확인했으면 다음 차례를 지금부터 간격 뒤로 다시 정한다.
    public void Restart(float interval) => nextTime = Time.time + Mathf.Max(0f, interval);

    // 확인할 차례면 true를 돌려주고 다음 차례를 간격 뒤로 정한다.
    public bool Due(float interval)
    {
        if (interval <= 0f)
            return true;
        float now = Time.time;
        if (now < nextTime)
            return false;
        nextTime = now + interval;
        return true;
    }
}

// 주기 확인의 진단용 횟수(Editor 전용). 회귀가 지운 뒤 일정 시간 동안 센 값으로 프레임당 확인 수를 잰다.
// 빌드에서는 Add 호출이 지워진다(Conditional).
public enum EnemyCheckKind
{
    AegisCandidate,
    AegisCandidateSkipped,
    PatrolYield,
    PatrolYieldSkipped,
    FlightSoundRank,
    ShieldRegenUi,
    ShieldRegenUiSkipped
}

public static class EnemyCheckStats
{
    private static readonly int[] counts = new int[7];

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Add(EnemyCheckKind kind) => counts[(int)kind]++;

    public static int Get(EnemyCheckKind kind) => counts[(int)kind];

    public static void Clear() => System.Array.Clear(counts, 0, counts.Length);
}
