using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// 지형 준비와 Wave 시작 Spawn의 잠금을 소유한다. 방 전환의 UI 잠금 초기화와 독립적이다.
[DisallowMultipleComponent]
public sealed class WavePlayerStart : MonoBehaviour
{
    private static readonly HashSet<WavePlayerStart> holders = new();
    private static readonly int ArrivalState = Animator.StringToHash("Player_WarpEnd");
    private PlayerController player;
    private bool previousForces;
    private bool previousInvincible;
    private AnimatorCullingMode previousCulling;
    public bool IsHoldingPlayer { get; private set; }
    public bool IsSpawning { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHolders() => holders.Clear();
    public static bool IsHolding(PlayerController target)
    {
        foreach (var holder in holders) if (holder != null && holder.player == target) return true;
        return false;
    }

    public void Hold()
    {
        if (IsHoldingPlayer) return;
        player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;
        ClearMotion(player);
        previousForces = player.IgnoreExternalForces;
        previousInvincible = player.Health.IsInvincible;
        previousCulling = player.animator.cullingMode;
        player.IgnoreExternalForces = true;
        player.Health.IsInvincible = true;
        player.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        IsHoldingPlayer = true;
        holders.Add(this);
    }
    private static void ClearMotion(PlayerController target)
    {
        target.AbilityManager?.InterruptAllAbilities();
        target.AbilityManager?.JumpAbility?.ClearPendingInput();
        target.AbilityManager?.DashAbility?.ClearPendingInput();
        target.Velocity = Vector2.zero;
        target.externalForce = Vector2.zero;
    }
    public static void Relocate(PlayerController target, Vector3 position, bool effect)
    {
        ClearMotion(target);
        Vector3 before = target.transform.position;
        target.ResetPlayerPosition(position);
        CinemachineCore.OnTargetObjectWarped(target.transform, target.transform.position - before);
        if (effect && target.animator != null && target.animator.HasState(0, ArrivalState))
        {
            target.animator.ResetTrigger("TitleTransition");
            // 같은 상태에서 연속으로 복귀해도 원본 SplashWarp AnimationEvent를 다시 재생한다.
            target.animator.Play(ArrivalState, 0, 0);
        }
    }
    public IEnumerator Play(WaveDataSO data, int index, Transform coordinates)
    {
        if (player == null || !data.waves[index].teleportAtStart) yield break;
        IsSpawning = true;
        Relocate(player, coordinates.TransformPoint(WaveRoomState.PlayerStartAt(data, index)), true);
        coordinates.GetComponent<WaveCellTerrain>()?.RefreshPlayerSpace();
        // 원본 0.25초 등장 Clip의 이벤트와 종료까지 기다린 뒤 Wave 타이머를 시작한다.
        yield return null;
        float limit = Time.time + 2;
        while (player != null && player.animator.GetCurrentAnimatorStateInfo(0).shortNameHash == ArrivalState
            && player.animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1 && Time.time < limit)
            yield return null;
        IsSpawning = false;
    }
    public void Release()
    {
        holders.Remove(this);
        if (IsHoldingPlayer && player != null)
        {
            player.Velocity = Vector2.zero;
            player.externalForce = Vector2.zero;
            player.IgnoreExternalForces = previousForces;
            player.Health.IsInvincible = previousInvincible;
            player.animator.cullingMode = previousCulling;
        }
        IsHoldingPlayer = IsSpawning = false;
        player = null;
    }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
