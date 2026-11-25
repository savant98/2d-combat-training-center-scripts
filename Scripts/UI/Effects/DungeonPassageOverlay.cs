using UnityEngine;

/// <summary>
/// 던전 관 통과 중 화면을 덮는 관 속 오버레이. PipePassageEffectSO가 실행 중 UI Canvas(암전 바로 앞)에 한 번 만들어 재사용한다.
/// Prefab: Assets/Resources/UI/DungeonPassageOverlay.prefab(tools/Unity/DungeonPassageSetup.cs가 만든다, 평소 꺼짐).
/// </summary>
/// <remarks>
/// 덮는 정도(head)와 걷히는 정도(tail)는 연출이 프레임마다 넘긴다. 연출이 끊겨(바깥에서 코루틴을 멈춤 등) 구동이 끊기면
/// 잠시 뒤 스스로 꺼져 화면이 덮인 채 남지 않는다. 리브의 흐름은 실제 시간(unscaledDeltaTime)으로 움직인다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class DungeonPassageOverlay : MonoBehaviour
{
    [SerializeField, Tooltip("관 속을 그리는 그래픽.")] private DungeonPipeTunnelGraphic tunnel;
    [SerializeField, Min(.1f), Tooltip("연출이 이 시간(실제 초) 넘게 덮는 정도를 넘기지 않으면 스스로 끈다.")] private float idleTimeout = .75f;

    private float lastDriven;
    private int lastDrivenFrame;

    public bool IsShowing => gameObject.activeSelf;
    public DungeonPipeTunnelGraphic Tunnel => tunnel;

    /// <summary>direction(관 통과 진행 방향, 월드)으로 지나가는 관 속을 보여 줄 준비를 한다. 아직 화면을 덮지 않는다(head 0).</summary>
    public void Begin(Vector2 direction)
    {
        if (tunnel == null) tunnel = GetComponentInChildren<DungeonPipeTunnelGraphic>(true);
        if (tunnel != null)
        {
            tunnel.Direction = direction.x < 0f ? -1 : 1;
            tunnel.ResetFlow();
        }
        SetCoverage(0f, 0f);
    }

    /// <summary>head: 진행 방향 쪽에서 밀려와 덮은 정도(0~1), tail: 같은 방향으로 걷힌 정도(0~1). 꺼져 있으면 다시 켠다.</summary>
    public void SetCoverage(float head, float tail)
    {
        lastDriven = Time.unscaledTime;
        lastDrivenFrame = Time.frameCount;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (tunnel != null) tunnel.SetCoverage(head, tail);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void Update()
    {
        if (tunnel != null) tunnel.Advance(Time.unscaledDeltaTime);
        // 연출이 프레임마다 구동하지 않으면(멈춘 코루틴) 덮인 채 남지 않게 끈다. 몇 프레임과 실제 시간을 함께 본다(긴 프레임 하나로 꺼지지 않게).
        if (Time.frameCount - lastDrivenFrame > 3 && Time.unscaledTime - lastDriven > idleTimeout) Hide();
    }
}
