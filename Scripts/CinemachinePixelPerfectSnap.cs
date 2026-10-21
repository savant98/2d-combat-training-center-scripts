using UnityEngine;
using Unity.Cinemachine;
[ExecuteAlways]
[RequireComponent(typeof(CinemachineBrain))]
public class CinemachinePixelPerfectSnap : MonoBehaviour
{
    [Header("Pixels Per Unit (Tile PPU)")]
    public float pixelsPerUnit = 1080f;  // 타일 1칸 = 1유닛

    private CinemachineBrain brain;
    private Camera cam;

    void OnEnable()
    {
        brain = GetComponent<CinemachineBrain>();
        cam = brain.OutputCamera;

        // 이벤트 중복 등록 방지
        CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
        CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
    }
    void OnDisable()
    {
        CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
    }

    private void OnCameraUpdated(CinemachineBrain updatedBrain)
    {
        // 내가 속한 Brain이 아니면 무시
        if (updatedBrain != brain) return;

        if (cam == null) cam = brain.OutputCamera;
        if (cam == null || !cam.orthographic) return;

        // 스냅 전 위치
        Vector3 originalPos = cam.transform.position;

        // 픽셀 단위로 스냅
        Vector3 snappedPos = new (
            Mathf.Round(originalPos.x * pixelsPerUnit) / pixelsPerUnit,
            Mathf.Round(originalPos.y * pixelsPerUnit) / pixelsPerUnit,
            originalPos.z
        );

        // 카메라 위치 적용
        cam.transform.position = snappedPos;

        // Debug: 스냅 전후 위치 확인
        //Debug.Log($"Original: {originalPos}, Snapped: {snappedPos}, Diff: {snappedPos - originalPos}");
        Debug.DrawLine(originalPos, snappedPos, Color.yellow, 0.1f, false);
    }
}
