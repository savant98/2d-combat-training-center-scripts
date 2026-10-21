using UnityEngine;

public class SoundHandle
{
    private AudioSource source;
    private SoundManager owner;

    public bool IsValid => source != null && owner != null;

    public SoundHandle(AudioSource source, SoundManager owner)
    {
        this.source = source;
        this.owner = owner;
    }

    public void Stop()
    {
        if (!IsValid)
            return;

        owner.StopLoop(this);
    }

    public void SetPosition(Vector3 position)
    {
        if (!IsValid)
            return;

        source.transform.position = position;
    }

    public AudioSource GetSource()
    {
        return source;
    }

    public void Clear()
    {
        source = null;
        owner = null;
    }
}