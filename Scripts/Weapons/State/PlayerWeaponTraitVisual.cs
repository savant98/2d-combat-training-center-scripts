using UnityEngine;

// 무장 특성의 Player 주변 연출을 묶는다. PlayerWeaponAbility.Initialize가 Player 루트에 붙이고 Bind를 부른다(Player.prefab 무수정).
// 2026-11-21 개편: 해머 방어막 외피(HammerBarrierVisual), 작열탄 총열 가열(IncendiaryBarrelVisual),
// Player 주변 무장 특성 아이콘(PlayerTraitGaugeVisual)을 각자 컴포넌트로 나눴다.
[DefaultExecutionOrder(130)]
public sealed class PlayerWeaponTraitVisual : MonoBehaviour
{
    public HammerBarrierVisual Barrier { get; private set; }
    public IncendiaryBarrelVisual Barrel { get; private set; }
    public PlayerTraitGaugeVisual Gauge { get; private set; }

    public void Bind(PlayerController owner, PlayerWeaponAbility ability, PlayerStarShotAim aim)
    {
        Barrier = Ensure<HammerBarrierVisual>();
        Barrel = Ensure<IncendiaryBarrelVisual>();
        Gauge = Ensure<PlayerTraitGaugeVisual>();
        Barrier.Bind(owner, ability, aim);
        Barrel.Bind(owner, ability, aim);
        Gauge.Bind(owner, ability, aim);
    }

    private T Ensure<T>() where T : Component
    {
        var part = GetComponent<T>();
        return part != null ? part : gameObject.AddComponent<T>();
    }
}
