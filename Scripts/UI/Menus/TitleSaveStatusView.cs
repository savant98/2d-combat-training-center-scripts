using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>타이틀 메뉴 위쪽의 세이브 현황 표시. 세이브 파일을 요약으로 읽기만 한다.</summary>
[DisallowMultipleComponent]
public sealed class TitleSaveStatusView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countLabel;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI detailLabel;
    [SerializeField, Tooltip("슬롯마다 하나씩, 사용 여부를 보여 주는 표시등")] private Graphic[] pips = Array.Empty<Graphic>();

    /// <summary>세이브가 있는 칸 수.</summary>
    public int UsedSlots { get; private set; }
    /// <summary>가장 최근에 플레이한 칸(0부터). 없으면 -1.</summary>
    public int RecentSlot { get; private set; } = -1;

    public void Refresh()
    {
        var data = DataManager.Instance;
        UsedSlots = 0;
        RecentSlot = -1;
        DateTime latest = DateTime.MinValue;
        SaveSlotSummary recent = default;
        for (int i = 0; i < DataManager.SlotCount; i++)
        {
            var summary = data.GetSlotSummary(i);
            if (i < pips.Length && pips[i] != null)
                pips[i].color = !summary.exists ? TrainingBoardStyle.LineDim : summary.corrupted ? TrainingBoardStyle.Warning : TrainingBoardStyle.Line;
            if (!summary.exists) continue;
            UsedSlots++;
            if (summary.corrupted || (RecentSlot >= 0 && summary.lastPlayedLocal <= latest)) continue;
            RecentSlot = i;
            latest = summary.lastPlayedLocal;
            recent = summary;
        }
        if (RecentSlot >= 0 && RecentSlot < pips.Length && pips[RecentSlot] != null) pips[RecentSlot].color = TrainingBoardStyle.LineBright;

        if (countLabel != null)
            countLabel.text = UsedSlots.ToString(CultureInfo.InvariantCulture) + " / " + DataManager.SlotCount.ToString(CultureInfo.InvariantCulture);
        if (RecentSlot < 0)
        {
            if (titleLabel != null) titleLabel.text = UsedSlots > 0 ? "읽을 수 있는 세이브 없음" : "세이브 없음";
            if (detailLabel != null) detailLabel.text = "게임 시작에서 새 세이브를 만듭니다";
            return;
        }
        if (titleLabel != null)
            titleLabel.text = "최근 세이브  " + (RecentSlot + 1).ToString("00", CultureInfo.InvariantCulture);
        if (detailLabel != null)
        {
            string when = recent.lastPlayedLocal == DateTime.MinValue ? "" : "   ·   " + recent.lastPlayedLocal.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            detailLabel.text = (recent.isFirstStart ? "최초 시작 전" : "훈련 진행 중") + when;
        }
    }
}
