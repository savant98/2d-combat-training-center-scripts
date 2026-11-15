using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EnemyUI : MonoBehaviour
{
    [Header("Shield UI")]
    [SerializeField] private CanvasGroup shieldGroup;
    [SerializeField] private Image shieldBar;

    [Header("HP UI")]
    [SerializeField] private CanvasGroup hpGroup;
    [SerializeField] private Image hpBar;
    [SerializeField] private Image subHpBar;
    [SerializeField] private Image subHpBarFadeImage;

    [Header("Messages")]
    [SerializeField] private CanvasGroup breakNowGroup;
    [SerializeField] private CanvasGroup knockDownGroup;
    [SerializeField] private CanvasGroup bossMessageGroup;
    [Tooltip("보스 등장 이름표의 글자. EnemyController.DisplayName(표준 이름)으로 채운다.")]
    [SerializeField] private TMP_Text bossNameText;
    [Tooltip("브레이크 가능 아이콘 둘레의 남은 시간 고리(Filled). 브레이크 가능 시간 동안 1에서 0으로 줄어든다.")]
    [SerializeField] private Image breakTimerFill;

    private Tween breakTimerTween;

    private Tween hideShieldTween;
    private Tween hideHPTween;
    // fillAmount 트윈은 대상이 Image라 transform.DOKill로 꺼지지 않아 쉴드 재생 중 매 프레임 쌓였다.
    // 막대마다 최근 트윈만 추적해 오래된 것을 끈다(FillTweenSlot 설명 참고).
    private FillTweenSlot shieldFill;
    private FillTweenSlot hpFill;
    private FillTweenSlot subHpFill;
    private FillTweenSlot subHpFadeFill;
    private void Awake()
    {
        GetComponent<Canvas>().worldCamera = Camera.main;
        DisableUnusedRaycaster();

        hpGroup.alpha = 0;

        // ShieldSystem없으면 계산안함
        if (shieldBar == null)
            return;

        shieldGroup.alpha = 0;
        breakNowGroup.alpha = 0;
        knockDownGroup.alpha = 0;

        //Break시 효과
        // 무한 반복 트윈은 풀에서 비활성으로 대기하는 동안 멈추고 다시 켜지면 이어서 돈다(SetLink).
        // 비활성 개체는 그려지지 않으므로 화면은 같고, 대기 중인 적의 트윈 갱신 비용만 없앤다.
        breakNowGroup.transform.DOScale(Vector3.one * 1.1f, 0.5f).SetLoops(-1, LoopType.Yoyo)
            .SetLink(gameObject, LinkBehaviour.PauseOnDisablePlayOnEnable);

        Transform[] childrenObjs = knockDownGroup.transform.GetComponentsInChildren<Transform>();
        foreach (Transform t in childrenObjs)
        {
            t.DOShakePosition(
                duration: 0.2f, 
                strength:  new Vector3(
                    Random.Range(-5f, 5f),
                    Random.Range(-5f, 5f),
                    0f),
                vibrato: Random.Range(5, 10),
                randomness: 90
                )
                .SetRelative()
                .SetLoops(-1, LoopType.Yoyo)
                .SetDelay(Random.Range(0f, 3f))
                .SetLink(gameObject, LinkBehaviour.PauseOnDisablePlayOnEnable);
        }
    }
    public void ShowShield(float normalized, bool isBoss)
    {
        shieldBar.transform.DOKill();

        shieldFill.Push(shieldBar.DOFillAmount(normalized, 0.2f));

        // Scale Punch 적용
        shieldBar.rectTransform.localScale = Vector3.one;

        shieldBar.rectTransform
            .DOPunchScale(new Vector3(0.2f, 0.2f, 0), 0.2f, 8, 0.5f)
            .SetEase(Ease.OutQuad);

        // UI 즉시 표시
        shieldGroup.alpha = 1;

        // 기존 hideTween 중지
        hideShieldTween?.Kill();

        // 1초 후 서서히 사라짐
        hideShieldTween = shieldGroup.DOFade(0, 0.5f).SetDelay(1f);

        // BossUI 처리
        if (isBoss)
        {
            UIManager.Instance.UpdateBossShield(normalized);
        }
    }
    public void ShowHP(float normalized, bool isBoss)
    {
        hpBar.transform.DOKill();
        subHpBar.transform.DOKill();
        subHpBarFadeImage.transform.DOKill();

        // UI 즉시 표시
        hpGroup.alpha = 1;

        subHpBar.fillAmount = hpBar.fillAmount;

        hpFill.Push(hpBar.DOFillAmount(normalized, 0.2f));
        subHpFill.Push(subHpBar.DOFillAmount(normalized, 0.5f).SetDelay(0.5f));
        subHpFadeFill.Push(subHpBarFadeImage.DOFillAmount(normalized, 0.2f));

        // 기존 hideTween 중지
        hideHPTween?.Kill();

        // 1초 후 서서히 사라짐
        hideHPTween = hpGroup.DOFade(0, 0f).SetDelay(1f);

        // BossUI 처리
        if (isBoss)
        {
            UIManager.Instance.UpdateBossHP(normalized);
        }
    }
    public void ShowBreakable() => ShowBreakable(0f);
    // duration이 있으면 남은 시간 고리가 그동안 줄어든다.
    public void ShowBreakable(float duration)
    {
        breakNowGroup.alpha = 1f;
        knockDownGroup.alpha = 0f;

        if (breakTimerFill == null)
            return;
        breakTimerTween?.Kill();
        breakTimerFill.fillAmount = 1f;
        if (duration > 0f)
            breakTimerTween = breakTimerFill.DOFillAmount(0f, duration).SetEase(Ease.Linear);
    }
    public void ShowKnockDown()
    {
        knockDownGroup.alpha = 1f;
        breakNowGroup.alpha = 0f;
    }
    public void HideBreakable()
    {
        breakNowGroup.DOFade(0, 0.3f);
    }
    
    public void HideKnockDown()
    {
        knockDownGroup.DOFade(0, 0.3f);
    }
    public void ResetEnemyHUD()
    {
        RefreshBossName();
        breakTimerTween?.Kill();
        if (breakTimerFill != null) breakTimerFill.fillAmount = 1f;

        hpBar.transform.DOKill();
        subHpBar.transform.DOKill();
        subHpBarFadeImage.transform.DOKill();

        hpBar.fillAmount = 1;
        subHpBar.fillAmount = 1;
        subHpBarFadeImage.fillAmount = 1;

        if (shieldBar == null)
            return;

        knockDownGroup.alpha = 0;
        breakNowGroup.alpha = 0;
        shieldBar.fillAmount = 1;

        if (bossMessageGroup != null)
            ShowBossMessage();
    }
    // 이름표의 글자를 적의 표준 이름으로 맞춘다(Prefab에 남은 옛 글자를 덮는다).
    private void RefreshBossName()
    {
        if (bossNameText == null)
            return;
        var owner = GetComponentInParent<EnemyController>();
        if (owner != null)
            bossNameText.text = owner.DisplayName;
    }
    private void ShowBossMessage()
    {
        bossMessageGroup.DOFade(1f, 1f).OnComplete(() => bossMessageGroup.DOFade(0f, 1f).SetDelay(2f));
    }
    // 적 HUD에는 클릭·드래그를 받는 요소가 없는데 GraphicRaycaster가 켜져 있어, 마우스가 있으면
    // EventSystem이 매 프레임 이 Canvas의 Graphic을 레이캐스트했다. 이 Canvas 아래와 위(포인터 이벤트가
    // 전달되는 부모 경로)에 이벤트를 받는 컴포넌트가 없을 때만 끈다. 그리기에는 영향이 없다.
    private void DisableUnusedRaycaster()
    {
        var raycaster = GetComponent<GraphicRaycaster>();
        if (raycaster == null || !raycaster.enabled)
            return;
        if (GetComponentInChildren<UnityEngine.EventSystems.IEventSystemHandler>(true) != null
            || GetComponentInParent<UnityEngine.EventSystems.IEventSystemHandler>(true) != null)
            return;
        raycaster.enabled = false;
    }

    // 같은 Image의 fillAmount 트윈 정리. 기존에는 트윈이 생성 순서대로 갱신되어 화면 값은 가장 새 트윈이
    // 정하고, 새 트윈의 시작값은 시작하는 프레임에 직전 트윈이 막 쓴 값이었다. 그래서 직전 트윈이 이미
    // 시작했다면(IsInitialized) 그보다 오래된 트윈은 이후 값에 영향이 없으므로 complete 없이 끈다.
    // 직전 트윈은 새 트윈의 시작값을 정하므로 남긴다(ResetEnemyHUD 같은 외부 쓰기가 그 사이 있어도 기존처럼 덮인다).
    // 화면 값은 기존과 같다. timeScale이 0보다 크면 지연 없는 막대(쉴드·HP·페이드)를 매 프레임 호출해도
    // 살아 있는 채우기 트윈이 막대당 2개 이하로 유지된다. 일시정지(timeScale 0) 중에는 DOTween이 Startup 전에
    // 갱신을 건너뛰어 직전 트윈이 시작되지 않으므로 기존처럼 쌓이고, 재개 뒤 0.2초 안에 끝난다.
    // 지연 0.5초인 subHpBar도 지연 중 연타에는 상한이 없다(모두 기존과 같은 동작).
    // 주의: 변경 가능한 struct라 클래스 필드에서 직접 Push를 호출해야 한다. 필드를 readonly로 바꾸거나
    // 지역 변수·프로퍼티로 복사해 호출하면 복사본만 바뀌어 경고 없이 효과가 사라진다.
    private struct FillTweenSlot
    {
        private Tween latest;
        private Tween previous;

        public void Push(Tween next)
        {
            // 직전 트윈이 아직 시작 전(지연 중 포함)이면 그 앞 트윈이 시작값을 정하므로 끄지 않고 추적만 넘긴다.
            bool canKillPrevious = latest == null || !latest.IsActive() || latest.IsInitialized();
            if (canKillPrevious && previous != null && previous.IsActive())
                previous.Kill();
            previous = latest;
            latest = next;
        }
    }
}
