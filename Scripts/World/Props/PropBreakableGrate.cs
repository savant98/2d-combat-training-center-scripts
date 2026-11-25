using System;
using System.Collections.Generic;
using UnityEngine;

// 파괴 격자(N20, 파괴형 + OneWay 발판). 위에 설 수 있고(밑에서 뛰어 올라올 수 있다), 부수면 아래가 열린다(갱도로 내려가는 지름길).
// - 체력·피격 흔들림·부서짐(모든 Collider 끔·표시 숨김·Broken 이벤트·풀 반환)은 같은 루트의 DestructibleProp가 맡는다.
//   이 부품은 길이(2~10칸, 배치 설정)에 맞춰 OneWay 면·hurtbox·표시 칸 조각을 맞추고, 부서질 때 길이 전체에서 파편을 낸다.
// - 왼쪽 끝은 footprint 왼쪽 변에 고정되고 오른쪽으로 늘어난다. 표시는 생성 도구가 만든 한 칸 조각(Cell)을 실행 중 복제한다(크기 늘이기 없음).
//   늘어난 칸의 흰 외곽은 피격 번쩍임에 들어가지 않는다(번쩍임 대상은 Prefab에 저장된 칸만 — 판단값).
[DisallowMultipleComponent]
[RequireComponent(typeof(DestructibleProp))]
public sealed class PropBreakableGrate : MonoBehaviour, IDungeonPropPart, IDungeonPropSettings
{
    public const string LengthKey = "grateLength";
    public const int MinLength = 2, MaxLength = 10;

    [Header("배치 기본값")]
    [SerializeField, Range(MinLength, MaxLength)] private int length = 2;

    [Header("구성(생성 도구가 연결)")]
    [SerializeField, Min(1)] private int footprintCells = 2;
    [SerializeField, Tooltip("OneWay 발판 면(TransparentPlatform + OneWayPlatform).")] private BoxCollider2D surface;
    [SerializeField, Tooltip("피격 상자(Enemy 레이어 트리거) — DestructibleProp.hurtbox와 같다.")] private BoxCollider2D hurtbox;
    [SerializeField, Min(0f), Tooltip("hurtbox를 발판보다 좌우로 넓히는 폭(칸, 한쪽).")] private float hurtboxMargin = .04f;
    [SerializeField] private Transform cellTemplate;
    [SerializeField] private Transform[] builtCells = Array.Empty<Transform>();
    [SerializeField] private Transform capRight;
    [SerializeField, Tooltip("부서질 때 길이 전체에서 떨어지는 파편(표시 묶음 밖 — 표시가 숨겨져도 보인다).")] private ParticleSystem breakDebris;
    [SerializeField, Tooltip("부서질 때 튀는 불꽃.")] private ParticleSystem breakSparks;

    private readonly List<Transform> cells = new List<Transform>();
    private DestructibleProp health;
    private Vector3 capRightHome;
    private int activeLength;
    private bool cached;

    public int Length => cached ? activeLength : length;
    public BoxCollider2D Surface => surface;
    public int CellCount => cells.Count;
    private float LeftLocal => -footprintCells * .5f;

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        health = GetComponent<DestructibleProp>();
        ReadSettings(WaveDeviceSettings.None);
        if (capRight != null) capRightHome = capRight.localPosition;
        cells.Clear();
        foreach (var cell in builtCells) if (cell != null) cells.Add(cell);
        if (cells.Count == 0 && cellTemplate != null) cells.Add(cellTemplate);
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        // DestructibleProp.OnPropReturning이 Broken 구독자를 지우므로 대여마다 다시 건다.
        if (health != null) { health.Broken -= OnBroken; health.Broken += OnBroken; }
        PropHazardKit.Stop(breakDebris, true);
        PropHazardKit.Stop(breakSparks, true);
    }

    public void OnPropReturning(DungeonProp prop) { }

    private void OnDestroy()
    {
        if (health != null) health.Broken -= OnBroken;
    }

    private void OnBroken(IDungeonBreakable _)
    {
        int cellsWide = Mathf.Clamp(activeLength, MinLength, MaxLength);
        float left = LeftLocal;
        foreach (var system in new[] { breakDebris, breakSparks })
        {
            if (system == null) continue;
            // 입자 상자 가운데를 격자 가운데로 옮기고 가로 폭만 길이에 맞춘다.
            system.transform.localPosition = new Vector3(left + cellsWide * .5f, system.transform.localPosition.y, system.transform.localPosition.z);
            var shape = system.shape;
            shape.scale = new Vector3(cellsWide, shape.scale.y, shape.scale.z);
            system.Emit(system == breakDebris ? 7 * cellsWide : 5 * cellsWide);
        }
    }

    // ── 배치별 설정 ─────────────────────────────

    private WaveDeviceParameter LengthParameter => new WaveDeviceParameter(LengthKey, "길이", "격자 길이(칸)입니다. 왼쪽 끝은 그대로 두고 오른쪽으로 늘어납니다. 늘어난 칸에는 다른 것을 놓지 마세요.", "칸", MinLength, MaxLength, 1f, length, true);

    public WaveDeviceParameter[] PropParameters => new[] { LengthParameter };

    public void ApplyPropSettings(WaveDeviceSetting[] overrides)
    {
        ReadSettings(overrides);
        if (Application.isPlaying && cached) Rebuild();
    }

    private void ReadSettings(WaveDeviceSetting[] overrides) => activeLength = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, LengthParameter));

    public bool TryGetPropRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range)
    {
        int cellsWide = Mathf.RoundToInt(WaveDeviceSettings.Value(overrides, LengthParameter));
        // 중립(흰) 범위: 위험이 아니라 발판이 차지하는 칸을 보인다.
        range = new WaveDeviceRange(WaveEnvironmentTool.RangeShape.Box, new Vector2(LeftLocal + cellsWide * .5f, 0f), new Vector2(cellsWide, 1f), new Color(.85f, .9f, .95f, .8f));
        return true;
    }

    private void Rebuild()
    {
        int wanted = Mathf.Clamp(activeLength, MinLength, MaxLength);
        float left = LeftLocal;
        var template = cellTemplate != null ? cellTemplate : cells.Count > 0 ? cells[0] : null;
        while (template != null && cells.Count < wanted)
        {
            var clone = Instantiate(template, template.parent, false);
            clone.name = template.name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + cells.Count;
            cells.Add(clone);
        }
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell == null) continue;
            bool on = i < wanted;
            if (cell.gameObject.activeSelf != on) cell.gameObject.SetActive(on);
            if (on) cell.localPosition = new Vector3(left + i + .5f, cell.localPosition.y, cell.localPosition.z);
        }
        if (capRight != null) capRight.localPosition = capRightHome + new Vector3(wanted - footprintCells, 0f, 0f);
        if (surface != null)
        {
            surface.offset = new Vector2(left + wanted * .5f, surface.offset.y);
            surface.size = new Vector2(wanted, surface.size.y);
        }
        if (hurtbox != null)
        {
            hurtbox.offset = new Vector2(left + wanted * .5f, hurtbox.offset.y);
            hurtbox.size = new Vector2(wanted + hurtboxMargin * 2f, hurtbox.size.y);
        }
        RaycastMotor2D.InvalidateAttackQuerySync();
    }
}
