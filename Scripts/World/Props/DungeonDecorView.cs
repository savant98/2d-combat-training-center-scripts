using UnityEngine;

/// <summary>
/// 던전 장식 움직임 부품(사슬·전선 다발·분류 팔·점검 트롤리)이 지금 화면 근처에 있는지 묻는다(2026-11-24, 던전 2차 decor).
/// Renderer.isVisible은 실제로 그린 결과에 기대므로 창 없는 Editor·일시정지 Step에서 흔들린다. 그래서 Camera.main의 정사영 화면 사각형과
/// 부품의 대략적인 영역을 직접 비교한다(물리 질의 없음). 화면 밖이면 움직임 계산과 반복음을 건너뛴다.
/// </summary>
public static class DungeonDecorView
{
    /// <summary>center·halfSize(월드)의 사각형이 화면 사각형에 margin칸 여유를 더한 영역과 겹치는가. 카메라가 없으면 true.</summary>
    public static bool Near(Vector2 center, Vector2 halfSize, float margin = 2f)
    {
        var camera = Camera.main;
        if (camera == null) return true;
        // 원근 카메라(3D 연출 장면)는 대략 10칸 높이로 본다.
        float halfHeight = camera.orthographic ? camera.orthographicSize : 10f;
        float halfWidth = halfHeight * Mathf.Max(.1f, camera.aspect);
        Vector2 eye = camera.transform.position;
        return Mathf.Abs(center.x - eye.x) <= halfWidth + Mathf.Abs(halfSize.x) + margin
            && Mathf.Abs(center.y - eye.y) <= halfHeight + Mathf.Abs(halfSize.y) + margin;
    }
}
