using UnityEngine;

// 같은 웨이브에서 플레이어가 맞으면 체력을 되돌리고, 화면을 빠르게 어둡게 한 뒤 이 위치에서 다시 시작하게 한다.
// 암전 동안 입력을 막아 맞은 뒤에도 계속 움직이다가 갑자기 돌아가는 느낌이 나지 않게 하고, 카메라도 옮긴 위치로 바로 따라가게 한다.
// courseGrinderOnly를 켜면 훈련 코스 추격 분쇄기에 닿았을 때만 되돌리고, 코스에 놓인 적에게 맞은 피해는 일반 피해로 남긴다.
// 암전·이동은 추격 분쇄기와 같은 WavePlayerReturn을 쓴다. 이 장치가 켜져 있는 Wave에서는 분쇄기가 직접 옮기지 않는다(AnyActive).
public class TutorialPositionResetter : WorldEntityBase
{
    private static int activeCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => activeCount = 0;

    /// <summary>지금 켜져 있는 위치 초기화 장치가 있으면 true다. 추격 분쇄기는 이때 플레이어를 직접 옮기지 않는다.</summary>
    public static bool AnyActive => activeCount > 0;

    [SerializeField] private string playerTransitionTriggerName = "";
    [SerializeField, Tooltip("켜면 훈련 코스 추격 분쇄기(ChasingGrinder)에 닿았을 때만 되돌립니다. 끄면 어떤 피해든 되돌립니다(튜토리얼).")]
    private bool courseGrinderOnly;
    [SerializeField, Min(0f), Tooltip("맞은 뒤 화면이 완전히 어두워지기까지의 시간(초)입니다.")]
    private float fadeOutDuration = .15f;
    [SerializeField, Min(0f), Tooltip("어두운 상태로 머무는 시간(초)입니다. 이때 시작 위치로 옮긴다.")]
    private float blackHold = .15f;
    [SerializeField, Min(0f), Tooltip("다시 밝아지는 시간(초)입니다.")]
    private float fadeInDuration = .3f;

    private PlayerHealth subscribedHealth;
    private readonly WavePlayerReturn playerReturn = new WavePlayerReturn();
    private Coroutine returnRoutine;

    public bool IsResetting => playerReturn.IsRunning;
    public bool CourseGrinderOnly => courseGrinderOnly;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        Unsubscribe();
        subscribedHealth = GameManager.Instance.Player.Health;
        subscribedHealth.OnDamaged += HandleTutorialFail;
        subscribedHealth.OnPlayerDeath += HandlePlayerDeath;
    }

    public override void ForceReturnToPool()
    {
        Unsubscribe();
        RecoverScreen();
        base.ForceReturnToPool();
    }

    private void HandleTutorialFail(WeaponBaseSO.DamageInfo damageInfo)
    {
        // 배치 함정은 지정 피해를 유지하고 자체 복귀 경로를 사용한다.
        if (WaveTrapDamage.IsApplying) return;
        // 코스 분쇄기만 되돌리는 경우, 다른 피해(코스 적)는 회복하지 않고 그 자리에서 이어 간다.
        if (courseGrinderOnly && !ChasingGrinder.IsGrinderHit) return;
        // 이 피격으로 패배가 확정됐으면(훈련 성적 미흡 자동 사망) 회복·되돌림을 하지 않는다. 사망 연출과 겹치지 않게 한다.
        if (subscribedHealth != null && (subscribedHealth.IsDead || subscribedHealth.IsDeathPending)) return;
        // 받은 데미지 복구
        GameManager.Instance.Player.Health.Heal(damageInfo);
        if (playerReturn.IsRunning || !isActiveAndEnabled) return;
        returnRoutine = StartCoroutine(playerReturn.Run(GameManager.Instance.Player, transform.position, !string.IsNullOrEmpty(playerTransitionTriggerName),
            fadeOutDuration, blackHold, fadeInDuration));
    }

    // 되돌리는 도중 플레이어가 사망하면(제한 시간 초과 등) 되돌림을 멈추고 화면과 입력을 돌려준다.
    private void HandlePlayerDeath()
    {
        if (!playerReturn.IsRunning) return;
        if (returnRoutine != null) StopCoroutine(returnRoutine);
        returnRoutine = null;
        RecoverScreen();
    }

    // 다시 밝게 하고 막았던 입력을 돌려준다. 회수·비활성화로 연출이 끊겨도 화면과 입력이 남지 않게 한다.
    private void RecoverScreen() => playerReturn.Recover(fadeInDuration);

    private void Unsubscribe()
    {
        if (subscribedHealth != null)
        {
            subscribedHealth.OnDamaged -= HandleTutorialFail;
            subscribedHealth.OnPlayerDeath -= HandlePlayerDeath;
        }
        subscribedHealth = null;
    }

    private void OnEnable() => activeCount++;

    private void OnDisable()
    {
        activeCount = Mathf.Max(0, activeCount - 1);
        Unsubscribe();
        RecoverScreen();
    }
}
