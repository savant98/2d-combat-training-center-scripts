using System;
using System.Collections;
using UnityEngine;

// 칼 무장 Gear 3단계. 준비 단계부터 암전과 세계 정지를 소유하며 Player 시전만 실제 시간으로 진행한다.
// 실행 개체가 화면 중앙의 15도 선·균열·화면 분할·피해를 처리한 뒤 정지를 반환한다.
// 중단된 개체는 자기 시전 세대만 정리한다. 적중 게이지 보상에는 한 번 사용당 상한이 있다.
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
    [Tooltip("유리 균열이 나타날 때 한 번 재생한다.")]
    [SerializeField] private FeedbackDataSO splitFeedback;
    [Tooltip("유리 파쇄·분단·피해가 함께 시작되는 순간에 재생한다.")]
    [SerializeField] private FeedbackDataSO impactFeedback;

    public SwordFinisherSlash SlashPrefab => slashPrefab;
    public Vector2 SpawnOffset => spawnOffset;
    public LayerMask DamageMask => damageMask;
    public float MaxGaugeRewardPerUse => maxGaugeRewardPerUse;
    // 한 번 쓸 때 게이지 보상을 받는 적의 수(적중당 보상이 0이면 제한 없음)
    public int RewardedHitLimit => skillGainPerHit <= 0f ? int.MaxValue : Mathf.FloorToInt(maxGaugeRewardPerUse / skillGainPerHit + 1e-4f);

    public override string PresentationWeaponName => "Sword";
    public override bool RecoverEndsAtIdle => true;

    public override void OnWindupStarted(CharacterController2D controller) => SwordFinisherCinematic.Begin(controller);

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        if (controller == null || slashPrefab == null || WorldEntityPool.Instance == null)
            yield break;

        int facing = controller.Facing >= 0 ? 1 : -1;
        Vector3 origin = controller.transform.position + new Vector3(spawnOffset.x * facing, spawnOffset.y, 0f);
        SwordFinisherSlash slash = WorldEntityPool.Instance.Spawn(slashPrefab, origin, Quaternion.identity);
        slash.SetHit(hitFeedback, splitFeedback, impactFeedback, RewardedHitLimit, (owner, point) => OnHitEnemy?.Invoke(owner, point));
        slash.Initialize(controller, WithSource(damageInfo));   // 밀리는 방향은 발도한 자리와 방향을 기준으로 한다
        slash.Begin(facing, damageMask);
        while (slash.IsRunning && !slash.IsCut) yield return null;
        if (slash.IsCut) forceRecover?.Invoke();
    }
}
