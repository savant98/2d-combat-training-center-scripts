using UnityEngine.EventSystems;

/// <summary>드래그 중에는 위치만 표시하고 놓는 순간 한 번 탐색한다.</summary>
public sealed class TerminalVideoSeekBar : UnityEngine.UI.Slider
{
    [UnityEngine.SerializeField] private TerminalTutorialVideo owner;
    public void Configure(TerminalTutorialVideo video) { owner = video; }
    public override void OnPointerDown(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable()) return;
        owner?.BeginScrub(); base.OnPointerDown(data);
    }
    public override void OnPointerUp(PointerEventData data)
    {
        base.OnPointerUp(data);
        if (data.button == PointerEventData.InputButton.Left) owner?.EndScrub();
    }
}
