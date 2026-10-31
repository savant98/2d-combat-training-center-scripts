using UnityEngine;
using System.Collections;

public class EnemySpawnController : MonoBehaviour
{
    [Header("Feedback")]
    [SerializeField] private FeedbackDataSO spawnFeedback;
    [SerializeField] private Transform spawnFeedbackTr;
    [SerializeField] private GameObject laserParticle;

    [Header("Reveal Loop Sound")]
    [SerializeField] private SoundOptions revealLoopSound;

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

    private void Awake()
    {
        if (revealMask != null) initialMaskPosition = revealMask.transform.localPosition;
        if (laserLine != null) initialLaserPosition = laserLine.transform.localPosition;
        initialMaskInteractions = new SpriteMaskInteraction[maskOffSprites == null ? 0 : maskOffSprites.Length];
        for (int i = 0; i < initialMaskInteractions.Length; i++)
            if (maskOffSprites[i] != null) initialMaskInteractions[i] = maskOffSprites[i].maskInteraction;
        if (maskLine != null) initialLineMaskInteraction = maskLine.maskInteraction;
    }

    public void ResetSpawnSequence()
    {
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
    }


    private void OnDisable()
    {
        ResetSpawnSequence();
    }

    public IEnumerator PlaySpawnSequence()
    {
        // 이전 생성 연출 도중 남은 소리가 있을 경우 정리
        StopRevealLoopSound();

        if (animationOnly)
        {
            yield return new WaitForSeconds(spareTime);
            yield break;
        }

        // Mask 초기화
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

        yield return new WaitForSeconds(spareTime);
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