using TMPro;
using UnityEngine;

/// <summary>
/// 훈련실 벽면에 배치되는 튜토리얼 안내판.
/// 기본 무장 소개 영상의 둥근 카드·좌측 포인트 바·아이콘 타일·실시간 3D 키캡 언어를 벽면에도 통일해 적용한다.
/// 기존 Heading·Instruction·Controls TMP 참조와 문구 규격은 그대로 보존한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialWallPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI heading;
    [SerializeField] private TextMeshProUGUI instruction;
    [SerializeField] private TextMeshProUGUI controls;

    private bool styled;
    private TutorialCardGraphic cardBack;
    private TutorialCardGraphic iconTile;
    private TerminalEquipmentGlyph iconGlyph;
    private TutorialCardGraphic badgePill;
    private TutorialCardGraphic badgeDot;
    private TextMeshProUGUI badgeText;
    private TutorialKeycapStrip keyStrip;

    private void OnEnable()
    {
        InputManager.BindingsChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => InputManager.BindingsChanged -= Refresh;

    private void EnsureModernStyle()
    {
        if (styled || heading == null) return;
        styled = true;
        var sign = heading.transform.parent as RectTransform;
        if (sign == null) return;

        // 기존 직각 평면 사각형(Frame·Inset·Face·Power·Divider)은 숨기고 둥근 카드 그래픽으로 대체한다.
        foreach (string legacy in new[] { "Frame", "Inset", "Face", "Power", "Divider" })
        {
            var t = sign.Find(legacy);
            if (t != null) t.gameObject.SetActive(false);
        }

        // 1) 둥근 카드 바탕(어두운 무채색 + 소프트 그림자 + 상단 광택 + 좌측 포인트 바)
        var cardRect = CreateChild(sign, "WallCardSurface", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        cardRect.SetAsFirstSibling();
        cardBack = cardRect.gameObject.AddComponent<TutorialCardGraphic>();

        // 2) 왼쪽 위 어두운 아이콘 타일 + 절차 아이콘(Sign 1030×364 기준 상단 여백 18px)
        var tileRect = CreateChild(sign, "WallIconTile", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(62f, -48f), new Vector2(60f, 60f));
        iconTile = tileRect.gameObject.AddComponent<TutorialCardGraphic>();
        var glyphGo = new GameObject("WallLessonGlyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(TerminalEquipmentGlyph));
        glyphGo.layer = sign.gameObject.layer;
        var glyphRect = (RectTransform)glyphGo.transform;
        glyphRect.SetParent(tileRect, false);
        glyphRect.anchorMin = glyphRect.anchorMax = glyphRect.pivot = new Vector2(0.5f, 0.5f);
        glyphRect.sizeDelta = new Vector2(40f, 40f);
        iconGlyph = glyphGo.GetComponent<TerminalEquipmentGlyph>();

        // 3) 오른쪽 위 SceneTag 스타일 주제 배지
        var badgeRect = CreateChild(sign, "WallCategoryPill", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-144f, -48f), new Vector2(228f, 38f));
        badgePill = badgeRect.gameObject.AddComponent<TutorialCardGraphic>();
        var dotRect = CreateChild(badgeRect, "WallCategoryDot", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(18f, 18f));
        badgeDot = dotRect.gameObject.AddComponent<TutorialCardGraphic>();
        var badgeLabelGo = new GameObject("WallCategoryLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        badgeLabelGo.layer = sign.gameObject.layer;
        var blRect = (RectTransform)badgeLabelGo.transform;
        blRect.SetParent(badgeRect, false);
        blRect.anchorMin = new Vector2(0f, 0.5f);
        blRect.anchorMax = new Vector2(1f, 0.5f);
        blRect.pivot = new Vector2(0f, 0.5f);
        blRect.offsetMin = new Vector2(36f, -16f);
        blRect.offsetMax = new Vector2(-10f, 16f);
        badgeText = badgeLabelGo.GetComponent<TextMeshProUGUI>();
        badgeText.font = heading.font;
        badgeText.fontSize = 20f;
        badgeText.fontStyle = FontStyles.Bold;
        badgeText.alignment = TextAlignmentOptions.MidlineLeft;
        badgeText.textWrappingMode = TextWrappingModes.NoWrap;
        badgeText.overflowMode = TextOverflowModes.Overflow;
        badgeText.raycastTarget = false;

        // 4) 헤더 아래 미세 구분선
        var divRect = CreateChild(sign, "WallHeaderLine", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -86f), new Vector2(-64f, 2f));
        var divImg = divRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        divImg.color = TutorialGuideTheme.DividerColor;
        divImg.raycastTarget = false;

        // 5) 실시간 3D 입체 키캡 줄(Instruction 아래, Controls 위)
        var stripRect = CreateChild(sign, "WallKeycapStrip", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -68f), new Vector2(910f, 56f));
        keyStrip = stripRect.gameObject.AddComponent<TutorialKeycapStrip>();
        keyStrip.Initialize(heading.font, "WallKey", centered: true);

        // 6) 기존 Heading·Instruction·Controls 배치 정돈(이름·문구·넘침 조건 보존)
        heading.alignment = TextAlignmentOptions.MidlineLeft;
        heading.fontSize = 40f;
        heading.color = Color.white;
        var hr = heading.rectTransform;
        hr.anchorMin = hr.anchorMax = new Vector2(0f, 1f);
        hr.pivot = new Vector2(0f, 0.5f);
        hr.anchoredPosition = new Vector2(108f, -48f);
        hr.sizeDelta = new Vector2(560f, 58f);

        if (instruction != null)
        {
            instruction.alignment = TextAlignmentOptions.Center;
            instruction.fontSize = 31f;
            instruction.color = TutorialGuideTheme.BodyColor;
            var ir = instruction.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.pivot = new Vector2(0.5f, 0.5f);
            ir.anchoredPosition = new Vector2(0f, 30f);
            ir.sizeDelta = new Vector2(910f, 108f);
        }

        if (controls != null)
        {
            controls.alignment = TextAlignmentOptions.Center;
            controls.fontSize = 23f;
            controls.color = TutorialGuideTheme.SoftColor;
            var cr = controls.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.anchoredPosition = new Vector2(0f, -138f);
            cr.sizeDelta = new Vector2(910f, 44f);
        }
    }

    private void Refresh()
    {
        var manager = WaveManager.ActiveInstance;
        if (manager == null || manager.Data == null || manager.currentWaveIndex < 0
            || manager.currentWaveIndex >= manager.Data.waves.Length) return;

        var lesson = manager.Data.waves[manager.currentWaveIndex].tutorial;
        if (lesson == null || !lesson.wallInstructions) return;

        EnsureModernStyle();

        Color accent = TutorialGuideTheme.AccentOf(lesson.action);
        if (cardBack != null)
            cardBack.ConfigureCard(26f, TutorialGuideTheme.InkFill, new Color(accent.r, accent.g, accent.b, 0.30f), 2.5f,
                accent, 11f, 0.48f, 18f, 0.08f);
        if (iconTile != null)
            iconTile.ConfigureCard(18f, TutorialGuideTheme.TileFill, accent, 2.8f, accent, 0f, 0f, 0f, 0.05f);
        if (iconGlyph != null)
            iconGlyph.Configure(TutorialGuideTheme.DefaultGlyph(lesson.action), accent, 3.6f);
        if (badgePill != null)
            badgePill.ConfigureCard(22f, TutorialGuideTheme.TileFill, new Color(accent.r, accent.g, accent.b, 0.40f), 1.8f, accent, 0f, 0f, 0f, 0.04f);
        if (badgeDot != null)
            badgeDot.ConfigureDot(accent);
        if (badgeText != null)
        {
            badgeText.text = TutorialGuideTheme.CategoryLabel(lesson.action);
            badgeText.color = Color.white;
        }
        if (keyStrip != null)
            keyStrip.SetItems(TutorialGuideTheme.BuildActionKeys(lesson.action), accent, height: 42f, textSize: 21f, availableWidth: 910f);

        heading.text = lesson.title;
        instruction.text = lesson.description;
        controls.text = lesson.action switch
        {
            TutorialAction.ShieldBreakGear => Key("Attack") + " 기본 공격    ·    " + Key("Gear") + " 유지 + " + Key("Attack") + " 기어 발동",
            TutorialAction.SkillStorage => Key("Gear") + " 기어 준비    ·    " + Key("StoreSkill") + " 저장 / 사용",
            // 보조 무기 수업은 해머 무장의 '회복'을 쓰므로 전환 키와 회복을 함께 알린다(Controls 910px·30pt 한 줄).
            TutorialAction.SecondaryWeapon => Key("SwapWeapon") + " 무장 전환    ·    " + Key("SecondaryWeapon") + " 보조 무기(회복)",
            TutorialAction.GearQuickCharge => Key("Gear") + " 기어 → 떼었다가 다시 " + Key("Gear") + " 유지 → " + Key("Attack") + " 발동",
            _ => Key(TutorialWaveGuide.ActionName(lesson.action))
        };
    }

    private static RectTransform CreateChild(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        return rect;
    }

    private static string Key(string action) => "[ " + InputBindingDisplay.Get(action) + " ]";
}
