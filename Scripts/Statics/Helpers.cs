using System.Collections.Generic;
using TMPro;
using UnityEngine;

public static class LayerMaskHelper
{
    /// <summary>
    /// GameObject가 LayerMask에 포함되어 있는지 확인
    /// </summary>
    public static bool IsInLayerMask(GameObject obj, LayerMask mask)
    {
        return (mask.value & (1 << obj.layer)) != 0;
    }

    /// <summary>
    /// Layer 번호(int)가 LayerMask에 포함되어 있는지 확인
    /// </summary>
    public static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}
public static class LaserLineUtility
{
    public static RaycastHit2D GetBlockHit(
        Vector2 origin,
        Vector2 direction,
        float maxLength,
        float radius,
        LayerMask blockMask)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return default;

        direction = direction.normalized;
        maxLength = Mathf.Max(0f, maxLength);
        radius = Mathf.Max(0f, radius);

        return Physics2D.CircleCast(
            origin,
            radius,
            direction,
            maxLength,
            blockMask);
    }

    public static void UpdateCaps(
    Transform startCap,
    Transform endCap,
    Vector2 start,
    Vector2 end,
    Vector2 direction,
    float width,
    float startScale,
    float endScale)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        if (startCap != null)
        {
            startCap.position = start;
            startCap.rotation = Quaternion.Euler(0f, 0f, angle);
            startCap.localScale = Vector3.one * width * startScale;
        }

        if (endCap != null)
        {
            endCap.position = end;
            endCap.rotation = Quaternion.Euler(0f, 0f, angle + 180f);
            endCap.localScale = Vector3.one * width * endScale;
        }
    }
}

public static class TrainingScoreFormatHelper
{
    public static string FormatTime(float seconds)
    {
        if (seconds < 0)
            seconds = 0;
        int min = (int)(seconds / 60f);
        float sec = seconds - (min * 60f);
        return $"{min:00}:{sec:00}";
    }
    public static void GradeToText(Grade grade, TextMeshProUGUI gradeText)
    {
        switch (grade)
        {
            case Grade.Poor:
                gradeText.text = "미흡";
                gradeText.color = Color.red;
                break;
            case Grade.Pass:
                gradeText.text = "통과";
                gradeText.color = Color.green;
                break;
            case Grade.Excellent:
                gradeText.text = "우수!";
                gradeText.color = Color.yellow;
                break;
            case Grade.Flawless:
                gradeText.text = "무결점!";
                gradeText.color = Color.cyan;
                break;
            default:
                gradeText.text = "기록 없음";
                gradeText.color = Color.white;
                break;
        }
    }
}

#region ProjectileSpawnUtility
public static class ProjectileSpawnUtility
{
    public enum SpawnPattern
    {
        AlternatingHorizontal, // 0, -1, +1, -2, +2 ...
        RightOnly,             // 0, +1, +2, +3 ...
        LeftOnly,              // 0, -1, -2, -3 ...
    }

    public struct SpawnValidationSettings
    {
        public float checkRadius;
        public LayerMask blockMask;
        public LayerMask overlapMask;
    }

    public static Vector2 GetValidSpawnPosition(
        Vector2 anchorPosition,
        Vector2 baseOffset,
        int shotIndex,
        SpawnPattern pattern,
        SpawnValidationSettings validationSettings,
        int maxSearchDepth = 20)
    {
        List<int> candidateSlots = BuildCandidateSlotOrder(shotIndex, pattern, maxSearchDepth);

        for (int i = 0; i < candidateSlots.Count; i++)
        {
            int slot = candidateSlots[i];
            Vector2 candidatePos = GetPositionFromSlot(anchorPosition, baseOffset, slot);

            if (IsValidSpawnPosition(candidatePos, validationSettings))
                return candidatePos;
        }

        return anchorPosition + baseOffset;
    }

    public static Vector2 GetPositionFromSlot(Vector2 anchorPosition, Vector2 baseOffset, int slotIndex)
    {
        Vector2 offset = new Vector2(baseOffset.x * slotIndex, baseOffset.y);
        return anchorPosition + offset;
    }

    public static int GetAlternatingSlotIndex(int shotIndex)
    {
        if (shotIndex == 0)
            return 0;

        int ring = (shotIndex + 1) / 2;
        return (shotIndex % 2 == 1) ? -ring : ring;
    }

    public static bool IsValidSpawnPosition(Vector2 position, SpawnValidationSettings settings)
    {
        if (settings.checkRadius <= 0f)
            return true;

        if (Physics2D.OverlapCircle(position, settings.checkRadius, settings.blockMask))
            return false;

        if (Physics2D.OverlapCircle(position, settings.checkRadius, settings.overlapMask))
            return false;

        return true;
    }

    private static List<int> BuildCandidateSlotOrder(int shotIndex, SpawnPattern pattern, int maxSearchDepth)
    {
        List<int> slots = new List<int>();

        int preferredSlot = GetPrimarySlotIndex(shotIndex, pattern);
        AddUnique(slots, preferredSlot);

        if (preferredSlot != 0)
            AddUnique(slots, -preferredSlot);

        switch (pattern)
        {
            case SpawnPattern.AlternatingHorizontal:
                {
                    for (int ring = 0; ring <= maxSearchDepth; ring++)
                    {
                        if (ring == 0)
                        {
                            AddUnique(slots, 0);
                            continue;
                        }

                        AddUnique(slots, -ring);
                        AddUnique(slots, ring);
                    }
                    break;
                }

            case SpawnPattern.RightOnly:
                {
                    for (int i = 0; i <= maxSearchDepth; i++)
                        AddUnique(slots, i);
                    break;
                }

            case SpawnPattern.LeftOnly:
                {
                    for (int i = 0; i <= maxSearchDepth; i++)
                        AddUnique(slots, -i);
                    break;
                }
        }

        return slots;
    }

    private static int GetPrimarySlotIndex(int shotIndex, SpawnPattern pattern)
    {
        switch (pattern)
        {
            case SpawnPattern.AlternatingHorizontal:
                return GetAlternatingSlotIndex(shotIndex);

            case SpawnPattern.RightOnly:
                return shotIndex;

            case SpawnPattern.LeftOnly:
                return -shotIndex;
        }

        return 0;
    }

    private static void AddUnique(List<int> list, int value)
    {
        if (!list.Contains(value))
            list.Add(value);
    }
}
public static class RadialShotHelper
{
    public static Vector2 GetRadialDirection(
        int shotIndex,
        int totalShotCount,
        float startAngleDeg = 90f,
        bool clockwise = true)
    {
        totalShotCount = Mathf.Max(1, totalShotCount);

        float step = 360f / totalShotCount;
        float signedStep = clockwise ? -step : step;
        float angle = startAngleDeg + (shotIndex * signedStep);

        float rad = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;
    }

    public static float ToAngleChargeShotAngle(Vector2 worldDirection)
    {
        if (worldDirection.sqrMagnitude <= 0.0001f)
            return 0f;

        worldDirection.Normalize();
        return -Vector2.SignedAngle(Vector2.down, worldDirection);
    }
}
#endregion