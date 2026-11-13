using System.Collections;
using UnityEngine;

/// <summary>
/// 화면을 빠르게 어둡게 한 동안 플레이어를 다른 자리로 옮기고 다시 밝힌다(위치 초기화 장치·추격 분쇄기의 "처음부터 다시").
/// 암전 동안 입력과 맞은 반동을 막아 계속 움직이다가 갑자기 옮겨지는 느낌이 나지 않게 하고, 카메라도 옮긴 자리로 바로 따라가게 한다.
/// 코루틴은 쓰는 쪽이 돌리고, 쓰는 쪽이 꺼지거나 플레이어가 죽어 도중에 멈추면 Recover로 화면과 입력을 돌려준다.
/// </summary>
public sealed class WavePlayerReturn
{
    private bool blocking;
    private bool holdingForces;

    /// <summary>옮기기를 시작해 Recover로 화면을 돌려주기 전까지 true다.</summary>
    public bool IsRunning { get; private set; }

    /// <param name="fadeOut">맞은 뒤 화면이 완전히 어두워지기까지의 시간(초).</param>
    /// <param name="blackHold">어두운 상태로 머무는 시간(초). 이때 옮긴다.</param>
    /// <param name="fadeIn">다시 밝아지는 시간(초).</param>
    public IEnumerator Run(PlayerController player, Vector3 destination, bool effect, float fadeOut, float blackHold, float fadeIn)
    {
        IsRunning = true;
        // 입력을 막아 그 자리에 멈추고 화면을 빠르게 어둡게 한다.
        if (UIManager.Instance != null)
        {
            UIManager.Instance.PushGameplayBlock();
            blocking = true;
            UIManager.Instance.StartScreenFade(1f, fadeOut);
        }
        player.Velocity = Vector2.zero;
        // 맞은 반동(외부 힘)도 멈춰 암전 동안 밀려나지 않게 한다. 입력을 막아도 반동은 물리 단계마다 더해지기 때문이다.
        player.IgnoreExternalForces = true;
        player.externalForce = Vector2.zero;
        holdingForces = true;
        yield return new WaitForSeconds(fadeOut);

        // 어두운 동안 옮기고, 카메라가 먼 거리를 끌려오지 않게 바로 따라가게 한다.
        WavePlayerStart.Relocate(player, destination, effect);
        yield return new WaitForSeconds(blackHold);

        Recover(fadeIn);
    }

    /// <summary>다시 밝게 하고 막았던 입력을 돌려준다. 회수·비활성화·사망으로 옮기기가 끊겨도 화면과 입력이 남지 않게 한다.</summary>
    public void Recover(float fadeIn)
    {
        if (blocking && UIManager.Instance != null)
        {
            UIManager.Instance.StartScreenFade(0f, fadeIn);
            UIManager.Instance.PopGameplayBlock();
        }
        blocking = false;
        IsRunning = false;
        // 남은 반동 없이 외부 힘을 다시 받게 해 옮긴 자리에서 밀려나지 않게 한다.
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (holdingForces && player != null)
        {
            player.externalForce = Vector2.zero;
            player.IgnoreExternalForces = false;
        }
        holdingForces = false;
    }
}
