using UnityEngine;
using UnityEngine.Serialization;

// Drone의 비행 순찰을 유지하고 준비된 공격 중 가장 오래 쉬었던 패턴을 예약한다.
public sealed class AegisPatrolAbility : FlyingPatrolAbility
{
    [FormerlySerializedAs("pulseAbility")]
    [SerializeField] private FlyingAbility laserAbility;
    [SerializeField] private FlyingAbility missileAbility;
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
        bool laser = laserAbility != null && laserAbility.IsReady() && laserAbility.CanBegin();
        bool missile = missileAbility != null && missileAbility.IsReady() && missileAbility.CanBegin();
        selected = laser && (!missile || laserAbility.LastSelectionOrder <= missileAbility.LastSelectionOrder)
            ? laserAbility : missile ? missileAbility : null;
        return selected != null;
    }

    protected override void OnStop()
    {
        base.OnStop();
        if (HasTarget) ReserveNext(selected);
        selected = null;
    }
}
