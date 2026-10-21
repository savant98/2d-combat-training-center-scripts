using DG.Tweening;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class StageHUD : MonoBehaviour
{
    [Header("BossHUD")]
    [SerializeField] private Image hpBar;
    [SerializeField] private Image subHpBar;
    [SerializeField] private Image subHpBarFadeImage;
    [SerializeField] private Image shieldBar;
    [SerializeField] private CanvasGroup hpGroup;

    [Header("Warning Effect")]
    [SerializeField] private GameObject warningEffect;

    //private Tween hideHPTween;
    public void ShowWarningLine()
    {
        warningEffect.SetActive(false);
        warningEffect.SetActive(true);
    }
    public void ShowBossHP()
    {
        hpGroup.DOFade(1f, 0.5f);
        UpdateBossHP(1f);
        UpdateBossShield(1f);
    }
    public void HideBossHP()
    {
        hpGroup.DOFade(0f, 0.5f);
    }
    public void UpdateBossHP(float normalized)
    {
        hpBar.transform.DOKill();
        subHpBar.transform.DOKill();
        subHpBarFadeImage.transform.DOKill();

        hpBar.DOFillAmount(normalized, 0.2f);
        subHpBar.DOFillAmount(normalized, 0.5f).SetDelay(0.5f);
        subHpBarFadeImage.DOFillAmount(normalized, 0.2f);
    }
    public void UpdateBossShield(float normalized)
    {
        shieldBar.transform.DOKill();

        shieldBar.DOFillAmount(normalized, 0.2f);
    }
}
