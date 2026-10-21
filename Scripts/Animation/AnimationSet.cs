using UnityEngine;

[CreateAssetMenu(menuName = "Animation/Common Animation Set")]
public class AnimationSet : ScriptableObject
{
    public AnimationClip windup;
    public AnimationClip execute;
    public AnimationClip recover;
}
