using System;
using UnityEngine;

// 셔터 게이트(N22, 진행 — 클리어 반응). 1×3 고체 문. 방을 클리어하면 빗장이 풀리고 셔터 판이 위로 겹쳐 올라가 지름길·보조 구역을 연다.
// - 던전 안: DungeonRoomStage가 방을 세울 때 그 방의 클리어 여부로 즉시 적용(미클리어 = 닫힘, 클리어 = 열림), 클리어 순간 여는 연출.
// - 던전 밖(기본 진행 Wave): 방 신호가 없으므로 열린 채 선다(통로를 막지 않는다 — 판단값).
// - 닫힌 동안 SolidMount(Platform 레이어 고체)를 켜고, 다 열리면 끈다. 여는 도중에는 아래쪽 판이 올라간 만큼만 통과할 수 있게 보이지만
//   판정은 다 열린 뒤에 끈다(판 사이에 끼지 않게 짧게 — 0.45초).
// - 움직임: 빗장 회전으로 풀림 → 셔터 판 3장이 차례로 위 칸으로 겹쳐 올라감(이동, 크기 변형 없음) → 상태등 흰색(잠김) → 파랑(열림). 정지 구조라 소리는 여닫을 때만.
[DisallowMultipleComponent]
public sealed class PropShutterGate : MonoBehaviour, IDungeonPropPart, IDungeonRoomPowered
{
    [Header("구성(생성 도구가 연결)")]
    [SerializeField] private BoxCollider2D solid;
    [SerializeField, Tooltip("아래에서 위 순서의 셔터 판. 열리면 맨 위 판 자리로 겹쳐 올라간다.")] private Transform[] slats = Array.Empty<Transform>();
    [SerializeField] private Transform latch;
    [SerializeField] private float latchOpenAngle = 80f;
    [SerializeField] private Renderer[] lockedLights = Array.Empty<Renderer>();
    [SerializeField] private Renderer[] openLights = Array.Empty<Renderer>();

    [Header("움직임")]
    [SerializeField, Min(.05f)] private float latchTime = .18f;
    [SerializeField, Min(.05f)] private float openTime = .45f;
    [SerializeField, Tooltip("판마다 출발을 늦추는 시간(초).")] private float slatStagger = .06f;

    [Header("효과(임시: 기존 효과)")]
    [SerializeField] private FeedbackDataSO openFeedback;

    private bool cached;
    private bool isLive;
    private bool open;
    private float progress;           // 0 닫힘 ~ 1 열림(빗장 + 판)
    private Vector3[] slatHome = Array.Empty<Vector3>();
    private Quaternion latchHome;
    private FacilityLights lockedLamp, openLamp;

    public bool IsOpen => open && progress >= 1f;
    public bool IsClosed => !open && progress <= 0f;
    public bool SolidEnabled => solid != null && solid.enabled;
    private float Total => latchTime + openTime + slatStagger * Mathf.Max(0, slats.Length - 1);

    private void Awake() => Cache();

    private void Cache()
    {
        if (cached) return;
        cached = true;
        slatHome = new Vector3[slats.Length];
        for (int i = 0; i < slats.Length; i++) slatHome[i] = slats[i] != null ? slats[i].localPosition : Vector3.zero;
        if (latch != null) latchHome = latch.localRotation;
        lockedLamp = new FacilityLights(lockedLights);
        openLamp = new FacilityLights(openLights);
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Cache();
        isLive = true;
        // 방 신호(SetRoomPowered)가 곧바로 오지 않으면(던전 밖) 열린 채 선다.
        Set(!FacilityAccess.InDungeon, true);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        isLive = false;
        lockedLamp?.Restore();
        openLamp?.Restore();
    }

    public void SetRoomPowered(bool powered, bool instant)
    {
        Cache();
        Set(powered, instant);
    }

    private void Set(bool wantOpen, bool instant)
    {
        bool was = open;
        open = wantOpen;
        if (instant) progress = open ? 1f : 0f;
        else if (open && !was) FacilityFeedback.Play(openFeedback, transform.position);
        Apply();
    }

    private void Update()
    {
        if (!isLive) return;
        float target = open ? 1f : 0f;
        // Approximately로 멈추면 0.9999991처럼 1에 못 미친 채 서서 고체가 꺼지지 않는다. MoveTowards는 마지막 걸음에 목표값을 정확히 돌려준다.
        if (progress != target)
        {
            progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Total);
            Apply();
        }
    }

    private void Apply()
    {
        float elapsed = progress * Total;
        if (latch != null)
        {
            float k = Mathf.Clamp01(elapsed / latchTime);
            latch.localRotation = latchHome * Quaternion.Euler(0f, 0f, latchOpenAngle * k * k * (3f - 2f * k));
        }
        int count = slats.Length;
        Vector3 stack = count > 0 ? slatHome[count - 1] : Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            if (slats[i] == null) continue;
            float local = Mathf.Clamp01((elapsed - latchTime - slatStagger * (count - 1 - i)) / openTime);
            float e = local * local * (3f - 2f * local);
            // 위 판부터 먼저 올라가 맨 위 판 자리(위 칸) 뒤로 겹친다. 겹친 판은 조금씩 안쪽(z)으로.
            Vector3 raised = new Vector3(slatHome[i].x, stack.y, slatHome[i].z + .001f * (count - i));
            slats[i].localPosition = Vector3.Lerp(slatHome[i], raised, e);
        }
        if (solid != null)
        {
            bool block = progress < 1f;
            if (solid.enabled != block)
            {
                solid.enabled = block;
                RaycastMotor2D.InvalidateAttackQuerySync();
            }
        }
        lockedLamp?.Set(1f - Mathf.Clamp01(progress * 3f));
        openLamp?.Set(Mathf.Clamp01(progress * 2f - 1f));
    }
}
