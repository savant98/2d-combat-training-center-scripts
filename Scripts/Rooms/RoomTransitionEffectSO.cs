using System.Collections;
using UnityEngine;
public abstract class RoomTransitionEffectSO : ScriptableObject
{
    public string changePlayerAnim;
    public abstract IEnumerator Play(RoomTransitionContext context);
}
