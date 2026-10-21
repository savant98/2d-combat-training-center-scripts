using UnityEngine;
using System.Collections;

public class RoomTransitionManager : MonoBehaviour
{
    public static RoomTransitionManager Instance { get; private set; }

    private bool isTransitioning;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void RequestTransition(RoomTransitionRequest request)
    {
        if (isTransitioning)
            return;

        StartCoroutine(TransitionRoutine(request));
    }

    private IEnumerator TransitionRoutine(RoomTransitionRequest request)
    {
        isTransitioning = true;

        // Player 입력 차단
        UIManager.Instance.PushGameplayBlock();

        var context = new RoomTransitionContext
        {
            request = request
        };

        // Effect 실행
        if (request.transitionEffect != null)
            yield return request.transitionEffect.Play(context);
        else
            context.SwitchRoom();

        // 잠시 대기 -> 후처리
        yield return new WaitForSeconds(0.25f);

        UIManager.Instance.ClearGameplayBlocks();
        isTransitioning = false;

        GameManager.Instance.Player.AbilityManager.InterruptAllAbilities();
    }
}
