using UnityEngine;

public class InputBuffer
{
    private float bufferTime = 0.1f;
    private float lastInputTime = -999f;
    private float lastUseTime = -999f;
    public InputBuffer(float bufferTime)
    {
        this.bufferTime = bufferTime;
    }
    public void SetBufferTime(float bufferTime)
    {
        this.bufferTime = bufferTime;
    }
    public void RegisterInput()
    {
        lastInputTime = Time.time;
    }

    public bool ConsumeIfValid()
    {
        if (IsBuffered())
        {
            lastInputTime = -999f;
            return true;
        }
        return false;
    }

    private bool IsBuffered()
    {
        return Time.time - lastInputTime <= bufferTime;
    }

    public bool Use()
    {
        if (!ConsumeIfValid() && Time.time >= lastUseTime + bufferTime)
            return false;

        lastUseTime = Time.time;
        return true;
    }
    public void Clear() => lastUseTime = -999f;
}
