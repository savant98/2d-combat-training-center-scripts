using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Parabolic Dash")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ParabolicDashData")]
public class ParabolicDashDataSO : EnemyAbilityDataSO
{
    [Header("Parabolic Settings")]
    public float maxHeight = 4f;
    public float flyDuration = 1f;
    public float maxDistance = 10f;
}
