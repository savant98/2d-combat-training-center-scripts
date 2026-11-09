using UnityEngine;

[CreateAssetMenu(menuName = "Player/Ability Data/Move Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerMoveData")]
public class PlayerMoveDataSO : PlayerAbilityDataSO
{
    [Header("Move Option")]
    public float moveSpeed = 10f;

    [Header("Feedback")]
    [Tooltip("바닥에서 이동하는 동안 지면과 닿는 지점에 표시할 궤적 Prefab입니다.")]
    public PlayerGroundTrail groundTrail;
}
