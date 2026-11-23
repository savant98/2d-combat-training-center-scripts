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
    private Vector2 textOrigin;
    private float inkCenterX, inkTop, inkBottom;
    private Sequence seq;
    // 표시가 끝나면 풀로 돌려보내는 콜백. 피격마다 람다를 새로 만들지 않도록 Awake에서 한 번만 만든다.
    private TweenCallback returnToPool;
   
    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        txt = GetComponentInChildren<TextMeshProUGUI>();
        group = GetComponent<CanvasGroup>();
        textOrigin = txt.rectTransform.anchoredPosition;
        returnToPool = ReturnToPool;
    }

    public void Show(WeaponBaseSO.DamageInfo damageInfo, Color? overrideColor = null)
    {
        FloatingTextColor baseColor;
        baseColor.topColor = overrideColor ?? normalColor.topColor;
        baseColor.bottomColor = overrideColor ?? normalColor.bottomColor;

        txt.fontSize = damageInfo.damageType == DamageType.Normal ? normalSize : skillSize;
        txt.text = Mathf.RoundToInt(damageInfo.damage).ToString();

        // 초기 투명도와 스케일
        txt.alpha = 1f;
        txt.rectTransform.anchoredPosition = textOrigin;
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
        float punch = damageInfo.damageType == DamageType.Normal ? punchScale : 2f * punchScale;
        if (damageInfo.popupGroup != 0 && !damageInfo.popupAnchorOnly)
            punch = Mathf.Min(punch, .15f);
        seq.Append(transform.DOPunchScale(punch * Vector3.one, punchDuration).SetEase(Ease.OutQuad));

        // 위로 이동
        seq.Append(transform.DOMoveY(transform.position.y + moveUpDistance, lifeTime)
            .SetEase(Ease.OutQuad));

        // Fade Out
        seq.Join(txt.DOFade(0f, lifeTime));

        seq.OnComplete(returnToPool);
    }

    internal void MeasureStackInk()
    {
        txt.ForceMeshUpdate();
        inkCenterX = txt.textBounds.center.x;
        inkBottom = float.PositiveInfinity;
        inkTop = float.NegativeInfinity;
        for (int i = 0; i < txt.textInfo.characterCount; i++)
        {
            var character = txt.textInfo.characterInfo[i];
            if (!character.isVisible) continue;

            var metrics = character.textElement.glyph.metrics;
            float top = character.baseLine + metrics.horizontalBearingY * character.scale;
            inkTop = Mathf.Max(inkTop, top);
            inkBottom = Mathf.Min(inkBottom, top - metrics.height * character.scale);
        }
        if (inkBottom > inkTop) inkBottom = inkTop = 0f;
    }

    internal float StackRise => rect.TransformVector(txt.rectTransform.anchoredPosition - textOrigin).y;

    internal void PlaceAbove(FloatingText previous)
    {
        var previousRect = previous.txt.rectTransform;
        var currentRect = txt.rectTransform;
        var previousTop = previousRect.TransformPoint(previous.inkCenterX, previous.inkTop, 0f);
        var currentBottom = currentRect.TransformPoint(inkCenterX, inkBottom, 0f);

        // 실제 두 숫자 중 작은 쪽 높이의 10%만 겹쳐 촘촘하게 쌓는다.
        float previousHeight = previousRect.TransformVector(0f, previous.inkTop - previous.inkBottom, 0f).y;
        float currentHeight = currentRect.TransformVector(0f, inkTop - inkBottom, 0f).y;
        float overlap = .1f * Mathf.Min(previousHeight, currentHeight);
        currentRect.position += previousTop - currentBottom - Vector3.up * overlap;
    }

    // 기존에는 끝나면 비활성화만 하고 풀에 돌려보내지 않아(Return 호출처 0곳) 풀 20개를 다 쓴 뒤
    // 피격마다 새로 Instantiate했다. 이제 FloatingTextManager.Return이 비활성화와 풀 반환을 함께 한다.
    // 다시 꺼낼 때는 Show가 글자·크기·색·투명도·배율을, 매니저가 위치를 다시 정하므로 표시와 수명은 같다.
    private void ReturnToPool()
    {
        seq = null;
        var manager = FloatingTextManager.Instance;
        if (manager != null)
            manager.Return(this);
        else
            gameObject.SetActive(false);
    }
}
