using UnityEngine;
using static WeaponBaseSO;

[CreateAssetMenu(menuName = "Enemy/Ability Data/LandMine Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "LandMineData")]
public class LandMineDataSO : EnemyAbilityDataSO
{
    [Header("Scatter Settings")]
    public ProjectileBase minePrefab;
    public int mineCount = 12;
    public float launchSpeed = 8f;
    public float scatterInterval = 0.2f;

    [Header("고른 착지점 살포")]
    public bool distributeLandingPoints;
    [Min(1f)] public float scatterRadius = 12f;
    [Min(.1f)] public float minimumSeparation = .9f;
    [Range(0f, .4f)] public float landingJitter = .15f;
    [Min(.1f)] public float scatterFlightTime = .7f;

    [Header("Mine Settings")]
    public DamageInfo damageInfo;
    public LayerMask platformLayer;
}
