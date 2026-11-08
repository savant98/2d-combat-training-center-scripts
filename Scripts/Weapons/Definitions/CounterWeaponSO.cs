using UnityEngine;
using System.Collections;
using System;

[CreateAssetMenu(menuName = "Weapon/Counter")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "CounterWeapon")]
public class CounterWeaponSO : WeaponBaseSO
{
    [SerializeField] private MeleeHitbox counterHitbox;
    [SerializeField] private DamageInfo counterDamageInfo;
    [SerializeField] private FeedbackDataSO counterHitFeedback;   // 충격파에 맞은 적 위치의 효과

    [SerializeField] private float counterWindowTime = 0.25f;
    [SerializeField] private float counterHitboxDelay = 0.1f;
    // 반격 성공 순간부터의 무적 시간(초). 회수 동작(recoverTime)이 끝난 뒤에도 이어져 그 차이만큼 판정 밖으로 빠져나갈 수 있다.
    [SerializeField] private float successInvincibleTime = 1f;
    [SerializeField] private CounterShield shieldPrefab;

    private bool counterSucceeded;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        counterSucceeded = false;

        PlayerController playerController = controller as PlayerController;

        if (playerController == null)
            yield break;

        PlayerHealth health = playerController.Health;
        PlayerCounterState counter = playerController.PlayerCounterState;

        if (health == null || counter == null)
            yield break;

        CounterShield shield = SpawnShield(controller, counter);

        counter.StartCounter((hitPosition) =>
        {
            if (!IsStillAttackState(playerController))
            {
                counter.EndCounter();
                return;
            }

            counterSucceeded = true;
            // 이 순간부터 무적 시간을 잰다. 끝내는 일은 PlayerCounterState가 맡으므로 공격 상태가 먼저 끝나도 이어진다.
            counter.BeginInvulnerability(health, successInvincibleTime);
            if (shield != null) shield.Parry(hitPosition, counterHitboxDelay);
            forceRecover?.Invoke();
        });

        float timer = 0f;

        while (timer < counterWindowTime && !counterSucceeded)
        {
            if (!IsStillAttackState(playerController))
            {
                counter.EndCounter();
                if (shield != null) shield.Expire();
                yield break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        if (!counterSucceeded)
        {
            counter.EndCounter();
            if (shield != null) shield.Expire();
            yield break;
        }

        // 쉴드가 타격을 받아 수축한 뒤 충격파로 터진다. 이미 성공한 반격이므로 그사이 대시로 취소해도 충격파는 나간다.
        float hitboxSpawnTime = Time.time + counterHitboxDelay;

        while (Time.time < hitboxSpawnTime)
            yield return null;

        SpawnCounterHitbox(controller);
        if (shield != null) shield.Release();

        counter.EndCounter();
    }

    private bool IsStillAttackState(PlayerController playerController)
    {
        return playerController.AbilityManager.StateMachine.CurrentState == PlayerState.Attack;
    }

    private CounterShield SpawnShield(CharacterController2D controller, PlayerCounterState counter)
    {
        if (shieldPrefab == null || WorldEntityPool.Instance == null)
            return null;

        CounterShield shield = WorldEntityPool.Instance.Spawn(shieldPrefab, controller.transform.position, Quaternion.identity);
        shield.Begin(controller, counter);
        return shield;
    }

    private void SpawnCounterHitbox(CharacterController2D controller)
    {
        MeleeHitbox hitbox = WorldEntityPool.Instance.Spawn(
            counterHitbox,
            controller.transform.position,
            Quaternion.identity);

        hitbox.SetHitFeedback(counterHitFeedback);
        hitbox.Initialize(controller, WithSource(counterDamageInfo));
    }

    public override bool ShouldEnterRecover()
    {
        return counterSucceeded;
    }

    public override bool CanForceRecoverOnSuccess()
    {
        return true;
    }
}