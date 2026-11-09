using UnityEngine;

// 컨트롤러의 모터 이동(기본 순서 0)이 끝난 뒤 새 면의 실제 도착 여부를 판정한다.
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(TumblePatrolAbility))]
public sealed class TumblePatrolDirectionSampler : MonoBehaviour
{
    private TumblePatrolAbility patrol;
    private void Awake() => patrol = GetComponent<TumblePatrolAbility>();
    private void FixedUpdate() => patrol.SamplePlayerAfterFaceMove();
}
