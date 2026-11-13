using TMPro;
using UnityEngine;

/// <summary>단말기와 교본 영상에서 함께 사용하는 코어 전개·스캔·설명 카드.</summary>
public sealed class TerminalInstructionCard : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform plate, scan, core;
    [SerializeField] private UnityEngine.UI.Image flash;
    [SerializeField] private TextMeshProUGUI heading, body, input, counter, hint;
    private float age;
    [SerializeField] private Color tint;
    public static TerminalInstructionCard Create(Transform parent,TMP_FontAsset font,Color accent)
    {
        var rect=Rect(parent,"InstructionCard",new Vector2(720,280));
        var card=rect.gameObject.AddComponent<TerminalInstructionCard>();card.plate=rect;card.tint=accent;
        card.group=rect.gameObject.AddComponent<CanvasGroup>();card.group.blocksRaycasts=false;
        Fill(rect,"Back",Vector2.zero,new Vector2(720,280),new Color(.018f,.035f,.055f,.96f));
        var frame=Rect(rect,"Frame",new Vector2(720,280)).gameObject.AddComponent<TrainingHologramGraphic>();
        frame.Configure(TrainingHologramGraphic.Shape.Frame,accent,2,18,.025f);
        card.flash=Fill(rect,"Flash",Vector2.zero,new Vector2(716,276),accent);
        card.scan=Fill(rect,"Scan",new Vector2(-348,0),new Vector2(5,260),accent).rectTransform;
        card.core=Rect(rect,"Core",new Vector2(42,42));card.core.anchoredPosition=new Vector2(-313,91);
        card.core.gameObject.AddComponent<TrainingHologramGraphic>().Configure(TrainingHologramGraphic.Shape.Frame,accent,3,14,.12f);
        Fill(rect,"SupportLine",new Vector2(10,63),new Vector2(620,2),accent);
        card.counter=Text(rect,font,"Counter",new Vector2(244,113),new Vector2(172,22),16,accent);
        card.heading=Text(rect,font,"Heading",new Vector2(-44,94),new Vector2(414,54),27,Color.white);
        card.body=Text(rect,font,"Body",new Vector2(0,-4),new Vector2(644,112),25,new Color(.88f,.95f,1));card.body.alignment=TextAlignmentOptions.MidlineLeft;
        card.input=Text(rect,font,"Input",new Vector2(0,-83),new Vector2(644,40),25,accent);
        card.hint=Text(rect,font,"Hint",new Vector2(0,-119),new Vector2(644,25),16,new Color(.62f,.76f,.84f));return card;
    }
    public void Show(string title,string explanation,string keys,int index,int total,FeedbackDataSO feedback=null)
    {
        heading.text=title;body.text=explanation;input.text=keys;counter.text=$"STEP {index+1:00} / {total:00}";
        hint.text="시연을 확인하세요";age=0;gameObject.SetActive(true);
        if(feedback!=null&&FeedbackPlayer.Instance!=null)FeedbackPlayer.Instance.Play(feedback,transform);Apply();
    }
    public void SetHint(string text){hint.text=text;}
    private void Update(){age+=Time.unscaledDeltaTime;Apply();}
    private void Apply()
    {
        if(group==null)return;
        group.alpha=TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(age,.02f,.22f));
        plate.localScale=new Vector3(Mathf.Lerp(.72f,1,TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(age,0,.23f))),Mathf.Lerp(.1f,1,TrainingBoardMotion.OutBack(TrainingBoardMotion.Seg(age,.08f,.38f))),1);
        core.localEulerAngles=new Vector3(0,0,Mathf.Lerp(-135,0,TrainingBoardMotion.OutBack(TrainingBoardMotion.Seg(age,0,.45f)))+Mathf.Sin(age*.8f)*6);
        var color=tint;color.a=.23f*TrainingBoardMotion.Pulse(age,.08f,.45f);flash.color=color;
        scan.anchoredPosition=new Vector2(Mathf.Lerp(-348,348,TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(age,.15f,.75f))),0);
        color=tint;color.a=TrainingBoardMotion.Pulse(age,.15f,.75f)*.8f;scan.GetComponent<UnityEngine.UI.Image>().color=color;
        body.maxVisibleCharacters=Mathf.FloorToInt(Mathf.Max(0,age-.18f)*120);
    }
    public static RectTransform Rect(Transform parent,string name,Vector2 size)
    {var go=new GameObject(name,typeof(RectTransform));go.layer=parent.gameObject.layer;var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;r.sizeDelta=size;return r;}
    public static UnityEngine.UI.Image Fill(Transform parent,string name,Vector2 pos,Vector2 size,Color color)
    {var r=Rect(parent,name,size);r.anchoredPosition=pos;var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=false;return image;}
    public static TextMeshProUGUI Text(Transform parent,TMP_FontAsset font,string name,Vector2 pos,Vector2 size,float fontSize,Color color)
    {var r=Rect(parent,name,size);r.anchoredPosition=pos;var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=fontSize;t.color=color;t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;return t;}
}
