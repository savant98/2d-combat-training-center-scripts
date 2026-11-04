using UnityEngine;

// 플레이어 쪽으로 방향을 정하고 제자리에서 바퀴를 공회전시킨 뒤 짧게 가속하는 돌진 패턴이다.
// 준비 동작은 공회전을 여러 번 끊어 올리는 방식이다. rev 시점·회전·달아오르는 색·방향 화살표는 AnimationClip이 정하고,
// 이 패턴은 rev마다 바퀴 뒤로 튀는 불씨·바닥 불꽃과 차지 소리, 발진 순간의 돌진 소리, 이동을 맡는다.
// 소리는 데이터의 Clip을 그대로 재생하며 음높이·재생 속도를 바꾸지 않는다. 공회전 소리(rev마다 하나)와 돌진 소리를 나누고,
// 돌진 소리는 발진 순간 한 번 재생한다. 벽에 막히거나 미끄러짐이 끝났는데 남아 있으면 짧게 줄여 끝내 움직임이 멈추면 소리도 멈춘다.
// 이동은 EnemyController.Velocity만 사용하고, 돌진 중 벽에 막히면 RaycastMotor2D의 충돌 결과(OnWallHit)로 즉시 후딜레이로 넘어간다.
// 발진부터 정지까지의 거리는 chargeDistance로 맞춘다. 남은 제동 거리까지 더해 목표에 닿으면 속도는 그대로 둔 채 제동으로 넘어간다.
public class BurstChargeAbility : AiAbility
{
    private BurstChargeDataSO chargeData;
    private LaserPreviewLine activePreview;
    private bool wallHitSubscribed;

    private int chargeDirection = 1;
    private float chargeSpeed;
    private float brakeStartSpeed;
    private float sparkTimer;

    private SoundHandle dashSoundHandle;
    private float dashSoundVolume;
    private float dashFadeTimer = -1f;

    private float executeStartX;
    private bool chargeDistanceReached;

    public int ChargeDirection => chargeDirection;
    public float ChargeSpeed => chargeSpeed;
    public bool StoppedByWall { get; private set; }
    public int RevCount { get; private set; }
    public int DashSoundCount { get; private set; }
    public bool DashSoundPlaying => dashSoundHandle != null && dashSoundHandle.IsValid;
    public bool DashSoundFading => DashSoundPlaying && dashFadeTimer >= 0f;

    private void Awake()
    {
        chargeData = (BurstChargeDataSO)data;
    }

    protected override void OnWindupStart()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        // 돌진 속도는 플레이어 대시 속도(PlayerDashDataSO.dashForce, 칸/초)의 비율로 정한다.
        chargeSpeed = chargeData.playerDashData != null
            ? chargeData.playerDashData.dashForce * chargeData.playerDashSpeedRatio
            : chargeData.fallbackSpeed;

        // 준비 동작 시작 시 방향을 고정해 방향 화살표와 실제 돌진 방향을 일치시킨다.
        if (player != null)
        {
            float deltaX = player.transform.position.x - transform.position.x;
            chargeDirection = Mathf.Abs(deltaX) > 0.01f ? (deltaX > 0f ? 1 : -1) : controller.Facing;
        }
        else
        {
            chargeDirection = controller.Facing;
        }

        controller.FacingUpdate(chargeDirection);
        brakeStartSpeed = Mathf.Abs(controller.Velocity.x);
        StoppedByWall = false;
        RevCount = 0;
        sparkTimer = 0f;

        SetWallHitSubscription(true);
        SpawnPreview();
    }

    protected override void OnWindup(float t)
    {
        // 순찰하던 속도를 짧게 멈추고 제자리에서 공회전한다.
        Brake(chargeData.windupBrakeTime);

        UpdatePreview();
        activePreview?.UpdatePreviewVisual(data.windupTime - t, data.windupTime);
    }

    // Windup Clip의 OnChargeRev 이벤트(BurstChargeAnimationRelay)로 호출된다.
    // rev마다 바퀴 뒤 바닥에서 불씨·불꽃이 튀고, rev 순서에 맞는 차지 소리 조각을 낸다(이어 들으면 올라가는 한 차지).
    public void PlayRev(int index)
    {
        if (State != AbilityState.Windup)
            return;

        RevCount++;
        PlayKick(chargeData.revFeedback);
        PlaySparkNow();
        PlaySound(RevSound(index));
    }

    protected override void OnExecuteStart()
    {
        ReturnPreview();
        controller.Velocity.x = 0f;
        sparkTimer = 0f;

        // 발진: 마지막으로 크게 튀고 돌진 소리를 한 번 낸다.
        PlayKick(chargeData.revFeedback);
        PlayDashSound();
        executeStartX = transform.position.x;
        chargeDistanceReached = false;
    }

    protected override void OnExecute(float t)
    {
        if (StoppedByWall)
        {
            controller.Velocity.x = 0f;
            FadeDashSound();
            UpdateDashSound();
            return;
        }

        UpdateDashSound();

        // 목표 거리에 닿으면 지금 속도를 그대로 두고 제동으로 넘긴다(CanFinishExecute). 속도는 줄이지 않는다.
        if (ReachedChargeDistance())
        {
            chargeDistanceReached = true;
            PlaySpark();
            return;
        }

        float acceleration = chargeSpeed / Mathf.Max(0.01f, chargeData.accelerationTime);
        controller.Velocity.x = Mathf.MoveTowards(
            controller.Velocity.x,
            chargeDirection * chargeSpeed,
            acceleration * Time.deltaTime);

        PlaySpark();
    }

    // Execute 시간이 끝나기 전이라도 목표 거리에 닿으면 AiAbility의 흐름대로 Recover로 넘어간다.
    protected override bool CanFinishExecute(float elapsed) => chargeDistanceReached || base.CanFinishExecute(elapsed);

    // 지금까지 간 거리와 지금 속도에서 제동(Brake, recoverBrakeTime 동안 일정하게 감속)으로 더 미끄러질 거리의 합이
    // chargeDistance에 닿았는지 본다. 이렇게 하면 속도·프레임 수와 관계없이 발진부터 정지까지의 거리가 거의 일정하다.
    // 이 프레임에 Recover로 넘어가도 감속은 다음 프레임 OnRecover부터 걸리므로, 한 프레임 동안 최고 속도로 더 가는 거리도 더한다.
    private bool ReachedChargeDistance()
    {
        if (chargeData.chargeDistance <= 0f)
            return false;

        float speed = Mathf.Abs(controller.Velocity.x);
        float traveled = Mathf.Abs(transform.position.x - executeStartX);
        float transitionFrame = speed * Time.deltaTime;
        float brakeDistance = speed * chargeData.recoverBrakeTime * 0.5f;
        return traveled + transitionFrame + brakeDistance >= chargeData.chargeDistance;
    }

    protected override void OnRecoverStart()
    {
        brakeStartSpeed = Mathf.Abs(controller.Velocity.x);
    }

    protected override void OnRecover(float t)
    {
        // 바퀴가 잠긴 채 미끄러지는 동안 바닥에서 불꽃이 튄다. 미끄러짐이 끝났는데 돌진 소리가 남아 있으면 줄여 끝낸다.
        Brake(chargeData.recoverBrakeTime);
        bool skidding = Mathf.Abs(controller.Velocity.x) >= chargeData.skidSparkMinSpeed;
        if (skidding)
            PlaySpark();
        else
            FadeDashSound();
        UpdateDashSound();
    }

    protected override void OnStop()
    {
        ReturnPreview();
        SetWallHitSubscription(false);
        StopDashSound();
        controller.Velocity.x = 0f;
    }

    private void OnDisable()
    {
        // 패턴이 끝나기 전에 적이 풀로 돌아가거나 비활성화되면 돌진 소리를 바로 멈춘다.
        StopDashSound();
    }

    private void OnDestroy()
    {
        SetWallHitSubscription(false);
        StopDashSound();
    }

    private void Brake(float brakeTime)
    {
        float deceleration = Mathf.Max(0.01f, brakeStartSpeed) / Mathf.Max(0.01f, brakeTime);
        controller.Velocity.x = Mathf.MoveTowards(controller.Velocity.x, 0f, deceleration * Time.deltaTime);
    }

    private void HandleWallHit()
    {
        if (State != AbilityState.Execute && State != AbilityState.Recover)
            return;

        // 돌진 방향으로 민 이동이 벽에 막힌 경우만 처리한다.
        float push = controller.Velocity.x + (controller.IgnoreExternalForces ? 0f : controller.externalForce.x);
        if (Mathf.Abs(push) < 0.001f || (push > 0f ? 1 : -1) != chargeDirection)
            return;

        controller.Velocity.x = 0f;
        FadeDashSound();

        if (State == AbilityState.Execute)
        {
            StoppedByWall = true;
            ForceRecover();
        }
    }

    // ===== 불씨·불꽃·소리 =====
    private Vector3 KickOrigin => executePoint != null ? executePoint.position : transform.position;

    // 효과 Prefab은 오른쪽을 볼 때 기준으로 만들고, 왼쪽을 보면 Y축 180도 회전으로 좌우를 뒤집는다.
    private Quaternion FacingRotation => chargeDirection < 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

    private void PlayKick(FeedbackDataSO feedback)
    {
        if (feedback == null || feedbackPlayer == null)
            return;

        Vector3 offset = feedback.offset;
        if (chargeDirection < 0)
            offset.x *= -1f;
        feedbackPlayer.PlayExact(feedback, KickOrigin + offset, FacingRotation);
    }

    private void PlaySpark()
    {
        sparkTimer -= Time.deltaTime;
        if (sparkTimer > 0f)
            return;

        PlaySparkNow();
    }

    private void PlaySparkNow()
    {
        sparkTimer = chargeData.sparkInterval;
        PlayKick(chargeData.sparkFeedback);
    }

    // 돌진 소리는 도중에 줄여 끝낼 수 있도록 핸들을 돌려주는 SoundManager.PlayLoop로 재생하고 반복만 끈다.
    // 소리는 적을 따라가며, 적이 풀로 돌아가 비활성화되면 SoundManager가 멈춘다. 끝까지 재생되면 OnStop 전에도 바로 반환한다.
    private void PlayDashSound()
    {
        StopDashSound();
        SoundOptions sound = chargeData.dashSound;
        if (sound == null || sound.clip == null || SoundManager.Instance == null)
            return;

        dashSoundHandle = SoundManager.Instance.PlayLoop(sound, KickOrigin, transform);
        if (dashSoundHandle == null)
            return;

        AudioSource source = dashSoundHandle.GetSource();
        source.loop = false;
        dashSoundVolume = source.volume;
        dashFadeTimer = -1f;
        DashSoundCount++;
    }

    // 벽에 막혔거나 미끄러짐이 끝났을 때 남은 돌진 소리를 dashSoundStopFade 동안 줄인다. 이미 줄이는 중이면 그대로 둔다.
    private void FadeDashSound()
    {
        if (DashSoundPlaying && dashFadeTimer < 0f)
            dashFadeTimer = chargeData.dashSoundStopFade;
    }

    private void UpdateDashSound()
    {
        if (dashSoundHandle == null)
            return;

        // 다른 곳에서 SFX를 모두 멈춰 이미 반환된 경우
        if (!dashSoundHandle.IsValid)
        {
            dashSoundHandle = null;
            return;
        }

        AudioSource source = dashSoundHandle.GetSource();
        if (!source.isPlaying)
        {
            StopDashSound();
            return;
        }

        if (dashFadeTimer < 0f)
            return;

        dashFadeTimer -= Time.deltaTime;
        if (dashFadeTimer <= 0f)
        {
            StopDashSound();
            return;
        }

        source.volume = dashSoundVolume * (dashFadeTimer / chargeData.dashSoundStopFade);
    }

    private void StopDashSound()
    {
        if (dashSoundHandle != null && dashSoundHandle.IsValid)
            dashSoundHandle.Stop();

        dashSoundHandle = null;
        dashFadeTimer = -1f;
    }

    private SoundOptions RevSound(int index)
    {
        SoundOptions[] sounds = chargeData.revSounds;
        if (sounds == null || sounds.Length == 0)
            return null;
        return sounds[Mathf.Clamp(index, 0, sounds.Length - 1)];
    }

    private void PlaySound(SoundOptions sound)
    {
        if (sound == null || sound.clip == null || SoundManager.Instance == null)
            return;

        SoundManager.Instance.PlaySound(sound, KickOrigin);
    }

    // ===== 선택형 예고선(LaserPreviewLine) =====
    private Vector2 PreviewDirection => chargeDirection > 0 ? Vector2.right : Vector2.left;

    private Vector2 PreviewOrigin => (windupPoint != null ? (Vector2)windupPoint.position : (Vector2)transform.position)
        + PreviewDirection * chargeData.prevLineStartOffset;

    private void SpawnPreview()
    {
        if (chargeData.prevLinePrefab == null || activePreview != null || WorldEntityPool.Instance == null)
            return;

        activePreview = WorldEntityPool.Instance.Spawn(chargeData.prevLinePrefab, PreviewOrigin, Quaternion.identity);
        activePreview.Show(
            PreviewOrigin,
            PreviewDirection,
            chargeData.prevLineLength,
            chargeData.prevLineWidth,
            chargeData.prevLineBlockMask,
            chargeData.prevLineColor,
            // 같은 프레임에 수명 종료로 풀에 돌아가 다른 적에게 재사용되기 전에 이 패턴이 먼저 반환한다.
            data.windupTime + 0.1f);
    }

    private void UpdatePreview()
    {
        if (activePreview == null)
            return;

        if (!activePreview.gameObject.activeInHierarchy)
        {
            activePreview = null;
            return;
        }

        activePreview.SetOrigin(PreviewOrigin);
        activePreview.SetDirection(PreviewDirection);
    }

    private void ReturnPreview()
    {
        if (activePreview == null)
            return;

        if (activePreview.gameObject.activeInHierarchy)
            activePreview.ForceReturnToPool();

        activePreview = null;
    }

    private void SetWallHitSubscription(bool subscribe)
    {
        if (controller == null || wallHitSubscribed == subscribe)
            return;

        if (subscribe)
            controller.OnWallHit += HandleWallHit;
        else
            controller.OnWallHit -= HandleWallHit;

        wallHitSubscribed = subscribe;
    }
}
