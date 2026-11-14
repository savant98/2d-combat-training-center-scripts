using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lobby의 입구·설비의 상호작용을 잠그는 공용 문.
/// 잠그는 쪽(Lobby 안내, 공사 현장)이 조건을 넣고, 설비(RoomTrigger·UpgradeTrigger)는 잠겼는지 묻기만 한다.
/// 조건이 여러 개면 하나라도 잠그라고 하면 잠긴다. 잠긴 설비는 상호작용 표시를 띄우지 않고 입력도 받지 않는다. 판정·이동과는 무관하다.
/// </summary>
public static class LobbyInteractionLock
{
    private static readonly List<Func<Component, bool>> Blockers = new List<Func<Component, bool>>();

    /// <summary>잠긴 설비에 Player가 상호작용하려 했다(잠근 쪽이 까닭을 다시 알려 줄 수 있게).</summary>
    public static event Action<Component> Rejected;

    /// <summary>잠겨 있으면 true를 돌려주는 조건을 넣는다. 같은 조건을 두 번 넣어도 한 번만 들어간다.</summary>
    public static void AddBlocker(Func<Component, bool> blocker)
    {
        if (blocker != null && !Blockers.Contains(blocker)) Blockers.Add(blocker);
    }

    public static void RemoveBlocker(Func<Component, bool> blocker) => Blockers.Remove(blocker);

    public static bool IsBlocked(Component target)
    {
        for (int i = 0; i < Blockers.Count; i++)
            if (Blockers[i](target)) return true;
        return false;
    }

    public static void NotifyRejected(Component target) => Rejected?.Invoke(target);

    // Domain Reload를 끈 Play 설정에서도 이전 Play의 조건·구독이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Blockers.Clear();
        Rejected = null;
    }
}
