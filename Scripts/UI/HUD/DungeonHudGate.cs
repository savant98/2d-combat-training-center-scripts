using UnityEngine;

/// <summary>
/// 던전 HUD(미니맵·방 상태 띠·적 위치 마커)가 함께 쓰는 보임 판정(2026-11-24 던전형 Wave 2차, DungeonMinimap.HudVisible에서 꺼냄).
/// - HUD 보임: UIManager.HUDVisible(bool) → PlayerHUD.HUDVisible → PlayerHUD와 같은 GameObject의 CanvasGroup 알파 0/1.
///   HUDVisible에는 이벤트가 없어 매번 그 CanvasGroup 알파를 읽는다(PlayerHUD.cs는 CP949 — 고치지 않는다). PlayerHUD 검색은 실제 시간 1초에 한 번까지.
///   PlayerHUD가 없는 장면(검증 등)이면 보인다고 본다. 검증은 CanvasGroup을 직접 넘긴다(override).
/// - 가리는 화면: 패배 화면·결과 보드가 열려 있으면 던전 HUD를 숨긴다.
/// 참조 카운트가 없는 UIManager.HUDVisible의 한계(마지막 호출이 이김)를 그대로 따른다.
/// 6차 fix6d: 정적 캐시는 Play를 시작할 때마다 비운다(Enter Play Mode Options — 도메인 리로드 꺼짐). 비우지 않으면 지난 Play의 파괴된 PlayerHUD와
/// 지난 Play의 실제 시간으로 잡은 다음 검색 시각이 남고, Editor는 Play를 시작할 때 Time.realtimeSinceStartup을 다시 0부터 세므로 그 시각까지 HUD를 다시 찾지 않아
/// '없는 장면 = 보인다'로 답한다(첫 레버 컷씬 동안 던전 HUD가 숨지 않던 원인). 찾아 둔 HUD가 파괴됐거나 기한이 1.5초보다 멀면 기다리지 않고 바로 다시 찾는다.
/// </summary>
public static class DungeonHudGate
{
    private static PlayerHUD hud;
    private static CanvasGroup hudGroup;
    private static float nextSearch = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCache();

    /// <summary>PlayerHUD가 보이는가. overrideGroup이 있으면 그 알파(&gt; 0.5)로만 판정한다(검증용).</summary>
    public static bool IsHudVisible(CanvasGroup overrideGroup = null)
    {
        if (overrideGroup != null) return overrideGroup.alpha > .5f;
        if (hud == null)
        {
            float now = Time.realtimeSinceStartup;
            // 1초 제한은 PlayerHUD가 없는 장면에서 매 프레임 검색하지 않으려는 것이다. 찾아 둔 HUD가 파괴됐으면(장면 다시 읽기·Play 재시작) 바로 다시 찾고,
            // 기한이 1.5초보다 멀면(실제 시간 기준이 뒤로 감 — Editor Play 시작) 그 기한을 버린다. 1초 그대로 비교하면 같은 프레임의 now + 1f 반올림 오차로 1초를 조금 넘어 다시 찾는다.
            bool lost = !object.ReferenceEquals(hud, null);
            if (lost || now >= nextSearch || nextSearch - now > 1.5f)
            {
                nextSearch = now + 1f;
                hud = Object.FindFirstObjectByType<PlayerHUD>();
                hudGroup = null;
                if (hud != null)
                {
                    hudGroup = hud.GetComponent<CanvasGroup>();
                    if (hudGroup == null) hudGroup = hud.GetComponentInParent<CanvasGroup>();
                }
            }
        }
        if (hud == null) return true;
        if (!hud.isActiveAndEnabled) return false;
        return hudGroup == null || hudGroup.alpha > .5f;
    }

    /// <summary>패배 화면·결과 보드가 열려 있는가(그 동안 던전 HUD를 숨긴다).</summary>
    public static bool IsBlockingScreenOpen()
    {
        var ui = UIManager.Instance;
        return ui != null && (ui.IsDeathUIOpen || ui.IsCompleteBoardOpen);
    }

    /// <summary>던전 HUD를 보여도 되는가(HUD 보임 && 가리는 화면 없음). 던전 진행 여부는 부르는 쪽이 따로 본다.</summary>
    public static bool ShouldShowDungeonUi(CanvasGroup overrideGroup = null) => IsHudVisible(overrideGroup) && !IsBlockingScreenOpen();

    /// <summary>캐시를 비운다(다음 판정에서 PlayerHUD를 바로 다시 찾는다).</summary>
    public static void ResetCache()
    {
        hud = null;
        hudGroup = null;
        nextSearch = float.NegativeInfinity;
    }
}
