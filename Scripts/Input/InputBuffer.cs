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
    // 행동할 수 없는 동안(피격 경직) 들어온 입력이 만료되지 않게 지금 시각으로 다시 찍는다. 유효한 입력이 없으면 그대로 둔다.
    public void Hold()
    {
        if (IsBuffered())
            lastInputTime = Time.time;
    }
    public void Reset() { lastInputTime = lastUseTime = -999f; }
}
