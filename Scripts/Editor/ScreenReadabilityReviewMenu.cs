using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

// 화면 가독성 처리 검수용 메뉴(Play 중). 무작위 던전에서 검수 템플릿 방을 찾아다니지 않도록 전투 훈련 던전을 열고 그 방으로 바로 옮기며,
// 처리 켜기·끄기를 저장 없이 바꿔 같은 화면에서 비교한다. 설정은 Resources/Camera/ScreenReadability(ScreenReadabilitySO)다.
public static class ScreenReadabilityReviewMenu
{
    private const string Root = "Tools/Visual/가독성 검수/";
    private const string CombatWavePath = "Assets/Data/Waves/CombatBasicWave.asset";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool busy;

    [MenuItem(Root + "검수 방으로 이동 (Play 중)", false, 1)]
    private static async void GoToReviewRoom()
    {
        if (busy) return;
        busy = true;
        try { Debug.Log("[가독성 검수] " + await MoveToReviewRoom()); }
        catch (Exception e) { Debug.LogWarning("[가독성 검수] 이동 실패: " + e.Message); }
        finally { busy = false; }
    }

    [MenuItem(Root + "검수 방으로 이동 (Play 중)", true)]
    private static bool CanGoToReviewRoom() => Application.isPlaying && !busy;

    [MenuItem(Root + "처리 켜기·끄기 (Play 비교용, 저장 안 함)", false, 2)]
    private static void ToggleReadability()
    {
        var settings = ScreenReadabilitySO.Default;
        bool current = ScreenReadability.RuntimeOverride ?? (settings != null && settings.readabilityEnabled);
        ScreenReadability.RuntimeOverride = !current;
        Debug.Log($"[가독성 검수] 처리 {(!current ? "켬" : "끔")} — 지금 방 적용: {(ScreenReadability.Active ? "예" : "아니오(범위 밖)")}");
    }

    [MenuItem(Root + "처리 켜기·끄기 (Play 비교용, 저장 안 함)", true)]
    private static bool CanToggleReadability() => Application.isPlaying;

    [MenuItem(Root + "통창 숨기기·되돌리기 (Play 비교용, 저장 안 함)", false, 3)]
    private static void ToggleObservationWindow()
    {
        bool hidden = WaveRoomBackground.ObservationWindowOverride == false;
        WaveRoomBackground.SetObservationWindowOverride(hidden ? (bool?)null : false);
        Debug.Log($"[가독성 검수] 통창 {(hidden ? "원래대로(Wave 데이터·벽면 방 규칙)" : "숨김(시차 배경 방 포함)")}");
    }

    [MenuItem(Root + "통창 숨기기·되돌리기 (Play 비교용, 저장 안 함)", true)]
    private static bool CanToggleObservationWindow() => Application.isPlaying;

    [MenuItem(Root + "카메라 프레이밍 켜기·끄기 (Play 비교용, 저장 안 함)", false, 4)]
    private static void ToggleFraming()
    {
        var settings = CameraFramingSO.Default;
        bool current = PlayerCameraFraming.RuntimeOverride ?? (settings != null && settings.framingEnabled);
        PlayerCameraFraming.RuntimeOverride = !current;
        Debug.Log($"[가독성 검수] 카메라 프레이밍 {(!current ? "켬" : "끔(예전 화면 중앙 고정)")}");
    }

    [MenuItem(Root + "카메라 프레이밍 켜기·끄기 (Play 비교용, 저장 안 함)", true)]
    private static bool CanToggleFraming() => Application.isPlaying;

    [MenuItem(Root + "설정 에셋 선택", false, 20)]
    private static void SelectSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<ScreenReadabilitySO>("Assets/Resources/Camera/ScreenReadability.asset");
        if (settings != null) { Selection.activeObject = settings; EditorGUIUtility.PingObject(settings); }
    }

    private static async Task Until(Func<bool> condition, string label, double seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (!condition())
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Play가 끝났습니다.");
            if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(label + " 대기 시간 초과");
            await Task.Delay(50);
        }
    }

    // 전투 훈련 던전을 열고(이미 열려 있으면 그대로) 검수 템플릿 방이 생긴 층을 찾아 그 방으로 옮긴다.
    private static async Task<string> MoveToReviewRoom()
    {
        var settings = ScreenReadabilitySO.Default;
        if (settings == null) return "설정 에셋이 없습니다.";
        var wanted = settings.previewTemplate != null ? new[] { settings.previewTemplate } : settings.reviewTemplates.Where(t => t != null).ToArray();
        if (wanted.Length == 0) return "검수 방 템플릿(previewTemplate)이 설정 에셋에 없습니다.";
        await Until(() => GameManager.Instance != null && GameManager.Instance.Player != null && RoomManager.Instance != null
            && RoomManager.Instance.CurrentRoom != null && RoomTransitionManager.Instance != null && !RoomTransitionManager.Instance.IsTransitioning, "게임 시작", 60);
        var host = UnityEngine.Object.FindFirstObjectByType<TrainingRoomHost>(FindObjectsInactive.Include);
        if (host == null) return "TrainingRoomHost를 찾지 못했습니다.";
        var room = host.GetComponent<Room>();
        var wm = room.WaveManager;
        var dm = wm != null ? wm.GetComponent<DungeonManager>() : null;
        if (RoomManager.Instance.CurrentRoom != room || dm == null || !dm.HasFloor)
        {
            var wave = AssetDatabase.LoadAssetAtPath<WaveDataSO>(CombatWavePath);
            if (wave == null) return "전투 훈련 WaveData가 없습니다: " + CombatWavePath;
            RoomTransitionManager.Instance.RequestTransition(new RoomTransitionRequest { targetRoom = room, teleportPoint = host.entryPoint, waveData = wave });
            await Until(() => RoomManager.Instance.CurrentRoom == room && !RoomTransitionManager.Instance.IsTransitioning, "훈련장 입장", 120);
            wm = room.WaveManager;
            dm = wm != null ? wm.GetComponent<DungeonManager>() : null;
            if (dm == null) return "DungeonManager를 찾지 못했습니다.";
            await Until(() => dm.HasFloor && !dm.IsPassing, "던전 시작", 120);
        }
        var swap = typeof(DungeonManager).GetMethod("SwapRoom", Flags, null, new[] { typeof(int), typeof(bool), typeof(int) }, null);
        if (swap == null) return "DungeonManager.SwapRoom(int, bool, int)이 없습니다(이동 메뉴를 고칠 것).";
        for (int attempt = 0; attempt < 8; attempt++)
        {
            foreach (var data in dm.Floor.rooms)
            {
                var template = data?.node?.template;
                if (template == null || !wanted.Any(t => t == template || t.name == template.name)) continue;
                int door = Array.FindIndex(data.doors, d => d != null && d.Connected);
                if (door < 0) continue;
                swap.Invoke(dm, new object[] { data.Id, false, door });
                return $"{template.name} 방으로 이동했습니다(층 다시 만들기 {attempt}회). 처리 켜기·끄기 메뉴로 비교하세요.";
            }
            // 이 층에 없으면 같은 전투 훈련 Wave를 다시 시작해 새 층을 만든다.
            wm.BeginWaves(false);
            await Until(() => dm.HasFloor && !dm.IsPassing && wm.IsWaveActive, "새 층", 120);
        }
        return "8번 다시 만들어도 검수 템플릿 방이 나오지 않았습니다.";
    }
}
