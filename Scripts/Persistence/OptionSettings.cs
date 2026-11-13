using System;
using UnityEngine;

/// <summary>진행 기록과 함께 저장하되 새 게임에서도 유지하는 사용자 환경 설정.</summary>
[Serializable]
public class OptionSettings
{
    public int version = 1;
    public int width;
    public int height;
    public bool windowed;
    public bool exclusiveFullscreen;
    public int monitorIndex;
    public string monitorName = "";
    public uint refreshNumerator;
    public uint refreshDenominator;
    public bool vSync;
    public int frameRate = 144;
    public float masterVolume = 1f;
    public float bgmVolume = 0.17782794f; // 기존 -15 dB 유지
    public float sfxVolume = 1f;
    public float bloom = 0.6f;
    public float flash = 0.5f;
    public string language = "ko";
    public string bindingOverrides = "";

    public OptionSettings Copy() => (OptionSettings)MemberwiseClone();

    public void Sanitize()
    {
        version = 1;
        monitorIndex = Mathf.Max(0, monitorIndex);
        monitorName ??= "";
        if (refreshDenominator == 0 || refreshNumerator / (double)refreshDenominator > 1000)
            refreshNumerator = refreshDenominator = 0;
        if (width < 640 || height < 360 || width > 16384 || height > 16384) width = height = 0;
        if (frameRate != -1 && frameRate != 60 && frameRate != 120 && frameRate != 144 && frameRate != 165 && frameRate != 240) frameRate = 144;
        masterVolume = Finite01(masterVolume, 1f);
        bgmVolume = Finite01(bgmVolume, 0.17782794f);
        sfxVolume = Finite01(sfxVolume, 1f);
        bloom = Finite01(bloom, 0.6f);
        flash = Finite01(flash, 0.5f);
        if (language != "ko" && language != "en") language = "ko";
        bindingOverrides ??= "";
    }

    private static float Finite01(float value, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);
}
