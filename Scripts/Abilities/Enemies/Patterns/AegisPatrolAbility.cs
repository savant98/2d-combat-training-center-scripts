using UnityEngine;
using UnityEngine.Serialization;

// Drone의 비행 순찰을 유지하고 준비된 공격(3방향 레이저·유도탄·내려찍기) 중 가장 오래 쉬었던 패턴을 예약한다.
// 이력이 같으면 레이저 → 유도탄 → 내려찍기 순서다.
public sealed class AegisPatrolAbility : FlyingPatrolAbility
{
    [FormerlySerializedAs("pulseAbility")]
    [SerializeField] private FlyingAbility laserAbility;
    [SerializeField] private FlyingAbility missileAbility;
    [SerializeField] private FlyingAbility downAttackAbility;
    private FlyingAbility selected;

    protected override void OnWindupStart()
    {
        selected = null;
        ResetPatrolSide();
        base.OnWindupStart();
    }

    protected override bool CanFinishExecute(float elapsed)
    {
        if (!HasTarget || elapsed < HoldDuration) return false;
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
