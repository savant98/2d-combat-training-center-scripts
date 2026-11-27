using System;
using UnityEngine;

/// <summary>
/// 일반 Wave(던전이 아닌 훈련 방)에서도 휴대용 단말기(DungeonFieldTerminal)를 열어
/// 훈련 상황 확인 및 Wave 재시작을 할 수 있도록 연결하는 호스트.
/// DungeonManager와 대칭적으로 WaveManager 이벤트를 구독하며, WaveManager 본체에 UI 로직을 넣지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class WaveFieldTerminalHost : MonoBehaviour, IWaveTerminalSource
{
    private WaveManager waves;
    private GameObject terminalObject;
    private IWaveTerminalView terminalView;
    private bool runActive;

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
        waves = target;
        Subscribe();
    }

    private void Subscribe()
    {
        if (waves == null) return;
        waves.WaveIntroStarted += OnWaveIntroStarted;
        waves.WaveStarted += OnWaveStarted;
        waves.WaveEnded += OnWaveEnded;
        waves.WavesReset += OnWavesReset;
        waves.OnAllWavesCleared += OnAllWavesCleared;
    }

    private void Unsubscribe()
    {
        if (waves == null) return;
        waves.WaveIntroStarted -= OnWaveIntroStarted;
        waves.WaveStarted -= OnWaveStarted;
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
        WaveStateChanged?.Invoke();
    }

    private void OnWaveEnded() => WaveStateChanged?.Invoke();

    private void OnWavesReset()
    {
        runActive = false;
        WaveStateChanged?.Invoke();
    }

    private void OnAllWavesCleared()
    {
        runActive = false;
        WaveStateChanged?.Invoke();
    }

    private void OnDisable()
    {
        runActive = false;
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
