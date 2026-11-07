using UnityEngine;

// 긴 선형 Trail 대신 로터 추진·발진 불씨·풍압을 사용한다. 재장착 중에도 이탈 비행을 표시한다.
public class BomberChargeTrail : MonoBehaviour
{
    [SerializeField] private FlyingBombChargeAbility ability;
    [SerializeField] private TrailRenderer trail;
    [SerializeField] private ParticleSystem[] jets;
    [SerializeField] private Transform[] jetOrigins;
    [SerializeField] private Transform bomb;
    [SerializeField] private ParticleSystem launchBurst;
    [SerializeField] private ParticleSystem pressure;
    private EnemyController controller;
    private int previousPhase;
    private bool wasReloading;
    private static readonly Vector3 BombPosition=new(0,-.65f,0);
    private void Awake()=>controller=GetComponent<EnemyController>();
    private void OnEnable(){ClearEffects();RestoreBomb();wasReloading=false;}
    private void LateUpdate()
    {
        if(ability.IsReloading&&!wasReloading)BeginReload();
        int phase=ability.State==AbilityState.Execute&&!ability.HasDetonated?2:ability.IsRepositioning?1:ability.IsReloading?3:0;
        Vector2 velocity=controller!=null?controller.Velocity:Vector2.zero;
        Vector3 behind=velocity.sqrMagnitude>.001f?-(Vector3)velocity.normalized:Vector3.down;
        if(jets!=null)for(int i=0;i<jets.Length;i++)
        {
            var jet=jets[i];if(jet==null)continue;
            if(phase!=0)
            {
                jet.transform.position=jetOrigins[i].position;
                jet.transform.rotation=Quaternion.FromToRotation(Vector3.forward,behind);
                if(phase!=previousPhase){var emission=jet.emission;emission.rateOverTime=phase==2?75:phase==1?40:24;jet.Play(true);}
            }
            else if(previousPhase!=0)jet.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if(phase==2)
        {
            if(launchBurst!=null&&previousPhase!=2){Align(launchBurst,behind);launchBurst.Play(true);}
            if(pressure!=null){Align(pressure,behind);if(previousPhase!=2)pressure.Play(true);}
        }
        else if(previousPhase==2)
        {
            if(launchBurst!=null)launchBurst.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(pressure!=null)pressure.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        previousPhase=phase;
        if(!ability.IsReloading)RestoreBomb();
        wasReloading=ability.IsReloading;
    }
    private void Align(ParticleSystem effect,Vector3 direction)
    {
        effect.transform.position=jetOrigins[0].parent.position;
        effect.transform.rotation=Quaternion.FromToRotation(Vector3.forward,direction);
    }
    public void BeginReload(){if(bomb!=null)bomb.localScale=Vector3.zero;ClearEffects();}
    private void RestoreBomb(){if(bomb==null)return;bomb.localPosition=BombPosition;bomb.localScale=Vector3.one;}
    private void ClearEffects()
    {
        if(trail!=null){trail.emitting=false;trail.Clear();trail.enabled=false;}
        if(jets!=null)foreach(var jet in jets)if(jet!=null)jet.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(launchBurst!=null)launchBurst.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(pressure!=null)pressure.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        previousPhase=0;
    }
    private void OnDisable()
    {
        ClearEffects();RestoreBomb();
        if(bomb!=null)bomb.localRotation=Quaternion.identity;
        if(jetOrigins!=null&&jetOrigins.Length>0&&jetOrigins[0]!=null)
        {var frame=jetOrigins[0].parent;frame.localPosition=Vector3.zero;frame.localScale=Vector3.one;}
    }
}
