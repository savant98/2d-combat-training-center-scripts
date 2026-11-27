using System;
using System.Collections;
using UnityEngine;

// 레일건 충전(Railgun 무장 Gear 1단계). 실행 첫 프레임에 PlayerWeaponTraits.ChargeRail(과열 게이지 +30% + ChargeDuration(10초) 충전, 완료 큐 RailCharge)을 부른다.
// 2026-11-21 무장 특성 3차(사용자 요청 "레일건 충전 시 효과·Animation도 다른 레일건 무장과 톤을 맞춰" — 사용자 검수 전 시안):
// - 준비 시작에 충전 연출(chargeUpFeedback: 둘레에서 코어로 모이는 분홍·청록 빛 알갱이, 코일 고리 세 개가 뒤에서 앞으로 차례로 켜짐)을
//   레일건 코어(모형 중심)에서 보는 쪽으로 돌려 재생하고, 총열 덧그림(RailgunBarrelVisual)에 준비 시간을 넘겨 코일·전류가 함께 차오르게 한다.
// - 모형 동작은 전용 Clip(Player_RailgunCharge_*, tools/Unity/RailgunTraitSetup.cs)이 맡는다. 실행 순간의 짧은 섬광·흔들림은 RailCharge 큐(표)다.
// - 4차(2026-11-21, 사용자 요청 "레일건 충전 지속시간도 이펙트로 표시"): 준비 시작에 Player 몸 둘레의 충전 지속 표시(RailChargeAura)를
//   없으면 붙여 둔다. 충전이 걸리면 그 표시가 궤도를 펼치고 남은 초만큼의 칸으로 남은 시간을 보여 준다.
public sealed class RailgunChargeWeaponSO : WeaponBaseSO
{
    public override string PresentationWeaponName => "Railgun";

    [Tooltip("준비 시작에 레일건 코어에서 보는 쪽으로 돌려 재생하는 충전 연출(소리 칸은 음원 담당이 채운다)")]
    public FeedbackDataSO chargeUpFeedback;

    public override void OnWindupStarted(CharacterController2D controller)
    {
        if (controller == null) return;
        var aim = RailgunWeaponSO.FindAim(controller);
        Vector3 core = aim != null ? aim.transform.position : controller.transform.position;
        if (chargeUpFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(chargeUpFeedback, core, Quaternion.Euler(0f, 0f, controller.Facing < 0 ? 180f : 0f));
        if (aim != null) aim.Barrel.BeginChargeUp(windupTime);
        RailChargeAura.Ensure(controller);
    }

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        var attack = WeaponTraitEffects.Attack(controller);
        if (attack != null)
        {
            RailChargeAura.Ensure(controller);
            attack.Traits.ChargeRail();
        }
        yield break;
    }
}
