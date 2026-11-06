using UnityEngine;

// 화면 왜곡 원천. 켜져 있고 strength가 0보다 크면 ScreenDistortionRenderer가 메인 카메라에 전체 화면 왜곡 패스를 넣는다.
// 왜곡 모양(셰이더 BlackHoleDistortion, 2D 게임의 노말맵 굴절 방식):
// - 렌즈: innerRadius 밖에서 화면을 더 바깥에서 가져와 공간이 중심으로 빨려 드는 것처럼 보인다.
// - 소용돌이: 안쪽일수록 크게 비튼다.
// - 노말맵 굴절: 회전하는 노말맵의 xy만큼 표본 위치를 옮긴다(파문이 소용돌이치며 흐른다).
// innerRadius 안(보스 몸통)은 왜곡하지 않는다. 거리 값은 모두 월드 칸이다.
[DisallowMultipleComponent]
public sealed class ScreenDistortionSource : MonoBehaviour
{
    [SerializeField] private Material distortionMaterial;

    [Header("Shape (World Units)")]
    [Tooltip("왜곡이 닿는 반지름(칸)")]
    [Min(0.01f)] public float radius = 7f;
    [Tooltip("이 반지름 안은 왜곡하지 않는다(보스 몸통이 일그러지지 않게)")]
    [Min(0f)] public float innerRadius = 2.6f;
    [Tooltip("공간이 중심으로 빨려 드는 정도(표본을 바깥에서 가져오는 비율)")]
    [Range(0f, 1.5f)] public float lens = 0.38f;
    [Tooltip("소용돌이 비틀림(라디안, 안쪽 가장자리 기준)")]
    [Range(-3f, 3f)] public float twist = 0.85f;
    [Tooltip("노말맵 굴절 세기(칸)")]
    [Min(0f)] public float normalStrength = 0.22f;
    [Tooltip("노말맵 회전 속도(도/초). 음수는 시계 방향")]
    public float normalSpin = -55f;

    [Header("Runtime")]
    [Tooltip("전체 세기. 0이면 패스를 넣지 않는다")]
    [Range(0f, 1f)] public float strength;

    public Material Material => distortionMaterial;
    public float Rotation { get; private set; }

    private void OnEnable() => ScreenDistortionRenderer.Register(this);

    private void OnDisable() => ScreenDistortionRenderer.Unregister(this);

    private void Update()
    {
        Rotation = Mathf.Repeat(Rotation + normalSpin * Mathf.Deg2Rad * Time.deltaTime, Mathf.PI * 2f);
    }
}
