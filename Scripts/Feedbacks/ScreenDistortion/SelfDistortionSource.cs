using UnityEngine;

// 자기 왜곡 원천(2026-11-14, Nightmare의 코어). 화면 전체를 굴절시키는 ScreenDistortionSource(Admin의 블랙홀)와 달리,
// 여기에 등록한 Renderer(이 개체의 발광 파츠)만 따로 그려 그 그림에만 노말맵 굴절을 적용한다. 배경·Player·다른 개체는 일그러지지 않는다.
// - 켜져 있고 strength가 0보다 크면 SelfDistortionRenderer가 메인 카메라에 패스를 넣는다. 0이면 평소대로 그린다.
// - 굴절 모양은 블랙홀과 같다: 원판(radius)을 노말맵 한 장으로 덮어 돌리고, 그 xy만큼 표본 위치를 옮긴다. innerRadius 안(코어 몸통)은 왜곡하지 않는다.
// - 대상은 가산 발광(GlowMultiply·GlowAdd) Renderer만 넣는다. 따로 그린 그림을 화면에 더해 넣으므로, 뒤를 가리는(알파) Renderer는 맞지 않는다.
//   대상은 이 패스가 그리는 동안 화면의 다른 것들 위에 더해진다. 가산 발광끼리는 순서가 달라져도 색이 같다.
//   다만 대상보다 앞에 그려지던 불투명한(알파) 그림은 대상의 빛을 가리지 못하고 그 위에 빛이 더해진다(이 방식의 한계).
//   Nightmare에서는 코어에서 막 나온 유도탄의 검은 원판과 코어 가까이의 머리 위 표시가 그렇다.
// 거리 값은 모두 월드 칸이다.
[DisallowMultipleComponent]
public sealed class SelfDistortionSource : MonoBehaviour
{
    [SerializeField] private Material distortionMaterial;
    [SerializeField, Tooltip("왜곡의 중심. 비워 두면 이 Transform")] private Transform center;
    [SerializeField, Tooltip("따로 그려 왜곡할 가산 발광 Renderer")] private Renderer[] targets;

    [Header("Shape (World Units)")]
    [Tooltip("왜곡이 닿는 반지름(칸)")]
    [Min(0.01f)] public float radius = 3.6f;
    [Tooltip("이 반지름 안은 왜곡하지 않는다(코어 몸통이 일그러지지 않게)")]
    [Min(0f)] public float innerRadius = 0.8f;
    [Tooltip("노말맵 굴절 세기(칸)")]
    [Min(0f)] public float normalStrength = 0.2f;
    [Tooltip("소용돌이 비틀림(라디안, 가장 강한 곳 기준). 0이면 비틀지 않는다")]
    [Range(-1.5f, 1.5f)] public float twist = 0f;
    [Tooltip("노말맵 회전 속도(도/초). 음수는 시계 방향")]
    public float normalSpin = -40f;

    [Header("Runtime")]
    [Tooltip("전체 세기. 0이면 패스를 넣지 않는다")]
    [Range(0f, 1f)] public float strength;

    public Material Material => distortionMaterial;
    public Renderer[] Targets => targets;
    // 모터로 움직이는 개체(FixedMotionBody)는 카메라를 그리는 동안 표시 위치로 옮겨져 있다(FixedMotionPresentation이 먼저 구독해 먼저 불린다).
    // 패스는 그 뒤에 이 값을 읽으므로 화면에 보이는 자리와 왜곡의 중심이 맞는다.
    public Vector3 Center => center != null ? center.position : transform.position;
    /// <summary>노말맵이 지금까지 돈 각도(라디안).</summary>
    public float Rotation { get; private set; }

    private void OnEnable() => SelfDistortionRenderer.Register(this);

    private void OnDisable() => SelfDistortionRenderer.Unregister(this);

    private void Update()
    {
        Rotation = Mathf.Repeat(Rotation + normalSpin * Mathf.Deg2Rad * Time.deltaTime, Mathf.PI * 2f);
    }
}
