using TMPro;
using UnityEngine;

// 무기 로직의 읽기 전용 목록과 이벤트를 구독하는 기술 기억 표시.
// 저장 중 큰 아이콘·수렴 입자 → 완성된 아이콘이 3칸 중 마지막 슬롯으로 들어간다.
[DefaultExecutionOrder(115)]
public sealed class PlayerSkillMemoryVisual : MonoBehaviour
{
    [SerializeField] private Transform spotlight;
    [SerializeField] private SpriteRenderer spotlightFrame, spotlightRing, spotlightIcon;
    [SerializeField] private Transform[] slots;
    [SerializeField] private SpriteRenderer[] slotFrames, slotRings, slotIcons;
    [SerializeField] private TextMeshPro[] slotCooldownTexts;
    [SerializeField] private ParticleSystem motes;
    [SerializeField] private SpriteRenderer fabricationFrame, fabricationLink;
    [SerializeField] private ParticleSystem weldSparks;
    private PlayerSkillBatteryVisual battery;
    private CharacterController2D owner;
    private PlayerWeaponAbility ability;
    private PlayerWeaponDataSO data;
    private MaterialPropertyBlock block;
    private SoundHandle storeSound;
    private StoredGearSkill pending;
    private bool storing, finishing, cancelled;
    private float age;
    private Vector3 finishStart;
    private int destination;
    private float sparkBudget;
    private readonly System.Random visualRandom = new System.Random();
    private const float PreviewSize = 2.7f, SlotSize = .8f, PreviewHeight = 2.22f;
    public int VisibleSlotCount { get; private set; }
    public bool IsShowingPreview => spotlight != null && spotlight.gameObject.activeSelf;
    public bool IsStoreSoundPlaying => storeSound != null && storeSound.IsValid;

    public void Initialize(PlayerWeaponAbility source, PlayerWeaponDataSO settings)
    {
        ability = source; data = settings; block = new MaterialPropertyBlock();
        owner = source.GetComponent<CharacterController2D>();
        battery = source.GetComponentInChildren<PlayerSkillBatteryVisual>(true);
        ability.StoredSkillsChanged += RefreshSlots;
        ability.SkillStorageStarted += BeginStore;
        ability.SkillStorageFinished += FinishStore;
        spotlight.gameObject.SetActive(false);
        if (fabricationLink != null) fabricationLink.enabled = false;
        RefreshSlots();
    }
    private void BeginStore(StoredGearSkill skill)
    {
        StopSound(); pending = skill; storing = true; finishing = cancelled = false; age = 0f;
        spotlight.gameObject.SetActive(true); spotlight.localPosition = new Vector3(0, PreviewHeight, 0);
        spotlight.localScale = Vector3.one;
        spotlightFrame.enabled = spotlightRing.enabled = spotlightIcon.enabled = true;
        spotlightIcon.sprite = skill.Weapon.weaponSprite;
        SizeIcon(spotlightIcon, PreviewSize);
        sparkBudget = 0;
        ApplyFabrication(0, 1, 0);
        if (weldSparks != null) { weldSparks.Clear(true); weldSparks.Play(true); }
        if (SoundManager.Instance != null) storeSound = SoundManager.Instance.PlayLoop(data.skillStoreSound, transform.position);
        if (motes != null)
        {
            var main = motes.main; main.startColor = skill.Color; motes.Clear(true); motes.Play(true);
        }
        RefreshSlots();
    }
    private void FinishStore(bool complete)
    {
        StopSound(); storing = false; finishing = true; cancelled = !complete; age = 0f;
        finishStart = spotlight.localPosition;
        destination = Mathf.Max(0, ability.StoredSkills.Count - 1);
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (weldSparks != null) weldSparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (complete && SoundManager.Instance != null) SoundManager.Instance.PlaySound(data.swapFeedback != null ? data.swapFeedback.sound : data.skillStoreCompleteSound, transform.position);
    }
    private void RefreshSlots()
    {
        if (ability == null) return;
        VisibleSlotCount = ability.StoredSkills.Count;
        if (finishing && !cancelled)
        {
            if (VisibleSlotCount == 0) { finishing = false; spotlight.gameObject.SetActive(false); if (fabricationLink != null) fabricationLink.enabled = false; }
            else destination = VisibleSlotCount - 1;
        }
        bool show = VisibleSlotCount > 0 || storing || finishing;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].gameObject.SetActive(show);
            bool occupied = i < VisibleSlotCount;
            bool incoming = i == VisibleSlotCount - 1 && ((storing && !ability.IsStoringSkill) || (finishing && !cancelled));
            slotIcons[i].enabled = occupied && !incoming;
            slotIcons[i].sprite = occupied ? ability.StoredSkills[i].Weapon.weaponSprite : null;
            SizeIcon(slotIcons[i], SlotSize);
            slotFrames[i].enabled = slotRings[i].enabled = show;
            Color tint = occupied ? ability.StoredSkills[i].Color : new Color(.2f, .32f, .42f);
            Paint(slotFrames[i], tint, occupied ? .8f : .25f);
            Paint(slotRings[i], tint, occupied ? (i == 0 ? 1.5f : .7f) : .15f);
        }
        RefreshCooldowns();
    }

    private void RefreshCooldowns()
    {
        if (slotCooldownTexts == null) return;
        for (int i = 0; i < slotCooldownTexts.Length && i < slotIcons.Length; i++)
        {
            var label = slotCooldownTexts[i];
            if (label == null) continue;
            float remaining = slotIcons[i].enabled ? ability.GetStoredSkillCooldownRemaining(i) : 0f;
            bool coolingDown = remaining > 0f;
            if (label.gameObject.activeSelf != coolingDown) label.gameObject.SetActive(coolingDown);
            slotIcons[i].color = coolingDown ? new Color(.26f, .32f, .42f) : Color.white;
            if (coolingDown) label.SetText("{0}", Mathf.CeilToInt(remaining));
        }
    }
    private void LateUpdate()
    {
        if (ability == null) return;
        age += Time.deltaTime;
        // 배터리가 좌우로 옮겨가는 동안에도 두 표시의 방향이 정확히 반대가 되게 한다.
        float side = battery != null ? -battery.Side : owner.Facing;
        for (int i = 0; i < slots.Length; i++) slots[i].localPosition = new Vector3(side * 1.5f, 1f - i * .95f, 0);
        if (storing)
        {
            float p = ability.SkillStorageProgress;
            spotlight.localScale = Vector3.one;
            spotlight.localPosition = new Vector3(0, PreviewHeight, 0);
            spotlightRing.transform.localRotation = Quaternion.Euler(0, 0, -160f * p);
            SizeIcon(spotlightIcon, PreviewSize);
            Paint(spotlightFrame, pending.Color, .6f + .4f * p);
            Paint(spotlightRing, pending.Color, .5f + p);
            spotlightIcon.color = Color.white;
            ApplyFabrication(p, 1, 0);
            EmitWeld(p);
        }
        else if (finishing)
        {
            float p = Mathf.Clamp01(age / .2f), eased = p * p * (3f - 2f * p);
            spotlight.localPosition = Vector3.Lerp(finishStart, cancelled ? finishStart + Vector3.up * .35f : slots[destination].localPosition, eased) + Vector3.up * Mathf.Sin(p * Mathf.PI) * .3f;
            spotlight.localScale = Vector3.one * Mathf.Lerp(1f, cancelled ? 1.25f : SlotSize / PreviewSize, eased);
            float alpha = cancelled ? 1f - p : 1f;
            Paint(spotlightFrame, pending.Color, alpha);
            Paint(spotlightRing, pending.Color, (1f - p) * 2f);
            spotlightIcon.color = new Color(1, 1, 1, alpha);
            ApplyFabrication(1, 1 - p, cancelled ? 0 : 1 - p);
            if (p >= 1f) { finishing = false; spotlight.gameObject.SetActive(false); if (fabricationLink != null) fabricationLink.enabled = false; RefreshSlots(); }
        }
        RefreshCooldowns();
        // 이미 저장한 아이콘은 항상 FIFO 순서대로 고정된 슬롯에 남는다.
        for (int i = 0; i < VisibleSlotCount; i++)
            slotRings[i].transform.localRotation = Quaternion.Euler(0, 0, Time.time * (i == 0 ? 35f : 15f));
    }
    private void ApplyFabrication(float progress, float alpha, float flash)
    {
        if (fabricationFrame == null || fabricationLink == null) return;
        fabricationFrame.enabled = alpha > 0;
        fabricationFrame.GetPropertyBlock(block);
        block.SetFloat("_Progress", progress); block.SetFloat("_Alpha", alpha);
        block.SetFloat("_Flash", flash); block.SetFloat("_Spin", Time.time * 1.1f);
        block.SetFloat("_Open", 1 + flash * .18f); block.SetFloat("_Direction", 1);
        block.SetFloat("_GantryY", Mathf.Lerp(-.98f, .98f, progress));
        block.SetFloat("_GantryAlpha", storing ? alpha : 0);
        block.SetColor("_AccentColor", pending.Color * 1.4f);
        fabricationFrame.SetPropertyBlock(block);
        float end = spotlight.localPosition.y + Mathf.Lerp(-1.2f, 1.2f, progress);
        float length = Mathf.Max(.1f, end - .62f);
        fabricationLink.enabled = storing && alpha > 0;
        fabricationLink.transform.localPosition = new Vector3(0, .62f + length * .5f, 0);
        float quad = fabricationLink.sprite.bounds.size.x;
        fabricationLink.transform.localScale = new Vector3(.5f / quad, length / quad, 1);
        fabricationLink.GetPropertyBlock(block); block.SetFloat("_Alpha", alpha);
        block.SetFloat("_Length", length); block.SetFloat("_Scroll", Time.time * 5);
        fabricationLink.SetPropertyBlock(block);
    }
    private void EmitWeld(float progress)
    {
        if (weldSparks == null) return;
        sparkBudget += Time.deltaTime * 70;
        while (sparkBudget >= 1)
        {
            sparkBudget -= 1;
            float side = visualRandom.Next(2) == 0 ? -1 : 1;
            var point = transform.TransformPoint(new Vector3(side * 1.6f, PreviewHeight + Mathf.Lerp(-1.2f, 1.2f, progress), 0));
            var emit = new ParticleSystem.EmitParams { position = point, velocity = new Vector3(side * VisualRange(1.2f, 4f), VisualRange(-1f, 3f), 0), startColor = pending.Color, applyShapeToPosition = false };
            weldSparks.Emit(emit, 1);
        }
    }
    private float VisualRange(float min, float max) => Mathf.Lerp(min, max, (float)visualRandom.NextDouble());
    private static void SizeIcon(SpriteRenderer icon, float size)
    {
        if (icon.sprite == null) return;
        var bounds = icon.sprite.bounds.size;
        icon.transform.localScale = Vector3.one * (size / Mathf.Max(.001f, Mathf.Max(bounds.x, bounds.y)));
    }
    private void Paint(SpriteRenderer sprite, Color color, float gain)
    {
        if (sprite == null) return;
        sprite.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(gain));
        sprite.GetPropertyBlock(block);
        // GlowMultiply는 정점 알파를 쓰지 않으므로 발광색도 함께 줄인다.
        block.SetColor("_EmissionColor", color * gain);
        sprite.SetPropertyBlock(block);
    }
    private void StopSound()
    {
        if (storeSound != null && SoundManager.Instance != null) SoundManager.Instance.StopLoop(storeSound);
        storeSound = null;
    }
    private void OnEnable() { if (ability != null) RefreshSlots(); }
    private void OnDisable()
    {
        StopSound(); storing = finishing = false;
        if (spotlight != null) spotlight.gameObject.SetActive(false);
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (weldSparks != null) weldSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (fabricationLink != null) fabricationLink.enabled = false;
    }
    private void OnDestroy()
    {
        StopSound();
        if (ability == null) return;
        ability.StoredSkillsChanged -= RefreshSlots;
        ability.SkillStorageStarted -= BeginStore;
        ability.SkillStorageFinished -= FinishStore;
    }
}
