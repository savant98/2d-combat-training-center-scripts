using System.Collections.Generic;
using UnityEngine;

// 별도 동작이나 효과 없이 가시 영역에 닿은 플레이어에게 피해를 줍니다.
[DisallowMultipleComponent]
public sealed class WaveSpikeTrap : WorldEntityBase, IWaveDeviceSettings
{
    [SerializeField, Min(0f)] private float damage = 10f;
    private readonly WaveTrapDamage trapDamage = new WaveTrapDamage();
    public WaveDeviceParameter[] Parameters => new[] { WaveTrapDamage.DamageParameter(damage), WaveTrapDamage.ReturnParameter };
    public bool SettingsChangeShape => false;
    public void ApplySettings(WaveDeviceSetting[] overrides) => trapDamage.ApplySettings(overrides, damage);
    public bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range) { range = default; return false; }
    [SerializeField] private Vector2 damageAreaOffset = new Vector2(0f, -.15f);
    [SerializeField] private Vector2 damageAreaSize = new Vector2(.9f, .7f);
    [SerializeField] private LayerMask targetMask;
    [SerializeField, Min(.01f)] private float interval = .1f;

    private RaycastMotor2D motor;
    private WaveEnvironmentLifecycle lifecycle;
    private Collider2D[] hits = new Collider2D[8];
    private readonly HashSet<PlayerHealth> damagedPlayers = new();
    private float nextDamageTime;

    protected override void Awake()
    {
        base.Awake();
        ApplySettings(WaveDeviceSettings.None);
        lifecycle = GetComponent<WaveEnvironmentLifecycle>();
        motor = GetComponent<RaycastMotor2D>();
        if (motor == null) motor = gameObject.AddComponent<RaycastMotor2D>();
        if (targetMask.value == 0) targetMask = LayerMask.GetMask("Player");
    }

    private void OnEnable() => nextDamageTime = 0f;
    private void OnDisable() => trapDamage.Cancel();

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        trapDamage.Cancel();
        ApplySettings(WaveDeviceSettings.None);
        nextDamageTime = 0f;
        if (lifecycle != null) lifecycle.BeginSpawn();
    }

    public override void ForceReturnToPool()
    {
        if (isReturning) return;
        trapDamage.Cancel();
        if (lifecycle != null) lifecycle.BeginDespawn(ReturnAfterDespawn);
        else base.ForceReturnToPool();
    }

    private void ReturnAfterDespawn() => base.ForceReturnToPool();

    private void FixedUpdate()
    {
        if (lifecycle != null && !lifecycle.IsReady) return;
        if (Time.time < nextDamageTime || (trapDamage.Damage <= 0f && !trapDamage.ReturnToStart)) return;
        nextDamageTime = Time.time + Mathf.Max(.01f, interval);
        var scale = transform.lossyScale;
        Vector2 size = new Vector2(Mathf.Abs(damageAreaSize.x * scale.x), Mathf.Abs(damageAreaSize.y * scale.y));
        Vector2 center = transform.TransformPoint(damageAreaOffset);
        int count;
        do
        {
            count = motor.OverlapAttackBox(center, size, transform.eulerAngles.z, hits, targetMask);
            if (count < hits.Length) break;
            System.Array.Resize(ref hits, hits.Length * 2);
        } while (true);

        damagedPlayers.Clear();
        var info = new WeaponBaseSO.DamageInfo { damage = damage, damageType = DamageType.Normal };
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == null) continue;
            var player = hits[i].GetComponentInParent<PlayerHealth>();
            if (player == null || !damagedPlayers.Add(player)) continue;
            trapDamage.TryDamage(this, player, info, hits[i].ClosestPoint(center));
        }
    }
}
