using UnityEngine;

[CreateAssetMenu(menuName = "Player/Health")]
public class PlayerHealthData : ScriptableObject
{
    [Header("Health")]
    public float maxhp = 100;
}
