using UnityEngine;

// 모터가 위에서의 착지만 허용하고 투사체·비행체는 통과시키는 생성 발판 표식이다.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class OneWayPlatform : MonoBehaviour
{
    // "TransparentPlatform" 레이어 번호. 모터가 광선·적중마다 하던 LayerMask.NameToLayer(문자열 검색)를 처음 한 번만 하고 그 값을 계속 쓴다.
    // 레이어 이름 표는 실행 중에 바뀌지 않으므로 값이 같다(레이어가 없으면 예전처럼 -1).
    // 정적 필드 초기화식은 Prefab을 읽는 중(직렬화 중)에 실행될 수 있어 NameToLayer 호출이 막힐 수 있으므로 처음 쓸 때 찾는다.
    private static int surfaceLayer = int.MinValue;
    public static int SurfaceLayer
    {
        get
        {
            if (surfaceLayer == int.MinValue)
                surfaceLayer = LayerMask.NameToLayer("TransparentPlatform");
            return surfaceLayer;
        }
    }
    // Domain Reload를 끈 Play 반복에서도 Play마다 다시 찾는다(Play 사이에 레이어 이름을 바꿔도 예전처럼 새 값을 쓴다).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSurfaceLayer() => surfaceLayer = int.MinValue;

    // 같은 Prefab의 일반 Platform 끝블록은 OneWay 상판과 구분한다.
    public static bool IsSurface(Collider2D collider)
        => collider != null && collider.gameObject.layer == SurfaceLayer
            && collider.GetComponentInParent<OneWayPlatform>(true) != null;
}
