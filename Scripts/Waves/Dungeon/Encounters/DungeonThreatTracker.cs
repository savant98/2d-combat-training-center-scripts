using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실시간 난이도(4차 R17, 순수 C#). 첫 클리어 방 수를 훈련 한 판 동안 누적해 문턱(기본 0·2·4·6·8)으로 5단계를 정한다.
/// DungeonManager가 훈련 시작 때 만들고(규칙 SO의 문턱·배율), 방을 처음 클리어할 때 RecordFirstClear를 부르며, 재도전하면 Reset한다.
/// 단계가 바뀐 순간 ThreatChanged(이전, 새)를 올린다 — 단계 상승은 ROOM CLEAR 직후이고, 적 구성에는 다음 방부터 적용된다
/// (방에 들어오는 순간 그때의 단계로 구성을 짜므로). 무엇을 셀지(시작 방·적 없는 방·순찰 처치 제외)는 부르는 쪽이 정한다.
/// IDungeonThreatSource를 직접 구현하므로 DungeonManager는 위임만 하면 된다.
/// </summary>
public sealed class DungeonThreatTracker : IDungeonThreatSource
{
    /// <summary>기본 문턱(첫 클리어 방 수).</summary>
    public static readonly int[] DefaultThresholds = { 0, 2, 4, 6, 8 };
    /// <summary>기본 단계 배율.</summary>
    public static readonly float[] DefaultMultipliers = { .7f, 1f, 1.3f, 1.65f, 2f };

    private const int LevelCount = 5;
    private readonly int[] thresholds = new int[LevelCount];
    private readonly float[] multipliers = new float[LevelCount];
    // 이미 센 방((층, 방 id)). 같은 방을 두 번 세지 않는다.
    private readonly HashSet<long> counted = new HashSet<long>();

    public DungeonThreatTracker() : this(null, null) { }

    /// <summary>thresholds·multipliers가 null이거나 짧으면 기본값으로 채운다.</summary>
    public DungeonThreatTracker(IReadOnlyList<int> thresholds, IReadOnlyList<float> multipliers)
    {
        Configure(thresholds, multipliers);
    }

    /// <summary>규칙 SO의 문턱·배율로 만든다(rules가 null이면 기본값).</summary>
    public static DungeonThreatTracker FromRules(DungeonEncounterRulesSO rules)
        => new DungeonThreatTracker(rules != null ? rules.thresholds : null, rules != null ? rules.multipliers : null);

    /// <summary>이 던전(훈련)이 실시간 난이도를 쓰는지. DungeonManager가 Dynamic 던전 동안 true로 둔다.</summary>
    public bool HasThreat { get; set; }
    public DungeonThreatLevel ThreatLevel { get; private set; }
    public int ClearedRooms { get; private set; }
    /// <summary>다음 단계 문턱(최고 단계면 -1).</summary>
    public int NextLevelAt
    {
        get
        {
            int next = (int)ThreatLevel + 1;
            return next < LevelCount ? thresholds[next] : -1;
        }
    }
    /// <summary>다음 단계까지 남은 첫 클리어 수(최고 단계면 0).</summary>
    public int RoomsToNextLevel => NextLevelAt < 0 ? 0 : Mathf.Max(0, NextLevelAt - ClearedRooms);
    public float DensityMultiplier => multipliers[(int)ThreatLevel];
    /// <summary>단계의 문턱·배율(검사·표시용).</summary>
    public int Threshold(DungeonThreatLevel level) => thresholds[Mathf.Clamp((int)level, 0, LevelCount - 1)];
    public float Multiplier(DungeonThreatLevel level) => multipliers[Mathf.Clamp((int)level, 0, LevelCount - 1)];

    public event Action<DungeonThreatLevel, DungeonThreatLevel> ThreatChanged;

    /// <summary>
    /// 문턱·배율을 바꾼다(지금 클리어 수로 단계를 다시 계산하고, 바뀌면 ThreatChanged). 문턱은 오름차순으로 맞추고 첫 값은 0 이상,
    /// 배율은 0 이상으로 자른다.
    /// </summary>
    public void Configure(IReadOnlyList<int> newThresholds, IReadOnlyList<float> newMultipliers)
    {
        int previous = 0;
        for (int i = 0; i < LevelCount; i++)
        {
            int value = newThresholds != null && i < newThresholds.Count ? newThresholds[i] : DefaultThresholds[i];
            if (i == 0) value = Mathf.Max(0, value);
            else value = Mathf.Max(previous, value);
            thresholds[i] = previous = value;
            float m = newMultipliers != null && i < newMultipliers.Count ? newMultipliers[i] : DefaultMultipliers[i];
            multipliers[i] = float.IsFinite(m) ? Mathf.Max(0f, m) : DefaultMultipliers[i];
        }
        UpdateLevel();
    }

    /// <summary>첫 클리어 방 수로 단계를 계산한다(문턱 이상인 가장 높은 단계, 첫 문턱 미만이면 입문).</summary>
    public static DungeonThreatLevel LevelFor(int clearedRooms, IReadOnlyList<int> levelThresholds)
    {
        int level = 0;
        if (levelThresholds != null)
            for (int i = 1; i < Mathf.Min(LevelCount, levelThresholds.Count); i++)
                if (clearedRooms >= levelThresholds[i]) level = i;
        return (DungeonThreatLevel)level;
    }

    /// <summary>
    /// 방 하나의 첫 클리어를 센다. 반환 = 이 방의 클리어 순번(0부터, DungeonRoomState.clearOrder에 적는다). 이미 센 방이면 -1(세지 않음).
    /// floor는 Wave 번호(방 id는 Wave마다 0부터라 함께 쓴다).
    /// </summary>
    public int RecordFirstClear(int floor, int roomId)
    {
        long key = ((long)floor << 32) | (uint)roomId;
        if (!counted.Add(key)) return -1;
        int order = ClearedRooms;
        ClearedRooms++;
        UpdateLevel();
        return order;
    }

    /// <summary>이 방을 이미 셌는지.</summary>
    public bool WasCounted(int floor, int roomId) => counted.Contains(((long)floor << 32) | (uint)roomId);

    /// <summary>처음(클리어 0, 입문)으로 돌린다 — 재도전·훈련 시작. 단계가 바뀌면 ThreatChanged를 올린다(notify false면 조용히).</summary>
    public void Reset(bool notify = true)
    {
        counted.Clear();
        ClearedRooms = 0;
        UpdateLevel(notify);
    }

    private void UpdateLevel(bool notify = true)
    {
        var previous = ThreatLevel;
        ThreatLevel = LevelFor(ClearedRooms, thresholds);
        if (ThreatLevel == previous || !notify) return;
        var handler = ThreatChanged;
        if (handler == null) return;
        // 구독자 예외가 진행(클리어 처리)을 막지 않게 하나씩 잡는다.
        foreach (Action<DungeonThreatLevel, DungeonThreatLevel> one in handler.GetInvocationList())
        {
            try { one(previous, ThreatLevel); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
