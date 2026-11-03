using System;
using UnityEngine;

// 훈련 구간이 열리는 동안 통창·외곽선·후경 띠처럼 구간 끝까지 닿아야 하는 장식을 진행 방향으로 늘린다.
// 각 대상의 가운데보다 진행 방향 쪽(끝 쪽) 꼭짓점·점은 Apply로, 반대쪽(시작 쪽)은 ApplyStart로 옮겨 테두리 굵기와 모양은 그대로 둔다.
// 시작 쪽을 옮기면 구간 끝에 원래 방과 같은 크기의 공간을 만들 수 있다.
// 공유 Mesh 원본은 바꾸지 않고 실행 중 복사본을 쓰며, 다 닫히면 원래 Mesh와 점으로 되돌린다.
[DisallowMultipleComponent]
public sealed class TrainingCourseStretch : MonoBehaviour
{
    [Serializable]
    private struct Target
    {
        [Tooltip("늘릴 MeshFilter 또는 LineRenderer입니다.")]
        public Component component;
        [Tooltip("최대로 늘릴 때 끝 쪽을 옮기는 거리(월드, 칸)입니다.")]
        public float distance;
        [Tooltip("ApplyStart로 시작 쪽을 진행 방향으로 옮기는 거리(월드, 칸)입니다. 0이면 옮기지 않습니다.")]
        public float startDistance;
    }

    private sealed class State
    {
        public MeshFilter filter;
        public Mesh original;
        public Mesh copy;
        public LineRenderer line;
        public Vector3[] baseVertices;
        public Vector3[] working;
        public Vector3 localAxis;
        public float threshold;
    }

    [SerializeField, Tooltip("늘리는 방향(월드)입니다.")]
    private Vector2 direction = Vector2.up;
    [SerializeField] private Target[] targets = new Target[0];

    private State[] states;

    public float Amount { get; private set; }
    public float StartAmount { get; private set; }
    public int TargetCount => targets != null ? targets.Length : 0;

    // 끝 쪽을 늘린다(0~1).
    public void Apply(float amount)
    {
        Amount = Mathf.Clamp01(amount);
        Refresh();
    }

    // 시작 쪽을 진행 방향으로 옮긴다(0~1).
    public void ApplyStart(float amount)
    {
        StartAmount = Mathf.Clamp01(amount);
        Refresh();
    }

    private void Refresh()
    {
        if (Amount <= 0f && StartAmount <= 0f)
        {
            Restore();
            return;
        }
        Prepare();
        for (int i = 0; i < states.Length; i++)
        {
            var state = states[i];
            if (state == null) continue;
            Transform space = state.filter != null ? state.filter.transform : state.line.transform;
            Vector3 endDelta = Local(state, space, direction.normalized * targets[i].distance * Amount);
            Vector3 startDelta = Local(state, space, direction.normalized * targets[i].startDistance * StartAmount);
            for (int v = 0; v < state.baseVertices.Length; v++)
            {
                Vector3 vertex = state.baseVertices[v];
                state.working[v] = vertex + (Vector3.Dot(vertex, state.localAxis) > state.threshold ? endDelta : startDelta);
            }
            if (state.copy != null)
            {
                state.copy.vertices = state.working;
                state.copy.RecalculateBounds();
            }
            else state.line.SetPositions(state.working);
        }
    }

    private static Vector3 Local(State state, Transform space, Vector2 worldDelta)
        => state.line != null && state.line.useWorldSpace ? (Vector3)worldDelta : space.InverseTransformVector(worldDelta);

    private void Prepare()
    {
        if (states != null) return;
        states = new State[targets.Length];
        Vector3 worldAxis = direction.normalized;
        for (int i = 0; i < targets.Length; i++)
        {
            var state = new State();
            if (targets[i].component is MeshFilter filter && filter.sharedMesh != null)
            {
                state.filter = filter;
                state.original = filter.sharedMesh;
                state.copy = Instantiate(state.original);
                state.copy.name = state.original.name + " (Stretch)";
                state.copy.hideFlags = HideFlags.DontSave;
                state.baseVertices = state.original.vertices;
                state.localAxis = filter.transform.InverseTransformDirection(worldAxis).normalized;
                filter.sharedMesh = state.copy;
            }
            else if (targets[i].component is LineRenderer line)
            {
                state.line = line;
                state.baseVertices = new Vector3[line.positionCount];
                line.GetPositions(state.baseVertices);
                state.localAxis = line.useWorldSpace ? worldAxis : line.transform.InverseTransformDirection(worldAxis).normalized;
            }
            else continue;

            // 가운데를 기준으로 진행 방향 쪽 절반만 옮긴다.
            float min = float.MaxValue, max = float.MinValue;
            foreach (var vertex in state.baseVertices)
            {
                float along = Vector3.Dot(vertex, state.localAxis);
                min = Mathf.Min(min, along);
                max = Mathf.Max(max, along);
            }
            state.threshold = (min + max) * .5f;
            state.working = new Vector3[state.baseVertices.Length];
            states[i] = state;
        }
    }

    private void Restore()
    {
        Amount = 0f;
        StartAmount = 0f;
        if (states == null) return;
        foreach (var state in states)
        {
            if (state == null) continue;
            if (state.filter != null)
            {
                if (state.filter.sharedMesh == state.copy) state.filter.sharedMesh = state.original;
                if (state.copy != null) Destroy(state.copy);
            }
            else if (state.line != null) state.line.SetPositions(state.baseVertices);
        }
        states = null;
    }

    private void OnDisable() => Restore();
}
