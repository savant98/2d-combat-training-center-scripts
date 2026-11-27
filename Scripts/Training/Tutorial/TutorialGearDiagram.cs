using TMPro;
using UnityEngine;

/// <summary>
/// 기본 무장 소개 영상의 기어 단계 도식(GearSteps: 1단계 ── 2단계 ── 3단계)을 튜토리얼 카드 안에 실시간으로 표시한다.
/// 현재 무장 세트의 각 단계 기술 이름·잠금 상태·충전/준비·빠른 장전 점등을 실시간으로 반영한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialGearDiagram : MonoBehaviour
{
    private static readonly Color[] StageColors =
    {
        new Color(0.392f, 0.992f, 0.992f, 1f), // 1단계 #64FDFD
        new Color(1.000f, 0.353f, 0.165f, 1f), // 2단계 #FF5A2A
        new Color(1.000f, 0.416f, 1.000f, 1f), // 3단계 #FF6AFF
    };

    private sealed class StageNode
    {
        public TutorialCardGraphic Card;
        public TutorialCardGraphic Badge;
        public TextMeshProUGUI Number;
        public TextMeshProUGUI StageLabel;
        public TextMeshProUGUI SkillName;
        public RectTransform Rect;
        public float Pulse;
    }

    private RectTransform root;
    private TMP_FontAsset font;
    private TutorialCardGraphic connector;
    private readonly StageNode[] stages = new StageNode[3];
    private bool built;
    private float smoothProgress;

    public void Initialize(TMP_FontAsset labelFont)
    {
        root = transform as RectTransform;
        font = labelFont;
        if (!built) Build();
    }

    private void Build()
    {
        built = true;
        if (root == null) root = transform as RectTransform;

        // 뒤쪽 단계 연결 막대(1단계 → 2단계 → 3단계)
        var barGo = new GameObject("GearDiagramTrack", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialCardGraphic));
        barGo.layer = gameObject.layer;
        var barRect = (RectTransform)barGo.transform;
        barRect.SetParent(root, false);
        barRect.anchorMin = new Vector2(0.08f, 0.5f);
        barRect.anchorMax = new Vector2(0.92f, 0.5f);
        barRect.pivot = new Vector2(0.5f, 0.5f);
        barRect.sizeDelta = new Vector2(0f, 5f);
        barRect.anchoredPosition = Vector2.zero;
        connector = barGo.GetComponent<TutorialCardGraphic>();
        connector.ConfigurePillBar(new Color(1f, 1f, 1f, 0.14f), StageColors[0], StageColors[1], 0f);

        for (int i = 0; i < 3; i++)
        {
            float anchorX = i == 0 ? 0.16f : i == 1 ? 0.50f : 0.84f;
            var nodeGo = new GameObject("GearStageNode_" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialCardGraphic));
            nodeGo.layer = gameObject.layer;
            var nodeRect = (RectTransform)nodeGo.transform;
            nodeRect.SetParent(root, false);
            nodeRect.anchorMin = nodeRect.anchorMax = new Vector2(anchorX, 0.5f);
            nodeRect.pivot = new Vector2(0.5f, 0.5f);
            nodeRect.sizeDelta = new Vector2(216f, 54f);
            nodeRect.anchoredPosition = Vector2.zero;
            var card = nodeGo.GetComponent<TutorialCardGraphic>();
            card.ConfigureCard(14f, new Color(0.035f, 0.047f, 0.067f, 0.94f), new Color(1f, 1f, 1f, 0.16f), 1.5f,
                StageColors[i], 0f, 0.22f, 6f, 0.04f);

            // 왼쪽 단계 숫자 배지
            var badgeGo = new GameObject("GearStageBadge_" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialCardGraphic));
            badgeGo.layer = gameObject.layer;
            var badgeRect = (RectTransform)badgeGo.transform;
            badgeRect.SetParent(nodeRect, false);
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(10f, 0f);
            badgeRect.sizeDelta = new Vector2(34f, 34f);
            var badge = badgeGo.GetComponent<TutorialCardGraphic>();
            badge.ConfigureCard(17f, new Color(0.055f, 0.067f, 0.086f, 0.96f), StageColors[i], 2f,
                StageColors[i], 0f, 0f, 0f, 0f);

            var num = CreateText(badgeRect, "GearStageNum_" + (i + 1), (i + 1).ToString(), 17f, StageColors[i],
                TextAlignmentOptions.Center, FontStyles.Bold);
            num.rectTransform.anchorMin = Vector2.zero;
            num.rectTransform.anchorMax = Vector2.one;
            num.rectTransform.offsetMin = Vector2.zero;
            num.rectTransform.offsetMax = Vector2.zero;

            var stageTxt = CreateText(nodeRect, "GearStageTag_" + (i + 1), (i + 1) + "단계", 12.5f, StageColors[i],
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            stageTxt.enableAutoSizing = true;
            stageTxt.fontSizeMin = 10f;
            stageTxt.fontSizeMax = 12.5f;
            stageTxt.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            stageTxt.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            stageTxt.rectTransform.pivot = new Vector2(0f, 0.5f);
            stageTxt.rectTransform.offsetMin = new Vector2(52f, 2f);
            stageTxt.rectTransform.offsetMax = new Vector2(-10f, 22f);

            var nameTxt = CreateText(nodeRect, "GearStageSkill_" + (i + 1), "-", 15.5f, Color.white,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            nameTxt.enableAutoSizing = true;
            nameTxt.fontSizeMin = 11f;
            nameTxt.fontSizeMax = 15.5f;
            nameTxt.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            nameTxt.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            nameTxt.rectTransform.pivot = new Vector2(0f, 0.5f);
            nameTxt.rectTransform.offsetMin = new Vector2(52f, -22f);
            nameTxt.rectTransform.offsetMax = new Vector2(-10f, 3f);

            stages[i] = new StageNode
            {
                Card = card,
                Badge = badge,
                Number = num,
                StageLabel = stageTxt,
                SkillName = nameTxt,
                Rect = nodeRect
            };
        }
    }

    public void Refresh(PlayerWeaponAbility ability, TutorialAction lessonAction)
    {
        if (!built) Build();
        if (ability == null) return;

        var set = ability.GetWeaponSet(ability.CurrentSetIndex);
        int activeStage = ability.IsGearWaiting ? ability.GearStage : 0;
        bool charging = ability.IsGearCharging;
        bool quick = ability.IsGearQuickCharged;
        int highlightStage = activeStage > 0 ? activeStage : (charging ? (quick ? 2 : 1) : 0);

        float targetProg = activeStage switch
        {
            1 => 0.16f,
            2 => 0.58f,
            3 => 1.00f,
            _ => charging ? (quick ? 0.45f : 0.08f) : 0f
        };
        smoothProgress = Mathf.MoveTowards(smoothProgress, targetProg, Time.unscaledDeltaTime * 3.8f);
        if (connector != null) connector.SetPillBarProgress(smoothProgress);

        for (int i = 0; i < 3; i++)
        {
            var node = stages[i];
            int stageNum = i + 1;
            var weapon = set != null ? set.GetGearWeapon(stageNum) : null;
            bool hasWeapon = weapon != null;
            bool isTargetStage = (lessonAction == TutorialAction.GearQuickCharge && stageNum == 2)
                || ((lessonAction == TutorialAction.ShieldBreakGear || lessonAction == TutorialAction.SkillStorage) && stageNum == 1);
            bool isLit = activeStage == stageNum;
            bool isReached = activeStage >= stageNum || (charging && highlightStage == stageNum);

            string title = hasWeapon ? weapon.DisplayName : "비어 있음";
            if (node.SkillName.text != title) node.SkillName.text = title;

            string tag = isLit && quick && stageNum >= 2
                ? stageNum + "단계 · 빠른 장전"
                : stageNum + "단계" + (hasWeapon ? " · " + weapon.skillGauge.ToString("0") : "");
            if (node.StageLabel.text != tag) node.StageLabel.text = tag;

            Color col = StageColors[i];
            Color fill = isLit
                ? Color.Lerp(new Color(0.055f, 0.067f, 0.086f, 0.96f), col, 0.18f)
                : new Color(0.035f, 0.047f, 0.067f, hasWeapon ? 0.94f : 0.72f);
            Color border = isLit
                ? col
                : isTargetStage && hasWeapon
                    ? new Color(col.r, col.g, col.b, 0.55f)
                    : new Color(1f, 1f, 1f, hasWeapon ? 0.16f : 0.08f);
            node.Card.ConfigureCard(14f, fill, border, isLit ? 2.6f : 1.4f, col, isLit ? 5f : 0f, 0.2f, 6f, isLit ? 0.1f : 0.04f);

            Color badgeCol = isReached || (isTargetStage && hasWeapon) ? col : new Color(col.r, col.g, col.b, 0.42f);
            node.Badge.SetCardAccent(badgeCol, badgeCol);
            node.Number.color = badgeCol;
            node.StageLabel.color = isLit ? col : new Color(col.r, col.g, col.b, hasWeapon ? 0.85f : 0.42f);
            node.SkillName.color = hasWeapon
                ? (isLit ? Color.white : new Color(0.88f, 0.92f, 0.96f, 1f))
                : new Color(0.54f, 0.60f, 0.66f, 1f);

            float targetPulse = isLit ? 1.035f : 1f;
            node.Pulse = Mathf.MoveTowards(node.Pulse <= 0f ? 1f : node.Pulse, targetPulse, Time.unscaledDeltaTime * 3.5f);
            node.Rect.localScale = new Vector3(node.Pulse, node.Pulse, 1f);
        }
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string objectName, string value, float size, Color color,
        TextAlignmentOptions alignment, FontStyles style)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        var txt = go.GetComponent<TextMeshProUGUI>();
        if (font != null) txt.font = font;
        txt.text = value;
        txt.fontSize = size;
        txt.color = color;
        txt.alignment = alignment;
        txt.fontStyle = style;
        txt.enableAutoSizing = false;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode = TextOverflowModes.Overflow;
        txt.raycastTarget = false;
        return txt;
    }
}
