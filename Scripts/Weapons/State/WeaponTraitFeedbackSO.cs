using System;
using UnityEngine;

// 표(Resources/WeaponTraitFeedback.asset)의 cues 배열은 trigger 정수값 = 배열 순서로 저장된다.
// 새 큐는 반드시 맨 뒤에 덧붙인다(중간에 끼우면 저장된 trigger 값이 어긋난다).
public enum WeaponTraitCue
{
    ShieldCharge, ShieldRaise, ShieldHit, ShieldBreak,
    RifleRound, MagazineEmpty, ReloadStart, ReloadEnd,
    IncendiaryMount, IncendiaryFire, IncendiaryHit, BurnApply, BurnTick, MachineGunBurn,
    ResolveGain, ResolveSpend, SwordWave, SwordWaveEmpowered, SwordWaveHit, SwordWaveHeavyHit, SwordWaveLand,
    RailHeat, RailCharge, RailVent, RailOverheat, RailReady, RailEcho, RailCross,
    // 2026-11-21 무장 특성 피드백 개편: 반격 성공으로 남은 방어막을 다시 채움, 핸드캐논 화상 기폭(스택마다 1회), 작열탄 조건부 폭발
    ShieldRefill, BurnDetonate, IncendiaryBlast,
    // 2026-11-21 3차: 검기방출 도약(빠른 점프 순간), 레일건 충전 추가타(일반 공격 추가 2발·미니 별창·미니 레이저)의 적중
    SwordWaveLeap, RailEchoHit
}

public sealed class WeaponTraitFeedbackSO : ScriptableObject
{
    [Serializable]
    private sealed class Cue
    {
        public WeaponTraitCue trigger;
        public FeedbackDataSO feedback;
        public Color color;
        public float size;
        public float duration;
        public int sparks;
        public float spread;
    }

    [SerializeField] private Cue[] cues;
    [Tooltip("해머 방어막 외피(Player/HammerBarrier)")]
    [SerializeField] private Material barrierMaterial;
    [SerializeField] private Material slashMaterial;
    [Tooltip("Player 주변 무장 특성 아이콘(월드 공간 SpriteRenderer)")]
    [SerializeField] private Material gaugeMaterial;
    [Tooltip("PlayerHUD 상태 표시 띠의 아이콘(Canvas Image)")]
    [SerializeField] private Material gaugeUiMaterial;
    [Tooltip("작열탄 장착 중 총열 가열 표시(별 총 Sprite 위에 덧그림)")]
    [SerializeField] private Material barrelHeatMaterial;
    [Tooltip("적 머리 위 화상 표시 'x 스택 수' 글자 글꼴(피해 숫자와 같은 글꼴). 불꽃은 gaugeMaterial(_Kind 6)로 그린다")]
    [SerializeField] private TMPro.TMP_FontAsset burnFont;
    [Tooltip("화상 스택 수 글자 재질(어두운 외곽선)")]
    [SerializeField] private Material burnTextMaterial;
    private static readonly int CueCount = Enum.GetValues(typeof(WeaponTraitCue)).Length;
    private static WeaponTraitFeedbackSO database;
    [NonSerialized] private Cue[] cache;

    private static WeaponTraitFeedbackSO Database
    {
        get
        {
            if (database == null) database = Resources.Load<WeaponTraitFeedbackSO>("WeaponTraitFeedback");
            return database;
        }
    }

    public static Material BarrierMaterial => Database != null ? Database.barrierMaterial : null;
    public static Material SlashMaterial => Database != null ? Database.slashMaterial : null;
    public static Material GaugeMaterial => Database != null ? Database.gaugeMaterial : null;
    public static Material GaugeUiMaterial => Database != null ? Database.gaugeUiMaterial : null;
    public static Material BarrelHeatMaterial => Database != null ? Database.barrelHeatMaterial : null;
    public static TMPro.TMP_FontAsset BurnFont => Database != null ? Database.burnFont : null;
    public static Material BurnTextMaterial => Database != null ? Database.burnTextMaterial : null;

    /// <summary>
    /// 큐 하나를 재생한다. cameraEffects가 false면 흔들림·화면 효과·입자 없이 소리와 절차 효과만 낸다
    /// (반격 성공처럼 더 큰 연출과 같은 순간에 겹칠 때).
    /// </summary>
    public static void Play(WeaponTraitCue trigger, Vector3 position, Vector2 axis = default, float scale = 1f, bool cameraEffects = true,
        SoundOptions soundOverride = null)
    {
        var table = Database;
        if (table == null || (uint)trigger >= (uint)CueCount) return;
        // Editor 스크립트 재로딩 뒤 복원된 빈 캐시도 다시 구성한다.
        if (table.cache == null || table.cache.Length != CueCount)
        {
            table.cache = new Cue[CueCount];
            if (table.cues != null)
                foreach (var item in table.cues)
                    if (item != null && (uint)item.trigger < (uint)CueCount) table.cache[(int)item.trigger] = item;
        }
        var cue = table.cache[(int)trigger];
        if (cue == null) return;
        Quaternion rotation = axis == Vector2.zero ? Quaternion.identity
            : Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.right, axis));
        if (cue.feedback != null)
        {
            var feedback = cue.feedback;
            var post = feedback.postFX;
            bool usesCamera = feedback.shakeIntensity > 0f || (post != null &&
                (post.useFlash || post.useVignette || post.useBloom || post.useChromaticAberration || post.useSaturation))
                || (feedback.particlePrefabs != null && feedback.particlePrefabs.Length > 0);
            if (cameraEffects && usesCamera && FeedbackPlayer.Instance != null)
                FeedbackPlayer.Instance.PlayExact(feedback, position, rotation, soundOverride: soundOverride);
            else
                SoundManager.Instance?.PlaySound(soundOverride ?? feedback.sound, position);
        }
        if (cue.size > 0f && cue.duration > 0f)
            WeaponTraitBurst.Spawn(position, rotation, cue.color, cue.size * scale,
                cue.duration, cue.sparks, cue.spread);
    }
}
