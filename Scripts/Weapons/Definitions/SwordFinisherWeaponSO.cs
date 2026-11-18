using System;
using System.Collections;
using UnityEngine;

// 일격필살(2026-11-18). 칼(Sword) 무장의 Gear 3단계 기술.
// 사용자 지시: 무장 4종 정리 — Sword 무장은 기본 Sword / 1 돌진 베기 / 2 없음 / 3 일격필살(새로 만듦).
//   "현재 개발되지 않은 무장들은 개발된 무장을 참고해서 만들어봐" — 돌진 베기(일섬: 지나간 뒤 늦게 베임 + 납도)와 같은 미감으로,
//   돌진하지 않고 제자리에서 칼을 검집에 넣은 채 힘을 모은 뒤(준비 = 차지), 발도와 동시에 화면을 가르고, 한 박자 늦게 크게 벤다.
// 2차(같은 날): "Sword의 일격필살의 경우 화면을 반으로 가르는 연출(normal map이나 카메라 활용)을 하고, 화면 전체에 데미지를 주도록 해줘.
//   화면을 반으로 가름, 둘로 쪼개지는게 보임. 그 후 자연스럽게 참격이 보임. 화면을 15도 대각선으로 자르면 됨."
//   → 선은 벽에서 멈추지 않고 화면 끝에서 끝까지 15도로 그어지고, 화면(메인 카메라)이 그 선을 따라 갈라졌다 붙은 뒤 화면 안의 모든 적이 맞는다.
// - 준비(windupTime)는 차지다. 지면 평탄화(HammerSkill)처럼 긴 준비 + 준비 Feedback(화면 후처리·차지 소리)으로 힘을 모으는 느낌을 준다.
//   준비하는 동안 PlayerWeaponAbility가 중력과 속도를 끄므로 공중에서도 그 자리에 머문다(위치를 직접 바꾸지 않는다).
// - 실행이 시작되는 순간(발도) SwordFinisherSlash(풀 개체)를 꺼낸다. 선 긋기·화면 가르기·늦게 베기·판정·그림은 그 개체가 맡는다.
//   발도가 끝나기 전에 공격이 끊기면(피격·잡기) 그 개체가 스스로 공격 상태를 보고 정리한다(코루틴에 기대지 않는다).
// - 게이지 보상: 적중당 보상(skillGainPerHit)이 화면 안의 적 수만큼 쌓이면 비용이 거의 다 돌아온다. 한 번 쓸 때 되찾는 합계를
//   maxGaugeRewardPerUse 이하로 묶는다(보상을 받는 적 수 = 상한 ÷ 적중당 보상, 내림).
// - 연출은 칼 모형으로 한다(어느 세트에 넣어도 칼을 뽑아 든다).
[CreateAssetMenu(menuName = "Weapon/SwordFinisher")]
public class SwordFinisherWeaponSO : WeaponBaseSO
{
    [Header("Slash")]
    [Tooltip("발도 순간 꺼내는 일섬(가르는 선·화면 가르기·참격과 판정).")]
    [SerializeField] private SwordFinisherSlash slashPrefab;
    [Tooltip("가르는 선이 지나는 자리(Player 기준, 칸 — 칼 높이). x는 보는 방향으로 뒤집는다.")]
    [SerializeField] private Vector2 spawnOffset = Vector2.zero;
    [Tooltip("베는 대상 레이어. Player 레이저와 같은 값.")]
    [SerializeField] private LayerMask damageMask;

    [Header("Gauge")]
    [Tooltip("한 번 쓸 때 적중 보상으로 되찾는 게이지 합계의 상한. 적중당 보상이 적 수만큼 쌓여 비용이 다 돌아오지 않게 한다.")]
    [SerializeField, Min(0f)] private float maxGaugeRewardPerUse = 30f;

    [Header("Feedback")]
    [Tooltip("베인 적마다 그 자리에서 재생한다.")]
    [SerializeField] private FeedbackDataSO hitFeedback;
    [Tooltip("선이 다 그어져 화면이 갈라지기 시작할 때 한 번 — 거의 멈춘 시간(히트스톱, 실제 시간).")]
    [SerializeField] private FeedbackDataSO splitFeedback;
    [Tooltip("베는 순간 한 번(적이 없어도) — 히트스톱·큰 흔들림·화면 Flash.")]
    [SerializeField] private FeedbackDataSO impactFeedback;
    [Tooltip("베는 순간 직전, 칼을 칼집에 집어넣는 납도 소리.")]
    [SerializeField] private FeedbackDataSO sheatheFeedback;

    public SwordFinisherSlash SlashPrefab => slashPrefab;
    public Vector2 SpawnOffset => spawnOffset;
    public LayerMask DamageMask => damageMask;
    public float MaxGaugeRewardPerUse => maxGaugeRewardPerUse;
    // 한 번 쓸 때 게이지 보상을 받는 적의 수(적중당 보상이 0이면 제한 없음)
    public int RewardedHitLimit => skillGainPerHit <= 0f ? int.MaxValue : Mathf.FloorToInt(maxGaugeRewardPerUse / skillGainPerHit + 1e-4f);

    public override string PresentationWeaponName => "Sword";
    public override bool RecoverEndsAtIdle => true;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (controller == null || slashPrefab == null || WorldEntityPool.Instance == null)
            yield break;

        int facing = controller.Facing >= 0 ? 1 : -1;
        Vector3 origin = controller.transform.position + new Vector3(spawnOffset.x * facing, spawnOffset.y, 0f);
        SwordFinisherSlash slash = WorldEntityPool.Instance.Spawn(slashPrefab, origin, Quaternion.identity);
        slash.SetHit(hitFeedback, splitFeedback, impactFeedback, sheatheFeedback, RewardedHitLimit, (owner, point) => OnHitEnemy?.Invoke(owner, point));
        slash.Initialize(controller, WithSource(damageInfo));   // 밀리는 방향은 발도한 자리와 방향을 기준으로 한다
        slash.Begin(facing, damageMask);
        // 실행 시간이 끝나면 PlayerWeaponAbility가 회수(납도)로 넘긴다. 화면 가르기·베는 순간은 개체가 실제 시간으로 스스로 잰다.
    }
}
