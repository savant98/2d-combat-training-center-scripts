using UnityEngine;
using UnityEngine.Rendering.Universal;

// 거울 파괴 진행률에 맞춰 URP 광원·전구를 끄고 구조물과 유리 조각을 흩어지게 한다.
[DisallowMultipleComponent]
public sealed class NightmareStreetLights : MonoBehaviour
{
    [SerializeField] private Transform[] lamps;
    [SerializeField] private Renderer[] structures;
    [SerializeField] private Renderer[] bulbs;
    [SerializeField] private Light2D[] lights;
    [SerializeField] private Light2D neutralLight;
    [SerializeField] private GameObject[] brokenGroups;
    [SerializeField] private Transform[] fragments;
    private Vector3[] homePositions;
    private Quaternion[] homeRotations;
    private Renderer[] fragmentRenderers;
    private float[] lightIntensities;
    private MaterialPropertyBlock properties;
    public bool IsDestroyed { get; private set; }
    public int LampCount => lamps == null ? 0 : lamps.Length;
    public int ActiveLightCount
    {
        get { int count=0;if(lights!=null)foreach(var light in lights)if(light!=null&&light.isActiveAndEnabled)count++;return count; }
    }

    private void Cache()
    {
        if(properties==null)properties=new MaterialPropertyBlock();
        if(homePositions!=null&&homePositions.Length==fragments.Length&&homeRotations!=null&&homeRotations.Length==fragments.Length
            &&fragmentRenderers!=null&&fragmentRenderers.Length==fragments.Length&&lightIntensities!=null&&lightIntensities.Length==lights.Length)return;
        homePositions=new Vector3[fragments.Length];homeRotations=new Quaternion[fragments.Length];fragmentRenderers=new Renderer[fragments.Length];
        for(int i=0;i<fragments.Length;i++){homePositions[i]=fragments[i].localPosition;homeRotations[i]=fragments[i].localRotation;fragmentRenderers[i]=fragments[i].GetComponent<Renderer>();}
        lightIntensities=new float[lights.Length];for(int i=0;i<lights.Length;i++)lightIntensities[i]=lights[i].intensity;
    }

    public void Restore()
    {
        Cache();IsDestroyed=false;RestorePieces();
        if(neutralLight!=null)neutralLight.enabled=true;
        for(int i=0;i<lamps.Length;i++)
        {
            lamps[i].gameObject.SetActive(true);structures[i].enabled=bulbs[i].enabled=true;
            bulbs[i].SetPropertyBlock(null);lights[i].intensity=lightIntensities[i];lights[i].enabled=true;
            brokenGroups[i].SetActive(false);
        }
    }

    public void BeginDestruction(){Cache();IsDestroyed=false;SetDestructionProgress(0);}

    public void SetDestructionProgress(float progress)
    {
        Cache();
        for(int lamp=0;lamp<lamps.Length;lamp++)
        {
            float p=Mathf.Clamp01((progress-.012f*lamp)/.85f);
            bool whole=p<.13f;
            float flash=1+Mathf.Sin(Mathf.Clamp01(p/.13f)*Mathf.PI)*3;
            lights[lamp].intensity=lightIntensities[lamp]*flash;lights[lamp].enabled=whole;
            structures[lamp].enabled=bulbs[lamp].enabled=whole;brokenGroups[lamp].SetActive(!whole);
            Color color=bulbs[lamp].sharedMaterial.GetColor("_Color");float alpha=color.a;color*=flash;color.a=alpha;
            properties.Clear();properties.SetColor("_Color",color);bulbs[lamp].SetPropertyBlock(properties);
            float fall=Mathf.Clamp01((p-.13f)/.87f);
            for(int piece=0;piece<6;piece++)
            {
                int i=lamp*6+piece;float sign=piece%2==0?-1:1;
                float distance=piece<3?5+piece*3:8;
                var offset=new Vector3(sign*(.6f+piece*.32f)*fall,-distance*fall*fall+(piece>=3?Mathf.Sin(fall*Mathf.PI)*1.6f:0),0);
                fragments[i].localPosition=homePositions[i]+offset;
                fragments[i].localRotation=homeRotations[i]*Quaternion.Euler(0,0,sign*(65+piece*23)*fall);
                properties.Clear();properties.SetFloat("_Visibility",1-Mathf.SmoothStep(0,1,Mathf.Clamp01((fall-.48f)/.52f)));
                fragmentRenderers[i].SetPropertyBlock(properties);
            }
            if(p>=1)lamps[lamp].gameObject.SetActive(false);
        }
        if(progress>=1)HideDestroyed();
    }

    public void HideDestroyed()
    {
        IsDestroyed=true;
        if(neutralLight!=null)neutralLight.enabled=false;
        if(lamps!=null)foreach(var lamp in lamps)if(lamp!=null)lamp.gameObject.SetActive(false);
        if(lights!=null)foreach(var light in lights)if(light!=null)light.enabled=false;
    }

    private void RestorePieces()
    {
        if(homePositions==null||fragments==null||homePositions.Length!=fragments.Length)return;
        for(int i=0;i<fragments.Length;i++)
        {if(fragments[i]==null)continue;fragments[i].localPosition=homePositions[i];fragments[i].localRotation=homeRotations[i];if(fragmentRenderers[i]!=null)fragmentRenderers[i].SetPropertyBlock(null);}
    }
    private void OnDisable()=>RestorePieces();
}
