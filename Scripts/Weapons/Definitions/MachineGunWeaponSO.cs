using System.Collections;
using UnityEngine;

// Rifle Gear 3단계 '머신건 장착' 동안 기본 공격 자리에 들어가는 머신건(2026-11-18 2차).
// 사용자 지시: "머신건 사용시에는 약간의 탄 흩어짐이 있어서 탄착군을 형성하도록 해." "머신건 탄의 생김새를 더 크고 강하게 강화해줘."
// 연사 흐름(한 사이클 count발 · interval 간격 · 조준 방향)은 RangedWeaponSO를 쓰고, 격발마다 탄 퍼짐과 총구 효과를 적용한다.
// - 흩어짐: PlayerStarShotAim이 총신은 조준 방향에 고정한 채 탄 방향만 Scatter로 돌린다(탄마다 총신이 떨리지 않는다).
//   각도는 정규분포(표준편차 σ)이고 maxSpread도에서 자른다. 계속 쏘면 σ가 spreadSigmaStart에서 spreadSigma까지 spreadRampTime초에 걸쳐 커진다
//   (첫 몇 발은 모이고, 이어 쏘면 탄착군이 조금 넓어진다). 쏘는 사이가 spreadResetGap초보다 벌어지면 처음부터 다시 잰다.
// - 총구 섬광: muzzleFlash(청록)를 PlayerStarShotAim이 총구 자세(StarMuzzlePoint)로 재생한다. Rifle StarShot의 분홍 섬광(Player.prefab의 StarGunMuzzle 키)은 내지 않는다.
// 판정·피해·탄 Prefab은 RangedWeaponSO 필드 그대로다(MachineGun.asset은 RangedWeaponSO에서 이 클래스로 스크립트만 바꿔 GUID·값을 유지한다).
// - 연사 반복음(2026-11-21 4차, 사용자 지정 Freesound 212602 'Machine Gun 001 - loop'): fireLoop에 소리가 있으면 연사 사이클이 시작될 때
//   MachineGunFireLoop.Begin을 불러 연사하는 동안 끊김 없이 반복하고, 연사를 멈추면 지금 발의 끝까지 들려 준 뒤 끝낸다.
//   반복음을 쓰면 탄마다 내는 shotSounds는 비워 둔다(PlayerStarShotAim이 shotSounds가 있으면 탄마다 따로 낸다 — 겹쳐 두 번 나지 않게).
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
    [Tooltip("실제로 발사한 탄마다 순서대로 재생하는 격발음(연사 반복음 fireLoop를 쓰면 비운다)")]
    public SoundOptions[] shotSounds = System.Array.Empty<SoundOptions>();

    [Header("Fire Loop")]
    [Tooltip("연사하는 동안 끊김 없이 반복할 발사음(SoundManager.PlayLoop). 비우면 탄마다 shotSounds만 낸다")]
    public SoundOptions fireLoop = new();
    [Tooltip("반복음 한 바퀴에 든 발 수. 연사를 멈추면 지금 발이 끝나는 곳(다음 발 직전)까지 들려 준 뒤 끝낸다")]
    [Min(1)] public int fireLoopShots = 3;
    [Tooltip("연사를 멈춘 뒤 다음 발 직전까지 줄여 끝내는 시간(초). 다음 발까지 남은 시간이 이보다 짧으면 남은 시간 안에 줄인다")]
    [Min(0.005f)] public float fireLoopFadeOut = 0.03f;

    // 연사 사이클(실행 단계)마다 불린다. 반복음은 첫 사이클에서 시작하고 이어지는 사이클에서는 그대로 이어 간다.
    // 반복음 시작은 같은 프레임의 첫 발(PlayerStarShotAim.LateUpdate)과 맞는다 — 반복음도 발 시작부에서 시작한다.
    public override IEnumerator WeaponUseSequence(CharacterController2D controller, System.Action forceRecover = null)
    {
        if (fireLoop != null && fireLoop.clip != null && controller is PlayerController player)
            MachineGunFireLoop.Begin(player, this);
        return base.WeaponUseSequence(controller, forceRecover);
    }

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
