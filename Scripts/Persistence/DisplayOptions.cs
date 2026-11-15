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

    public static bool CanChangeRefresh(OptionSettings settings) => Mode(settings) == FullScreenMode.ExclusiveFullScreen;

    /// <summary>게임의 16:9 비율을 유지하며 요청한 영역 안에 최대 4K UHD로 맞춘다.</summary>
    public static Vector2Int FitResolution(Vector2Int size)
    {
        int unit = Mathf.Clamp(Mathf.Min(size.x / 16, size.y / 9), 1, 240);
        return new Vector2Int(unit * 16, unit * 9);
    }

    public static bool IsWidescreen(Vector2Int size) => size.x > 0 && size.y > 0 && (long)size.x * 9 == (long)size.y * 16;

    /// <summary>모니터 지원 모드와 창·테두리 없는 전체 화면의 표준 렌더 해상도를 합친다.</summary>
    public static List<Vector2Int> Resolutions(OptionSettings settings) => ResolutionChoices(Modes(settings), DesktopSize(settings), Size(settings));

    public static List<Vector2Int> ResolutionChoices(IEnumerable<Resolution> modes, Vector2Int desktop, Vector2Int current)
    {
        var result = new List<Vector2Int>();
        void Add(Vector2Int size)
        {
            if (size.x >= 960 && size.y >= 540 && size.x <= 3840 && size.y <= 2160 && IsWidescreen(size) && !result.Contains(size)) result.Add(size);
        }
        foreach (var mode in modes) Add(new Vector2Int(mode.width, mode.height));
        var limit = FitResolution(desktop);
        foreach (var size in new[] { new Vector2Int(960, 540), new Vector2Int(1280, 720), new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3200, 1800), new Vector2Int(3840, 2160) })
            if (size.x <= limit.x && size.y <= limit.y) Add(size);
        var currentSize = FitResolution(current);
        if (currentSize.x >= 640 && currentSize.y >= 360 && !result.Contains(currentSize)) result.Add(currentSize);
        if (result.Count == 0) result.Add(new Vector2Int(960, 540));
        result.Sort((a, b) => a.x.CompareTo(b.x));
        return result;
    }

    public static bool CanUseExclusive(OptionSettings settings) => SupportsExclusive(DesktopSize(settings), Size(settings), Modes(settings));

    public static bool SupportsExclusive(Vector2Int desktop, Vector2Int size, IEnumerable<Resolution> modes)
    {
        if (!IsWidescreen(desktop)) return false;
        foreach (var mode in modes)
            if (mode.width == size.x && mode.height == size.y) return true;
        return false;
    }

    /// <summary>비16:9 모니터는 Unity의 기본 레터박스를 보장하는 FullScreenWindow를 사용한다.</summary>
    public static FullScreenMode Mode(OptionSettings settings) => settings.windowed ? FullScreenMode.Windowed :
        settings.exclusiveFullscreen && CanUseExclusive(settings) ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;

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

    public static Vector2Int Size(OptionSettings settings) => FitResolution(settings.width > 0 && settings.height > 0
        ? new Vector2Int(settings.width, settings.height) : DesktopSize(settings));
    public static int Width(OptionSettings settings) => Size(settings).x;
    public static int Height(OptionSettings settings) => Size(settings).y;
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
