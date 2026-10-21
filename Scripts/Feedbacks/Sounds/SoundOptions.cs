using UnityEngine;

[System.Serializable]
public class SoundOptions
{
    [Header("Basic")]
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0.1f, 3f)] public float pitch = 1f;
    [Range(0f, 1f)] public float spatialBlend = 0f;
    // 0 = 2D 사운드, 1 = 3D 사운드

    [Header("Randomize")]
    public bool randomizePitch = false;
    public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

    [Header("Duplicate Control")]
    public float minInterval = 0.03f;
    public int maxSimultaneous = 5;
    [Range(0f, 1f)] public float crowdedVolumeMultiplier = 0.7f;
}