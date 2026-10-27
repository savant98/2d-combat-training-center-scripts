using UnityEngine;
using UnityEngine.Animations;

/// <summary>편집 중에는 기준 배치를 유지하고 게임 실행 중에만 후경 시차를 활성화한다.</summary>
[DisallowMultipleComponent]
public class LobbyBackdropParallax : MonoBehaviour
{
    private PositionConstraint[] layers;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (layers == null) layers = GetComponentsInChildren<PositionConstraint>(true);
        SetEnabled(true);
    }

    private void OnDisable()
    {
        SetEnabled(false);
    }

    private void SetEnabled(bool value)
    {
        if (layers == null) return;
        foreach (var layer in layers)
            if (layer != null) layer.enabled = value;
    }
}
