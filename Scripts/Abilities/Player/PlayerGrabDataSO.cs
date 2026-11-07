using UnityEngine;

[CreateAssetMenu(menuName = "Player/Grab Presentation")]
public sealed class PlayerGrabDataSO : ScriptableObject
{
    public AnimationClip restrainedLoop;
    public Material wireMaterial;
    public GrabCinematicPresentation presentationPrefab;
    public CinematicLetterbox letterboxPrefab;
    [Min(0)] public float releaseProtectionTime = .35f;
}
