using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 기본 무장 소개 영상(gfx.py / templates.py)의 색 팔레트·주제 배지·키워드 강조·키캡 구성을
/// 상단 튜토리얼 패널(TutorialWaveGuide)과 벽면 안내판(TutorialWallPanel)에서 함께 쓴다.
/// </summary>
internal static class TutorialGuideTheme
{
    public static readonly Color InkFill = new Color(0.055f, 0.067f, 0.086f, 0.92f);        // #0E1116
    public static readonly Color TileFill = new Color(0.020f, 0.027f, 0.039f, 0.96f);       // #05070A
    public static readonly Color FooterFill = new Color(0.082f, 0.106f, 0.141f, 0.94f);     // #151B24
    public static readonly Color SubtleBorder = new Color(1f, 1f, 1f, 0.14f);
    public static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.09f);
    public static readonly Color BodyColor = new Color(0.929f, 0.949f, 0.969f, 1f);         // #EDF2F7
    public static readonly Color SoftColor = new Color(0.788f, 0.824f, 0.863f, 1f);         // #C9D2DC
    public static readonly Color PromptAmber = new Color(1f, 0.835f, 0.482f, 1f);           // #FFD57B
    public static readonly Color CompleteGreen = new Color(0.357f, 0.941f, 0.541f, 1f);     // #5BF08A

    public static readonly Color RifleCyan = new Color(0.298f, 0.851f, 1f, 1f);             // #4CD9FF
    public static readonly Color HammerPink = new Color(1f, 0.310f, 0.722f, 1f);            // #FF4FB8
    public static readonly Color DamageGold = new Color(1f, 0.761f, 0.239f, 1f);            // #FFC23D
    public static readonly Color ShieldTeal = new Color(0.345f, 0.941f, 0.878f, 1f);        // #58F0E0
    public static readonly Color GearOrange = new Color(1f, 0.353f, 0.165f, 1f);            // #FF5A2A
    public static readonly Color StorageViolet = new Color(0.753f, 0.518f, 1f, 1f);         // #C084FF

    public static Color AccentOf(TutorialAction action) => action switch
    {
        TutorialAction.Attack => DamageGold,
        TutorialAction.SwapWeapon => HammerPink,
        TutorialAction.SecondaryWeapon => CompleteGreen,
        TutorialAction.Shield or TutorialAction.Break or TutorialAction.ShieldBreakGear => ShieldTeal,
        TutorialAction.Gear or TutorialAction.GearQuickCharge => GearOrange,
        TutorialAction.SkillStorage => StorageViolet,
        _ => RifleCyan
    };

    public static string CategoryLabel(TutorialAction action) => action switch
    {
        TutorialAction.Attack => "기본 공격",
        TutorialAction.SwapWeapon => "무장 전환",
        TutorialAction.SecondaryWeapon => "보조 무기 · 회복",
        TutorialAction.Shield or TutorialAction.Break or TutorialAction.ShieldBreakGear => "쉴드 · 브레이크",
        TutorialAction.Gear => "기어 스킬",
        TutorialAction.GearQuickCharge => "기어 테크닉",
        TutorialAction.SkillStorage => "스킬 저장",
        _ => "기본 기동"
    };

    public static TerminalEquipmentGlyph.Glyph DefaultGlyph(TutorialAction action) => action switch
    {
        TutorialAction.Jump or TutorialAction.DoubleJump => TerminalEquipmentGlyph.Glyph.Jump,
        TutorialAction.Dash => TerminalEquipmentGlyph.Glyph.Dash,
        TutorialAction.Attack => TerminalEquipmentGlyph.Glyph.Attack,
        TutorialAction.SwapWeapon => TerminalEquipmentGlyph.Glyph.Swap,
        TutorialAction.SecondaryWeapon => TerminalEquipmentGlyph.Glyph.Secondary,
        TutorialAction.Shield or TutorialAction.ShieldBreakGear => TerminalEquipmentGlyph.Glyph.Shield,
        TutorialAction.Break => TerminalEquipmentGlyph.Glyph.Breakable,
        TutorialAction.Gear or TutorialAction.GearQuickCharge => TerminalEquipmentGlyph.Glyph.Gear,
        TutorialAction.SkillStorage => TerminalEquipmentGlyph.Glyph.Storage,
        _ => TerminalEquipmentGlyph.Glyph.Move
    };

    public static TerminalEquipmentGlyph.Glyph StepGlyph(TutorialStepIcon icon, TutorialAction fallbackAction) => icon switch
    {
        TutorialStepIcon.Attack => TerminalEquipmentGlyph.Glyph.Attack,
        TutorialStepIcon.AutoAim => TerminalEquipmentGlyph.Glyph.AutoAim,
        TutorialStepIcon.Shield => TerminalEquipmentGlyph.Glyph.Shield,
        TutorialStepIcon.Gear => TerminalEquipmentGlyph.Glyph.Gear,
        TutorialStepIcon.Break => TerminalEquipmentGlyph.Glyph.Breakable,
        TutorialStepIcon.Gauge => TerminalEquipmentGlyph.Glyph.Gauge,
        TutorialStepIcon.GearReady => TerminalEquipmentGlyph.Glyph.GearReady,
        TutorialStepIcon.Storage => TerminalEquipmentGlyph.Glyph.Storage,
        TutorialStepIcon.Swap => TerminalEquipmentGlyph.Glyph.Swap,
        TutorialStepIcon.Secondary => TerminalEquipmentGlyph.Glyph.Secondary,
        _ => DefaultGlyph(fallbackAction)
    };

    // 본문 안 핵심 전투 용어에 기본 무장 소개 영상의 시맨틱 색을 입힌다(이미 rich text 태그가 있으면 건드리지 않는다).
    private static readonly (string Term, string Hex)[] KeywordColors =
    {
        ("무장 세트", "#FF85D0"),
        ("무장 전환", "#FF85D0"),
        ("빠른 장전", "#FF8A5B"),
        ("자동 조준", "#4CD9FF"),
        ("기어 스킬", "#64FDFD"),
        ("기어 준비", "#64FDFD"),
        ("무기 투척", "#FF8A5B"),
        ("핸드캐논", "#64FDFD"),
        ("보조무기", "#5BF08A"),
        ("보조 무기", "#5BF08A"),
        ("회복", "#5BF08A"),
        ("브레이크", "#FFC23D"),
        ("스킬 저장", "#C084FF"),
        ("쉴드", "#58F0E0"),
        ("게이지", "#FFC23D"),
        ("자물쇠", "#FFC23D"),
    };

    public static string HighlightBody(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') >= 0) return text ?? "";
        var sb = new StringBuilder(text.Length + 64);
        int i = 0;
        while (i < text.Length)
        {
            int matched = -1;
            for (int k = 0; k < KeywordColors.Length; k++)
            {
                string term = KeywordColors[k].Term;
                if (i + term.Length <= text.Length && string.Compare(text, i, term, 0, term.Length, StringComparison.Ordinal) == 0)
                {
                    matched = k;
                    break;
                }
            }
            if (matched >= 0)
            {
                string term = KeywordColors[matched].Term;
                sb.Append("<color=").Append(KeywordColors[matched].Hex).Append('>').Append(term).Append("</color>");
                i += term.Length;
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    public static List<TutorialKeycapStrip.Item> BuildStepKeys(TutorialLesson lesson, TutorialFocusStep step)
    {
        if (lesson == null) return new List<TutorialKeycapStrip.Item>();
        if (step == null || lesson.action == TutorialAction.GearQuickCharge)
            return BuildActionKeys(lesson.action);

        switch (step.icon)
        {
            case TutorialStepIcon.Swap: return SwapKeys();
            case TutorialStepIcon.Gear:
            case TutorialStepIcon.GearReady: return GearKeys();
            case TutorialStepIcon.Storage: return StorageStepKeys();
            case TutorialStepIcon.Gauge: return GaugeKeys();
            case TutorialStepIcon.Secondary: return SecondaryOnlyKeys();
        }
        switch (step.target)
        {
            case TutorialFocusTarget.AutoAim: return new() { Key("ToggleAutoAim"), Label("자동 조준 전환") };
            case TutorialFocusTarget.WeaponSet: return SwapKeys();
            case TutorialFocusTarget.GearOne:
            case TutorialFocusTarget.GearTwo:
            case TutorialFocusTarget.GearThree:
            case TutorialFocusTarget.GearStages: return GearKeys();
            case TutorialFocusTarget.SkillGauge: return GaugeKeys();
            case TutorialFocusTarget.SecondaryWeapon: return SecondaryOnlyKeys();
        }
        return BuildActionKeys(lesson.action);
    }

    public static List<TutorialKeycapStrip.Item> BuildActionKeys(TutorialAction action)
    {
        var list = new List<TutorialKeycapStrip.Item>(8);
        switch (action)
        {
            case TutorialAction.Move:
                list.Add(Key("Move")); list.Add(Label("이동")); break;
            case TutorialAction.Jump:
                list.Add(Key("Jump")); list.Add(Label("점프")); break;
            case TutorialAction.DoubleJump:
                list.Add(Key("Jump")); list.Add(Label("점프")); list.Add(Arrow()); list.Add(Label("공중에서", true)); list.Add(Key("Jump")); break;
            case TutorialAction.Dash:
                list.Add(Key("Dash")); list.Add(Label("대시")); break;
            case TutorialAction.Attack:
            case TutorialAction.Shield:
                list.Add(Key("Attack")); list.Add(Label("기본 공격")); break;
            case TutorialAction.SwapWeapon:
                return SwapKeys();
            case TutorialAction.SecondaryWeapon:
                list.Add(Key("SwapWeapon")); list.Add(Label("무장 전환"));
                list.Add(Dot());
                list.Add(Key("SecondaryWeapon")); list.Add(Label("보조 무기(회복)"));
                break;
            case TutorialAction.ShieldBreakGear:
                list.Add(Key("Attack")); list.Add(Label("공격"));
                list.Add(Dot());
                list.Add(Key("Gear", "길게")); list.Add(Arrow()); list.Add(Key("Attack")); list.Add(Label("기어 발동"));
                break;
            case TutorialAction.GearQuickCharge:
                list.Add(Key("Gear")); list.Add(Arrow()); list.Add(Label("떼고 다시", true));
                list.Add(Key("Gear", "유지")); list.Add(Arrow()); list.Add(Key("Attack")); list.Add(Label("발동"));
                break;
            case TutorialAction.Break:
            case TutorialAction.Gear:
                return GearKeys();
            case TutorialAction.SkillStorage:
                list.Add(Key("Gear", "길게")); list.Add(Arrow());
                list.Add(StoreKey()); list.Add(Label("저장 · 사용"));
                break;
            case TutorialAction.Platforms:
            case TutorialAction.Trap:
                list.Add(Key("Move")); list.Add(Label("이동")); list.Add(Dot()); list.Add(Key("Jump")); list.Add(Label("점프"));
                break;
        }
        return list;
    }

    private static List<TutorialKeycapStrip.Item> SwapKeys() => new()
    {
        Key("SwapWeapon"), Label("무장 전환")
    };

    private static List<TutorialKeycapStrip.Item> GearKeys() => new()
    {
        Key("Gear", "길게"), Arrow(), Label("단계 점등 시", true), Key("Attack"), Label("발동")
    };

    private static List<TutorialKeycapStrip.Item> StorageStepKeys() => new()
    {
        Key("Gear", "준비 중"), Arrow(), StoreKey(), Label("스킬 저장")
    };

    private static List<TutorialKeycapStrip.Item> GaugeKeys() => new()
    {
        Key("Attack"), Label("기본 공격으로 게이지 충전")
    };

    private static List<TutorialKeycapStrip.Item> SecondaryOnlyKeys() => new()
    {
        Key("SecondaryWeapon"), Label("보조 무기")
    };

    private static TutorialKeycapStrip.Item Key(string action, string hold = null) =>
        TutorialKeycapStrip.Item.Key(action, InputBindingDisplay.Get(action), hold);

    private static TutorialKeycapStrip.Item StoreKey() =>
        TutorialKeycapStrip.Item.Key("StoreSkill", InputBindingDisplay.StoreSkill);

    private static TutorialKeycapStrip.Item Label(string text, bool muted = false) =>
        TutorialKeycapStrip.Item.Label(text, muted);

    private static TutorialKeycapStrip.Item Arrow() => TutorialKeycapStrip.Item.Label("→", true);
    private static TutorialKeycapStrip.Item Dot() => TutorialKeycapStrip.Item.Label("·", true);
}
