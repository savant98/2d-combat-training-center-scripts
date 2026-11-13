using TMPro;
using UnityEngine;

/// <summary>마우스 포커스와 별도로 영상의 지속 일시정지 상태를 눌린 키캡으로 표시한다.</summary>
public sealed class TerminalPlaybackButton : MonoBehaviour
{
    [SerializeField] private RectTransform keycap;
    [SerializeField] private TerminalChassisGraphic surface;
    [SerializeField] private TrainingHologramGraphic rim;
    [SerializeField] private UnityEngine.UI.Image lamp;
    [SerializeField] private TextMeshProUGUI label, hint;
    public bool IsPressed { get; private set; }
    private bool initialized, lastEnded, lastReady;
    public void Configure(RectTransform cap,TerminalChassisGraphic face,TrainingHologramGraphic frame,
        UnityEngine.UI.Image indicator,TextMeshProUGUI title,TextMeshProUGUI description)
    {keycap=cap;surface=face;rim=frame;lamp=indicator;label=title;hint=description;initialized=false;}
    public void SetState(bool paused,bool ended,bool ready)
    {
        if(initialized&&IsPressed==paused&&lastEnded==ended&&lastReady==ready)return;
        initialized=true;IsPressed=paused;lastEnded=ended;lastReady=ready;
        keycap.anchoredPosition=new Vector2(0,paused?-3:1);
        surface.Configure(paused?new Color32(226,245,255,255):new Color32(23,59,81,255),
            paused?new Color32(182,217,236,255):new Color32(9,29,46,255),
            paused?new Color32(101,136,158,255):new Color32(62,106,130,255),10,paused?4:2,12);
        surface.raycastTarget=true;
        rim.Configure(TrainingHologramGraphic.Shape.Frame,paused?new Color32(23,59,82,255):new Color32(84,145,176,255),paused?3:1.5f,10);
        lamp.color=paused?new Color32(23,59,82,255):new Color32(74,162,198,255);
        label.text=ended?"재생 완료":!ready?"영상 준비 중":paused?"일시정지":"재생 중";
        label.color=paused?new Color32(12,26,38,255):new Color32(185,221,236,255);
        hint.text=paused?(ended?"눌림 · 누르면 처음부터":"눌림 · 누르면 재생"):"해제 · 누르면 일시정지";
        hint.color=paused?new Color32(36,77,102,255):new Color32(138,182,205,255);
    }
}
