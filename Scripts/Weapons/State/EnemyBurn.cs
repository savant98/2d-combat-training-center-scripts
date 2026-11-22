using TMPro;
using UnityEngine;

// 화상(Rifle 무장 특성). 작열탄 적중과 기관총 3번째 유효 적중에서 1스택씩 쌓인다(최대 Capacity).
// 2026-11-21 사용자 결정·지시:
// - TickInterval(0.5초)마다 '스택 × DamagePerStack' 피해를 준다. 첫 틱은 첫 부여 0.5초 뒤다.
// - 마지막 부여 Duration(5초) 뒤에 1스택이 꺼지고, 그 뒤로도 5초마다 1스택씩 꺼진다(5→4에 5초가 걸리듯 4→3, 3→2…도 5초씩).
//   다시 부여하면 1스택이 늘고(최대 Capacity) 다음 감소까지 5초가 새로 시작된다(2026-11-21 사용자 지시 3차).
// - 핸드캐논 직격은 Detonate로 스택을 모두 소모해 그 수만큼 연속 폭발한다(BurnDetonation).
// - 표시: 몸통 위에 '불꽃 x 스택 수'. 불꽃은 무장 특성 아이콘과 같은 셰이더(Player/TraitGauge, _Kind 6 — 어두운 몸통 + 얇은 열색 발광선 +
//   스택만큼 커지는 안쪽 불꽃 심과 불씨)로 그리고, 글자는 피해 숫자와 같은 글꼴(WeaponTraitFeedbackSO의 burnFont·burnTextMaterial).
//   수가 바뀌면 살짝 튀고, 다음 스택이 곧 꺼질 때(남은 0.6초) 불꽃 윤곽과 글자가 깜빡여 줄어들 것을 알린다.
public sealed class EnemyBurn : MonoBehaviour
{
    public const int Capacity = 5;
    public const float TickInterval = 0.5f;
    public const float Duration = 5f;
    public const float DamagePerStack = 1f;
    private const float BadgeGap = 0.42f;       // 몸통 윗변에서 표시 가운데까지(칸)
    private const float IconUnit = 0.21f;       // 아이콘 좌표 1 = 0.21칸(불꽃 높이 약 0.4칸)
    private static readonly Vector4 IconFrame = new(0f, 0.15f, 0.85f, 1.15f);   // 불꽃·불씨가 들어가는 아이콘 좌표 범위(가운데, 반너비)
    private const float TextSize = 3.2f;        // TextMeshPro 글자 크기(약 0.32칸 높이)
    private const float PopTime = 0.18f;
    private const float WarnTime = 0.6f;
    private static readonly int KindId = Shader.PropertyToID("_Kind");
    private static readonly int StateAId = Shader.PropertyToID("_StateA");
    private static readonly int FrameId = Shader.PropertyToID("_Frame");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");

    private EnemyHealth health;
    private Collider2D body;
    private float nextTick;
    private float decayAt;   // 다음 1스택이 꺼지는 시각
    private int count;
    private Transform badge;
    private SpriteRenderer icon;
    private TextMeshPro label;
    private MaterialPropertyBlock block;
    private int shownCount = -1;
    private float popAge = 1f, changeTime = -10f;

    // 감소 시각이 지났는데 이 컴포넌트의 Update가 아직 돌지 않은 프레임에도(탄 충돌이 먼저 처리되는 경우) 줄어든 값으로 읽힌다.
    public int Stacks => Mathf.Max(0, count - DueDecays(Time.time));
    /// <summary>모두 꺼지기까지 남은 시간(스택이 없으면 0).</summary>
    public float Remaining => Stacks > 0 ? Mathf.Max(0f, decayAt - Time.time) + (count - 1) * Duration : 0f;
    /// <summary>다음 스택 하나가 꺼지기까지 남은 시간(스택이 없으면 0).</summary>
    public float NextExpiry => Stacks > 0 ? Mathf.Max(0f, decayAt - Time.time) : 0f;

    // now까지 처리되지 않은 감소 횟수(decayAt부터 Duration마다 1번)
    private int DueDecays(float now) => count > 0 && now >= decayAt ? 1 + Mathf.FloorToInt((now - decayAt) / Duration) : 0;
    /// <summary>표시 중인 스택 수 글자(검증용).</summary>
    public string BadgeText => label != null && label.enabled && shownCount > 0 ? "x " + shownCount : string.Empty;

    /// <summary>화상 1스택을 쌓는다. 쌓인 수가 늘었을 때만 부여 큐(BurnApply)를 낸다(가득 찬 뒤의 갱신·기관총 연사로 소리가 쌓이지 않게).</summary>
    public static void Apply(EnemyHealth target)
    {
        if (target == null || target.currentHP <= 0f || !target.isActiveAndEnabled) return;
        var burn = target.GetComponent<EnemyBurn>();
        if (burn == null) burn = target.gameObject.AddComponent<EnemyBurn>();
        burn.health = target;
        if (burn.body == null) burn.body = target.GetComponentInChildren<Collider2D>();
        float now = Time.time;
        burn.DropExpired(now);   // 이미 지난 감소를 먼저 반영한다(꺼진 스택은 되살리지 않는다)
        if (burn.count == 0) burn.nextTick = now + TickInterval;
        bool grew = burn.count < Capacity;
        if (grew) burn.count++;
        burn.decayAt = now + Duration;
        burn.enabled = true;
        if (grew) WeaponTraitFeedbackSO.Play(WeaponTraitCue.BurnApply, target.transform.position, Vector2.up);
    }

    /// <summary>지금 스택을 모두 소모한다(핸드캐논 기폭). 소모한 스택 수를 돌려준다.</summary>
    public int Detonate()
    {
        int stacks = Stacks;
        Clear();
        return stacks;
    }

    private void Update()
    {
        if (health == null || health.currentHP <= 0f) { Clear(); return; }
        float now = Time.time;
        // 틱과 스택 감소를 시각 순서대로 처리한다. 같은 시각이면 틱을 먼저 낸다(그 순간까지 붙어 있던 스택 수로 피해).
        while (count > 0)
        {
            float expire = decayAt;
            if (nextTick <= now && nextTick <= expire + 0.0001f)
            {
                nextTick += TickInterval;
                var hit = new WeaponBaseSO.DamageInfo { damage = count * DamagePerStack };
                if (WeaponTraitEffects.ApplyEnemyHit(health, hit, transform.position))
                    WeaponTraitFeedbackSO.Play(WeaponTraitCue.BurnTick, transform.position + Vector3.up * 0.45f, Vector2.up);
                if (!isActiveAndEnabled || health.currentHP <= 0f) { Clear(); return; }
            }
            else if (expire <= now) RemoveOldest();
            else break;
        }
        if (count == 0) Clear();
    }

    private void DropExpired(float now)
    {
        while (count > 0 && decayAt <= now) RemoveOldest();
    }

    // 1스택을 덜고 다음 감소를 Duration 뒤로 잡는다.
    private void RemoveOldest()
    {
        count = Mathf.Max(0, count - 1);
        decayAt += Duration;
    }

    private void LateUpdate()
    {
        int stacks = Stacks;
        if (stacks <= 0) { HideBadge(); return; }
        if (!EnsureBadge()) return;
        float now = Time.time;
        if (stacks != shownCount)
        {
            label.text = "x<space=0.12em>" + stacks;   // 글꼴(정적 아틀라스)에 공백 글자가 없어 태그로 띄운다
            shownCount = stacks;
            popAge = 0f;
            changeTime = now;
        }
        popAge += Time.deltaTime;
        float pop = popAge < PopTime ? Mathf.Sin(popAge / PopTime * Mathf.PI) * 0.18f : 0f;

        Vector3 top = transform.position + Vector3.up;
        if (body != null)
        {
            var bounds = body.bounds;
            top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        badge.SetPositionAndRotation(top + Vector3.up * BadgeGap, Quaternion.identity);
        // 부모(적)의 좌우 반전·크기와 관계없이 같은 크기·방향으로 보이게 월드 배율을 맞춘다.
        Vector3 parent = transform.lossyScale;
        float sx = Mathf.Max(1e-4f, Mathf.Abs(parent.x)), sy = Mathf.Max(1e-4f, Mathf.Abs(parent.y));
        badge.localScale = new Vector3(Mathf.Sign(parent.x) / sx, 1f / sy, 1f);

        icon.enabled = true;
        label.enabled = true;
        icon.GetPropertyBlock(block);
        block.SetFloat(KindId, 6f);
        block.SetVector(StateAId, new Vector4(stacks, Capacity, changeTime, count > 0 ? decayAt : 0f));
        block.SetVector(FrameId, IconFrame);
        block.SetFloat(AlphaId, 1f);
        block.SetFloat(ClockId, now);
        icon.SetPropertyBlock(block);
        label.transform.localScale = Vector3.one * (1f + pop);
        bool warn = NextExpiry < WarnTime;
        label.alpha = warn ? 0.45f + 0.55f * (Mathf.Sin(now * 37.7f) * 0.5f + 0.5f) : 1f;
    }

    private bool EnsureBadge()
    {
        if (badge != null) return true;
        var material = WeaponTraitFeedbackSO.GaugeMaterial;
        if (material == null) return false;   // 아이콘 재질이 연결되기 전(생성 도구 전)에는 그리지 않는다
        block = new MaterialPropertyBlock();
        badge = new GameObject("BurnBadge").transform;
        badge.SetParent(transform, false);
        icon = WeaponTraitEffects.MakeSprite("Icon", badge, material, 46);
        icon.transform.localPosition = new Vector3(-0.2f + IconFrame.x * IconUnit, IconFrame.y * IconUnit, 0f);
        icon.transform.localScale = new Vector3(IconFrame.z * 2f * IconUnit, IconFrame.w * 2f * IconUnit, 1f);
        var text = new GameObject("Count");
        text.transform.SetParent(badge, false);
        text.transform.localPosition = new Vector3(0.0f, -0.03f, 0f);
        label = text.AddComponent<TextMeshPro>();
        var font = WeaponTraitFeedbackSO.BurnFont;
        if (font != null) label.font = font;
        var textMaterial = WeaponTraitFeedbackSO.BurnTextMaterial;
        if (textMaterial != null) label.fontSharedMaterial = textMaterial;
        label.fontSize = TextSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.color = new Color(1f, 0.93f, 0.82f);
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(1.2f, 0.5f);
        label.sortingLayerID = icon.sortingLayerID;
        label.sortingOrder = 47;
        return true;
    }

    // OnDisable 중에도 불릴 수 있으므로 개체를 켜고 끄지 않고 렌더러만 끈다(비활성화 도중 SetActive 금지).
    private void HideBadge()
    {
        if (icon != null) icon.enabled = false;
        if (label != null) label.enabled = false;
        shownCount = -1;
    }

    public void Clear()
    {
        count = 0;
        HideBadge();
        enabled = false;
    }

    private void OnDisable() => Clear();
}
