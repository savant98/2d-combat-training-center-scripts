using UnityEngine;

// 모터가 위에서의 착지만 허용하고 투사체·비행체는 통과시키는 생성 발판 표식이다.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class OneWayPlatform : MonoBehaviour
{
    // 같은 Prefab의 일반 Platform 끝블록은 OneWay 상판과 구분한다.
    public static bool IsSurface(Collider2D collider)
        => collider != null && collider.gameObject.layer == LayerMask.NameToLayer("TransparentPlatform")
            && collider.GetComponentInParent<OneWayPlatform>(true) != null;
}
