using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 정산 시 복사한 이번 판의 Wave 시간을 표시한다. 게임 상태와 시간은 변경하지 않는다.
public sealed class TrainingWaveTimesPageUI : MonoBehaviour
{
    public const int RowsPerPage = 12;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private TextMeshProUGUI summary;
    [SerializeField] private TextMeshProUGUI empty;
    [SerializeField] private GameObject[] rows;
    [SerializeField] private TextMeshProUGUI[] waveLabels;
    [SerializeField] private TextMeshProUGUI[] times;
    [SerializeField] private TextMeshProUGUI[] states;
    [SerializeField] private RectTransform[] bars;
    private readonly List<TrainingWaveTimeRecord> records = new();
    private int waveCount;
    private float totalSeconds;

    public CanvasGroup Group => group;
    public int PageCount => Mathf.Max(1, Mathf.CeilToInt(waveCount / (float)RowsPerPage));

    public void SetRecords(IReadOnlyList<TrainingWaveTimeRecord> source, int totalWaves, float seconds)
    {
        records.Clear();
        if (source != null) for (int i = 0; i < source.Count; i++) records.Add(source[i]);
        waveCount = totalWaves;
        foreach (var record in records) waveCount = Mathf.Max(waveCount, record.WaveNumber);
        totalSeconds = seconds;
        ShowPage(0);
    }

    public void ShowPage(int page)
    {
        page = Mathf.Clamp(page, 0, PageCount - 1);
        title.text = PageCount > 1 ? $"Wave별 소요 시간  {page + 1} / {PageCount}" : "Wave별 소요 시간";
        summary.text = $"측정 시간  {FormatTime(totalSeconds)}  ·  전환 준비 시간 제외";
        empty.gameObject.SetActive(waveCount == 0);
        float longest = 0f;
        foreach (var record in records) longest = Mathf.Max(longest, record.TimeSeconds);
        for (int i = 0; i < rows.Length; i++)
        {
            int waveNumber = page * RowsPerPage + i + 1;
            rows[i].SetActive(waveNumber <= waveCount);
            if (waveNumber > waveCount) continue;
            waveLabels[i].text = $"WAVE {waveNumber:00}";
            bool found = false;
            TrainingWaveTimeRecord value = default;
            foreach (var record in records) if (record.WaveNumber == waveNumber) { value = record; found = true; break; }
            times[i].text = found ? FormatTime(value.TimeSeconds) : "--:--.--";
            states[i].text = !found ? "미진행" : value.Cleared ? "완료" : "중단";
            states[i].color = !found ? TrainingBoardStyle.LineDim : value.Cleared ? TrainingBoardStyle.Line : TrainingBoardStyle.Warning;
            bars[i].localScale = new Vector3(found && longest > 0 ? Mathf.Clamp01(value.TimeSeconds / longest) : 0, 1, 1);
        }
    }

    public static string FormatTime(float seconds)
    {
        int ticks = Mathf.RoundToInt(Mathf.Max(0, seconds) * 100);
        return $"{ticks / 6000:00}:{ticks / 100 % 60:00}.{ticks % 100:00}";
    }
}
