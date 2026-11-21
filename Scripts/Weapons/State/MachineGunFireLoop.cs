using UnityEngine;

// 기관총 연사 반복음(2026-11-21 4차 요청, 사용자 지정 Freesound 212602 'Machine Gun 001 - loop' → MachineGunFireLoop.wav).
// - 시작: MachineGunWeaponSO.WeaponUseSequence(연사 사이클의 실행 시작)가 Begin을 부른다. 반복음은 발 시작부에서 시작하므로 첫 탄과 같은 순간에 첫 발이 들린다.
//   이어지는 사이클에서는 그대로 이어 간다(사이클 사이 회수 0.01초에 끊기지 않는다). 끝내는 중에 다시 쏘면 처음(발 시작부)부터 다시 낸다.
// - 유지: PlayerWeaponAbility.IsMachineGunAttacking(기관총으로 공격 중 — 키를 누르고 있는 동안 실행·회수·다음 사이클)인 동안.
// - 끝: 연사가 멈추면(키를 뗌·장착 종료·저장 기술 등) 새 발을 시작하지 않는다. 지금 울리는 발을 다음 발 직전까지 들려 주고,
//   그 앞 fireLoopFadeOut초 동안 음량을 줄여 끝낸다 — 탄이 멈춘 뒤 총성이 더 나지 않고, 발 중간에서 뚝 끊기지도 않는다.
//   timeSamples는 오디오 버퍼 단위로 늦게 갱신되므로, 다음 발보다 '한 프레임 반(12~50ms) + 오디오 버퍼 한 개' 앞에서 음량 0이 되게 하고,
//   음량 0을 한 프레임 적용한 뒤에 멈춘다(멈추는 순간 '딱' 소리가 나지 않게).
// - 일시정지(timeScale 0) 동안은 소리를 멈췄다가 풀리면 이어서 낸다. Player가 꺼지면 바로 멈춘다.
// 재생은 SoundManager.PlayLoop(같은 SFX 풀·버스)로 하고 음량은 AudioSource.volume으로만 바꾼다(MachineGunMountVisual 배터리음과 같은 방식).
// Player.prefab은 바꾸지 않는다 — 처음 쏠 때 Player에 이 컴포넌트를 붙인다. 판정·이동과는 관계없다(소리만).
[DisallowMultipleComponent]
public sealed class MachineGunFireLoop : MonoBehaviour
{
    private PlayerWeaponAbility ability;
    private MachineGunWeaponSO weapon;
    private SoundHandle handle;
    private bool releasing, paused;
    private int releaseFrom, boundary;     // 끝내기 시작한 표본 위치, 끝낼 발의 경계(다음 발 시작 표본)
    private float appliedGain = 1f;        // 지난 프레임에 적용한 음량 비율(0을 한 프레임 적용한 뒤에 멈춘다)
    private static float dspBufferSeconds = -1f;

    // 검증·디버그가 읽기만 하는 값
    public bool IsPlaying => handle != null && handle.IsValid;
    public bool IsReleasing => releasing && IsPlaying;
    public AudioClip Clip => IsPlaying ? handle.GetSource().clip : null;
    public float Volume => IsPlaying ? handle.GetSource().volume : 0f;
    public int TimeSamples => IsPlaying ? handle.GetSource().timeSamples : 0;

    public static void Begin(PlayerController player, MachineGunWeaponSO weapon)
    {
        if (player == null || weapon == null || weapon.fireLoop == null || weapon.fireLoop.clip == null || SoundManager.Instance == null) return;
        if (!player.TryGetComponent(out MachineGunFireLoop loop)) loop = player.gameObject.AddComponent<MachineGunFireLoop>();
        loop.Play(player, weapon);
    }

    private void Play(PlayerController player, MachineGunWeaponSO gun)
    {
        ability = player.AbilityManager != null ? player.AbilityManager.AttackAbility : null;
        if (IsPlaying && weapon == gun && handle.GetSource().clip == gun.fireLoop.clip)
        {
            if (!releasing) return;                        // 이어 쏘는 중
            var source = handle.GetSource();               // 끝내는 중에 다시 쐈다: 발 시작부부터 다시
            source.timeSamples = 0;
            source.volume = gun.fireLoop.volume;
            if (paused) { source.UnPause(); paused = false; }
            releasing = false;
            appliedGain = 1f;
            return;
        }
        Stop();
        weapon = gun;
        handle = SoundManager.Instance.PlayLoop(gun.fireLoop, transform.position, transform);
        releasing = paused = false;
        appliedGain = 1f;
    }

    private void Update()
    {
        if (handle == null) return;
        if (!handle.IsValid) { Forget(); return; }        // 장면 전환 등으로 SoundManager가 반복음을 거뒀다
        var source = handle.GetSource();
        bool firing = ability != null && ability.isActiveAndEnabled && ability.IsMachineGunAttacking;
        if (Time.timeScale <= 0f)
        {
            if (!firing) { Stop(); return; }
            if (!paused) { source.Pause(); paused = true; }
            return;
        }
        if (paused) { source.UnPause(); paused = false; }
        if (!releasing && !firing) BeginRelease(source);
        if (releasing) TickRelease(source);
    }

    // 지금 위치가 속한 발의 끝(다음 발 시작)을 경계로 잡는다.
    private void BeginRelease(AudioSource source)
    {
        releasing = true;
        int samples = source.clip.samples;
        int shots = Mathf.Max(1, weapon.fireLoopShots);
        releaseFrom = Mathf.Clamp(source.timeSamples, 0, samples - 1);
        int shot = Mathf.Min(shots - 1, (int)((long)releaseFrom * shots / samples));
        boundary = (int)((long)(shot + 1) * samples / shots);
    }

    private void TickRelease(AudioSource source)
    {
        int now = source.timeSamples;
        // 반복으로 처음으로 돌아갔으면(now < releaseFrom) 이미 경계를 지났다 — 음량을 0으로 두고 다음 프레임에 끝낸다.
        float left = now < releaseFrom ? 0f : (boundary - now) / (float)source.clip.frequency / Mathf.Max(0.01f, Mathf.Abs(source.pitch));
        float guard = Mathf.Clamp(Time.unscaledDeltaTime * 1.5f, 0.012f, 0.05f) + DspBufferSeconds();
        float gain = Mathf.Clamp01((left - guard) / Mathf.Max(0.005f, weapon.fireLoopFadeOut));
        if (gain <= 0f && appliedGain <= 0f) { Stop(); return; }
        appliedGain = gain;
        source.volume = weapon.fireLoop.volume * gain;
    }

    // 오디오 버퍼 한 개의 길이(초). timeSamples가 이만큼 늦을 수 있다.
    private static float DspBufferSeconds()
    {
        if (dspBufferSeconds < 0f)
        {
            AudioSettings.GetDSPBufferSize(out int length, out _);
            int rate = AudioSettings.outputSampleRate;
            dspBufferSeconds = length > 0 && rate > 0 ? Mathf.Min(0.05f, (float)length / rate) : 0.02f;
        }
        return dspBufferSeconds;
    }

    private void Stop()
    {
        if (handle != null && handle.IsValid) handle.Stop();
        Forget();
    }

    private void Forget()
    {
        handle = null;
        releasing = paused = false;
        appliedGain = 1f;
    }

    private void OnDisable() => Stop();
}
