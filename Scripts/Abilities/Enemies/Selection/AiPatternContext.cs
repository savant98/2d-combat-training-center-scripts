using System;
using UnityEngine;

[Serializable]
public class AiPatternContext
{
    public AiBrain Brain { get; private set; }
    public EnemyController Controller { get; private set; }
    public Transform PlayerTransform { get; private set; }

    public void Initialize(AiBrain brain, EnemyController controller, Transform playerTransform)
    {
        Brain = brain;
        Controller = controller;
        PlayerTransform = playerTransform;
    }

    public void SetPlayerTransform(Transform playerTransform)
    {
        PlayerTransform = playerTransform;
    }

    public float GetPlayerDistanceX()
    {
        if (PlayerTransform == null || Controller == null)
            return float.MaxValue;

        return Mathf.Abs(PlayerTransform.position.x - Controller.transform.position.x);
    }
}