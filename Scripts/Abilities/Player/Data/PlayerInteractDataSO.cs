using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Interact Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerInteractData")]
public class PlayerInteractDataSO : PlayerAbilityDataSO
{
    [Header("Interact Settings")]
    public float interactRange = 1.5f;
    public LayerMask interactLayer;
}
