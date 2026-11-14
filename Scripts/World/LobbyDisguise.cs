using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 최초 시작 도입부의 '가짜 Lobby'(2026-11-15, 사용자 요청: "FinalRoom으로 오면 바로 Lobby와 똑같이 생긴 곳(platform과 배경의 생김새만)에 도착,
/// 이동 가이드는 그대로, 왼쪽 길은 막혀 있고 오른쪽으로 8 정도 가면 경고").
/// 실제 Lobby 방을 잠시 위장해 쓴다. 지형·배경은 Lobby 그대로라 Lobby가 바뀌어도 늘 같은 모습이다.
/// 위장 중에는 Furniture 아래의 설비·간판·소품·입구를 숨기고(기둥·통창은 남김), 위층 플랫폼 왼쪽 끝을 막고,
/// Lobby BGM을 끄고(타이틀에서 이어 온 곡을 유지), 시작 지점 위의 이동 안내를 켠다. 끄면 숨겼던 것을 원래 활성 상태로 되돌린다.
/// 단말기 아이콘·Tab 열기와 Lobby 안내는 IsActive를 보고 위장 중에는 동작하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyDisguise : MonoBehaviour
{
    [SerializeField, Tooltip("Lobby의 Furniture. 그 바로 아래 자식 가운데 keepNames에 없는 것을 위장 중에 숨긴다")] private Transform furniture;
    [SerializeField, Tooltip("위장 중에도 남기는 Furniture 자식 이름(플랫폼 기둥·통창·위장용 부품)")] private string[] keepNames =
        { "PlatformPillar0", "PlatformPillar1", "LobbyObservationWindow", "DisguiseLeftWall", "DisguiseMoveHint" };
    [SerializeField, Tooltip("위장 중에만 켜는 왼쪽 막힘(Platform 레이어 판정)")] private GameObject leftWall;
    [SerializeField, Tooltip("위장 중에만 보여 주는 이동 안내")] private NightmareMoveHint moveHint;
    [SerializeField, Tooltip("위장 중에 끄는 Lobby BGM")] private RoomBgmPlayer bgm;

    private readonly List<(GameObject go, bool active)> hidden = new();
    private bool bgmWasEnabled;

    /// <summary>지금 Lobby를 도입부의 가짜 Lobby로 쓰고 있는가(단말기·안내·해금 이벤트가 읽기만 한다).</summary>
    public static bool IsActive { get; private set; }
    public bool IsDisguised { get; private set; }
    public NightmareMoveHint MoveHint => moveHint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => IsActive = false;

    public void SetDisguised(bool value)
    {
        if (IsDisguised == value) return;
        IsDisguised = value;
        IsActive = value;
        if (value)
        {
            hidden.Clear();
            if (furniture != null)
                foreach (Transform child in furniture)
                {
                    if (System.Array.IndexOf(keepNames, child.name) >= 0) continue;
                    hidden.Add((child.gameObject, child.gameObject.activeSelf));
                    child.gameObject.SetActive(false);
                }
            if (leftWall != null) leftWall.SetActive(true);
            if (bgm != null) { bgmWasEnabled = bgm.enabled; bgm.enabled = false; }
            if (moveHint != null) { moveHint.gameObject.SetActive(true); moveHint.Show(); }
        }
        else
        {
            foreach (var (go, active) in hidden) if (go != null) go.SetActive(active);
            hidden.Clear();
            if (leftWall != null) leftWall.SetActive(false);
            if (bgm != null) bgm.enabled = bgmWasEnabled;
            if (moveHint != null) moveHint.HideImmediately();
        }
    }

    private void OnDestroy()
    {
        if (IsDisguised) IsActive = false;
    }
}
