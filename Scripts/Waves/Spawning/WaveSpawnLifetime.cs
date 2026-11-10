using System;
using System.Collections;
using UnityEngine;

// 풀 인스턴스가 비활성화되면 수명을 취소하여 다음 대여에 이전 타이머가 개입하지 않는다.
public sealed class WaveSpawnLifetime : MonoBehaviour
{
    public void Arm(float seconds, Action expired)
    {
        StopAllCoroutines();
        if (seconds > 0) StartCoroutine(Wait(seconds, expired));
    }
    private IEnumerator Wait(float seconds, Action expired)
    {
        yield return new WaitForSeconds(seconds);
        expired();
    }
    private void OnDisable() => StopAllCoroutines();
}
