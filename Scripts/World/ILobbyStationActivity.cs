using System;

/// <summary>
/// Lobby 설비(LobbyStation)가 연 메뉴 안에서 설비가 일하는 순간(구매·장착 등)을 알리는 접점(2026-11-18).
/// 메뉴 쪽(업그레이드 메뉴의 상세·교체 페이지)이 구현하고, LobbyStation이 menus 아래에서 찾아 구독해
/// 설비 Animator의 작업 동작(예: 업그레이드 장치의 Upgrade 트리거)을 재생한다. 메뉴는 설비를 몰라도 된다.
/// </summary>
public interface ILobbyStationActivity
{
    /// <summary>설비가 일하는 순간(구매 확정·무장 장착 등). 연출을 기다리는 동안 설비가 움직이도록 연출 시작 때 낸다.</summary>
    event Action Performed;
}
