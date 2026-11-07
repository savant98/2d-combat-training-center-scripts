using UnityEngine;

// 기존 CharacterController2D의 모터 이동 직전에 Ability의 속도만 공급한다.
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(FlyingBombChargeAbility))]
public class BomberFlightMotion : MonoBehaviour
{
    private FlyingBombChargeAbility ability;
    private void Awake() => ability = GetComponent<FlyingBombChargeAbility>();
    private void FixedUpdate() => ability.PrepareMotorStep();
}
