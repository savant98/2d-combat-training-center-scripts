using TMPro;
using UnityEngine;
using DG.Tweening;
using System;

public class FloatingText : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float lifeTime = 1f;
    [SerializeField] private float moveUpDistance = 1f;
    [SerializeField] private float punchScale = 1.25f;
    [SerializeField] private float punchDuration = 0.2f;
    [SerializeField] private int normalSize = 40;
    [SerializeField] private int skillSize = 80;
    [SerializeField] private FloatingTextColor normalColor;
    [SerializeField] private FloatingTextColor skillColor;

    [HideInInspector] public RectTransform rect;
    [HideInInspector] public TextMeshProUGUI txt;
    [HideInInspector] public CanvasGroup group;

    [Serializable]
    private struct FloatingTextColor
    {
        public Color topColor;
        public Color bottomColor;
    }
    private Sequence seq;
   
    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        txt = GetComponentInChildren<TextMeshProUGUI>();
        group = GetComponent<CanvasGroup>();
    }

    public void Show(WeaponBase.DamageInfo damageInfo, Color? overrideColor = null)
    {
        FloatingTextColor baseColor;
        baseColor.topColor = overrideColor ?? normalColor.topColor;
        baseColor.bottomColor = overrideColor ?? normalColor.bottomColor;

        txt.fontSize = damageInfo.damageType == DamageType.Normal ? normalSize : skillSize;
        txt.text = Mathf.RoundToInt(damageInfo.damage).ToString();

        // 초기 투명도와 스케일
        txt.alpha = 1f;
        transform.localScale = Vector3.one;

        // Vertical Gradient 초기 설정
        txt.enableVertexGradient = true;

        // Skill : 더 겅한 색 / Normal : 기본 색
        Color topColor = damageInfo.damageType == DamageType.Skill ? skillColor.topColor : baseColor.topColor;
        Color bottomColor = damageInfo.damageType == DamageType.Skill ? skillColor.bottomColor : baseColor.bottomColor;

        // TMP 그라데이션 구조 생성
        var grad = txt.colorGradient;
        grad.topLeft = grad.topRight = topColor;
        grad.bottomLeft = grad.bottomRight = bottomColor;
        txt.colorGradient = grad;

        // DOTween Sequence 생성
        seq?.Kill();
        seq = DOTween.Sequence();

        // 스폰 시 Scale Punch
        seq.Append(transform.DOPunchScale(damageInfo.damageType == DamageType.Normal 
            ? punchScale * Vector3.one 
            : 2f * punchScale * Vector3.one, punchDuration).SetEase(Ease.OutQuad));

        // 위로 이동
        seq.Append(transform.DOMoveY(transform.position.y + moveUpDistance, lifeTime)
            .SetEase(Ease.OutQuad));

        // Fade Out
        seq.Join(txt.DOFade(0f, lifeTime));

        seq.OnComplete(() =>
        {
            gameObject.SetActive(false);
        });
    }
}
