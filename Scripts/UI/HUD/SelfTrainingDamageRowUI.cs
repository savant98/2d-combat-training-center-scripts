using TMPro;
using UnityEngine;

/// <summary>무기·스킬별 실제 적중 피해와 전체 피해 비율을 표시한다.</summary>
public sealed class SelfTrainingDamageRowUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sourceText, damageText, ratioText, dpsText;
    [SerializeField] private TextMeshProUGUI categoryText;
    [SerializeField] private UnityEngine.UI.Image sourceIcon;
    [SerializeField] private UnityEngine.UI.Image ratioFill;
    public void Show(SelfTrainingDamageRow row)
    {
        if (sourceText != null) sourceText.text = row.Name;
        if (categoryText != null)
        {
            categoryText.text = row.DamageType == DamageType.Skill ? "기어 스킬" : "일반 공격";
            categoryText.color = row.DamageType == DamageType.Skill ? new Color(.94f, .75f, .42f) : new Color(.33f, .75f, .95f);
        }
        if (sourceIcon != null)
        {
            sourceIcon.sprite = row.Weapon != null ? row.Weapon.weaponSprite : null;
            sourceIcon.enabled = sourceIcon.sprite != null;
        }
        if (damageText != null) damageText.text = row.Damage.ToString("N1");
        if (ratioText != null) ratioText.text = (row.Ratio * 100f).ToString("0.0") + "%";
        if (dpsText != null) dpsText.text = row.Dps.ToString("N1");
        if (ratioFill != null) ratioFill.fillAmount = Mathf.Clamp01(row.Ratio);
    }
}
