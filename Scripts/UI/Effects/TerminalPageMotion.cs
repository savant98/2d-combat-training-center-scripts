using UnityEngine;

/// <summary>단말기 페이지가 펼쳐질 때 테두리 점등과 스캔을 진행한다. 정지 중에도 동작한다.</summary>
public sealed class TerminalPageMotion : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image scan, flash;
    [SerializeField] private Color accent;
    [SerializeField] private SoundOptions openSound;
    private static readonly Color FlashColor=new Color(.66f,.86f,1f,1f);
    private float age;
    private bool pendingSound;
    public void Configure(UnityEngine.UI.Image sweep,UnityEngine.UI.Image light,Color color,SoundOptions sound)
    {scan=sweep;flash=light;accent=color;openSound=sound;}
    private void OnEnable()
    {
        age=0;
        pendingSound=true;
    }
    private void Update()
    {
        if(scan==null)return;age+=Time.unscaledDeltaTime;
        if(pendingSound)
        {
            pendingSound=false;var menu=GetComponentInParent<InventoryTerminalMenu>();
            if(Application.isPlaying&&menu!=null&&menu.IsActive&&openSound?.clip!=null&&SoundManager.Instance!=null)SoundManager.Instance.PlaySound(openSound,Vector3.zero);
        }
        float width=((RectTransform)transform).rect.width;
        scan.rectTransform.anchoredPosition=new Vector2(Mathf.Lerp(-width*.48f,width*.48f,TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(age,.05f,.7f))),0);
        var c=accent;c.a=TrainingBoardMotion.Pulse(age,.05f,.7f)*.3f;scan.color=c;
        // 면 전체에 번지는 섬광은 분류의 테마색을 쓰지 않는다(테마색은 테두리·선에만 쓴다).
        c=FlashColor;c.a=TrainingBoardMotion.Pulse(age,0,.28f)*.12f;flash.color=c;
    }
}
