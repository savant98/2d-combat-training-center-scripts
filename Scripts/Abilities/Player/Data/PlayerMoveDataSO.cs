using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Move Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerMoveData")]
public class PlayerMoveDataSO : PlayerAbilityDataSO
{
    [Header("Move Option")]
    public float moveSpeed = 10f;
}
