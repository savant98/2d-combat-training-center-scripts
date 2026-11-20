using System;
using UnityEngine;

public enum WeaponTraitCue
{
    ShieldCharge, ShieldRaise, ShieldHit, ShieldBreak,
    RifleRound, MagazineEmpty, ReloadStart, ReloadEnd,
    IncendiaryMount, IncendiaryFire, IncendiaryHit, BurnApply, BurnTick, MachineGunBurn,
    ResolveGain, ResolveSpend, SwordWave, SwordWaveEmpowered, SwordWaveHit, SwordWaveHeavyHit, SwordWaveLand,
    RailHeat, RailCharge, RailVent, RailOverheat, RailReady, RailEcho, RailCross
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
    [SerializeField] private Material barrierMaterial;
    [SerializeField] private Material slashMaterial;
    private static WeaponTraitFeedbackSO database;
    private Cue[] cache;

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

    public static void Play(WeaponTraitCue trigger, Vector3 position, Vector2 axis = default, float scale = 1f)
    {
        var table = Database;
        if (table == null) return;
        if (table.cache == null)
        {
            table.cache = new Cue[Enum.GetValues(typeof(WeaponTraitCue)).Length];
            foreach (var item in table.cues) table.cache[(int)item.trigger] = item;
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
                (post.useFlash || post.useVignette || post.useBloom || post.useChromaticAberration || post.useSaturation));
            if (usesCamera && FeedbackPlayer.Instance != null)
                FeedbackPlayer.Instance.PlayExact(feedback, position, rotation);
            else if (feedback.sound != null)
                SoundManager.Instance?.PlaySound(feedback.sound, position);
        }
        if (cue.size > 0f && cue.duration > 0f)
            WeaponTraitBurst.Spawn(position, rotation, cue.color, cue.size * scale,
                cue.duration, cue.sparks, cue.spread);
    }
}
