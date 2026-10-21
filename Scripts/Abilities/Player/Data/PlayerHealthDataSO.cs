using UnityEngine;

[CreateAssetMenu(menuName = "Player/Health")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerHealthData")]
public class PlayerHealthDataSO : ScriptableObject
{
    [Header("Health")]
    public float maxhp = 100;
}
