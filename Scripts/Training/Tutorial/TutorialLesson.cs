using System;
using UnityEngine;

// Wave 데이터에는 정수 값으로 저장되고 Wave 격자 편집기는 순번으로 읽으므로 새 값은 맨 끝에만 붙인다.
public enum TutorialAction
{
    None, Move, Jump, DoubleJump, Dash, SwapWeapon, SecondaryWeapon, Shield, Break, Gear, Platforms, Trap, ShieldBreakGear, SkillStorage, Attack,
    GearQuickCharge = 15
}

// 11은 지운 옛 대상(VideoWall)의 값이라 다시 쓰지 않는다. 여러 HUD 칸을 함께 강조하는 대상은 16부터 명시 값으로 둔다.
public enum TutorialFocusTarget
{
    Panel, GridArea, Goal, Platform, Player, PrimaryWeapon, SecondaryWeapon,
    GearOne, GearTwo, GearThree, SkillGauge,
    Enemy = 12, EnemyShield, EnemyBreak, LessonIcon,
    GearStages = 16, WeaponSet = 17
}

public enum TutorialStepIcon { None, Attack, Shield, Gear, Break, Gauge, GearReady, Storage, Swap, Secondary }

/// <summary>선택 입력 한 번으로 넘기는 설명입니다. 영역 좌표는 Wave 생성 기준점에 상대적입니다.</summary>
[Serializable]
public sealed class TutorialFocusStep
{
    public string title;
    [TextArea(2, 8)] public string description;
    public TutorialFocusTarget target;
    public TutorialStepIcon icon;
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
    public bool wallInstructions;
    public string title;
    [TextArea(3, 8)] public string description;
    public TutorialAction action;
    [Tooltip("무장 교체 실습이 제공하는 무장. 지정하면 필수 표적은 이 무장의 기본 공격으로 파괴합니다.")]
    public PlayerArmamentSO practiceArmament;
    [Min(.1f)] public float requiredDistance = 4f;
    [Min(0f)] public float dashCrossingOffset;
    public TutorialFocusStep[] steps = Array.Empty<TutorialFocusStep>();
}
