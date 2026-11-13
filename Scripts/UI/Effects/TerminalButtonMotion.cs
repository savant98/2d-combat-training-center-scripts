using UnityEngine;

/// <summary>기존 아이콘·색을 유지하며 별도 테두리와 선택 스캔만 움직인다.</summary>
public sealed class TerminalButtonMotion : MonoBehaviour
{
    [SerializeField] private TrainingHologramGraphic border;
    [SerializeField] private UnityEngine.UI.Image scan, rail;
    [SerializeField] private Color accent;
    private bool selected;
    private float age, weight;
    public void Configure(TrainingHologramGraphic frame,UnityEngine.UI.Image sweep,UnityEngine.UI.Image line,Color color)
    {border=frame;scan=sweep;rail=line;accent=color;}
    public void Select(bool value){if(value!=selected){selected=value;age=0;}}
    private void OnEnable(){age=0;weight=0;}
    private void Update()
    {
        if(border==null)return;age+=Time.unscaledDeltaTime;
        weight=Mathf.MoveTowards(weight,selected?1:0,Time.unscaledDeltaTime*7);
        Color c=Color.white;c.a=Mathf.Lerp(.16f,.9f,weight);border.color=c;c=accent;
        var size=((RectTransform)transform).rect.size;
        float t=TrainingBoardMotion.Seg(age,0,.5f);
        scan.rectTransform.anchoredPosition=new Vector2(Mathf.Lerp(-size.x*.44f,size.x*.44f,TrainingBoardMotion.OutCubic(t)),0);
        c.a=weight*TrainingBoardMotion.Pulse(age,0,.5f)*.4f;scan.color=c;
        rail.rectTransform.localScale=new Vector3(Mathf.Lerp(.15f,1,weight),1,1);
        c.a=weight*(.7f+.15f*Mathf.Sin(Time.unscaledTime*3));rail.color=c;
    }
}
