using UnityEngine;
using System.Collections;
using System;

[CreateAssetMenu(menuName = "Weapon/Counter")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "CounterWeapon")]
public class CounterWeaponSO : WeaponBaseSO
{
    [SerializeField] private MeleeHitbox counterHitbox;
    [SerializeField] private DamageInfo counterDamageInfo;

    [SerializeField] private float counterWindowTime = 0.25f;
    [SerializeField] private float counterHitboxDelay = 0.1f;
    [SerializeField] private float successInvincibleTime = 0.45f;

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

        health.IsInvincible = true;

        counter.StartCounter((hitPosition) =>
        {
            if (!IsStillAttackState(playerController))
            {
                counter.EndCounter();
                health.IsInvincible = false;
                return;
            }

            counterSucceeded = true;
            forceRecover?.Invoke();
        });

        float timer = 0f;

        while (timer < counterWindowTime && !counterSucceeded)
        {
            if (!IsStillAttackState(playerController))
            {
                counter.EndCounter();
                health.IsInvincible = false;
                yield break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        if (counterSucceeded)
        {
            float hitboxSpawnTime = Time.time + counterHitboxDelay;

            while (Time.time < hitboxSpawnTime)
            {
                if (!IsStillAttackState(playerController))
                {
                    counter.EndCounter();
                    health.IsInvincible = false;
                    yield break;
                }

                yield return null;
            }

            SpawnCounterHitbox(controller);

            float remainInvincibleTime = Mathf.Max(0f, successInvincibleTime - counterHitboxDelay);
            float invincibleEndTime = Time.time + remainInvincibleTime;

            while (Time.time < invincibleEndTime)
            {
                if (!IsStillAttackState(playerController))
                {
                    counter.EndCounter();
                    health.IsInvincible = false;
                    yield break;
                }

                yield return null;
            }
        }

        counter.EndCounter();
        health.IsInvincible = false;
    }

    private bool IsStillAttackState(PlayerController playerController)
    {
        return playerController.AbilityManager.StateMachine.CurrentState == PlayerState.Attack;
    }

    private void SpawnCounterHitbox(CharacterController2D controller)
    {
        MeleeHitbox hitbox = WorldEntityPool.Instance.Spawn(
            counterHitbox,
            controller.transform.position,
            Quaternion.identity);

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