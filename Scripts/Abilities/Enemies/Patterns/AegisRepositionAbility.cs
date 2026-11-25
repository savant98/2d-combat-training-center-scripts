using UnityEngine;

// 두 공격이 끝나면 기존 Sniper Teleporter의 한 물리 틱 이동으로 자리를 다시 잡는다.
// 후보는 도착 자리뿐 아니라 순간이동 L자 경로(EnemyFlightNavigator.IsTeleportPathClear)도 열려 있어야 한다.
[RequireComponent(typeof(EnemyTeleporter))]
public sealed class AegisRepositionAbility : FlyingAbility
{
    [SerializeField] private AiAbility nextAbility;
    [SerializeField] private Renderer[] teleportRenderers;
    private EnemyTeleporter teleporter;
    private AegisRepositionDataSO settings;
    private bool[] rendererStates;
    private bool wasInvincible, ignoredForces, ownsState, finished;
    public bool IsTeleporting { get; private set; }
    public Vector2 Destination { get; private set; }
    public int CompletedCount { get; private set; }
    public override bool IsBodyAttackActive => false;

    protected override void Awake()
    {
        base.Awake();
        teleporter = GetComponent<EnemyTeleporter>();
        settings = (AegisRepositionDataSO)data;
        rendererStates = new bool[teleportRenderers.Length];
    }

    protected override void OnWindupStart()
    {
        AcquireTarget();
        controller.Velocity = Vector2.zero;
        SetFlockRole(EnemyFlockRole.None);
        finished = false;
        FaceTarget();
    }

    protected override void OnExecuteStart()
    {
        if (!TryDestination(out Vector2 point)) { finished = true; return; }
        Destination = point;
        wasInvincible = controller.Health.IsInvincible;
        ignoredForces = controller.IgnoreExternalForces;
        ownsState = true;
        for (int i = 0; i < teleportRenderers.Length; i++)
            rendererStates[i] = teleportRenderers[i] != null && teleportRenderers[i].enabled;
        controller.IgnoreExternalForces = true;
        controller.externalForce = Vector2.zero;
        IsTeleporting = true;
        teleporter.TeleportSequence(point, feedbackPlayer, settings.teleportFeedback, settings.teleportFeedback,
            settings.disappearDelay, settings.appearDelay, () =>
            {
                IsTeleporting = false;
                finished = true;
                CompletedCount++;
                RestoreState();
                controller.Velocity = Vector2.zero;
                FaceTarget();
            });
    }

    // 순간이동이 쓰는 Velocity를 Update에서 덮어쓰지 않는다.
    protected override bool CanFinishExecute(float elapsed) => finished;
    protected override void OnRecover(float t) => controller.Velocity = Vector2.zero;

    private bool TryDestination(out Vector2 point)
    {
        point = transform.position;
        if (!HasTarget || controller.Motor == null) return false;
        Vector2 player = target.position;
        int side = transform.position.x >= player.x ? -1 : 1;
        for (int h = 0; h < 3; h++)
        {
            float height = Available(player, Vector2.up, settings.playerOffset.y + h * 1.5f);
            Vector2 anchor = player + Vector2.up * height;
            for (int s = 0; s < 2; s++)
            {
                int direction = s == 0 ? side : -side;
                float distance = Available(anchor, Vector2.right * direction, settings.playerOffset.x);
                Vector2 candidate = anchor + Vector2.right * (direction * distance);
                // 순간이동은 한 물리 틱에 수평(지금 높이) → 수직(목표 x)으로 잘리므로 그 L자 경로가 열린 후보만 쓴다(칸막이 너머 후보 제외).
                if (Vector2.Distance(candidate, player) < settings.minimumDistance
                    || Vector2.Distance(candidate, transform.position) < 1f
                    || (navigator != null && !navigator.IsTeleportPathClear(transform.position, candidate, settings.avoidance))
                    || controller.Motor.IsBodyBlockedAt(candidate - (Vector2)transform.position)) continue;
                point = candidate;
                return true;
            }
        }
        return false;
    }

    private float Available(Vector2 origin, Vector2 direction, float wanted)
    {
        var hit = controller.Motor.RaycastEnvironment(origin, direction, wanted + settings.wallMargin);
        return hit.collider != null ? Mathf.Clamp(hit.distance - settings.wallMargin, 0f, wanted) : wanted;
    }

    private void RestoreState()
    {
        if (!ownsState || controller == null) return;
        for (int i = 0; i < teleportRenderers.Length; i++)
            if (teleportRenderers[i] != null) teleportRenderers[i].enabled = rendererStates[i];
        controller.Health.IsInvincible = wasInvincible;
        controller.IgnoreExternalForces = ignoredForces;
        ownsState = false;
    }

    private void CancelTeleport()
    {
        if (teleporter != null) teleporter.Cancel();
        RestoreState();
        IsTeleporting = false;
        if (controller != null) controller.Velocity = Vector2.zero;
    }

    protected override void OnStop()
    {
        CancelTeleport();
        SetFlockRole(EnemyFlockRole.None);
        ReserveNext(nextAbility);
    }

    private void OnDisable()
    {
        CancelTeleport();
        CompletedCount = 0;
    }
}
