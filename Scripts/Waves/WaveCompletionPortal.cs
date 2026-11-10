using UnityEngine;

// 완료 직전에 포탈 입구만 옮기고, 자식에 저장된 Lobby 도착점은 월드 좌표로 유지한다.
[DisallowMultipleComponent]
public sealed class WaveCompletionPortal : MonoBehaviour
{
    private RoomTrigger portal;
    private Vector3 localOffset;
    private bool customized;

    public static bool Apply(WaveDataSO data, RoomTrigger portal, Transform coordinates)
    {
        if (portal == null) return false;
        var placement = portal.GetComponent<WaveCompletionPortal>();
        if (data == null || !data.overrideClearPortalPosition) { placement?.Restore(); return false; }
        if (coordinates == null || !float.IsFinite(data.clearPortalPosition.x) || !float.IsFinite(data.clearPortalPosition.y)) return false;
        if (placement == null) placement = portal.gameObject.AddComponent<WaveCompletionPortal>();
        placement.Restore();
        var before = portal.transform.localPosition;
        var destination = portal.TeleportPoint;
        Vector3 kept = destination != null ? destination.position : Vector3.zero;
        Vector3 target = coordinates.TransformPoint(data.clearPortalPosition);
        target.z = portal.transform.position.z;
        portal.transform.position = target;
        if (destination != null && destination != portal.transform) destination.position = kept;
        placement.portal = portal;
        placement.localOffset = portal.transform.localPosition - before;
        placement.customized = true;
        return true;
    }

    private void Restore()
    {
        if (!customized || portal == null) return;
        var destination = portal.TeleportPoint;
        Vector3 kept = destination != null ? destination.position : Vector3.zero;
        // 코스가 포탈에 더한 이동은 유지하고 Tool이 더한 차이만 제거한다.
        portal.transform.localPosition -= localOffset;
        if (destination != null && destination != portal.transform) destination.position = kept;
        customized = false;
    }
    private void OnDisable() => Restore();
}
