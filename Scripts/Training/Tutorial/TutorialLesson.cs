using System;
using UnityEngine;

public enum TutorialAction
{
    None, Move, Jump, DoubleJump, Dash, SwapWeapon, SecondaryWeapon, Shield, Break, Gear, Platforms, Trap
}

public enum TutorialFocusTarget
{
    Panel, GridArea, Goal, Platform, Player, PrimaryWeapon, SecondaryWeapon,
    GearOne, GearTwo, GearThree, SkillGauge,
    Enemy = 12, EnemyShield, EnemyBreak, LessonIcon
}

/// <summary>선택 입력 한 번으로 넘기는 설명입니다. 영역 좌표는 Wave 생성 기준점에 상대적입니다.</summary>
[Serializable]
public sealed class TutorialFocusStep
{
    public string title;
    [TextArea(2, 8)] public string description;
    public TutorialFocusTarget target;
    public Vector2 focusCenter;
    public Vector2 focusSize = new Vector2(3f, 3f);
    public Vector2 padding = new Vector2(16f, 16f);
}

/// <summary>Wave 격자 편집기에 저장하는 한 단계의 설명과 실습 조건입니다.</summary>
[Serializable]
public sealed class TutorialLesson
{
    public bool enabled;
    public bool focusEnabled = true;
    public string title;
    [TextArea(3, 8)] public string description;
    public TutorialAction action;
    [Min(.1f)] public float requiredDistance = 4f;
    public TutorialFocusStep[] steps = Array.Empty<TutorialFocusStep>();
}
