using UnityEngine;

public class PlayerCounterState : MonoBehaviour
{
    public bool IsWaiting { get; private set; }
    public bool IsSuccess { get; private set; }
    /// <summary>반격 성공 무적이 켜져 있는가(CounterShield 잔광 표시가 읽는다).</summary>
    public bool IsProtected => protectedHealth != null;
    /// <summary>반격 성공 무적의 남은 시간(초).</summary>
    public float ProtectionRemaining => protectedHealth != null ? Mathf.Max(0f, protectionEndTime - Time.time) : 0f;

    private System.Action<Vector3> onSuccess;
    private PlayerHealth protectedHealth;
    private bool savedInvulnerability;
    private float protectionEndTime;         // 무적이 끝나는 시각
    /// <summary>
    /// 반격 성공 무적을 지금부터 duration초 동안 켠다. 공격 상태와 무관하게 이어지므로 회수 동작이 끝나도(대시로 취소해도) 유지되어,
    /// 몸으로 부딪히는 공격을 반격한 뒤 판정 밖으로 빠져나갈 시간을 준다.
    /// 반격 창 자체에는 무적을 걸지 않는다. 걸어 두면 IsInvincible을 먼저 검사하는 공격(탄환·유도탄·던진 큐브·충격파·장판)이
    /// TakeDamage까지 오지 못해 반격이 발동되지 않는다. 창이 열린 동안의 피해는 TryCounter가 모두 받아낸다.
    /// </summary>
    public void BeginInvulnerability(PlayerHealth health, float duration)
    {
        protectionEndTime = Time.time + duration;
        if (protectedHealth != null) return;
        protectedHealth = health; savedInvulnerability = health.IsInvincible; health.IsInvincible = true;
    }
    public void EndInvulnerability()
    {
        if (protectedHealth == null) return;
        if (protectedHealth.currentHP > 0) protectedHealth.IsInvincible = savedInvulnerability;
        protectedHealth = null;
    }
    private void Update()
    {
        if (protectedHealth != null && Time.time >= protectionEndTime) EndInvulnerability();
    }
    private void OnDisable() => EndInvulnerability();

    public void StartCounter(System.Action<Vector3> onSuccess)
    {
        IsWaiting = true;
        IsSuccess = false;
        this.onSuccess = onSuccess;
    }

    public bool TryCounter(Vector3 hitPosition)
    {
        if (!IsWaiting)
            return false;

        IsWaiting = false;
        IsSuccess = true;

        onSuccess?.Invoke(hitPosition);
        return true;
    }

    public void EndCounter()
    {
        IsWaiting = false;
        IsSuccess = false;
        onSuccess = null;
    }
}