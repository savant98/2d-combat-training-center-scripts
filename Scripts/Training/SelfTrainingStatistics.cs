using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>체력 하한과 별개로 Dummy에 적중한 유효 피해를 측정한다.</summary>
public sealed class SelfTrainingStatistics
{
    private struct HitSample
    {
        public double time;
        public float damage;
    }

    private readonly List<SelfTrainingDamageRow> sources = new List<SelfTrainingDamageRow>();
    private readonly Queue<HitSample> recentHits = new Queue<HitSample>();
    private readonly float[] seconds = new float[10];
    private double startedAt;
    private float windowDamage;
    private bool completed;

    public float TotalDamage { get; private set; }
    public float MaximumHit { get; private set; }
    public float Dps { get; private set; }
    public float CurrentDps { get; private set; }
    public float PeakDps { get; private set; }
    public float Duration { get; private set; }
    public int HitCount { get; private set; }
    public IReadOnlyList<SelfTrainingDamageRow> Sources => sources;
    public IReadOnlyList<float> Seconds => seconds;

    public void Begin(double time)
    {
        startedAt = time;
        completed = false;
        TotalDamage = MaximumHit = Dps = CurrentDps = PeakDps = Duration = windowDamage = 0f;
        HitCount = 0;
        sources.Clear();
        recentHits.Clear();
        Array.Clear(seconds, 0, seconds.Length);
    }

    public void Record(WeaponBaseSO.DamageInfo info, double time)
    {
        if (completed || time < startedAt || float.IsNaN(info.damage) || float.IsInfinity(info.damage) || info.damage <= 0f)
            return;

        Advance(time);
        TotalDamage += info.damage;
        MaximumHit = Mathf.Max(MaximumHit, info.damage);
        HitCount++;
        windowDamage += info.damage;
        recentHits.Enqueue(new HitSample { time = time, damage = info.damage });
        CurrentDps = windowDamage;
        PeakDps = Mathf.Max(PeakDps, CurrentDps);

        int second = (int)Math.Floor(time - startedAt);
        if (second >= 0 && second < seconds.Length)
            seconds[second] += info.damage;

        SelfTrainingDamageRow row = null;
        foreach (var candidate in sources)
            if (candidate.Weapon == info.sourceWeapon && candidate.DamageType == info.damageType)
            {
                row = candidate;
                break;
            }

        if (row == null)
        {
            string label = info.sourceWeapon != null ? info.sourceWeapon.weaponName : "출처 미지정";
            if (string.IsNullOrWhiteSpace(label)) label = info.sourceWeapon.name;
            row = new SelfTrainingDamageRow(info.sourceWeapon, info.damageType, label);
            sources.Add(row);
        }
        row.Add(info.damage);
        sources.Sort((a, b) => b.Damage.CompareTo(a.Damage));
        RefreshRates();
    }

    public void Advance(double time)
    {
        if (completed) return;
        Duration = (float)Math.Max(0d, time - startedAt);
        while (recentHits.Count > 0 && time - recentHits.Peek().time >= 1d)
            windowDamage -= recentHits.Dequeue().damage;
        CurrentDps = Mathf.Max(0f, windowDamage);
        RefreshRates();
    }

    public void Complete(double time)
    {
        Advance(time);
        completed = true;
    }

    private void RefreshRates()
    {
        // 첫 프레임의 거의 0초를 나눠 DPS가 과도하게 치솟지 않게 1초 표본으로 시작한다.
        float divisor = Mathf.Max(1f, Duration);
        Dps = TotalDamage / divisor;
        foreach (var row in sources)
            row.UpdateRates(TotalDamage, divisor);
    }
}

public sealed class SelfTrainingDamageRow
{
    public WeaponBaseSO Weapon { get; }
    public DamageType DamageType { get; }
    public string Name { get; }
    public float Damage { get; private set; }
    public float Ratio { get; private set; }
    public float Dps { get; private set; }
    public float MaximumHit { get; private set; }
    public int HitCount { get; private set; }

    public SelfTrainingDamageRow(WeaponBaseSO weapon, DamageType type, string name)
    {
        Weapon = weapon;
        DamageType = type;
        Name = name;
    }

    internal void Add(float damage)
    {
        Damage += damage;
        MaximumHit = Mathf.Max(MaximumHit, damage);
        HitCount++;
    }

    internal void UpdateRates(float total, float duration)
    {
        Ratio = total > 0f ? Damage / total : 0f;
        Dps = Damage / duration;
    }
}
