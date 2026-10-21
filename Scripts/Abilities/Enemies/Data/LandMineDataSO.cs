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

    [Header("Mine Settings")]
    public DamageInfo damageInfo;
    public LayerMask platformLayer;
}
