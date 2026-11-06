using Unity.Cinemachine;
using UnityEngine;

// 적 패턴이 룸 범위를 정할 때 쓰는 현재 카메라 경계 조회.
// 메인 카메라가 따르는 가상 카메라의 CinemachineConfiner2D 경계 모양(Bounds)을 돌려준다.
// 복도로 이어진 룸에서 벽을 찾지 못하거나 천장이 화면 밖에 있을 때 이 경계를 룸 끝으로 본다(Admin 블랙홀·레이저 비).
public static class EnemyCameraBounds
{
    // 경계가 없거나 너무 작으면(1칸 이하) false.
    public static bool TryGet(out Bounds bounds)
    {
        bounds = default;
        var camera = Camera.main;
        if (camera == null || !camera.TryGetComponent(out CinemachineBrain brain))
            return false;
        if (brain.ActiveVirtualCamera is not Component virtualCamera)
            return false;
        if (!virtualCamera.TryGetComponent(out CinemachineConfiner2D confiner) || confiner.BoundingShape2D == null)
            return false;

        bounds = confiner.BoundingShape2D.bounds;
        return bounds.size.x > 1f && bounds.size.y > 1f;
    }
}
