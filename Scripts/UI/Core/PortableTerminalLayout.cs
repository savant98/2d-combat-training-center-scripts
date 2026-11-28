using UnityEngine;

/// <summary>휴대용 단말기는 화면 네 변을 채우고, 내부 글자와 조작부는 같은 비율로 확대한다.</summary>
public static class PortableTerminalLayout
{
    public static readonly Vector2 ReferenceSize = new Vector2(1480f, 832f);

    public static float Fit(RectTransform device, Vector2 available)
    {
        if (available.x <= 0f || available.y <= 0f) return 1f;
        float scale = Mathf.Min(available.x / ReferenceSize.x, available.y / ReferenceSize.y);
        Vector2 size = available / scale;
        if (device.sizeDelta != size) device.sizeDelta = size;
        return scale;
    }
}
