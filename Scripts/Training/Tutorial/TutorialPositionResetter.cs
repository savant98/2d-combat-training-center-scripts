using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

// 같은 웨이브에서 플레이어가 맞으면 체력을 되돌리고, 화면을 빠르게 어둡게 한 뒤 이 위치에서 다시 시작하게 한다.
// 암전 동안 입력을 막아 맞은 뒤에도 계속 움직이다가 갑자기 돌아가는 느낌이 나지 않게 하고, 카메라도 옮긴 위치로 바로 따라가게 한다.
// courseLaserOnly를 켜면 훈련 코스 추격 레이저에 맞았을 때만 되돌리고, 코스에 놓인 적에게 맞은 피해는 일반 피해로 남긴다.
public class TutorialPositionResetter : WorldEntityBase
{
    [SerializeField] private string playerTransitionTriggerName = "";
    [SerializeField, Tooltip("켜면 훈련 코스 추격 레이저(ChasingLaserWall)에 맞았을 때만 되돌립니다. 끄면 어떤 피해든 되돌립니다(튜토리얼).")]
    private bool courseLaserOnly;
    [SerializeField, Min(0f), Tooltip("맞은 뒤 화면이 완전히 어두워지기까지의 시간(초)입니다.")]
    private float fadeOutDuration = .15f;
    [SerializeField, Min(0f), Tooltip("어두운 상태로 머무는 시간(초)입니다. 이때 시작 위치로 옮긴다.")]
    private float blackHold = .15f;
    [SerializeField, Min(0f), Tooltip("다시 밝아지는 시간(초)입니다.")]
    private float fadeInDuration = .3f;

    private PlayerHealth subscribedHealth;
    private bool blocking;
    private bool resetting;
    private bool holdingForces;

    public bool IsResetting => resetting;
    public bool CourseLaserOnly => courseLaserOnly;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        Unsubscribe();
        subscribedHealth = GameManager.Instance.Player.Health;
        subscribedHealth.OnDamaged += HandleTutorialFail;
    }

    public override void ForceReturnToPool()
    {
        Unsubscribe();
        RecoverScreen();
        base.ForceReturnToPool();
    }

    private void HandleTutorialFail(WeaponBaseSO.DamageInfo damageInfo)
    {
        // 코스 레이저만 되돌리는 경우, 다른 피해(코스 적)는 회복하지 않고 그 자리에서 이어 간다.
        if (courseLaserOnly && !ChasingLaserWall.IsCourseLaserHit) return;
        // 받은 데미지 복구
        GameManager.Instance.Player.Health.Heal(damageInfo);
        if (resetting || !isActiveAndEnabled) return;
        StartCoroutine(ReturnBasePointTransition());
    }

    private IEnumerator ReturnBasePointTransition()
    {
        resetting = true;
        var player = GameManager.Instance.Player;

        // 입력을 막아 그 자리에 멈추고 화면을 빠르게 어둡게 한다.
        if (UIManager.Instance != null)
        {
            UIManager.Instance.PushGameplayBlock();
            blocking = true;
            UIManager.Instance.StartScreenFade(1f, fadeOutDuration);
        }
        player.Velocity = Vector2.zero;
        // 맞은 반동(외부 힘)도 멈춰 암전 동안 밀려나지 않게 한다. 입력을 막아도 반동은 물리 단계마다 더해지기 때문이다.
        player.IgnoreExternalForces = true;
        player.externalForce = Vector2.zero;
        holdingForces = true;
        yield return new WaitForSeconds(fadeOutDuration);

        // 어두운 동안 시작 위치로 옮기고, 카메라가 먼 거리를 끌려오지 않게 바로 따라가게 한다.
        WavePlayerStart.Relocate(player, transform.position, !string.IsNullOrEmpty(playerTransitionTriggerName));
        yield return new WaitForSeconds(blackHold);

        RecoverScreen();
    }

    // 다시 밝게 하고 막았던 입력을 돌려준다. 회수·비활성화로 연출이 끊겨도 화면과 입력이 남지 않게 한다.
    private void RecoverScreen()
    {
        if (blocking && UIManager.Instance != null)
        {
            UIManager.Instance.StartScreenFade(0f, fadeInDuration);
            UIManager.Instance.PopGameplayBlock();
        }
        blocking = false;
        resetting = false;
        // 남은 반동 없이 외부 힘을 다시 받게 해 시작 위치에서 밀려나지 않게 한다.
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (holdingForces && player != null)
        {
            player.externalForce = Vector2.zero;
            player.IgnoreExternalForces = false;
        }
        holdingForces = false;
    }

    private void Unsubscribe()
    {
        if (subscribedHealth != null) subscribedHealth.OnDamaged -= HandleTutorialFail;
        subscribedHealth = null;
    }

    private void OnDisable()
    {
        Unsubscribe();
        RecoverScreen();
    }
}
