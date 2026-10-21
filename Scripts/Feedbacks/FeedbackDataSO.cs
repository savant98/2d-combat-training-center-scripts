using System;
using UnityEngine;
[CreateAssetMenu(menuName = "Feedback Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "FeedbackData")]
public class FeedbackDataSO : ScriptableObject
{
    [Header("Particle")]
    public GameObject[] particlePrefabs;
    public float lifeTime = 1.5f;

    [Header("Sound")]
    public SoundOptions sound;

    [Header("Camera Shake")]
    public float shakeIntensity;
    public float shakeDuration = 0f;

    [Header("Offset (Local Space)")]
    public Vector3 offset;

    [Header("Time Control")]
    public bool useHitStop = false;
    public float hitStopScale = 0.1f; //  슬로우 강도 (0.0 ~ 1.0)
    public float hitStopDuration = 0.05f; // 유지 시간

    [Header("PostProcessing")]
    public PostProcessingOptions postFX;
}
