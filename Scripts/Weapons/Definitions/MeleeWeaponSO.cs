using System;
using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName ="Weapon/Melee")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "MeleeWeapon")]
public class MeleeWeaponSO : WeaponBaseSO
{
    public MeleeComboStep[] comboSteps;
    public float comboResetTime = 0.6f;

    [Header("Combo Chain")]
    [Tooltip("켜면 실행 끝·회수 중에 들어온 다음 타격 입력을 남은 시간을 기다리지 않고 바로 잇는다(칼처럼 빠른 연속 베기). 끄면 회수 동작을 끝까지 보인 뒤 잇는다(해머처럼 묵직한 무기).")]
    public bool chainWithoutRecover;

    [Header("Multi Hit")]
    [Tooltip("콤보 한 단계에서 판정이 열리는 횟수. 2면 단계마다 판정 두 개를 차례로 쓴다(이도류 칼: 주검·부검). 피해와 적중 Feedback은 열릴 때마다 그 단계의 값을 쓴다. 판정 묶음은 단계 × 이 수만큼, 단계 순서대로 있어야 한다.")]
    [Min(1)] public int hitsPerStep = 1;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        yield return null;
    }
    public void BindMeleeHitboxes(MeleeHitbox meleeHitbox, CharacterController2D controller, int currentComboIndex, int popupGroup = 0)
    {
        // 현재 콤보 스텝

        MeleeComboStep step = comboSteps[currentComboIndex];
        var info = WithSource(step.damageInfo, controller);
        info.popupGroup = popupGroup;
        meleeHitbox.Initialize(controller, info);
        meleeHitbox.SetHitFeedback(step.hitFeedbackData, (owner, hitPoint) => OnHitEnemy?.Invoke(owner, hitPoint));
    }
}
