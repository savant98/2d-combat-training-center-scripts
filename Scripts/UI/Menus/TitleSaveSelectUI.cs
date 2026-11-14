using System;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 타이틀의 세이브 파일 선택 화면. 세로 4칸에서 세이브를 고르거나 새로 만들고, 칸 오른쪽의 버튼으로 지울 수 있다.
/// 칸에는 게임 최초 시작 여부와 훈련 수료 기록을 표시한다. 저장 파일은 DataManager의 요약으로 읽기만 하고,
/// 실제로 슬롯을 고르는 일(SelectSlot)은 SlotChosen을 받은 쪽이 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleSaveSelectUI : MonoBehaviour, IUIInputReceiver
{
    [Serializable]
    public sealed class BadgeView
    {
        public GameObject root;
        public Graphic bar;                 // 등급 색 띠
        public TextMeshProUGUI nameLabel;
        public TextMeshProUGUI gradeLabel;
    }

    [Serializable]
    public sealed class SlotView
    {
        public RectTransform root;
        public UnityEngine.UI.Button button;
        public TrainingHologramGraphic focus;    // 선택 윤곽
        public Graphic accent;                    // 왼쪽 세로 강조선(상태 색)
        public TextMeshProUGUI ordinal, tag, title, detail;
        public GameObject trainingGroup;
        public BadgeView[] badges = Array.Empty<BadgeView>();
        public GameObject emptyGroup;
        public TextMeshProUGUI emptyLabel;
        public UnityEngine.UI.Button deleteButton;
        public TrainingHologramGraphic deleteFocus;
        public TextMeshProUGUI deleteLabel;
    }

    private enum Mode { Closed, List, Dialog }

    [SerializeField] private UIInputLayer inputLayer = UIInputLayer.System;
    [SerializeField] private TitlePanelMotion motion;
    [SerializeField] private SlotView[] slots = Array.Empty<SlotView>();
    [SerializeField, Tooltip("칸에 표시할 훈련(왼쪽부터)")] private TrainingDefinitionSO[] trainings = Array.Empty<TrainingDefinitionSO>();
    [SerializeField, Tooltip("훈련의 짧은 표시 이름. 비우면 displayName을 쓴다")] private string[] trainingLabels = Array.Empty<string>();
    [SerializeField] private TextMeshProUGUI counter;
    [SerializeField] private TextMeshProUGUI hint;

    [Header("삭제 확인")]
    [SerializeField] private TitlePanelMotion dialogMotion;
    [SerializeField] private TextMeshProUGUI dialogMessage;
    [SerializeField] private UnityEngine.UI.Button dialogCancel, dialogDelete;
    [SerializeField] private TrainingHologramGraphic dialogCancelFocus, dialogDeleteFocus;
    [SerializeField] private TextMeshProUGUI dialogCancelLabel, dialogDeleteLabel;

    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO openFeedback;
    [SerializeField] private FeedbackDataSO moveFeedback;
    [SerializeField] private FeedbackDataSO confirmFeedback;
    [SerializeField] private FeedbackDataSO cancelFeedback;
    [SerializeField] private FeedbackDataSO deniedFeedback;
    [SerializeField] private FeedbackDataSO deleteFeedback;

    private static readonly Color Body = new Color32(0xD3, 0xEB, 0xF2, 0xFF);
    private static readonly Color Sub = new Color32(0x7E, 0xA7, 0xB5, 0xFF);
    private const string ListHint = "↑ ↓ 선택   ·   Z 시작   ·   → 삭제   ·   X 뒤로";
    private const string EmptyHint = "↑ ↓ 선택   ·   Z 새 세이브로 시작   ·   X 뒤로";
    private const string DeleteHint = "Z 이 세이브 삭제   ·   ← 돌아가기   ·   X 뒤로";
    private const string DialogHint = "← → 선택   ·   Z 확인   ·   X 취소";

    private Mode mode = Mode.Closed;
    private SaveSlotSummary[] summaries = Array.Empty<SaveSlotSummary>();
    private int selected, dialogIndex;
    private bool onDelete;
    private float deniedUntil;

    /// <summary>고른 슬롯(0부터). 화면은 이미 닫히기 시작했다.</summary>
    public event Action<int> SlotChosen;
    /// <summary>고르지 않고 뒤로 갔다. 화면은 이미 닫히기 시작했다.</summary>
    public event Action Canceled;

    public bool IsActive { get; private set; }
    public int SelectedSlot => selected;
    public bool IsDeleteFocused => onDelete;
    public bool IsDialogOpen => mode == Mode.Dialog;
    private bool CanAct => IsActive && motion != null && motion.IsReady
        && UIManager.Instance != null && !UIManager.Instance.IsUIInputBlocked && UIManager.Instance.IsCurrentInputTarget(this);

    private void Awake()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            var view = slots[i];
            if (view.button != null)
            {
                view.button.onClick.AddListener(() => ClickSlot(index));
                Hover(view.button.gameObject, () => HoverSlot(index, false));
            }
            if (view.deleteButton != null)
            {
                view.deleteButton.onClick.AddListener(() => ClickDelete(index));
                Hover(view.deleteButton.gameObject, () => HoverSlot(index, true));
            }
        }
        if (dialogCancel != null)
        {
            dialogCancel.onClick.AddListener(() => ClickDialog(0));
            Hover(dialogCancel.gameObject, () => HoverDialog(0));
        }
        if (dialogDelete != null)
        {
            dialogDelete.onClick.AddListener(() => ClickDialog(1));
            Hover(dialogDelete.gameObject, () => HoverDialog(1));
        }
    }

    private static void Hover(GameObject target, Action action)
    {
        var trigger = target.GetComponent<EventTrigger>();
        if (trigger == null) trigger = target.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    /// <summary>세이브 파일을 다시 읽어 화면을 펼친다. 가장 최근에 플레이한 칸을 먼저 고른다.</summary>
    public void Open()
    {
        if (IsActive) return;
        IsActive = true;
        mode = Mode.List;
        onDelete = false;
        dialogIndex = 0;
        deniedUntil = 0f;
        ReadSummaries();
        selected = MostRecentSlot();
        if (dialogMotion != null) dialogMotion.SnapHidden();
        Refresh(true);
        motion.Open();
        UIManager.Instance.PushInputTarget(this, inputLayer);
        Play(openFeedback);
    }

    /// <summary>연출 없이 닫는다(Scene이 끝날 때 등).</summary>
    public void CloseImmediately()
    {
        if (!IsActive && (motion == null || !motion.IsVisible)) return;
        Release();
        if (motion != null) motion.SnapHidden();
    }

    private void Release()
    {
        IsActive = false;
        mode = Mode.Closed;
        if (UIManager.Instance != null) UIManager.Instance.RemoveInputTarget(this);
        if (dialogMotion != null) dialogMotion.SnapHidden();
    }

    private void ReadSummaries()
    {
        var data = DataManager.Instance;
        if (summaries.Length != slots.Length) summaries = new SaveSlotSummary[slots.Length];
        for (int i = 0; i < summaries.Length; i++) summaries[i] = data.GetSlotSummary(i);
    }

    private int MostRecentSlot()
    {
        int best = 0;
        DateTime latest = DateTime.MinValue;
        bool found = false;
        for (int i = 0; i < summaries.Length; i++)
        {
            if (!summaries[i].exists || summaries[i].corrupted) continue;
            if (found && summaries[i].lastPlayedLocal <= latest) continue;
            found = true;
            latest = summaries[i].lastPlayedLocal;
            best = i;
        }
        return best;
    }

    #region 입력

    public void OnMove(Vector2 direction)
    {
        if (!CanAct) return;
        if (mode == Mode.Dialog)
        {
            if (dialogMotion == null || !dialogMotion.IsReady) return;
            if (Mathf.Abs(direction.x) > .5f) SetDialogIndex(direction.x > 0f ? 1 : 0);
            return;
        }
        if (Mathf.Abs(direction.y) > .5f)
        {
            int next = Mathf.Clamp(selected + (direction.y < 0f ? 1 : -1), 0, slots.Length - 1);
            if (next != selected) Select(next, false);
        }
        else if (direction.x > .5f)
        {
            // 지울 세이브가 있는 칸에서만 삭제 버튼으로 넘어간다.
            if (!onDelete && summaries[selected].exists) Select(selected, true);
        }
        else if (direction.x < -.5f)
        {
            if (onDelete) Select(selected, false);
        }
    }

    public void OnConfirm()
    {
        if (!CanAct) return;
        if (mode == Mode.Dialog)
        {
            if (dialogMotion == null || !dialogMotion.IsReady) return;
            if (dialogIndex == 1) ConfirmDelete(); else CloseDialog(true);
        }
        else if (onDelete) OpenDialog();
        else Choose();
    }

    public void OnCancel()
    {
        if (!CanAct) return;
        if (mode == Mode.Dialog)
        {
            CloseDialog(true);
            return;
        }
        Play(cancelFeedback);
        Release();
        motion.Close();
        Canceled?.Invoke();
    }

    private void ClickSlot(int index)
    {
        if (!CanAct || mode != Mode.List) return;
        if (index != selected || onDelete) Select(index, false);
        Choose();
    }

    private void ClickDelete(int index)
    {
        if (!CanAct || mode != Mode.List || !summaries[index].exists) return;
        if (index != selected || !onDelete) Select(index, true);
        OpenDialog();
    }

    private void HoverSlot(int index, bool delete)
    {
        if (!CanAct || mode != Mode.List) return;
        if (delete && !summaries[index].exists) return;
        if (index != selected || delete != onDelete) Select(index, delete);
    }

    private void ClickDialog(int index)
    {
        if (!CanAct || mode != Mode.Dialog || dialogMotion == null || !dialogMotion.IsReady) return;
        if (index == 1) ConfirmDelete(); else CloseDialog(true);
    }

    private void HoverDialog(int index)
    {
        if (!CanAct || mode != Mode.Dialog || dialogMotion == null || !dialogMotion.IsReady) return;
        SetDialogIndex(index);
    }

    #endregion

    private void Select(int index, bool delete)
    {
        selected = index;
        onDelete = delete;
        deniedUntil = 0f;
        Refresh(false);
        Play(moveFeedback);
    }

    private void Choose()
    {
        if (summaries[selected].corrupted)
        {
            // 읽지 못하는 파일로는 시작하지 않는다. 지운 뒤에 그 칸을 다시 쓸 수 있다.
            deniedUntil = Time.unscaledTime + 2.5f;
            Play(deniedFeedback != null ? deniedFeedback : cancelFeedback);
            RefreshHint();
            return;
        }
        Play(confirmFeedback);
        int slot = selected;
        Release();
        motion.Close();
        SlotChosen?.Invoke(slot);
    }

    private void OpenDialog()
    {
        mode = Mode.Dialog;
        dialogIndex = 0;   // 기본 선택은 취소
        if (dialogMessage != null)
            dialogMessage.text = (selected + 1).ToString("00", CultureInfo.InvariantCulture) + "번 세이브를 삭제합니다.\n삭제한 기록은 되돌릴 수 없습니다.";
        RefreshDialog(true);
        if (dialogMotion != null) dialogMotion.Open();
        RefreshHint();
        Play(confirmFeedback);
    }

    private void CloseDialog(bool sound)
    {
        mode = Mode.List;
        if (dialogMotion != null) dialogMotion.Close();
        RefreshHint();
        if (sound) Play(cancelFeedback);
    }

    private void ConfirmDelete()
    {
        bool deleted = DataManager.Instance.DeleteSlot(selected);
        Play(deleted ? (deleteFeedback != null ? deleteFeedback : confirmFeedback) : (deniedFeedback != null ? deniedFeedback : cancelFeedback));
        ReadSummaries();
        onDelete = false;
        CloseDialog(false);
        Refresh(false);
    }

    private void SetDialogIndex(int index)
    {
        if (dialogIndex == index) return;
        dialogIndex = index;
        RefreshDialog(false);
        Play(moveFeedback);
    }

    private void Update()
    {
        // 읽기 오류 안내는 잠시 뒤 평소 안내로 돌아간다.
        if (deniedUntil > 0f && Time.unscaledTime >= deniedUntil)
        {
            deniedUntil = 0f;
            RefreshHint();
        }
    }

    #region 표시

    private void Refresh(bool instant)
    {
        int used = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (summaries[i].exists) used++;
            Fill(slots[i], summaries[i]);
            Highlight(slots[i], summaries[i], i == selected, i == selected && onDelete, instant);
        }
        if (counter != null)
            counter.text = "세이브  " + used.ToString(CultureInfo.InvariantCulture) + " / " + slots.Length.ToString(CultureInfo.InvariantCulture);
        RefreshHint();
    }

    private void Fill(SlotView view, SaveSlotSummary summary)
    {
        if (view.ordinal != null) view.ordinal.text = (summary.slot + 1).ToString("00", CultureInfo.InvariantCulture);
        bool readable = summary.exists && !summary.corrupted;
        string tag, title, detail;
        Color accent;
        if (!summary.exists)
        {
            tag = "EMPTY";
            title = "비어 있음";
            detail = "새 세이브를 만들어 처음부터 시작합니다";
            accent = TrainingBoardStyle.LineDim;
        }
        else if (summary.corrupted)
        {
            tag = "ERROR";
            title = "읽을 수 없는 세이브";
            detail = "파일이 손상되었습니다. 삭제하면 이 칸을 다시 쓸 수 있습니다";
            accent = TrainingBoardStyle.Warning;
        }
        else if (summary.isFirstStart)
        {
            tag = "FIRST START";
            title = "최초 시작 전";
            detail = "만든 날 " + Date(summary.lastPlayedLocal) + "   ·   아직 훈련소에 도착하지 않았습니다";
            accent = TrainingBoardStyle.Gold;
        }
        else
        {
            tag = "CONTINUE";
            title = "훈련 진행 중";
            detail = "마지막 접속 " + Date(summary.lastPlayedLocal) + "   ·   플레이 " + PlayTime(summary.playTimeSeconds)
                + "   ·   " + summary.gold.ToString("N0", CultureInfo.InvariantCulture) + " G";
            accent = TrainingBoardStyle.Line;
        }
        if (view.tag != null) { view.tag.text = tag; view.tag.color = accent; }
        if (view.title != null) view.title.text = title;
        if (view.detail != null) view.detail.text = detail;
        if (view.accent != null) view.accent.color = accent;

        if (view.trainingGroup != null) view.trainingGroup.SetActive(readable);
        if (view.emptyGroup != null) view.emptyGroup.SetActive(!readable);
        if (view.emptyLabel != null)
        {
            view.emptyLabel.text = summary.corrupted ? "삭제 필요" : "+  NEW SAVE";
            view.emptyLabel.color = summary.corrupted ? TrainingBoardStyle.Warning : TrainingBoardStyle.LineDim;
        }
        if (view.deleteButton != null) view.deleteButton.gameObject.SetActive(summary.exists);

        if (!readable) return;
        for (int i = 0; i < view.badges.Length; i++)
        {
            var badge = view.badges[i];
            bool used = i < trainings.Length && trainings[i] != null;
            if (badge.root != null) badge.root.SetActive(used);
            if (!used) continue;
            var record = summary.FindCleared(trainings[i].trainingId);
            if (badge.nameLabel != null)
                badge.nameLabel.text = i < trainingLabels.Length && !string.IsNullOrEmpty(trainingLabels[i]) ? trainingLabels[i] : trainings[i].displayName;
            Color color = record != null ? TrainingBoardStyle.GradeColor(record.bestGrade) : TrainingBoardStyle.LineDim;
            if (badge.gradeLabel != null)
            {
                badge.gradeLabel.text = record != null ? TrainingBoardStyle.GradeLabel(record.bestGrade) : "미수료";
                badge.gradeLabel.color = record != null ? color : Sub;
            }
            if (badge.bar != null) badge.bar.color = color;
        }
    }

    private void Highlight(SlotView view, SaveSlotSummary summary, bool isSelected, bool deleteFocused, bool instant)
    {
        bool body = isSelected && !deleteFocused;
        if (view.focus != null) view.focus.color = new Color(1f, 1f, 1f, body ? 1f : deleteFocused ? .35f : 0f);
        if (view.ordinal != null)
            view.ordinal.color = isSelected ? TrainingBoardStyle.LineBright : summary.exists ? TrainingBoardStyle.Line : TrainingBoardStyle.LineDim;
        if (view.title != null) view.title.color = isSelected ? Color.white : summary.exists ? Body : Sub;
        if (view.detail != null) view.detail.color = Sub;
        if (view.deleteFocus != null) view.deleteFocus.color = new Color(1f, 1f, 1f, deleteFocused ? 1f : 0f);
        if (view.deleteLabel != null) view.deleteLabel.color = deleteFocused ? Color.white : TrainingBoardStyle.Warning;
        if (view.root != null)
        {
            // 넓은 칸이라 조금만 키운다(버튼의 1.15배를 그대로 쓰면 판 밖으로 넘친다).
            float scale = isSelected ? 1.012f : 1f;
            view.root.DOKill();
            if (instant) view.root.localScale = Vector3.one * scale;
            else view.root.DOScale(scale, .16f).SetEase(Ease.OutCubic).SetUpdate(true);
        }
    }

    private void RefreshDialog(bool instant)
    {
        if (dialogCancelFocus != null) dialogCancelFocus.color = new Color(1f, 1f, 1f, dialogIndex == 0 ? 1f : 0f);
        if (dialogDeleteFocus != null) dialogDeleteFocus.color = new Color(1f, 1f, 1f, dialogIndex == 1 ? 1f : 0f);
        if (dialogCancelLabel != null) dialogCancelLabel.color = dialogIndex == 0 ? Color.white : Body;
        if (dialogDeleteLabel != null) dialogDeleteLabel.color = dialogIndex == 1 ? Color.white : TrainingBoardStyle.Warning;
        Scale(dialogCancel, dialogIndex == 0, instant);
        Scale(dialogDelete, dialogIndex == 1, instant);
    }

    private static void Scale(UnityEngine.UI.Button button, bool on, bool instant)
    {
        if (button == null) return;
        var rect = (RectTransform)button.transform;
        rect.DOKill();
        float scale = on ? 1.08f : 1f;
        if (instant) rect.localScale = Vector3.one * scale;
        else rect.DOScale(scale, .16f).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    private void RefreshHint()
    {
        if (hint == null) return;
        bool denied = deniedUntil > 0f;
        hint.color = denied ? TrainingBoardStyle.Warning : Sub;
        if (denied) hint.text = "읽을 수 없는 세이브입니다.  → 로 삭제 버튼을 골라 지운 뒤 사용하세요.";
        else if (mode == Mode.Dialog) hint.text = DialogHint;
        else if (onDelete) hint.text = DeleteHint;
        else hint.text = summaries.Length > selected && summaries[selected].exists ? ListHint : EmptyHint;
    }

    private static string Date(DateTime local) =>
        local == DateTime.MinValue ? "기록 없음" : local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string PlayTime(float seconds)
    {
        int minutes = Mathf.Max(0, Mathf.FloorToInt(seconds / 60f));
        return minutes >= 60
            ? (minutes / 60).ToString(CultureInfo.InvariantCulture) + "시간 " + (minutes % 60).ToString(CultureInfo.InvariantCulture) + "분"
            : minutes.ToString(CultureInfo.InvariantCulture) + "분";
    }

    #endregion

    private static void Play(FeedbackDataSO feedback)
    {
        if (feedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.PlayExact(feedback, Vector3.zero, Quaternion.identity);
    }

    private void OnDisable()
    {
        foreach (var view in slots)
            if (view != null && view.root != null) { view.root.DOKill(); view.root.localScale = Vector3.one; }
        if (dialogCancel != null) { dialogCancel.transform.DOKill(); dialogCancel.transform.localScale = Vector3.one; }
        if (dialogDelete != null) { dialogDelete.transform.DOKill(); dialogDelete.transform.localScale = Vector3.one; }
        if (IsActive) Release();
    }
}
