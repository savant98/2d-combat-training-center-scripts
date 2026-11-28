using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Gear 기술의 기본 효과와 해당 무장의 특성 연계를 상세 화면에 표시한다.</summary>
[RequireComponent(typeof(RectTransform))]
public sealed class WeaponGuidePanel : MonoBehaviour
{
    [SerializeField] private Graphic stageAccent;
    [SerializeField] private TextMeshProUGUI summary, synergy;
    [SerializeField] private GameObject synergyGroup;

    public void Show(WeaponBaseSO weapon, Color color)
    {
        var guide = ArmamentMenuGuide.Gear(weapon);
        string effect = string.IsNullOrWhiteSpace(guide.summary) ? weapon.unlockInfo.description : guide.summary;
        summary.text = string.IsNullOrWhiteSpace(effect) ? "등록된 기술 설명이 없습니다." : UpgradeMenuPage.EvenLines(effect);
        synergy.text = UpgradeMenuPage.EvenLines(guide.synergy);
        synergyGroup.SetActive(!string.IsNullOrWhiteSpace(guide.synergy));
        stageAccent.color = color;
    }
}
