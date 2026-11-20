using System;

/// <summary>
/// Lobby 설비(LobbyStation)가 연 메뉴 안에서 해금(훈련 필증 구매)이 시작되는 순간을 알리는 접점(2026-11-20 훈련 개편).
/// 업그레이드 메뉴의 상세·교체 페이지가 구현하고, LobbyStation이 menus 아래에서 찾아 구독해 Animator의 Trigger 'Unlock'으로
/// 해금 전용 작업 동작(메뉴 층 Unlocking)을 재생한다. 컨트롤러에 'Unlock'이 없으면 기존 'Upgrade'(Upgrading)로 대신한다.
/// </summary>
public interface ILobbyStationUnlockActivity
{
    /// <summary>해금 연출이 시작되는 순간(거래는 이미 끝났다).</summary>
    event Action UnlockPerformed;
}
