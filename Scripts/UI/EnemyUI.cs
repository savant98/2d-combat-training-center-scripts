using DG.Tweening;
using Unity.VisualScripting;
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

    private Tween hideShieldTween;
    private Tween hideHPTween;
    private void Awake()
    {
        GetComponent<Canvas>().worldCamera = Camera.main;

        hpGroup.alpha = 0;

        // ShieldSystem없으면 계산안함
        if (shieldBar == null)
            return;

        shieldGroup.alpha = 0;
        breakNowGroup.alpha = 0;
        knockDownGroup.alpha = 0;

        //Break시 효과
        breakNowGroup.transform.DOScale(Vector3.one * 1.1f, 0.5f).SetLoops(-1, LoopType.Yoyo);

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
                .SetDelay(Random.Range(0f, 3f));
        }
    }
    public void ShowShield(float normalized, bool isBoss)
    {
        shieldBar.transform.DOKill();

        shieldBar.DOFillAmount(normalized, 0.2f);

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

        hpBar.DOFillAmount(normalized, 0.2f);
        subHpBar.DOFillAmount(normalized, 0.5f).SetDelay(0.5f);
        subHpBarFadeImage.DOFillAmount(normalized, 0.2f);

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
    public void ShowBreakable()
    {
        breakNowGroup.alpha = 1f;
        knockDownGroup.alpha = 0f;
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
    private void ShowBossMessage()
    {
        bossMessageGroup.DOFade(1f, 1f).OnComplete(() => bossMessageGroup.DOFade(0f, 1f).SetDelay(2f));
    }
}
