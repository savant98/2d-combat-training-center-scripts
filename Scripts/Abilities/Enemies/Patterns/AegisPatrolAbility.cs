using UnityEngine;
using UnityEngine.Serialization;

// Drone의 비행 순찰을 유지하고 준비된 공격(3방향 레이저·유도탄·내려찍기) 중 가장 오래 쉬었던 패턴을 예약한다.
// 이력이 같으면 레이저 → 유도탄 → 내려찍기 순서다.
// 공격 후보 평가(사격선·바닥·천장·벽 광선과 내려찍기 자리의 몸 겹침 검사)는 비싸므로, 유지 시간이 끝난 첫 프레임에 한 번 바로 평가하고
// 그 뒤에는 순찰 SO의 candidateCheckInterval(기본 0.2초)마다 다시 평가한다. 그 사이에는 '후보 없음' 결과를 유지한다.
public sealed class AegisPatrolAbility : FlyingPatrolAbility
{
    [FormerlySerializedAs("pulseAbility")]
    [SerializeField] private FlyingAbility laserAbility;
    [SerializeField] private FlyingAbility missileAbility;
    [SerializeField] private FlyingAbility downAttackAbility;
    private FlyingAbility selected;
    private EnemyCheckTimer candidateTimer;

    // 검증용 읽기 전용 상태: 이번 순찰에서 후보를 평가한 횟수와, 유지 시간이 끝난 뒤 첫 평가까지 걸린 시간(초, 아직 없으면 -1).
    public int CandidateCheckCount { get; private set; }
    public float FirstCandidateCheckDelay { get; private set; } = -1f;

    private float CandidateCheckInterval => data is FlyingPatrolDataSO patrol ? patrol.candidateCheckInterval : 0f;

    protected override void OnWindupStart()
    {
        selected = null;
        candidateTimer.Reset();
        CandidateCheckCount = 0;
        FirstCandidateCheckDelay = -1f;
        ResetPatrolSide();
        base.OnWindupStart();
    }

    protected override bool CanFinishExecute(float elapsed)
    {
        if (!HasTarget || elapsed < HoldDuration) return false;
        if (!candidateTimer.Due(CandidateCheckInterval))
        {
            EnemyCheckStats.Add(EnemyCheckKind.AegisCandidateSkipped);
            return false;
        }

        EnemyCheckStats.Add(EnemyCheckKind.AegisCandidate);
        CandidateCheckCount++;
        if (FirstCandidateCheckDelay < 0f) FirstCandidateCheckDelay = elapsed - HoldDuration;
        selected = null;
        Consider(laserAbility);
        Consider(missileAbility);
        Consider(downAttackAbility);
        return selected != null;
    }

    // 먼저 넣은 후보가 동점에서 이기도록 더 오래 쉰 경우에만 바꾼다.
    private void Consider(FlyingAbility candidate)
    {
        if (candidate == null || !candidate.IsReady() || !candidate.CanBegin()) return;
        if (selected == null || candidate.LastSelectionOrder < selected.LastSelectionOrder)
            selected = candidate;
    }

    protected override void OnStop()
    {
        base.OnStop();
        if (HasTarget) ReserveNext(selected);
        selected = null;
    }
}
