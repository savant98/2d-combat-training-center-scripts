using UnityEngine;

public class ParticleAnimBridge : MonoBehaviour
{
    [SerializeField] private ParticleSystem targetPs;

    // Animation Event에서 호출
    public void ParticleOn()
    {
        if (targetPs == null) return;
        targetPs.Play(true);
    }

    // Animation Event에서 호출
    public void ParticleOff()
    {
        if (targetPs == null) return;
        targetPs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    // 정말 즉시 지워야 할 때만 사용
    public void ParticleClear()
    {
        if (targetPs == null) return;
        targetPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}