using System.Collections;
using UnityEngine;

public class TutorialPositionResetter : WorldEntityBase
{
    [SerializeField] private string playerTransitionTriggerName = "";
    [SerializeField] private float delay = 0.5f;


    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        GameManager.Instance.Player.Health.OnDamaged += HandleTutorialFail;
    }

    public override void ForceReturnToPool()
    {
        if (GameManager.Instance != null && GameManager.Instance.Player != null)
        {
            GameManager.Instance.Player.Health.OnDamaged -= HandleTutorialFail;
        }

        base.ForceReturnToPool();
    }

    private void HandleTutorialFail(WeaponBaseSO.DamageInfo damageInfo)
    {
        // 받은 데미지 복구
        GameManager.Instance.Player.Health.Heal(damageInfo);
        StartCoroutine(ReturnBasePointTransition());
    }

    private IEnumerator ReturnBasePointTransition()
    {
        yield return new WaitForSeconds(delay);

        // 강제 이동
        GameManager.Instance.Player.transform.position = transform.position;
        GameManager.Instance.Player.Velocity = Vector2.zero;
        GameManager.Instance.Player.animator.SetTrigger(playerTransitionTriggerName);
    }
}
