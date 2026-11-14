using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

// 배치용 후경 루트는 고정하고, 거울 조각과 카메라 상태만 연출 중에 변경한다.
[DisallowMultipleComponent]
public sealed class NightmareBackdrop : MonoBehaviour
{
    [SerializeField] private CinemachineCamera entryCamera;
    [SerializeField] private GameObject revealedBackdrop;
    [SerializeField] private Transform mirrorRoot;
    [SerializeField] private Transform revealBoundary;
    [SerializeField] private Transform entrySpawnPoint;
    [SerializeField] private Transform[] shards;
    [SerializeField] private Renderer[] warningSignals;
    [SerializeField] private NightmareStreetLights streetLights;
    [SerializeField] private GameObject entranceBarrier;
    [SerializeField, Tooltip("시작 지점 벽면의 좌·우 이동 안내. 도입부에서만 켠다")] private NightmareMoveHint moveHint;
    private Vector3[] positions,scales;
    private Quaternion[] rotations;
    private MaterialPropertyBlock properties;
    private bool prepared;
    private uint version;

    public bool IsRevealed { get; private set; }
    public float RevealWorldX => revealBoundary.position.x;
    public Transform EntrySpawnPoint => entrySpawnPoint;
    public CinemachineCamera EntryCamera => entryCamera;
    public int FragmentCount => shards!=null?shards.Length:0;
    public bool IsEntranceSealed => entranceBarrier != null && entranceBarrier.activeSelf;

    private void Cache()
    {
        if(properties==null)properties=new MaterialPropertyBlock();
        if(positions!=null&&positions.Length==shards.Length&&scales!=null&&scales.Length==shards.Length
            &&rotations!=null&&rotations.Length==shards.Length)return;
        positions=new Vector3[shards.Length];scales=new Vector3[shards.Length];rotations=new Quaternion[shards.Length];
        for(int i=0;i<shards.Length;i++){positions[i]=shards[i].localPosition;scales[i]=shards[i].localScale;rotations[i]=shards[i].localRotation;}
    }

    public void PrepareForIntro(Transform player)
    {
        Cache();++version;prepared=true;IsRevealed=false;
        if(entranceBarrier!=null)entranceBarrier.SetActive(false);
        RestoreShards();mirrorRoot.gameObject.SetActive(true);revealedBackdrop.SetActive(false);
        if(streetLights!=null)streetLights.Restore();
        if(moveHint!=null)moveHint.Show();
        entryCamera.Follow=player;entryCamera.Priority=30;entryCamera.enabled=true;
    }

    public IEnumerator Shatter(Action<float> progress,float duration=1.8f)
    {
        Cache();uint current=++version;
        if(entranceBarrier!=null)entranceBarrier.SetActive(true);
        revealedBackdrop.SetActive(true);
        if(streetLights!=null)streetLights.BeginDestruction();
        if(moveHint!=null)moveHint.Hide();
        entryCamera.Priority=1;entryCamera.enabled=false;
        double started=Time.unscaledTimeAsDouble;
        while(current==version&&isActiveAndEnabled&&Time.unscaledTimeAsDouble-started<duration)
        {
            float t=Mathf.Clamp01((float)((Time.unscaledTimeAsDouble-started)/duration));
            if(streetLights!=null)streetLights.SetDestructionProgress(t);
            for(int i=0;i<shards.Length;i++)
            {
                float delay=(i%7)*.018f;
                float p=Mathf.Clamp01((t-delay)/(1-delay));
                float drop=p*p;
                var offset=new Vector3((positions[i].x-3f)*.38f,-8f-(i%5)*.7f,0)*drop;
                shards[i].localPosition=positions[i]+offset;
                shards[i].localRotation=rotations[i]*Quaternion.Euler(0,0,((i%2==0)?1:-1)*(30+i%8*9)*drop);
                properties.Clear();properties.SetFloat("_Crack",Mathf.Clamp01(p*9));
                properties.SetFloat("_Visibility",1-Mathf.SmoothStep(0,1,Mathf.Clamp01((p-.22f)/.78f)));
                shards[i].GetComponent<Renderer>().SetPropertyBlock(properties);
            }
            progress?.Invoke(t);yield return null;
        }
        if(current!=version||!isActiveAndEnabled)yield break;
        if(streetLights!=null)streetLights.HideDestroyed();
        mirrorRoot.gameObject.SetActive(false);IsRevealed=true;progress?.Invoke(1);
    }

    private void Update()
    {
        if(!Application.isPlaying||!IsRevealed||warningSignals==null)return;
        if(properties==null)properties=new MaterialPropertyBlock();
        for(int i=0;i<warningSignals.Length;i++)
        {
            if(warningSignals[i]==null)continue;
            float flicker=Mathf.Sin(Time.unscaledTime*(2.7f+i*.31f)+i*7.13f);
            properties.Clear();properties.SetFloat("_Visibility",flicker>.88f?.2f:.85f);
            warningSignals[i].SetPropertyBlock(properties);
        }
    }

    private void RestoreShards()
    {
        if(positions==null||positions.Length!=shards.Length)return;
        for(int i=0;i<shards.Length;i++)
        {
            if(shards[i]==null)continue;
            shards[i].localPosition=positions[i];shards[i].localScale=scales[i];shards[i].localRotation=rotations[i];
            shards[i].GetComponent<Renderer>().SetPropertyBlock(null);
        }
    }

    private void OnEnable()
    {
        if(!Application.isPlaying||prepared)return;
        revealedBackdrop.SetActive(true);mirrorRoot.gameObject.SetActive(false);IsRevealed=true;
        if(streetLights!=null)streetLights.HideDestroyed();
        if(moveHint!=null)moveHint.HideImmediately();
        if(GameManager.Instance!=null&&GameManager.Instance.Player!=null)entryCamera.Follow=GameManager.Instance.Player.transform;
        entryCamera.enabled=false;
    }

    public void ResetPresentation()
    {
        ++version;prepared=false;RestoreShards();
        if(entranceBarrier!=null)entranceBarrier.SetActive(false);
        if(entryCamera!=null){entryCamera.Priority=1;entryCamera.enabled=false;}
        if(mirrorRoot!=null)mirrorRoot.gameObject.SetActive(false);
        if(revealedBackdrop!=null)revealedBackdrop.SetActive(true);
        if(streetLights!=null)streetLights.HideDestroyed();
        if(moveHint!=null)moveHint.HideImmediately();
        IsRevealed=true;
    }
    public void CancelShatter()=>++version;
    private void OnDisable(){if(Application.isPlaying)ResetPresentation();}
}
