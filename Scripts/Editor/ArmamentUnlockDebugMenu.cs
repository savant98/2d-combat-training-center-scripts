using UnityEditor;
using UnityEngine;

/// <summary>
/// 무장·기술 해금 테스트 진입점(2026-11-20 훈련 개편). Play 중에 지금 세이브의 해금 기록을 모두 켜거나 지운다(훈련 필증은 그대로).
/// 결과는 Console에 한 줄로 남는다. 검증 도구는 ArmamentUnlocks.DebugSetAll을 직접 부른다.
/// </summary>
internal static class ArmamentUnlockDebugMenu
{
    private const string UnlockAllPath = "Tools/Armament/해금 전부 켜기";
    private const string LockAllPath = "Tools/Armament/해금 전부 끄기";

    [MenuItem(UnlockAllPath)]
    private static void UnlockAll() => Debug.Log("[ArmamentUnlock] " + ArmamentUnlocks.DebugSetAll(true));

    [MenuItem(LockAllPath)]
    private static void LockAll() => Debug.Log("[ArmamentUnlock] " + ArmamentUnlocks.DebugSetAll(false));

    [MenuItem(UnlockAllPath, true)]
    [MenuItem(LockAllPath, true)]
    private static bool CanUse() => Application.isPlaying;
}
