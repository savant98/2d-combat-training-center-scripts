using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Interact Data")]
public class PlayerInteractData : PlayerAbilityData
{
    [Header("Interact Settings")]
    public float interactRange = 1.5f;
    public LayerMask interactLayer;
}
