using UnityEngine;

// Rifle Gear 3단계 '머신건 장착' 동안 기본 공격 자리에 들어가는 머신건(2026-11-18 2차).
// 사용자 지시: "머신건 사용시에는 약간의 탄 흩어짐이 있어서 탄착군을 형성하도록 해." "머신건 탄의 생김새를 더 크고 강하게 강화해줘."
// 연사 흐름(한 사이클 count발 · interval 간격 · 조준 방향)은 RangedWeaponSO 그대로이고, 이 클래스는 탄 방향의 흩어짐과 총구 섬광만 더한다.
// - 흩어짐: PlayerStarShotAim이 총신은 조준 방향에 고정한 채 탄 방향만 Scatter로 돌린다(탄마다 총신이 떨리지 않는다).
//   각도는 정규분포(표준편차 σ)이고 maxSpread도에서 자른다. 계속 쏘면 σ가 spreadSigmaStart에서 spreadSigma까지 spreadRampTime초에 걸쳐 커진다
//   (첫 몇 발은 모이고, 이어 쏘면 탄착군이 조금 넓어진다). 쏘는 사이가 spreadResetGap초보다 벌어지면 처음부터 다시 잰다.
// - 총구 섬광: muzzleFlash(청록)를 PlayerStarShotAim이 총구 자세(StarMuzzlePoint)로 재생한다. Rifle StarShot의 분홍 섬광(Player.prefab의 StarGunMuzzle 키)은 내지 않는다.
// 판정·피해·탄 Prefab은 RangedWeaponSO 필드 그대로다(MachineGun.asset은 RangedWeaponSO에서 이 클래스로 스크립트만 바꿔 GUID·값을 유지한다).
[CreateAssetMenu(menuName = "Weapon/Machine Gun")]
public class MachineGunWeaponSO : RangedWeaponSO
{
    [Header("Spread")]
    [Tooltip("연사를 시작할 때 탄 각도의 표준편차(도)")]
    [Min(0f)] public float spreadSigmaStart = 0.8f;
    [Tooltip("계속 쏠 때 탄 각도의 표준편차(도)")]
    [Min(0f)] public float spreadSigma = 1.8f;
    [Tooltip("표준편차가 시작 값에서 계속 쏠 때 값까지 커지는 시간(초)")]
    [Min(0.01f)] public float spreadRampTime = 0.4f;
    [Tooltip("조준 방향에서 벗어나는 최대 각도(도)")]
    [Min(0f)] public float maxSpread = 4f;
    [Tooltip("앞 발과 이 시간(초)보다 벌어지면 연사를 새로 시작한 것으로 본다(사이클 사이 0.07초 안팎은 이어진 연사)")]
    [Min(0.01f)] public float spreadResetGap = 0.15f;

    [Header("Muzzle")]
    [Tooltip("탄마다 총구 자세로 재생하는 섬광(청록). 비우면 Rifle StarShot과 같은 분홍 섬광(StarGunMuzzle)")]
    public FeedbackDataSO muzzleFlash;

    // 연사를 이어 온 시간(초)에 맞는 표준편차(도)
    public float SigmaAt(float sustained) =>
        Mathf.Lerp(spreadSigmaStart, spreadSigma, Mathf.Clamp01(sustained / spreadRampTime));

    // 조준 방향(단위 벡터)에서 정규분포 각도만큼 돌린 탄 방향. 연사를 이어 온 시간(초)으로 표준편차를 정한다.
    public Vector2 Scatter(Vector2 aim, float sustained)
    {
        float sigma = SigmaAt(sustained);
        if (sigma <= 0f || maxSpread <= 0f || aim.sqrMagnitude < 0.000001f)
            return aim;
        // Box-Muller: 균등 난수 두 개로 표준 정규분포 값 하나를 만든다(1 - value는 0이 되지 않게 아래를 막는다).
        float u1 = Mathf.Max(0.000001f, 1f - Random.value);
        float u2 = Random.value;
        float z = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        float degrees = Mathf.Clamp(z * sigma, -maxSpread, maxSpread);
        return Quaternion.Euler(0f, 0f, degrees) * aim;
    }
}
