using System;
using UnityEngine;

/// <summary>배치별 함정 피해와 시작점 복귀. 실제 피격이 성립한 경우에만 기존 암전·이동 경로를 사용한다.</summary>
public sealed class WaveTrapDamage
{
    public static bool IsApplying { get; private set; }
    public static bool IsRestartHit { get; private set; }
    public float Damage { get; private set; }
    public bool ReturnToStart { get; private set; }
    public bool IsReturning => playerReturn.IsRunning;
    public static WaveDeviceParameter DamageParameter(float defaultDamage) => new WaveDeviceParameter(
        "damage", "피해량", "한 번 피격될 때의 피해량입니다. 0이면 피해 없이 복귀만 시킬 수 있습니다.", "", 0f, 10000f, 1f, defaultDamage);
    public static WaveDeviceParameter ReturnParameter => new WaveDeviceParameter(
        "returnToStart", "피격 시 시작 위치 복귀", "실제로 맞았을 때 현재 Wave의 시작점으로 돌아갑니다. 무적·반격 중에는 발동하지 않습니다.",
        new[] { "복귀 안 함", "시작 위치로 복귀" }, 0f);

    private readonly WavePlayerReturn playerReturn = new WavePlayerReturn();
    private MonoBehaviour host;
    private PlayerHealth health;
    private Coroutine routine;
    private bool applied;
    private Action<WeaponBaseSO.DamageInfo, float> listener;

    public void ApplySettings(WaveDeviceSetting[] settings, float defaultDamage)
    {
        Damage = WaveDeviceSettings.Value(settings, DamageParameter(defaultDamage));
        ReturnToStart = WaveDeviceSettings.Value(settings, ReturnParameter) > .5f;
    }

    public bool TryDamage(MonoBehaviour source, PlayerHealth player, WeaponBaseSO.DamageInfo info, Vector3 position)
        => TryDamage(source, player, info, position, Damage, ReturnToStart);

    public bool TryDamage(MonoBehaviour source, PlayerHealth player, WeaponBaseSO.DamageInfo info, Vector3 position, float damage, bool returnToStart)
    {
        if (playerReturn.IsRunning || (damage <= 0f && !returnToStart)) return false;
        applied = false;
        listener ??= (_, _) => applied = true;
        bool previousApplying = IsApplying, previousRestart = IsRestartHit;
        IsApplying = true;
        IsRestartHit = returnToStart;
        player.OnDamageApplied += listener;
        info.damage = damage;
        try { player.TakeDamage(info, position); }
        finally
        {
            player.OnDamageApplied -= listener;
            IsApplying = previousApplying;
            IsRestartHit = previousRestart;
        }
        if (applied && returnToStart && !player.IsDead && !player.IsDeathPending && source.isActiveAndEnabled
            && TryGetStart(out var destination))
        {
            host = source;
            health = player;
            health.OnPlayerDeath += Cancel;
            routine = host.StartCoroutine(Return(player.GetComponentInParent<PlayerController>(), destination));
        }
        return applied;
    }

    private System.Collections.IEnumerator Return(PlayerController player, Vector3 destination)
    {
        yield return playerReturn.Run(player, destination, false, .15f, .15f, .3f);
        if (health != null) health.OnPlayerDeath -= Cancel;
        health = null;
        routine = null;
    }

    private static bool TryGetStart(out Vector3 destination)
    {
        destination = default;
        var waves = WaveManager.ActiveInstance;
        var data = waves != null ? waves.Data : null;
        if (data == null || data.waves == null || data.waves.Length == 0) return false;
        var cells = waves.GetComponentInParent<WaveCellTerrain>(true);
        var room = waves.GetComponentInParent<Room>(true);
        var coordinates = cells != null ? cells.transform : room != null ? room.transform : waves.transform;
        destination = coordinates.TransformPoint(WaveRoomState.PlayerStartAt(data, Mathf.Clamp(waves.currentWaveIndex, 0, data.waves.Length - 1)));
        return true;
    }

    public void Cancel()
    {
        if (health != null) health.OnPlayerDeath -= Cancel;
        health = null;
        if (host != null && routine != null) host.StopCoroutine(routine);
        routine = null;
        playerReturn.Recover(.3f);
    }
}
