using TMPro;
using UnityEngine;

// 스킬 게이지를 Player 몸 옆에 세로로 쌓이는 배터리로 보여 준다(2026-11-15, 사용자 요청 "orb가 날아올 때 투사체와 헷갈린다.
// skillGauge가 차면 player 주변에 배터리 아이콘이 차는 식으로, 배터리는 총 4개이므로 4개가 다 차면 4개가 나와야 한다.
// 각 칸을 달성하면 Charge! 효과와 소리(너무 크면 안 됨). 실시간으로 player를 보고 있어도 게이지를 빠르게 알 수 있게").
// - 다 찬 칸 수만큼 배터리가 보이고, 지금 충전 중인 다음 칸은 어둡게 차오른다. 아직 시작하지 않은 칸은 보이지 않는다.
// - 칸이 다 차는 순간 그 배터리가 번쩍이며 튀고, 옆에 'Charge!'가 떠올랐다 사라지며 작은 소리가 난다.
//   한 번에 여러 칸을 넘어도(예: 지면 평탄화 적중 +30) 글자와 소리는 한 번이고, 새로 찬 배터리는 모두 번쩍인다.
// - 무장을 써서 칸이 줄면 그 배터리가 작아지며 사라진다.
// - 기둥은 Player가 바라보는 반대쪽(등 뒤) 몸 옆에 선다. 공격이 나가는 앞쪽을 가리지 않게 하고, 방향을 바꾸면 미끄러지듯 옮겨 간다.
// Player 루트 아래(FacingModel 밖)에 두어 좌우 반전에 글자가 뒤집히지 않는다. FixedMotionBody가 렌더 때 보간 위치로 함께 옮긴다.
// 시간은 Time.deltaTime(Gear 표시와 같다). Player.prefab은 건드리지 않고 PlayerWeaponAbility가 실행 중에 만든다.
[DefaultExecutionOrder(110)]
public sealed class PlayerSkillBatteryVisual : MonoBehaviour
{
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int FullId = Shader.PropertyToID("_Full");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");

    [Header("Parts")]
    [Tooltip("배터리 기둥. 좌우 자리만 바뀐다")]
    [SerializeField] private Transform column;
    [Tooltip("아래에서 위로 쌓이는 배터리 칸(Player/SkillBattery 재질)")]
    [SerializeField] private SpriteRenderer[] cells;
    [SerializeField] private TextMeshPro chargeText;

    [Header("Layout")]
    [Tooltip("몸 가운데에서 기둥까지의 가로 거리(칸)")]
    [SerializeField] private float sideOffset = 1.12f;
    [SerializeField] private float bottomY = -0.42f;
    [SerializeField] private float spacing = 0.34f;
    [Tooltip("켜면 바라보는 반대쪽(등 뒤)에 선다. 끄면 늘 왼쪽")]
    [SerializeField] private bool followBack = true;
    [SerializeField, Min(0.01f)] private float sideSwitchTime = 0.14f;

    [Header("Motion")]
    [Tooltip("채움이 목표를 따라가는 빠르기(초당 비율)")]
    [SerializeField, Min(0.01f)] private float fillSpeed = 5f;
    [SerializeField, Min(0.01f)] private float appearTime = 0.12f;
    [SerializeField, Min(0.01f)] private float flashTime = 0.35f;
    [SerializeField] private float popScale = 0.35f;
    [SerializeField, Min(0.01f)] private float popTime = 0.22f;
    [SerializeField, Min(0.01f)] private float textTime = 0.75f;
    [SerializeField] private float textRise = 0.45f;
    [SerializeField] private float textGap = 0.42f;

    [Header("Sound")]
    [Tooltip("칸이 다 찰 때 한 번 나는 작은 소리")]
    [SerializeField] private SoundOptions chargeSound;

    private CharacterController2D owner;
    private PlayerHealth health;
    private MaterialPropertyBlock block;
    private float[] target, shown, alpha, flash, pop;
    private bool[] full;
    private float side = -1f;
    private float textAge = -1f;
    private int textCell;
    private float textSide = -1f;

    // 검증·연출이 읽기만 하는 값
    public int FullCells { get; private set; }
    public float ChargingFill { get; private set; }
    public int ChargeCount { get; private set; }
    public int VisibleCells { get; private set; }
    public float Side => side;

    public void Initialize(CharacterController2D controller)
    {
        owner = controller;
        health = controller != null ? controller.GetComponentInChildren<PlayerHealth>(true) : null;
        block = new MaterialPropertyBlock();   // MonoBehaviour 필드 초기화 중에는 만들 수 없다
        int n = cells != null ? cells.Length : 0;
        target = new float[n]; shown = new float[n]; alpha = new float[n]; flash = new float[n]; pop = new float[n];
        full = new bool[n];
        for (int i = 0; i < n; i++) pop[i] = popTime;
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
        side = DesiredSide();
        textAge = -1f;
        if (chargeText != null) chargeText.alpha = 0f;
        Apply();
    }

    // value/max를 칸 4개로 나눈다(PlayerHUD 배터리와 같은 계산). silent면 'Charge!'와 소리 없이 바로 맞춘다.
    public void SetGauge(float value, float max, bool silent)
    {
        if (target == null) return;
        // 사망 중에는 배터리·글자를 숨기므로(LateUpdate) 날아가던 탄이 적중해 칸이 차도 'Charge!' 소리를 내지 않는다.
        if (health != null && health.IsDead) silent = true;
        int count = cells.Length;
        float n = max > 0f ? Mathf.Clamp01(value / max) * count : 0f;
        int fullNow = Mathf.Clamp(Mathf.FloorToInt(n + 0.0001f), 0, count);
        float partial = fullNow < count ? Mathf.Clamp01(n - fullNow) : 0f;
        if (partial < 0.0001f) partial = 0f;

        for (int i = 0; i < count; i++)
        {
            target[i] = i < fullNow ? 1f : i == fullNow ? partial : 0f;
            full[i] = i < fullNow;
            if (silent) shown[i] = target[i];
        }

        if (!silent && fullNow > FullCells)
        {
            for (int i = FullCells; i < fullNow; i++)
            {
                flash[i] = 1f;
                pop[i] = 0f;
                shown[i] = 1f;
            }
            textCell = fullNow - 1;
            textSide = side;
            textAge = 0f;
            ChargeCount++;
            if (chargeSound != null && chargeSound.clip != null && SoundManager.Instance != null)
                SoundManager.Instance.PlaySound(chargeSound, transform.position);
        }

        FullCells = fullNow;
        ChargingFill = partial;
    }

    private float DesiredSide()
    {
        if (!followBack || owner == null) return -1f;
        return owner.Facing >= 0 ? -1f : 1f;
    }

    private void OnDisable()
    {
        textAge = -1f;
    }

    private void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;
        side = Mathf.MoveTowards(side, DesiredSide(), dt * 2f / sideSwitchTime);

        bool hidden = health != null && health.IsDead;
        int visible = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            // 줄어들 때는 바로, 늘 때는 부드럽게 따라간다(다 찬 칸은 SetGauge에서 바로 1로 맞췄다).
            shown[i] = target[i] < shown[i] ? target[i] : Mathf.MoveTowards(shown[i], target[i], dt * fillSpeed);
            bool on = !hidden && target[i] > 0f;
            alpha[i] = Mathf.MoveTowards(alpha[i], on ? 1f : 0f, dt / appearTime);
            flash[i] = Mathf.MoveTowards(flash[i], 0f, dt / flashTime);
            pop[i] = Mathf.Min(pop[i] + dt, popTime);
            if (alpha[i] > 0.01f) visible++;
        }
        VisibleCells = visible;
        if (textAge >= 0f)
        {
            textAge += dt;
            if (textAge >= textTime || hidden) textAge = -1f;
        }
        Apply();
    }

    private void Apply()
    {
        if (cells == null || block == null) return;
        if (column != null) column.localPosition = new Vector3(side * sideOffset, 0f, 0f);
        float clock = Time.time;
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell == null) continue;
            float a = alpha[i];
            cell.enabled = a > 0.001f;
            if (!cell.enabled) continue;
            float popT = pop[i] / popTime;
            float scale = Mathf.Lerp(0.6f, 1f, a) * (1f + popScale * Mathf.Sin(Mathf.Clamp01(popT) * Mathf.PI) * (1f - popT));
            cell.transform.localPosition = new Vector3(0f, bottomY + spacing * i, 0f);
            cell.transform.localScale = new Vector3(scale, scale, 1f);
            cell.GetPropertyBlock(block);
            block.SetFloat(FillId, shown[i]);
            block.SetFloat(FullId, full[i] ? 1f : 0f);
            block.SetFloat(FlashId, flash[i]);
            block.SetFloat(AlphaId, a);
            block.SetFloat(ClockId, clock);
            cell.SetPropertyBlock(block);
        }

        if (chargeText == null) return;
        if (textAge < 0f)
        {
            chargeText.alpha = 0f;
            return;
        }
        float t = textAge / textTime;
        float rise = 1f - (1f - t) * (1f - t);
        // 글자는 기둥 바깥쪽(몸에서 먼 쪽)에, 새로 찬 맨 위 칸 높이에서 떠오른다.
        float x = textSide * (sideOffset + textGap);
        chargeText.transform.localPosition = new Vector3(x, bottomY + spacing * textCell + textRise * rise, 0f);
        chargeText.alignment = textSide < 0f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        var rt = chargeText.rectTransform;
        rt.pivot = new Vector2(textSide < 0f ? 1f : 0f, 0.5f);
        float popText = t < 0.18f ? Mathf.Lerp(0.6f, 1.15f, t / 0.18f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.18f) / 0.2f));
        chargeText.transform.localScale = new Vector3(popText, popText, 1f);
        chargeText.alpha = t < 0.7f ? 1f : Mathf.Clamp01(1f - (t - 0.7f) / 0.3f);
    }
}
