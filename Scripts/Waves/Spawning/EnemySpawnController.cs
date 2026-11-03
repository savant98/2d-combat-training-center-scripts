using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;

public class EnemySpawnController : MonoBehaviour
{
    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO spawnFeedback;
    [SerializeField] private Transform spawnFeedbackTr;
    [SerializeField] private GameObject laserParticle;

    [Header("Reveal Loop Sound")]
    [SerializeField] private SoundOptions revealLoopSound;

    // 전용 BGM이 있는 보스(예: Nightmare)만 채운다. 비워 두면 방의 RoomBgmPlayer가 공용 보스 BGM을 재생한다.
    [Header("Dedicated Boss Music")]
    [SerializeField] private AudioClip bossBgmPrelude;
    [SerializeField] private AudioClip bossBgmLoop;
    [SerializeField, Min(0f), Tooltip("전주가 끝나고 메인으로 넘어가는 시각(초)입니다.")]
    private float bossBgmLoopStart = 16.2f;
    [SerializeField, Min(0f), Tooltip("메인을 처음 시작할 위치(초)입니다. 전주가 메인의 도입부를 대신할 때 쓰며, 반복은 메인 전체를 돕니다.")]
    private float bossBgmLoopOffset;
    [SerializeField, Range(0f, 1f)] private float bossBgmVolume = .38f;
    private int bossBgmPlaybackId;

    public bool HasDedicatedBossMusic => bossBgmPrelude != null && bossBgmLoop != null;

    [Header("Reveal Settings")]
    [SerializeField] private bool animationOnly = false;
    [SerializeField] private SpriteMask revealMask;
    [SerializeField] private GameObject laserLine;
    [SerializeField] private Transform maskStart;
    [SerializeField] private Transform maskEnd;
    [SerializeField] private float revelDuration = 1.0f; // 등장 애니메이션 시간
    [SerializeField] private float spareTime = 0f;

    [Header("MaskOff Sprites")]
    [SerializeField] private SpriteRenderer[] maskOffSprites;
    [SerializeField] private LineRenderer maskLine;

    private SoundHandle revealLoopHandle;
    private Vector3 initialMaskPosition;
    private Vector3 initialLaserPosition;
    private SpriteMaskInteraction[] initialMaskInteractions;
    private SpriteMaskInteraction initialLineMaskInteraction;
    // SpriteMask는 기본적으로 겹치는 모든 Sprite에 적용된다. 생성 연출 동안 이 적의 Renderer를 하나의 Sorting Group으로 묶어
    // 같은 자리에 함께 생성된 다른 적의 Mask가 아직 드러나지 않은 이 적의 Sprite를 미리 보여 주지 않게 한다.
    // 평소 그리기 순서를 바꾸지 않도록 연출이 끝나면 끈다.
    private SortingGroup revealSortingGroup;

    private void Awake()
    {
        if (revealMask != null) initialMaskPosition = revealMask.transform.localPosition;
        if (laserLine != null) initialLaserPosition = laserLine.transform.localPosition;
        initialMaskInteractions = new SpriteMaskInteraction[maskOffSprites == null ? 0 : maskOffSprites.Length];
        for (int i = 0; i < initialMaskInteractions.Length; i++)
            if (maskOffSprites[i] != null) initialMaskInteractions[i] = maskOffSprites[i].maskInteraction;
        if (maskLine != null) initialLineMaskInteraction = maskLine.maskInteraction;
        if (!animationOnly && revealMask != null && GetComponent<SortingGroup>() == null)
        {
            revealSortingGroup = gameObject.AddComponent<SortingGroup>();
            revealSortingGroup.enabled = false;
        }
    }

    public bool IsRevealIsolated => revealSortingGroup != null && revealSortingGroup.enabled;

    private void SetRevealIsolation(bool active)
    {
        if (revealSortingGroup == null) return;
        if (active)
        {
            // 본체가 평소 그려지는 순서를 그대로 써서 주변 오브젝트와의 앞뒤 관계를 유지한다.
            var reference = maskOffSprites != null && maskOffSprites.Length > 0 ? maskOffSprites[0] : null;
            if (reference != null)
            {
                revealSortingGroup.sortingLayerID = reference.sortingLayerID;
                revealSortingGroup.sortingOrder = reference.sortingOrder;
            }
        }
        revealSortingGroup.enabled = active;
    }

    public void ResetSpawnSequence()
    {
        StopBossMusic();
        StopRevealLoopSound();
        if (laserLine != null)
        {
            laserLine.SetActive(false);
            laserLine.transform.localPosition = initialLaserPosition;
        }
        if (laserParticle != null)
        {
            foreach (var particle in laserParticle.GetComponentsInChildren<ParticleSystem>(true))
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            laserParticle.SetActive(false);
        }
        if (revealMask != null)
        {
            revealMask.enabled = false;
            revealMask.transform.localPosition = initialMaskPosition;
        }
        if (initialMaskInteractions != null)
            for (int i = 0; i < initialMaskInteractions.Length; i++)
                if (maskOffSprites[i] != null) maskOffSprites[i].maskInteraction = initialMaskInteractions[i];
        if (maskLine != null) maskLine.maskInteraction = initialLineMaskInteraction;
        SetRevealIsolation(false);
    }


    private void OnDisable()
    {
        ResetSpawnSequence();
    }

    public IEnumerator PlaySpawnSequence()
    {
        // 이전 생성 연출 도중 남은 소리가 있을 경우 정리
        StopRevealLoopSound();
        StopBossMusic();
        if (SoundManager.Instance != null && HasDedicatedBossMusic)
            bossBgmPlaybackId = SoundManager.Instance.PlayBGMWithPrelude(bossBgmPrelude, bossBgmLoop, bossBgmLoopStart, bossBgmVolume, bossBgmLoopOffset);

        if (animationOnly)
        {
            yield return new WaitForSeconds(spareTime);
            yield break;
        }

        // Mask 초기화
        SetRevealIsolation(true);
        revealMask.enabled = true;
        revealMask.transform.localPosition = maskStart.localPosition;

        if (maskOffSprites.Length > 0)
        {
            foreach (SpriteRenderer sprite in maskOffSprites)
                sprite.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }

        if (maskLine != null)
            maskLine.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        // 스폰 시작 순간 피드백
        if (spawnFeedbackTr != null)
            FeedbackPlayer.Instance.Play(spawnFeedback, spawnFeedbackTr);
        else
            FeedbackPlayer.Instance.Play(spawnFeedback, transform.position);

        // 등장 전 대기: 포탈 효과가 재생되는 구간
        yield return new WaitForSeconds(spawnFeedback.lifeTime * 0.5f);

        // 실제로 아래에서 위로 생성되는 연출 시작
        laserLine.SetActive(true);
        laserParticle.SetActive(true);

        StartRevealLoopSound();

        float t = 0f;

        while (t < revelDuration)
        {
            t += Time.deltaTime;
            float normalized = Mathf.Clamp01(t / revelDuration);

            // 위치 이동
            revealMask.transform.localPosition = Vector3.Lerp(
                maskStart.localPosition,
                maskEnd.localPosition,
                normalized);

            laserLine.transform.position = revealMask.transform.position;

            yield return null;
        }

        // 생성 연출 종료
        StopRevealLoopSound();

        laserLine.SetActive(false);
        laserParticle.SetActive(false);
        revealMask.transform.localPosition = Vector3.zero;

        if (maskOffSprites.Length > 0)
        {
            foreach (SpriteRenderer sprite in maskOffSprites)
                sprite.maskInteraction = SpriteMaskInteraction.None;

            revealMask.enabled = false;
        }

        if (maskLine != null)
            maskLine.maskInteraction = SpriteMaskInteraction.None;

        SetRevealIsolation(false);
        yield return new WaitForSeconds(spareTime);
    }

    // 다른 음악이 시작된 뒤에는 이전 보스의 정리 요청으로 그 곡을 멈추지 않는다.
    public void StopBossMusic()
    {
        if (SoundManager.Instance != null && bossBgmPlaybackId != 0)
            SoundManager.Instance.StopBGM(bossBgmPlaybackId);
        bossBgmPlaybackId = 0;
    }

    private void StartRevealLoopSound()
    {
        StopRevealLoopSound();

        if (revealLoopSound == null || revealLoopSound.clip == null)
            return;

        if (SoundManager.Instance == null || revealMask == null)
            return;

        revealLoopHandle = SoundManager.Instance.PlayLoop(
            revealLoopSound,
            revealMask.transform.position,
            revealMask.transform);
    }

    private void StopRevealLoopSound()
    {
        revealLoopHandle?.Stop();
        revealLoopHandle = null;
    }
}