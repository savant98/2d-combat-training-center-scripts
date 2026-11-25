using System;
using UnityEngine;

/// <summary>
/// 분류 로봇 팔(DungeonPropSortingArm, 2×3 장식)의 공회전 동작(2026-11-24, 던전 2차 decor).
/// 천장 받침에 매달린 3관절 팔이 아래 라인에서 부품을 집어(집게 닫힘 → 부품 보임) 들어 올리고, 옆으로 옮겨 내려놓고(집게 열림 → 부품 사라짐),
/// 접힌 자세로 돌아와 잠시 쉰다. 관절 회전·집게 회전·부품 켜고 끄기만 쓴다(크기 늘이기 없음). Collider가 없는 장식이다.
/// - 소리: 관절이 움직이는 동안만 반복음(DungeonPropLoopSound.SetLoopActive), 집고 놓을 때 간헐음(gripCue).
/// - 화면 밖이면 시계를 멈춘다(DungeonDecorView). 여러 팔이 같은 박자로 움직이지 않게 놓인 자리로 시작 위상을 다르게 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropSortingArm : MonoBehaviour, IDungeonPropPart
{
    [Serializable]
    public struct Pose
    {
        [Tooltip("어깨 회전(도, 아래를 0으로 반시계 +)")] public float shoulder;
        [Tooltip("팔꿈치 회전(어깨 기준)")] public float elbow;
        [Tooltip("손목 회전(팔꿈치 기준)")] public float wrist;
        public Pose(float shoulder, float elbow, float wrist) { this.shoulder = shoulder; this.elbow = elbow; this.wrist = wrist; }
    }

    private enum Kind { Hold, Move, Grip, Release }

    private readonly struct Segment
    {
        public readonly Kind Kind;
        public readonly int From, To;
        public readonly float Duration;
        public Segment(Kind kind, int from, int to, float duration) { Kind = kind; From = from; To = to; Duration = duration; }
    }

    [Header("관절")]
    [SerializeField] private Transform shoulder;
    [SerializeField] private Transform elbow;
    [SerializeField] private Transform wrist;
    [SerializeField] private Transform fingerLeft;
    [SerializeField] private Transform fingerRight;
    [SerializeField, Tooltip("집고 있는 동안만 보이는 부품")] private GameObject item;

    [Header("자세(도)")]
    [SerializeField] private Pose rest = new Pose(34.7f, -104f, 94.2f);
    [SerializeField] private Pose pick = new Pose(-30.1f, 33.7f, -3.6f);
    [SerializeField] private Pose lift = new Pose(-47.8f, 99.2f, -51.4f);
    [SerializeField] private Pose place = new Pose(30.1f, -33.7f, 3.6f);
    [SerializeField, Tooltip("집게 손가락이 벌어진 각도")] private float fingerOpen = 22f;
    [SerializeField, Tooltip("집게 손가락이 닫힌 각도")] private float fingerClosed = 5f;

    [Header("시간(초)")]
    [SerializeField, Min(.05f)] private float restTime = 1.1f;
    [SerializeField, Min(.05f)] private float moveTime = .8f;
    [SerializeField, Min(.05f)] private float holdTime = .2f;
    [SerializeField, Min(.05f)] private float gripTime = .25f;

    [Header("소리")]
    [SerializeField] private DungeonPropLoopSound sound;
    [SerializeField, Tooltip("집고 놓을 때 낼 간헐음 번호")] private int gripCue;

    [Header("화면 판정")]
    [SerializeField, Tooltip("움직이는 범위의 반 크기(칸). footprint 반 크기와 같다.")] private Vector2 viewHalfSize = new Vector2(1f, 1.5f);

    private Segment[] timeline;
    private float cycleLength, clock;
    private int lastSegment = -1;

    /// <summary>지금 구간 번호(0 쉼, 1 집으러 감, 2 멈춤, 3 집기, 4 들어 올림, 5 옮김, 6 놓기, 7 멈춤, 8 돌아옴).</summary>
    public int SegmentIndex { get; private set; }
    public bool IsMoving { get; private set; }
    public bool ItemVisible => item != null && item.activeSelf;
    public float CycleLength { get { Build(); return cycleLength; } }
    /// <summary>풀에서 꺼낸 뒤 마친 동작 주기 수(검사용).</summary>
    public int CyclesCompleted { get; private set; }
    public bool WasAnimating { get; private set; }

    private void Awake()
    {
        Build();
        if (sound == null) sound = GetComponent<DungeonPropLoopSound>();
    }

    private void Build()
    {
        if (timeline != null) return;
        timeline = new[]
        {
            new Segment(Kind.Hold, 0, 0, restTime),
            new Segment(Kind.Move, 0, 1, moveTime),
            new Segment(Kind.Hold, 1, 1, holdTime),
            new Segment(Kind.Grip, 1, 1, gripTime),
            new Segment(Kind.Move, 1, 2, moveTime),
            new Segment(Kind.Move, 2, 3, moveTime * .7f),
            new Segment(Kind.Release, 3, 3, gripTime),
            new Segment(Kind.Hold, 3, 3, holdTime),
            new Segment(Kind.Move, 3, 0, moveTime),
        };
        cycleLength = 0f;
        foreach (var segment in timeline) cycleLength += segment.Duration;
    }

    private Pose PoseAt(int index)
    {
        switch (index)
        {
            case 1: return pick;
            case 2: return lift;
            case 3: return place;
            default: return rest;
        }
    }

    public void OnPropSpawned(DungeonProp prop)
    {
        Build();
        // 같은 방의 팔들이 같은 박자로 움직이지 않게 놓인 자리로 시작 위상을 정한다(쉼 구간 안에서 시작).
        Vector2 at = transform.position;
        clock = Mathf.Repeat(at.x * .37f + at.y * .71f, restTime);
        lastSegment = -1;
        CyclesCompleted = 0;
        IsMoving = false;
        ApplyPose(rest, 0f);
        if (item != null) item.SetActive(false);
        if (sound != null) sound.SetLoopActive(false);
    }

    public void OnPropReturning(DungeonProp prop)
    {
        IsMoving = false;
        if (sound != null) sound.SetLoopActive(false);
    }

    private void Update()
    {
        WasAnimating = false;
        Build();
        if (!DungeonDecorView.Near(transform.position, viewHalfSize))
        {
            IsMoving = false;
            if (sound != null) sound.SetLoopActive(false);
            return;
        }
        WasAnimating = true;
        float previous = clock;
        clock += Time.deltaTime;
        if (Mathf.FloorToInt(clock / cycleLength) > Mathf.FloorToInt(previous / cycleLength)) CyclesCompleted++;
        Evaluate(Mathf.Repeat(clock, cycleLength));
    }

    private void Evaluate(float time)
    {
        int index = 0;
        float local = time;
        while (index < timeline.Length - 1 && local >= timeline[index].Duration)
        {
            local -= timeline[index].Duration;
            index++;
        }
        var segment = timeline[index];
        float t = Mathf.Clamp01(local / Mathf.Max(.01f, segment.Duration));
        float eased = t * t * (3f - 2f * t);
        SegmentIndex = index;

        if (index != lastSegment)
        {
            // 집기·놓기 구간에 들어서는 순간 간헐음.
            if (lastSegment >= 0 && (segment.Kind == Kind.Grip || segment.Kind == Kind.Release) && sound != null) sound.PlayCue(gripCue);
            lastSegment = index;
        }

        Pose from = PoseAt(segment.From), to = PoseAt(segment.To);
        var pose = new Pose(Mathf.Lerp(from.shoulder, to.shoulder, eased), Mathf.Lerp(from.elbow, to.elbow, eased), Mathf.Lerp(from.wrist, to.wrist, eased));
        // 집게: 집기 구간에서 닫히고, 놓기 구간에서 열린다(들고 옮기는 동안 닫힘).
        float grip;
        if (index < 3 || index > 6) grip = 0f;
        else if (index == 3) grip = eased;
        else if (index == 6) grip = 1f - eased;
        else grip = 1f;
        ApplyPose(pose, grip);

        bool holding = (index == 3 && t >= .6f) || index == 4 || index == 5 || (index == 6 && t < .4f);
        if (item != null && item.activeSelf != holding) item.SetActive(holding);

        IsMoving = segment.Kind == Kind.Move;
        if (sound != null) sound.SetLoopActive(IsMoving, .55f + .45f * Mathf.Sin(t * Mathf.PI));
    }

    private void ApplyPose(Pose pose, float grip)
    {
        if (shoulder != null) shoulder.localRotation = Quaternion.Euler(0f, 0f, pose.shoulder);
        if (elbow != null) elbow.localRotation = Quaternion.Euler(0f, 0f, pose.elbow);
        if (wrist != null) wrist.localRotation = Quaternion.Euler(0f, 0f, pose.wrist);
        float finger = Mathf.Lerp(fingerOpen, fingerClosed, grip);
        // 손가락은 아래를 향해 그려져 있다: 왼손가락은 시계 방향(-)으로 벌어지고, 오른손가락은 반시계(+)로 벌어진다.
        if (fingerLeft != null) fingerLeft.localRotation = Quaternion.Euler(0f, 0f, -finger);
        if (fingerRight != null) fingerRight.localRotation = Quaternion.Euler(0f, 0f, finger);
    }
}
