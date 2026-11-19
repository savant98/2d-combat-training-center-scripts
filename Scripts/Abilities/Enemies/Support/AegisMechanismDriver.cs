using UnityEngine;

// Aegis의 회전 부품(날개 로터·허브, 톱니 링, 위성 궤도, 코어)을 속도로 돌린다.
// Clip은 회전 각도 대신 이 컴포넌트의 속도 값(도/초)만 기록하고, 각도는 여기서 매 프레임 적분한다(NightmareCore와 같은 방식).
// - 패턴 Clip이 바뀌거나 순찰처럼 아무 시점에서 끊겨도 각도가 0으로 돌아가 튀지 않는다.
// - 로터가 서서히 빨라지고(충전) 멈추는(브레이크) 기계적인 가감속을 곡선 하나로 표현한다.
// Animator(같은 GameObject) 평가 뒤 LateUpdate에서 회전만 쓴다. 크기(Scale)는 건드리지 않는다.
public sealed class AegisMechanismDriver : MonoBehaviour
{
    public enum Channel { Rotor, Hub, Gear, Orbit, Core }

    [System.Serializable]
    public struct Spinner
    {
        public Transform part;
        public Channel channel;
        [Tooltip("채널 각도에 곱하는 배율. 부호로 방향을 정한다(왼쪽 로터 -1, 안쪽 톱니 -1.4 등)")]
        public float ratio;
    }

    [Header("회전 속도(도/초) - Clip이 기록한다")]
    public float rotorSpeed = 730f;
    public float hubSpeed = 180f;
    public float gearSpeed = 125f;
    public float orbitSpeed = 75f;
    public float coreSpeed;

    [Header("부품")]
    [SerializeField] private Spinner[] spinners = System.Array.Empty<Spinner>();
    [Tooltip("한 프레임에 이 각도(도)보다 많이 돌지 않게 한다. 빠른 회전이 거꾸로 도는 것처럼 보이는 착시를 막는다")]
    [SerializeField, Min(1f)] private float maxStepDegrees = 22f;

    private readonly float[] angles = new float[5];
    private readonly float[] speeds = new float[5];
    private Vector3[] baseEuler;
    // 부품마다 따로 적분한 각도. 배율이 정수가 아닌 부품(안쪽 톱니 -1.4)도 360도에서 감을 때 이음매가 생기지 않는다.
    private float[] spinnerAngles;

    public float GetAngle(Channel channel) => angles[(int)channel];
    // 검증용: 부품의 적분 각도(도)
    public float GetSpinnerAngle(int index) => spinnerAngles != null && index >= 0 && index < spinnerAngles.Length ? spinnerAngles[index] : 0f;
    public int SpinnerCount => spinners.Length;

    private void Awake()
    {
        baseEuler = new Vector3[spinners.Length];
        spinnerAngles = new float[spinners.Length];
        for (int i = 0; i < spinners.Length; i++)
            baseEuler[i] = spinners[i].part != null ? spinners[i].part.localEulerAngles : Vector3.zero;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        speeds[(int)Channel.Rotor] = rotorSpeed;
        speeds[(int)Channel.Hub] = hubSpeed;
        speeds[(int)Channel.Gear] = gearSpeed;
        speeds[(int)Channel.Orbit] = orbitSpeed;
        speeds[(int)Channel.Core] = coreSpeed;
        for (int c = 0; c < speeds.Length; c++)
            angles[c] = Mathf.Repeat(angles[c] + Step(speeds[c] * dt), 360f);

        for (int i = 0; i < spinners.Length; i++)
        {
            var spinner = spinners[i];
            if (spinner.part == null)
                continue;
            // 한 프레임 회전 제한은 배율을 곱한 실제 부품 회전에 건다.
            spinnerAngles[i] = Mathf.Repeat(spinnerAngles[i] + Step(speeds[(int)spinner.channel] * dt * spinner.ratio), 360f);
            Vector3 euler = baseEuler[i];
            euler.z += spinnerAngles[i];
            spinner.part.localRotation = Quaternion.Euler(euler);
        }
    }

    private float Step(float degrees) => Mathf.Clamp(degrees, -maxStepDegrees, maxStepDegrees);
}
