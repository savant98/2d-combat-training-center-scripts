using UnityEngine;

// Wave 격자 편집기의 Platform 팔레트에 표시되는 환경 도구입니다.
[DisallowMultipleComponent]
public sealed class WaveEnvironmentTool : MonoBehaviour
{
    public enum RangeShape { None, Circle, Box }

    // 아래 값은 Wave 격자 편집기의 표시에만 쓰며 실행 중 판정에는 관여하지 않는다.
    [Header("편집기 표시")]
    [Tooltip("켜면 격자 표식의 배경과 선택 영역을 장치가 놓인 칸 하나로 줄입니다. 한 칸보다 큰 장치가 아래 지형을 가리지 않게 합니다.")]
    [SerializeField] private bool compactMarker;
    [Tooltip("장치가 움직이며 닿는 범위를 격자에 덧그립니다.")]
    [SerializeField] private RangeShape rangeShape = RangeShape.None;
    [Tooltip("장치 원점 기준의 범위 중심입니다. 장치와 함께 회전합니다.")]
    [SerializeField] private Vector2 rangeCenter;
    [Tooltip("Circle은 x가 반지름, Box는 가로·세로 크기입니다.")]
    [SerializeField] private Vector2 rangeSize = Vector2.one;
    [SerializeField] private Color rangeColor = new Color(1f, .22f, .16f, .9f);
    [Tooltip("장치가 차지하는 칸 수(회전 전 가로·세로). 같은 칸 중복 검사와 배치 기준점에 쓴다. 짝수 축은 격자 꼭짓점에 놓인다.")]
    [SerializeField] private Vector2Int footprint = Vector2Int.one;

    public bool CompactMarker => compactMarker;
    public RangeShape Range => rangeShape;
    public Vector2 RangeCenter => rangeCenter;
    public Vector2 RangeSize => rangeSize;
    public Color RangeColor => rangeColor;
    public Vector2Int Footprint => new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));

    /// <summary>생성 도구가 장치의 작동 범위를 편집기 표시용으로 기록한다.</summary>
    public void ConfigureEditorDisplay(bool compact, RangeShape shape, Vector2 center, Vector2 size, Color color) =>
        ConfigureEditorDisplay(compact, shape, center, size, color, Vector2Int.one);

    public void ConfigureEditorDisplay(bool compact, RangeShape shape, Vector2 center, Vector2 size, Color color, Vector2Int cells)
    {
        footprint = cells;
        compactMarker = compact;
        rangeShape = shape;
        rangeCenter = center;
        rangeSize = size;
        rangeColor = color;
    }
}
