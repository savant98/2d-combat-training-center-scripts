using UnityEngine;

[CreateAssetMenu(menuName = "Animation/Common Animation Set")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "AnimationSet")]
public class AnimationSetSO : ScriptableObject
{
    public AnimationClip windup;
    public AnimationClip execute;
    public AnimationClip recover;
}
