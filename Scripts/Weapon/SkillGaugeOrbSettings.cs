using System;
using UnityEngine;

[Serializable]
public class SkillGaugeOrbSettings
{
    public SkillGaugeOrb orbPrefab;

    [Header("Move")]
    public float popDistance = 0.8f;
    public float popDuration = 0.18f;
    public float waitDuration = 0.05f;
    public float homingDuration = 0.35f;
    public float arriveDistance = 0.2f;

    [Header("Random")]
    public Vector2 randomXRange = new Vector2(-1f, 1f);
    public Vector2 randomYRange = new Vector2(0.2f, 1.2f);

    [Header("Amount")]
    public float gaugePerOrb = 5f;

    [Header("Scale")]
    public float arriveScaleDuration = 0.08f;
}