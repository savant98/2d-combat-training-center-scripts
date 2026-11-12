using UnityEngine;

// 지형 한 칸(기본 지형·생성 발판·OneWay 발판)을 "기계팔이 든 패널"로 끼우거나 빼는 연출의 자세 계산이다.
// 벽 뒤 수납구에서 팔이 뻗어 패널을 내밀고(작고 어둡고 기울어진 상태 → 제 크기), 각도를 맞춘 뒤 눌러 끼운다. 뺄 때는 거꾸로다.
// 칸마다 방향·기울기·꺾이는 쪽이 달라 한 장씩 따로 움직이는 것처럼 보인다. 시간(0~1)만으로 정해지므로 어디서 불러도 같은 모습이다.
public static class WaveTerrainPanelMotion
{
    public struct Pose
    {
        public Vector2 offset;     // 칸 중심에서 벗어난 거리(칸)
        public float angle;        // 기울기(도)
        public float scale;        // 크기(1 = 제자리)
        public float brightness;   // 깊이 들어갈수록 어둡다(0~1)
        public float bay;          // 수납구가 열린 정도(0~1)
        public float outline;      // 패널 테두리 선 굵기 배율(0 = 숨김, 1 = 지형 선 굵기)
        public float flash;        // 맞물리거나 풀리는 순간의 섬광(0~1)
        public float arm;          // 팔이 보이는 정도(0~1)
    }

    public const float SingleDuration = .5f;   // 패널 한 장의 기본 연출 시간(초)
    public const float StowedScale = .3f;      // 수납구 안에 있을 때의 크기
    public const float LiftedScale = 1.1f;     // 벽면에서 살짝 들린 크기
    public const float LockTime = .86f;        // 끼울 때 맞물리는 시점(0~1)
    public const float UnlockTime = .06f;      // 뺄 때 잠금이 풀리는 시점(0~1)
    private const float Reach = .3f;           // 수납 위치가 칸 중심에서 벗어난 거리(칸)
    private const float StowedAngle = 34f, LiftedAngle = 7f;
    private const float StowedBrightness = .22f;

    // 칸 좌표로 정하는 고정 난수(0~1). 같은 칸은 늘 같은 방향·기울기로 움직인다.
    public static uint Seed(Vector2Int cell, int salt)
    {
        unchecked
        {
            uint h = (uint)cell.x * 0x9E3779B1u ^ (uint)cell.y * 0x85EBCA77u ^ (uint)salt * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
    }
    public static float Unit(uint seed, int channel)
    {
        unchecked
        {
            uint h = seed + (uint)channel * 0x9E3779B9u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    // 수납 위치의 방향. 대각선 네 방향에서 조금씩 벗어난다.
    public static Vector2 Direction(uint seed)
    {
        float angle = (45f + 90f * Mathf.FloorToInt(Unit(seed, 0) * 4f) + (Unit(seed, 1) - .5f) * 34f) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
    public static float Sign(uint seed, int channel) => Unit(seed, channel) < .5f ? -1f : 1f;

    public static Pose Evaluate(float time, bool install, uint seed)
    {
        time = Mathf.Clamp01(time);
        Vector2 direction = Direction(seed);
        float turn = Sign(seed, 2);
        return install ? Install(time, direction, turn) : Remove(time, direction, turn);
    }

    private static Pose Install(float t, Vector2 direction, float turn)
    {
        float reach = OutCubic(Seg(t, .10f, .62f));     // 팔이 뻗어 패널이 앞으로 나온 정도
        float align = Smooth(Seg(t, .62f, .80f));       // 들린 채 각도를 맞춘다
        float seat = Seg(t, .80f, .92f);                // 눌러 끼운다(살짝 더 들어갔다가 제자리)
        var pose = new Pose();
        pose.scale = Mathf.Lerp(StowedScale, LiftedScale, reach);
        if (seat > 0f) pose.scale = seat < .6f ? Mathf.Lerp(LiftedScale, .975f, Smooth(seat / .6f)) : Mathf.Lerp(.975f, 1f, Smooth((seat - .6f) / .4f));
        pose.offset = direction * (Reach * (1f - Smooth(Seg(t, .10f, .70f))));
        pose.angle = turn * Mathf.Lerp(StowedAngle, LiftedAngle, reach) * (1f - align);
        pose.brightness = Mathf.Lerp(StowedBrightness, 1f, Smooth(Seg(t, .10f, .55f)));
        pose.bay = OutCubic(Seg(t, 0f, .10f));
        pose.flash = Pulse(t, .82f, LockTime, .99f);
        pose.outline = Seg(t, .30f, .50f) * (1f + 1.5f * pose.flash);
        pose.arm = Seg(t, .02f, .12f) * (1f - Seg(t, .72f, .84f));
        return pose;
    }

    private static Pose Remove(float t, Vector2 direction, float turn)
    {
        float unlock = OutCubic(Seg(t, 0f, .14f));      // 잠금이 풀리며 살짝 들린다
        float tilt = Smooth(Seg(t, .12f, .34f));        // 팔이 잡고 기울인다
        float pull = InCubic(Seg(t, .30f, .86f));       // 수납구 안으로 당긴다
        float close = Smooth(Seg(t, .86f, 1f));         // 수납구가 닫힌다
        var pose = new Pose();
        pose.scale = Mathf.Lerp(Mathf.Lerp(1f, LiftedScale, unlock), StowedScale, pull) * (1f - close);
        pose.offset = direction * (Reach * Smooth(Seg(t, .30f, .86f)));
        pose.angle = turn * Mathf.Lerp(LiftedAngle * tilt, StowedAngle, pull);
        pose.brightness = Mathf.Lerp(1f, StowedBrightness, Smooth(Seg(t, .34f, .80f)));
        pose.bay = Seg(t, .10f, .22f) * (1f - close);
        pose.flash = Pulse(t, 0f, UnlockTime, .2f);
        pose.outline = (1f + 1.5f * pose.flash) * (1f - Seg(t, .46f, .74f));
        pose.arm = Seg(t, .14f, .26f) * (1f - Seg(t, .84f, .95f));
        return pose;
    }

    private static float Seg(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
    private static float Smooth(float x) => x * x * (3f - 2f * x);
    private static float OutCubic(float x) { x = 1f - x; return 1f - x * x * x; }
    private static float InCubic(float x) => x * x * x;
    // from에서 올라 peak에서 가장 밝고 to에서 꺼진다.
    private static float Pulse(float t, float from, float peak, float to)
    {
        if (t <= from || t >= to) return 0f;
        return t < peak ? Smooth((t - from) / Mathf.Max(.0001f, peak - from)) : 1f - Smooth((t - peak) / Mathf.Max(.0001f, to - peak));
    }
}
