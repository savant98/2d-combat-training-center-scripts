using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Approach Melee")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ApproachMeleeData")]
public class ApproachMeleeDataSO : EnemyAbilityDataSO
{
    [Header("Approach")]
    public float approachSpeed = 18f;
    public float approachOffsetX = 1.2f;
    public float approachDeadZone = 0.05f;
    public float faceDeadZone = 0.05f;
}
