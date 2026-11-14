using System;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Lobby의 훈련생 순위 전광판(2026-11-15). 표시 전용 장식이며 실제 기록·저장과는 아직 이어져 있지 않다.
/// - 지금은 가상의 훈련생 8명(번호 형식의 이름, 실제 인물이 아님)을 보여 준다. 1등은 Player가 모르는 훈련생이고 Player의 순위는 보이지 않는다.
/// - 나중에 Player가 1등이 되면 맨 위에 보여 줄 수 있게 SetPlayerEntry/ClearPlayerEntry를 둔다. 지금은 값만 받아 두고 아무것도 그리지 않는다.
/// - 1등 행의 은은한 빛, 위에서 아래로 행을 훑는 강조, 기어·표시등의 공회전은 Animator(공회전 Clip)가 맡는다.
/// - 행의 글씨는 tools/Unity/LobbyPropDesign.cs가 Prefab에 같은 내용으로 미리 적어 두고(편집 화면에서도 보이게), 실행할 때 한 번 더 적용한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyRankingBoard : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        [Tooltip("표시 이름. 가상 훈련생은 '훈련생 K-0412'처럼 번호 형식으로 쓴다")] public string name;
        [Min(0)] public int score;
        [Tooltip("등급(S+·S·A+ …)")] public string grade;

        public Entry(string name, int score, string grade)
        {
            this.name = name;
            this.score = score;
            this.grade = grade;
        }
    }

    [SerializeField, Tooltip("위에서부터 차례로 보이는 순위(1등이 첫 칸)")] private Entry[] entries = Array.Empty<Entry>();
    [SerializeField] private TMP_Text[] rankLabels = Array.Empty<TMP_Text>();
    [SerializeField] private TMP_Text[] nameLabels = Array.Empty<TMP_Text>();
    [SerializeField] private TMP_Text[] scoreLabels = Array.Empty<TMP_Text>();
    [SerializeField, Tooltip("1등 행의 글씨 색")] private Color leaderColor = new Color(1f, .86f, .45f, 1f);
    [SerializeField, Tooltip("다른 행의 글씨 색")] private Color rowColor = new Color(.86f, .92f, .96f, 1f);
    [SerializeField, Tooltip("2등 아래 순위 번호의 색")] private Color rankColor = new Color(.52f, .62f, .72f, 1f);
    [SerializeField, Tooltip("점수 뒤 등급의 색")] private Color gradeColor = new Color(1f, .8f, .36f, 1f);

    private bool hasPlayer;
    private string playerName;
    private int playerRank, playerScore;

    /// <summary>글씨가 연결된 행의 수.</summary>
    public int RowCount => Mathf.Min(rankLabels != null ? rankLabels.Length : 0, nameLabels != null ? nameLabels.Length : 0, scoreLabels != null ? scoreLabels.Length : 0);
    public int EntryCount => entries != null ? entries.Length : 0;
    /// <summary>SetPlayerEntry로 Player의 기록을 받아 두었는가.</summary>
    public bool HasPlayerEntry => hasPlayer;
    public string PlayerName => hasPlayer ? playerName : null;
    public int PlayerRank => hasPlayer ? playerRank : 0;
    public int PlayerScore => hasPlayer ? playerScore : 0;
    /// <summary>Player 행을 전광판에 그리고 있는가. 지금은 그리지 않으므로 늘 false다(나중에: Player가 1등이면 맨 위에 보인다).</summary>
    public bool ShowsPlayer => false;

    /// <summary>생성 도구가 Prefab에 순위와 글씨 칸을 적을 때 쓴다(Edit Mode).</summary>
    public void Configure(Entry[] values, TMP_Text[] ranks, TMP_Text[] names, TMP_Text[] scores)
    {
        entries = values != null ? values : Array.Empty<Entry>();
        rankLabels = ranks != null ? ranks : Array.Empty<TMP_Text>();
        nameLabels = names != null ? names : Array.Empty<TMP_Text>();
        scoreLabels = scores != null ? scores : Array.Empty<TMP_Text>();
    }

    /// <summary>
    /// 나중을 위한 진입점: Player의 순위·점수를 받아 둔다. 지금은 Player 행을 그리지 않는다(사용자 결정 2026-11-15: 내 순위는 보이지 않는다).
    /// 나중에 Player가 1등이 되면 이 값으로 맨 위 행을 Player로 바꿔 보여 준다.
    /// </summary>
    public void SetPlayerEntry(string name, int rank, int score)
    {
        hasPlayer = true;
        playerName = name;
        playerRank = Mathf.Max(1, rank);
        playerScore = Mathf.Max(0, score);
    }

    /// <summary>받아 둔 Player의 기록을 지운다.</summary>
    public void ClearPlayerEntry()
    {
        hasPlayer = false;
        playerName = null;
        playerRank = playerScore = 0;
    }

    private void Awake() => ApplyEntries();

    /// <summary>순위 목록을 행의 글씨에 적는다. 생성 도구도 같은 내용을 Prefab에 미리 적으려고 부른다.</summary>
    public void ApplyEntries()
    {
        int rows = RowCount;
        for (int i = 0; i < rows; i++)
        {
            bool has = entries != null && i < entries.Length;
            Color color = i == 0 ? leaderColor : rowColor;
            Set(rankLabels[i], (i + 1).ToString("00", CultureInfo.InvariantCulture), i == 0 ? leaderColor : rankColor);
            Set(nameLabels[i], has ? entries[i].name : "-", color);
            Set(scoreLabels[i], has ? FormatScore(entries[i].score, entries[i].grade) : "", color);
        }
    }

    /// <summary>점수(천 단위 쉼표)와 등급(색)을 한 칸에 적는 글.</summary>
    public string FormatScore(int score, string grade)
    {
        string value = score.ToString("#,0", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(grade)) return value;
        return value + "  <color=#" + ColorUtility.ToHtmlStringRGB(gradeColor) + ">" + grade + "</color>";
    }

    private static void Set(TMP_Text label, string text, Color color)
    {
        if (label == null) return;
        if (label.text != text) label.text = text;
        label.color = color;
    }
}
