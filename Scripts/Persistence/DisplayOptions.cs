using System.Collections.Generic;
using UnityEngine;

/// <summary>선택 모니터에서 지원하는 해상도와 정확한 분수 주사율을 사용한다.</summary>
public static class DisplayOptions
{
    public static RefreshRate AutoRate => new RefreshRate { numerator = 0, denominator = 1 };

    public static List<DisplayInfo> Layout()
    {
        var displays = new List<DisplayInfo>();
        Screen.GetDisplayLayout(displays);
        return displays;
    }

    public static int Monitor(OptionSettings settings, List<DisplayInfo> displays)
    {
        if (displays.Count == 0) return -1;
        if (settings.monitorIndex >= 0 && settings.monitorIndex < displays.Count && displays[settings.monitorIndex].name == settings.monitorName)
            return settings.monitorIndex;
        if (!string.IsNullOrEmpty(settings.monitorName))
        {
            int named = displays.FindIndex(d => d.name == settings.monitorName);
            return named >= 0 ? named : 0;
        }
        return Mathf.Clamp(settings.monitorIndex, 0, displays.Count - 1);
    }

    public static bool CanChangeRefresh(OptionSettings settings) => !settings.windowed && settings.exclusiveFullscreen;

    public static void NormalizeMonitor(OptionSettings settings)
    {
        var displays = Layout();
        int index = Monitor(settings, displays);
        if (index < 0) return;
        bool missing = !string.IsNullOrEmpty(settings.monitorName) && !displays.Exists(d => d.name == settings.monitorName);
        if (missing || settings.monitorIndex >= displays.Count)
        {
            settings.width = settings.height = 0;
            settings.refreshNumerator = settings.refreshDenominator = 0;
        }
        settings.monitorIndex = index;
        settings.monitorName = displays[index].name;
    }

    public static List<RefreshRate> Rates(OptionSettings settings)
    {
        var result = new List<RefreshRate> { AutoRate }; // 0은 Unity 자동 선택(지원 모드 중 선택)
        foreach (var mode in Modes(settings))
        {
            if (mode.width != Width(settings) || mode.height != Height(settings)) continue;
            if (!result.Exists(r => r.Equals(mode.refreshRateRatio))) result.Add(mode.refreshRateRatio);
        }
        result.Sort((a, b) => a.value.CompareTo(b.value));
        return result;
    }

    public static Resolution[] Modes(OptionSettings settings)
    {
        var displays = Layout();
        int index = Monitor(settings, displays);
        if (index >= 0 && WindowsDisplayModes.TryGetModes(displays[index], out var nativeModes)) return nativeModes;
        try { return index >= 0 ? displays[index].resolutions : Screen.resolutions; }
        catch (System.NotSupportedException)
        {
            // 일부 Editor·플랫폼은 DisplayInfo의 모드 열거를 지원하지 않는다.
            if (index < 0 || displays[index].Equals(Screen.mainWindowDisplayInfo)) return Screen.resolutions;
            return new[] { new Resolution { width = displays[index].width, height = displays[index].height, refreshRateRatio = displays[index].refreshRate } };
        }
    }

    public static int Width(OptionSettings settings) => settings.width > 0 ? settings.width : DesktopSize(settings).x;
    public static int Height(OptionSettings settings) => settings.height > 0 ? settings.height : DesktopSize(settings).y;
    private static Vector2Int DesktopSize(OptionSettings settings)
    {
        var displays = Layout();
        int index = Monitor(settings, displays);
        return index >= 0 ? new Vector2Int(displays[index].width, displays[index].height) : new Vector2Int(1920, 1080);
    }

    public static RefreshRate Rate(OptionSettings settings)
    {
        var desired = new RefreshRate { numerator = settings.refreshNumerator, denominator = settings.refreshDenominator };
        return settings.refreshNumerator > 0 && Rates(settings).Exists(r => r.Equals(desired)) ? desired : AutoRate;
    }

    public static bool Changed(OptionSettings a, OptionSettings b) => a.width != b.width || a.height != b.height ||
        a.windowed != b.windowed || a.exclusiveFullscreen != b.exclusiveFullscreen || a.monitorIndex != b.monitorIndex ||
        a.monitorName != b.monitorName || a.refreshNumerator != b.refreshNumerator || a.refreshDenominator != b.refreshDenominator;

    public static void Restore(OptionSettings target, OptionSettings source)
    {
        target.width = source.width; target.height = source.height; target.windowed = source.windowed;
        target.exclusiveFullscreen = source.exclusiveFullscreen; target.monitorIndex = source.monitorIndex;
        target.monitorName = source.monitorName; target.refreshNumerator = source.refreshNumerator; target.refreshDenominator = source.refreshDenominator;
    }
}
