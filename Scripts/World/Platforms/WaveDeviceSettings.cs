using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Wave 배치 항목 하나에만 적용하는 장치 설정값. Prefab 기본값과 다를 때만 저장한다.</summary>
[Serializable]
public struct WaveDeviceSetting
{
    public string key;
    public float value;

    public WaveDeviceSetting(string key, float value)
    {
        this.key = key;
        this.value = value;
    }
}

/// <summary>편집기에 보여 줄 장치 설정 항목. Default는 Prefab에 저장된 기본값이다.</summary>
public readonly struct WaveDeviceParameter
{
    public readonly string Key, Label, Tooltip, Unit;
    public readonly float Min, Max, Step, Default;
    /// <summary>값에 따라 장치의 모양이 바뀌면 true다. 편집기 그림 캐시는 이 항목만 열쇠로 쓴다.</summary>
    public readonly bool ChangesShape;

    public WaveDeviceParameter(string key, string label, string tooltip, string unit, float min, float max, float step, float defaultValue, bool changesShape = false)
    {
        Key = key; Label = label; Tooltip = tooltip; Unit = unit;
        Min = min; Max = max; Step = step; Default = defaultValue;
        ChangesShape = changesShape;
    }

    /// <summary>범위 안으로 자르고 눈금(Step)에 맞춘다.</summary>
    public float Clamp(float value)
    {
        if (!float.IsFinite(value)) value = Default;
        value = Mathf.Clamp(value, Min, Max);
        if (Step > 0f) value = Min + Mathf.Round((value - Min) / Step) * Step;
        return Mathf.Clamp((float)Math.Round(value, 4), Min, Max);
    }
}

/// <summary>공격·작동 범위. 장치 원점 기준이며 회전하기 전 좌표다.</summary>
public readonly struct WaveDeviceRange
{
    public readonly WaveEnvironmentTool.RangeShape Shape;
    public readonly Vector2 Center, Size;
    public readonly Color Color;

    public WaveDeviceRange(WaveEnvironmentTool.RangeShape shape, Vector2 center, Vector2 size, Color color)
    {
        Shape = shape; Center = center; Size = size; Color = color;
    }
}

/// <summary>배치마다 다른 값을 받는 환경 장치. Wave 격자 편집기와 WaveManager가 쓴다.</summary>
public interface IWaveDeviceSettings
{
    /// <summary>조정할 수 있는 항목과 이 Prefab의 기본값.</summary>
    WaveDeviceParameter[] Parameters { get; }
    /// <summary>설정에 따라 장치의 모양이 바뀌면 true다(편집기 미리보기를 다시 그린다).</summary>
    bool SettingsChangeShape { get; }
    /// <summary>이 개체에 값을 적용한다. 비어 있으면 Prefab 기본값으로 돌아간다. 편집기 미리보기를 위해 Edit Mode에서도 불린다.</summary>
    void ApplySettings(WaveDeviceSetting[] overrides);
    /// <summary>주어진 설정에서의 공격·작동 범위.</summary>
    bool TryGetRange(WaveDeviceSetting[] overrides, out WaveDeviceRange range);
}

public static class WaveDeviceSettings
{
    public static readonly WaveDeviceSetting[] None = Array.Empty<WaveDeviceSetting>();

    /// <summary>저장된 값이 있으면 그 값을, 없으면 기본값을 범위 안으로 맞춰 돌려준다.</summary>
    public static float Value(WaveDeviceSetting[] overrides, in WaveDeviceParameter parameter)
    {
        if (overrides != null)
            foreach (var setting in overrides)
                if (setting.key == parameter.Key) return parameter.Clamp(setting.value);
        return parameter.Default;
    }

    public static bool Has(WaveDeviceSetting[] overrides, string key)
    {
        if (overrides != null)
            foreach (var setting in overrides)
                if (setting.key == key) return true;
        return false;
    }

    /// <summary>한 항목을 바꾼 새 배열. 기본값과 같으면 저장하지 않는다.</summary>
    public static WaveDeviceSetting[] With(WaveDeviceSetting[] overrides, in WaveDeviceParameter parameter, float value)
    {
        value = parameter.Clamp(value);
        var result = new List<WaveDeviceSetting>();
        if (overrides != null)
            foreach (var setting in overrides)
                if (setting.key != parameter.Key && !string.IsNullOrEmpty(setting.key)) result.Add(setting);
        if (!Mathf.Approximately(value, parameter.Default)) result.Add(new WaveDeviceSetting(parameter.Key, value));
        result.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        return result.ToArray();
    }

    /// <summary>모양을 바꾸는 항목(ChangesShape)만 남긴 설정. 속도처럼 모양과 무관한 값이 달라도 같은 그림을 쓰게 한다.</summary>
    public static WaveDeviceSetting[] ShapeOnly(WaveDeviceSetting[] overrides, WaveDeviceParameter[] parameters)
    {
        if (overrides == null || overrides.Length == 0 || parameters == null) return None;
        var result = new List<WaveDeviceSetting>();
        foreach (var setting in overrides)
            foreach (var parameter in parameters)
                if (parameter.ChangesShape && parameter.Key == setting.key) { result.Add(setting); break; }
        return result.Count == overrides.Length ? overrides : result.ToArray();
    }

    /// <summary>같은 설정이면 같은 문자열. 편집기 캐시 열쇠로 쓴다.</summary>
    public static string Signature(WaveDeviceSetting[] overrides)
    {
        if (overrides == null || overrides.Length == 0) return string.Empty;
        var sorted = (WaveDeviceSetting[])overrides.Clone();
        Array.Sort(sorted, (a, b) => string.CompareOrdinal(a.key, b.key));
        var text = new StringBuilder();
        foreach (var setting in sorted) text.Append(setting.key).Append('=').Append(setting.value.ToString("R")).Append(';');
        return text.ToString();
    }

    /// <summary>생성한 개체가 설정을 받는 장치면 값을 적용한다.</summary>
    public static void Apply(GameObject instance, WaveDeviceSetting[] overrides)
    {
        if (instance != null && instance.TryGetComponent<IWaveDeviceSettings>(out var device))
            device.ApplySettings(overrides ?? None);
    }
}
