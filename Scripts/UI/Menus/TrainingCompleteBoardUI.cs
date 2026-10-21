using UnityEngine;
using TMPro;
using System.Collections;

public class TrainingCompleteBoardUI : MonoBehaviour, IUIInputReceiver
{
    [Header("TrainincCompleteBoard Component")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI hitText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI gradeText;
    [SerializeField] private TextMeshProUGUI goldText;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string openParameter = "Open";
    [SerializeField] private string closeParameter = "Close";

    [Header("Gold Count Effect")]
    [SerializeField] private float goldAnimationDuration = 0.5f;

    private int startGold;
    private int targetGold;
    private int earnedGoldAmount;
    private Coroutine goldAnimCoroutine;

    public bool IsActive => panelRoot != null && panelRoot.activeSelf;

    // UI가 현재 열려있는지 여부 (Room의 코루틴 대기 조건으로 사용)
    public bool IsOpen { get; private set; }

    public void ShowResult(RunResult result)
    {
        panelRoot.SetActive(true);
        IsOpen = true;
        animator.SetBool(openParameter, true);
        animator.SetBool(closeParameter, false);

        if (hitText != null)
            hitText.text = result.hitCount.ToString();

        if (timeText != null)
            timeText.text = TrainingScoreFormatHelper.FormatTime(result.timeSeconds);

        if (gradeText != null)
            TrainingScoreFormatHelper.GradeToText(result.grade, gradeText);

        if (goldText != null)
        {
            // 골드 역산 및 초기 상태 세팅
            targetGold = DataManager.Instance.CurrentGold;
            earnedGoldAmount = result.earnedGold;
            startGold = targetGold - earnedGoldAmount;

            goldText.text = $"x{startGold}";

            PlayGoldAnimation();
        }

        // UI 입력 계층 등록 (키보드/패드 입력 및 ESC, Confirm 등 제어)
        UIManager.Instance.PushInputTarget(this, UIInputLayer.System);
    }

    /// <summary>
    /// Gold Animation Event 
    /// </summary>
    private void PlayGoldAnimation()
    {
        // 획득한 골드가 없다면 생략
        if (earnedGoldAmount <= 0)
            return;

        if (goldAnimCoroutine != null)
            StopCoroutine(goldAnimCoroutine);

        goldAnimCoroutine = StartCoroutine(GoldCountAnimationCo());
    }

    private IEnumerator GoldCountAnimationCo()
    {
        yield return new WaitForSeconds(3.5f);
        goldText.text = $"x{startGold} (+{earnedGoldAmount})";

        yield return new WaitForSeconds(0.5f);

        float elapsed = 0f;

        // 정확히 goldAnimationDuration(0.5초) 동안만 실행
        while (elapsed < goldAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime; // UI 연출이므로 unscaled 시간 사용 권장

            // 0.0 ~ 1.0 사이의 진행률 (0초 -> 0%, 0.5초 -> 100%)
            float t = Mathf.Clamp01(elapsed / goldAnimationDuration);

            // 현재 보여줄 내 골드 (50 -> 100)
            int currentDisplayGold = Mathf.RoundToInt(Mathf.Lerp(startGold, targetGold, t));

            // 현재 줄어드는 보너스 골드 (50 -> 0)
            int currentDisplayEarned = Mathf.RoundToInt(Mathf.Lerp(earnedGoldAmount, 0, t));

            if (goldText != null)
            {
                goldText.text = $"x{currentDisplayGold} (+{currentDisplayEarned})";
            }

            yield return null;
        }

        // 연출 종료 시 목표값 정확히 보정 및 (+0) 표기
        if (goldText != null)
        {
            goldText.text = $"x{targetGold} (+0)";
        }
    }

    public void Hide()
    {
        if (!IsOpen) return;

        IsOpen = false;
        animator.SetBool(openParameter, false);
        animator.SetBool(closeParameter, true);

        // UI가 닫힐 때 혹시 돌고 있는 코루틴이 있다면 안전하게 정지
        if (goldAnimCoroutine != null)
        {
            StopCoroutine(goldAnimCoroutine);
            goldAnimCoroutine = null;
        }

        // UI 입력 계층 해제
        UIManager.Instance.RemoveInputTarget(this);
    }

    #region IUIInputReceiver 구현 (Confirm / Cancel / UI 버튼 확장성)

    public void OnConfirm()
    {
        // 확인 키(Jump/Space/A버튼 등) 누르면 UI 닫기
        Hide();
    }

    public void OnCancel()
    {
        // 취소 키(Attack/ESC/B버튼 등) 누르면 UI 닫기
        Hide();
    }

    public void OnMove(Vector2 dir)
    {
        // 추후 버튼 선택/이동이 필요해지면 여기서 구현
    }

    #endregion
}