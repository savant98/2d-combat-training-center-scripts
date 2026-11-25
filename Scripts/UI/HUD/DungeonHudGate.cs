using UnityEngine;

/// <summary>
/// 던전 HUD(미니맵·방 상태 띠·적 위치 마커)가 함께 쓰는 보임 판정(2026-11-24 던전형 Wave 2차, DungeonMinimap.HudVisible에서 꺼냄).
/// - HUD 보임: UIManager.HUDVisible(bool) → PlayerHUD.HUDVisible → PlayerHUD와 같은 GameObject의 CanvasGroup 알파 0/1.
///   HUDVisible에는 이벤트가 없어 매번 그 CanvasGroup 알파를 읽는다(PlayerHUD.cs는 CP949 — 고치지 않는다). PlayerHUD 검색은 실제 시간 1초에 한 번까지.
///   PlayerHUD가 없는 장면(검증 등)이면 보인다고 본다. 검증은 CanvasGroup을 직접 넘긴다(override).
/// - 가리는 화면: 패배 화면·결과 보드가 열려 있으면 던전 HUD를 숨긴다.
/// 참조 카운트가 없는 UIManager.HUDVisible의 한계(마지막 호출이 이김)를 그대로 따른다.
/// </summary>
public static class DungeonHudGate
{
    private static PlayerHUD hud;
    private static CanvasGroup hudGroup;
    private static float nextSearch = float.NegativeInfinity;

    /// <summary>PlayerHUD가 보이는가. overrideGroup이 있으면 그 알파(&gt; 0.5)로만 판정한다(검증용).</summary>
    public static bool IsHudVisible(CanvasGroup overrideGroup = null)
    {
        if (overrideGroup != null) return overrideGroup.alpha > .5f;
        if (hud == null)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= nextSearch)
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
