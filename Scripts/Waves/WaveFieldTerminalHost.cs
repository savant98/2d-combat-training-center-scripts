using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 일반 Wave(던전이 아닌 훈련 방)에서도 휴대용 단말기(DungeonFieldTerminal)를 열어
/// 훈련 상황 확인 및 Wave 재시작을 할 수 있도록 연결하는 호스트.
/// WaveManager 이벤트를 구독해 적 위치 마커도 표시한다. 던전의 단말기와 마커는 DungeonManager가 연결한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class WaveFieldTerminalHost : MonoBehaviour, IWaveTerminalSource
{
    private WaveManager waves;
    private GameObject terminalObject;
    private IWaveTerminalView terminalView;
    private bool runActive;
    private DungeonEnemyMarkers markers;
    private StageHUD stageHud;
    private readonly List<EnemyController> enemies = new();

    public bool HasActiveWave
    {
        get
        {
            if (waves == null || !runActive || !isActiveAndEnabled) return false;
            if (waves.IsHalted || LobbyDisguise.IsActive) return false;
            if (!ReferenceEquals(WaveManager.ActiveInstance, waves)) return false;
            var room = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
            return room != null && room.roomType == RoomType.Training;
        }
    }

    public int CurrentWaveNumber => waves != null ? Mathf.Clamp(waves.currentWaveIndex + 1, 1, Mathf.Max(1, TotalWaveCount)) : 0;
    public int TotalWaveCount => waves != null ? waves.TotalWaveCount : 0;
    public bool IsHalted => waves != null && waves.IsHalted;
    public event Action WaveStateChanged;

    public void Configure(WaveManager target)
    {
        Unsubscribe();
        ClearMarkers();
        waves = target;
        Subscribe();
    }

    private void Subscribe()
    {
        if (waves == null) return;
        waves.WaveIntroStarted += OnWaveIntroStarted;
        waves.WaveStarted += OnWaveStarted;
        waves.EnemySpawned += OnEnemySpawned;
        waves.WaveEnded += OnWaveEnded;
        waves.WavesReset += OnWavesReset;
        waves.OnAllWavesCleared += OnAllWavesCleared;
    }

    private void Unsubscribe()
    {
        if (waves == null) return;
        waves.WaveIntroStarted -= OnWaveIntroStarted;
        waves.WaveStarted -= OnWaveStarted;
        waves.EnemySpawned -= OnEnemySpawned;
        waves.WaveEnded -= OnWaveEnded;
        waves.WavesReset -= OnWavesReset;
        waves.OnAllWavesCleared -= OnAllWavesCleared;
    }

    private void OnWaveIntroStarted(int waveNumber)
    {
        runActive = true;
        EnsureTerminal();
        WaveStateChanged?.Invoke();
    }

    private void OnWaveStarted(Wave wave)
    {
        runActive = true;
        EnsureTerminal();
        if (markers == null)
        {
            var status = Resources.Load<GameObject>(DungeonRoomStatus.ResourcePath);
            var prefab = status != null ? status.GetComponent<DungeonRoomStatus>()?.Markers : null;
            var instance = prefab != null ? DungeonUiPlacement.Instantiate(prefab.gameObject, DungeonUiPlacement.Slot.AfterScoreboard) : null;
            if (instance != null) markers = instance.GetComponent<DungeonEnemyMarkers>();
            stageHud = FindFirstObjectByType<StageHUD>();
        }
        WaveStateChanged?.Invoke();
    }

    private void OnEnemySpawned(EnemyController enemy)
    {
        if (enemy != null && !enemies.Contains(enemy)) enemies.Add(enemy);
    }

    private void LateUpdate()
    {
        if (markers == null) return;
        bool visible = HasActiveWave && waves.IsWaveActive && DungeonHudGate.ShouldShowDungeonUi()
            && (UIManager.Instance == null || !UIManager.Instance.HasActiveInputUI);
        markers.BossBarVisible = stageHud != null && stageHud.isActiveAndEnabled && stageHud.CurrentBoss != null;
        markers.AdvanceEnemies(Time.unscaledDeltaTime, visible ? enemies : null, visible);
    }

    private void ClearMarkers()
    {
        enemies.Clear();
        if (markers != null) markers.Clear();
    }

    private void OnWaveEnded()
    {
        ClearMarkers();
        WaveStateChanged?.Invoke();
    }

    private void OnWavesReset()
    {
        runActive = false;
        ClearMarkers();
        WaveStateChanged?.Invoke();
    }

    private void OnAllWavesCleared()
    {
        runActive = false;
        ClearMarkers();
        WaveStateChanged?.Invoke();
    }

    private void OnDisable()
    {
        runActive = false;
        ClearMarkers();
        WaveStateChanged?.Invoke();
    }

    private void OnDestroy()
    {
        runActive = false;
        Unsubscribe();
        if (terminalView != null)
        {
            try { terminalView.UnbindWave(); }
            catch (Exception e) { Debug.LogException(e, this); }
            terminalView = null;
        }
        if (markers != null) Destroy(markers.gameObject);
        if (terminalObject != null) Destroy(terminalObject);
        terminalObject = null;
    }

    private void EnsureTerminal()
    {
        if (terminalObject != null) return;
        var prefab = Resources.Load<GameObject>(DungeonFieldTerminal.ResourcePath);
        if (prefab == null) return;
        terminalObject = DungeonUiPlacement.Instantiate(prefab, DungeonUiPlacement.Slot.BeforeFade);
        if (terminalObject == null) return;
        terminalView = terminalObject.GetComponentInChildren<IWaveTerminalView>(true);
        if (terminalView == null) return;
        try { terminalView.BindWave(this); }
        catch (Exception e) { Debug.LogException(e, this); }
    }
}
