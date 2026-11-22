using UnityEngine;

// 각 층이 같은 규격의 벽면 모듈을 쓰는 방에서 반복 폭과 시차를 지정한다.
[DisallowMultipleComponent]
public sealed class WaveBackdropPattern : MonoBehaviour
{
    [SerializeField] private Vector2 repeat = new Vector2(34f, 21f);
    [SerializeField, Range(0f, 1f)] private float farFollow = .88f;
    [SerializeField, Range(0f, 1f)] private float frameFollow = .66f;
    [SerializeField, Range(0f, 1f)] private float equipmentFollow = .44f;

    public Vector2 Repeat => new Vector2(Mathf.Max(1f, repeat.x), Mathf.Max(1f, repeat.y));
    public float FarFollow => farFollow;
    public float FrameFollow => frameFollow;
    public float EquipmentFollow => equipmentFollow;
}
