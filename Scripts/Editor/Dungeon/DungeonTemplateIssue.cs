using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 템플릿 검사 결과 한 줄. Error가 false면 경고다.
/// 6차(T16): 결과를 눌러 그 배치·칸으로 이동할 수 있게 위치(Wave 번호·배치 번호·칸)와 규칙 이름, 완성도 규칙 여부를 함께 담는다.
/// 위치를 직접 넣지 않은 예전 결과는 문장('맵 층 #3'·'적 구성 2(…) #4'·'(x, y)')에서 읽는다(<see cref="Locate"/>).
/// </summary>
public readonly struct DungeonTemplateIssue
{
    public readonly bool Error;
    public readonly string Message;
    /// <summary>규칙 이름(예: "T10 함정 간격", "v4-2 도달성"). 비면 예전 규칙.</summary>
    public readonly string Rule;
    /// <summary>완성도 규칙(생성 구역 필수·목표 자리 수·Prop 수 권장 등) — 테마 폴더의 실물 템플릿·빌더에서만 오류가 되는 규칙.</summary>
    public readonly bool Completeness;
    /// <summary>항목이 있는 Wave 번호(0 = 맵 층, 1.. = 적 구성). 모르면 -1.</summary>
    public readonly int Wave;
    /// <summary>그 Wave의 spawnObjects 번호. 모르면 -1.</summary>
    public readonly int Spawn;
    /// <summary>대표 칸이 있으면 true.</summary>
    public readonly bool HasCell;
    public readonly Vector2Int Cell;
    /// <summary>격자에 빨강 점선으로 칠할 위반 칸(T10 등). 없으면 null.</summary>
    public readonly Vector2Int[] Cells;

    public DungeonTemplateIssue(bool error, string message)
        : this(error, message, null, false, -1, -1, null, null) { }

    public DungeonTemplateIssue(bool error, string message, string rule, bool completeness, int wave, int spawn, Vector2Int? cell, Vector2Int[] cells)
    {
        Error = error; Message = message ?? ""; Rule = rule; Completeness = completeness;
        int w = wave, s = spawn; bool has = cell.HasValue; var c = cell ?? default;
        if (w < 0 || s < 0 || !has) Locate(Message, ref w, ref s, ref has, ref c);
        Wave = w; Spawn = s; HasCell = has; Cell = c; Cells = cells;
    }

    public override string ToString() => (Error ? "[오류] " : "[경고] ") + Message;

    static readonly Regex MapItem = new Regex(@"맵 층 #(\d+)", RegexOptions.Compiled);
    static readonly Regex EncounterItem = new Regex(@"적 구성 (\d+)(?:\([^)]*\))?\s*#(\d+)", RegexOptions.Compiled);
    static readonly Regex Encounter = new Regex(@"적 구성 (\d+)", RegexOptions.Compiled);
    static readonly Regex Point = new Regex(@"\((-?\d+), (-?\d+)\)", RegexOptions.Compiled);

    /// <summary>문장에서 위치를 읽는다(이미 아는 값은 그대로 둔다).</summary>
    static void Locate(string message, ref int wave, ref int spawn, ref bool hasCell, ref Vector2Int cell)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (wave < 0 || spawn < 0)
        {
            var map = MapItem.Match(message);
            var encounterItem = EncounterItem.Match(message);
            if (encounterItem.Success && (!map.Success || encounterItem.Index < map.Index))
            {
                if (wave < 0) wave = int.Parse(encounterItem.Groups[1].Value);
                if (spawn < 0) spawn = int.Parse(encounterItem.Groups[2].Value) - 1;
            }
            else if (map.Success)
            {
                if (wave < 0) wave = 0;
                if (spawn < 0) spawn = int.Parse(map.Groups[1].Value) - 1;
            }
            else if (wave < 0)
            {
                var encounter = Encounter.Match(message);
                if (encounter.Success) wave = int.Parse(encounter.Groups[1].Value);
            }
        }
        if (!hasCell)
        {
            var point = Point.Match(message);
            if (point.Success)
            {
                hasCell = true;
                cell = new Vector2Int(int.Parse(point.Groups[1].Value), int.Parse(point.Groups[2].Value));
            }
        }
    }
}
