using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Windows의 모니터 이름과 실제 GDI 출력 장치를 연결해 지원 모드를 읽는다.</summary>
public static class WindowsDisplayModes
{
    private static readonly Dictionary<string, Resolution[]> cache = new();
    private static float cacheExpires;

    public static bool TryGetModes(DisplayInfo display, out Resolution[] modes)
    {
        modes = Array.Empty<Resolution>();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (Time.realtimeSinceStartup >= cacheExpires) { cache.Clear(); cacheExpires = Time.realtimeSinceStartup + 5; }
        string key = display.name + "/" + display.width + "/" + display.height + "/" + display.refreshRate;
        if (cache.TryGetValue(key, out modes)) return modes.Length > 0;
        try
        {
            if (!FindDevice(display, out string device, out RefreshRate liveRate)) return false;
            var result = new List<Resolution>();
            for (uint i = 0; ; i++)
            {
                var mode = NewMode();
                if (!EnumDisplaySettingsW(device, i, ref mode)) break;
                if (mode.bits < 32 || mode.width == 0 || mode.height == 0 || mode.frequency <= 1 || (mode.flags & 2) != 0) continue;
                Add(result, mode, new RefreshRate { numerator = mode.frequency, denominator = 1 });
            }
            // DEVMODE는 명목 정수 Hz를 보고한다. 활성 모드는 정확한 분수 값도 제공한다.
            var current = NewMode();
            if (EnumDisplaySettingsW(device, uint.MaxValue, ref current) && liveRate.denominator > 0) Add(result, current, liveRate);
            modes = result.ToArray(); cache[key] = modes;
            return modes.Length > 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (ExternalException) { return false; }
#else
        return false;
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static void Add(List<Resolution> modes, DeviceMode source, RefreshRate rate)
    {
        var mode = new Resolution { width = (int)source.width, height = (int)source.height, refreshRateRatio = rate };
        if (!modes.Contains(mode)) modes.Add(mode);
    }
    private static DeviceMode NewMode() => new DeviceMode { size = (ushort)Marshal.SizeOf<DeviceMode>() };
    private sealed class Candidate { public string device, name; public uint width, height; public RefreshRate rate; }

    private static bool FindDevice(DisplayInfo display, out string device, out RefreshRate rate)
    {
        device = null; rate = default;
        var candidates = new List<Candidate>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(2, out uint pathCount, out uint modeCount) != 0 || pathCount == 0) return false;
            var paths = new DisplayPath[checked((int)pathCount)];
            IntPtr modes = Marshal.AllocHGlobal(checked((int)modeCount * 64));
            try
            {
                int error = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (error == 122) continue; // 조회 사이에 모니터가 연결/분리되면 다시 열거한다.
                if (error != 0) return false;
                for (int i = 0; i < pathCount; i++)
                {
                    var path = paths[i];
                    var source = new SourceName { header = new Header { type = 1, size = (uint)Marshal.SizeOf<SourceName>(), adapter = path.source.adapter, id = path.source.id } };
                    var target = new TargetName { header = new Header { type = 2, size = (uint)Marshal.SizeOf<TargetName>(), adapter = path.target.adapter, id = path.target.id } };
                    if (GetSourceName(ref source) != 0 || GetTargetName(ref target) != 0 || string.IsNullOrEmpty(source.name)) continue;
                    if (!string.Equals(target.name, display.name, StringComparison.OrdinalIgnoreCase)) continue;
                    var current = NewMode();
                    if (!EnumDisplaySettingsW(source.name, uint.MaxValue, ref current)) continue;
                    if (candidates.Exists(c => c.device == source.name)) continue;
                    candidates.Add(new Candidate { device = source.name, name = target.name, width = current.width, height = current.height,
                        rate = new RefreshRate { numerator = path.target.refresh.numerator, denominator = path.target.refresh.denominator } });
                }
                break;
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
        if (candidates.Count > 1) candidates.RemoveAll(c => c.width != display.width || c.height != display.height ||
            c.rate.denominator == 0 || Math.Abs(c.rate.value - display.refreshRate.value) > .01);
        // 동일 모델·동일 모드의 여러 모니터를 식별할 수 없을 때 다른 모니터의 모드를 추측하지 않는다.
        if (candidates.Count != 1) return false;
        device = candidates[0].device; rate = candidates[0].rate; return true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Adapter { public uint low; public int high; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint numerator, denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct Source { public Adapter adapter; public uint id, mode, status; }
    [StructLayout(LayoutKind.Sequential)] private struct Target
    { public Adapter adapter; public uint id, mode, technology, rotation, scaling; public Rational refresh; public uint scanline, available, status; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayPath { public Source source; public Target target; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint type, size; public Adapter adapter; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
    { public Header header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    {
        public Header header; public uint flags, technology; public ushort manufacturer, product; public uint connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string path;
    }
    [StructLayout(LayoutKind.Explicit, Size = 220)] private struct DeviceMode
    {
        [FieldOffset(68)] public ushort size;
        [FieldOffset(168)] public uint bits;
        [FieldOffset(172)] public uint width;
        [FieldOffset(176)] public uint height;
        [FieldOffset(180)] public uint flags;
        [FieldOffset(184)] public uint frequency;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, IntPtr modeArray, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref TargetName info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettingsW(string device, uint index, ref DeviceMode mode);
#endif
}
