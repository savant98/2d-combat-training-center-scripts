using System;
using System.Collections;
using UnityEngine;

/// <summary>환경 장치의 실제 등장·퇴장 Clip을 재생하고 충돌·작동·풀 반환 시점을 함께 관리한다.</summary>
[DisallowMultipleComponent]
public sealed class WaveEnvironmentLifecycle : MonoBehaviour
{
    public Transform visualRoot;
    public Animator animator;
    public AnimationClip spawnAnimation;
    public AnimationClip despawnAnimation;
    [Min(0f)] public float spawnDuration = .5f;
    [Min(0f)] public float despawnDuration = .5f;
    public string readyState = "Ready";

    public bool IsReady { get; private set; }
    public bool IsAppearing { get; private set; }
    public bool IsDespawning { get; private set; }
    public bool HasSpawned { get; private set; }
    public float PhaseProgress { get; private set; }

    private Collider2D[] colliders = Array.Empty<Collider2D>();
    private bool[] collisionRest = Array.Empty<bool>();
    private Vector3 restScale = Vector3.one;
    private bool animatorRestEnabled;
    private bool cached;
    private Transform[] poseTargets = Array.Empty<Transform>();
    private LocalPose[] restPoses = Array.Empty<LocalPose>();
    private LocalPose[] initialPoses = Array.Empty<LocalPose>();
    private LocalPose[] clipStartPoses = Array.Empty<LocalPose>();
    private LocalPose[] clipEndPoses = Array.Empty<LocalPose>();
    private PoseChannels[] animatedChannels = Array.Empty<PoseChannels>();

    private struct LocalPose
    {
        public Vector3 Position, Scale;
        public Quaternion Rotation;
    }

    private struct PoseChannels
    {
        public Vector3 Position, Scale;
        public bool Rotation;
    }
    private uint generation;
    private Coroutine sequence;
    private Action readyCallback;
    private Action returnCallback;

    private void Awake()
    {
        if (Application.isPlaying) CacheReferences();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        CacheReferences();
        // 풀을 확장하기 위한 Instantiate는 여기까지만 실행한다. 실제 등장은 OnSpawnedFromPool이 시작한다.
        RestoreRestPoses();
        ResetFlags();
        SetCollisions(false);
    }

    public void BeginSpawn(Action ready = null)
    {
        CacheReferences();
        CancelSequence();
        readyCallback = ready;
        returnCallback = null;
        HasSpawned = true;
        IsAppearing = true;
        IsReady = IsDespawning = false;
        PhaseProgress = 0f;
        SetCollisions(false);
        PauseAnimator();
        RestoreRestPoses();
        float duration = SafeDuration(spawnDuration);
        if (!CanAnimate() || duration <= 0f)
        {
            CompleteSpawn(generation);
            return;
        }
        SampleSpawn(0f);
        sequence = StartCoroutine(Appear(generation, duration));
    }

    public void BeginDespawn(Action returned)
    {
        if (IsDespawning) return;
        CacheReferences();
        CancelSequence();
        readyCallback = null;
        returnCallback = returned;
        IsReady = IsAppearing = false;
        IsDespawning = true;
        PhaseProgress = 0f;
        SetCollisions(false);
        PauseAnimator();
        float duration = SafeDuration(despawnDuration);
        if (!CanAnimate() || duration <= 0f)
        {
            CompleteDespawn(generation);
            return;
        }
        // 등장 중 취소해도 날·레일·기어가 완성 자세로 뛰지 않도록 모든 시각 부품의 현재 자세를 잇는다.
        CapturePoses(initialPoses);
        ReadDespawnEndpoints();
        ApplyPoses(initialPoses);
        sequence = StartCoroutine(Disappear(generation, duration));
    }

    private IEnumerator Appear(uint token, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (token != generation) yield break;
            PhaseProgress = Mathf.Clamp01(elapsed / duration);
            SampleSpawn(PhaseProgress);
            yield return null;
            elapsed += Time.deltaTime;
        }
        CompleteSpawn(token);
    }

    private IEnumerator Disappear(uint token, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (token != generation) yield break;
            PhaseProgress = Mathf.Clamp01(elapsed / duration);
            SampleDespawn(PhaseProgress);
            yield return null;
            elapsed += Time.deltaTime;
        }
        SampleDespawn(1f);
        CompleteDespawn(token);
    }

    private void CompleteSpawn(uint token)
    {
        if (token != generation) return;
        sequence = null;
        RestoreRestPoses();
        PhaseProgress = 1f;
        IsAppearing = IsDespawning = false;
        IsReady = true;
        SetCollisions(true);
        ResumeReadyAnimator();
        var callback = readyCallback;
        readyCallback = null;
        callback?.Invoke();
    }

    private void CompleteDespawn(uint token)
    {
        if (token != generation) return;
        sequence = null;
        PhaseProgress = 1f;
        IsReady = IsAppearing = IsDespawning = false;
        HasSpawned = false;
        var callback = returnCallback;
        returnCallback = null;
        callback?.Invoke();
    }

    private void SampleSpawn(float progress)
    {
        if (spawnAnimation != null)
            spawnAnimation.SampleAnimation(gameObject, spawnAnimation.length * Mathf.Clamp01(progress));
        else if (visualRoot != null)
            visualRoot.localScale = restScale * Mathf.Lerp(.001f, 1f, Mathf.SmoothStep(0, 1, progress));
    }

    private void ReadDespawnEndpoints()
    {
        // 기어의 대기 중 회전 등 Clip 밖의 움직임이 기준 자세에 섞이지 않게 한다.
        RestoreRestPoses();
        if (despawnAnimation != null)
        {
            despawnAnimation.SampleAnimation(gameObject, 0f);
            CapturePoses(clipStartPoses);
            despawnAnimation.SampleAnimation(gameObject, despawnAnimation.length);
            CapturePoses(clipEndPoses);
        }
        else
        {
            CapturePoses(clipStartPoses);
            CapturePoses(clipEndPoses);
            if (poseTargets.Length > 0) clipEndPoses[0].Scale = restScale * .001f;
        }
        Array.Clear(animatedChannels, 0, animatedChannels.Length);
        MarkAnimatedChannels(clipStartPoses);
        MarkAnimatedChannels(clipEndPoses);
        // 시작·끝이 같아도 중간에 움직이는 닫힌 곡선을 지원한다.
        if (despawnAnimation == null) return;
        for (int sample = 1; sample <= 3; sample++)
        {
            despawnAnimation.SampleAnimation(gameObject, despawnAnimation.length * sample * .25f);
            for (int i = 0; i < poseTargets.Length; i++)
                if (poseTargets[i] != null) MarkAnimatedChannels(i, ReadPose(poseTargets[i]));
        }
    }

    private void SampleDespawn(float progress)
    {
        if (visualRoot == null) return;
        if (despawnAnimation != null)
            despawnAnimation.SampleAnimation(gameObject, despawnAnimation.length * Mathf.Clamp01(progress));
        float eased = Mathf.SmoothStep(0, 1, progress);
        for (int i = 0; i < poseTargets.Length; i++)
        {
            var target = poseTargets[i];
            if (target == null) continue;
            var first = clipStartPoses[i];
            var last = clipEndPoses[i];
            var initial = initialPoses[i];
            var channels = animatedChannels[i];
            var sampled = despawnAnimation != null ? ReadPose(target) : new LocalPose
            {
                Position = Vector3.LerpUnclamped(first.Position, last.Position, eased),
                Scale = Vector3.LerpUnclamped(first.Scale, last.Scale, eased),
                Rotation = Quaternion.SlerpUnclamped(first.Rotation, last.Rotation, eased)
            };
            target.localPosition = MapPose(sampled.Position, first.Position, last.Position, initial.Position, channels.Position, progress);
            target.localScale = MapPose(sampled.Scale, first.Scale, last.Scale, initial.Scale, channels.Scale, progress);
            target.localRotation = channels.Rotation
                ? MapRotation(sampled.Rotation, first.Rotation, last.Rotation, initial.Rotation, progress)
                : initial.Rotation;
        }
    }

    private void MarkAnimatedChannels(LocalPose[] poses)
    {
        for (int i = 0; i < poses.Length; i++) MarkAnimatedChannels(i, poses[i]);
    }

    private void MarkAnimatedChannels(int index, LocalPose pose)
    {
        var channels = animatedChannels[index];
        var rest = restPoses[index];
        channels.Position = MarkChannels(channels.Position, pose.Position - rest.Position);
        channels.Scale = MarkChannels(channels.Scale, pose.Scale - rest.Scale);
        channels.Rotation |= Quaternion.Angle(pose.Rotation, rest.Rotation) > .0001f;
        animatedChannels[index] = channels;
    }

    private static Vector3 MarkChannels(Vector3 channels, Vector3 difference)
        => new Vector3(Mathf.Abs(difference.x) > .000001f ? 1 : channels.x,
            Mathf.Abs(difference.y) > .000001f ? 1 : channels.y,
            Mathf.Abs(difference.z) > .000001f ? 1 : channels.z);

    private static Vector3 MapPose(Vector3 sampled, Vector3 first, Vector3 last,
        Vector3 initial, Vector3 channels, float progress)
    {
        float Channel(float value, float start, float end, float from, float animated)
        {
            if (animated == 0f) return from;
            float difference = end - start;
            if (Mathf.Abs(difference) <= .000001f)
                return Mathf.LerpUnclamped(from, end, progress) + value - start;
            return Mathf.LerpUnclamped(from, end, (value - start) / difference);
        }
        return new Vector3(Channel(sampled.x, first.x, last.x, initial.x, channels.x),
            Channel(sampled.y, first.y, last.y, initial.y, channels.y),
            Channel(sampled.z, first.z, last.z, initial.z, channels.z));
    }

    private static Quaternion MapRotation(Quaternion sampled, Quaternion first, Quaternion last,
        Quaternion initial, float progress)
    {
        var delta = Quaternion.Inverse(first) * last;
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) { angle = 360f - angle; axis = -axis; }
        if (angle <= .0001f)
            return Quaternion.SlerpUnclamped(initial, last, progress) * (Quaternion.Inverse(first) * sampled);
        var relative = Quaternion.Inverse(first) * sampled;
        // 같은 회전을 나타내는 q와 -q 중 짧은 회전을 택해 0/360도 경계에서도 자세를 잇는다.
        if (relative.w < 0f) relative = new Quaternion(-relative.x, -relative.y, -relative.z, -relative.w);
        float sampledAngle = 2f * Mathf.Atan2(Vector3.Dot(new Vector3(relative.x, relative.y, relative.z), axis), relative.w) * Mathf.Rad2Deg;
        return Quaternion.SlerpUnclamped(initial, last, sampledAngle / angle);
    }

    private static LocalPose ReadPose(Transform target)
        => new LocalPose { Position = target.localPosition, Rotation = target.localRotation, Scale = target.localScale };

    private void CapturePoses(LocalPose[] poses)
    {
        for (int i = 0; i < poseTargets.Length; i++)
            if (poseTargets[i] != null) poses[i] = ReadPose(poseTargets[i]);
    }

    private void ApplyPoses(LocalPose[] poses)
    {
        for (int i = 0; i < poseTargets.Length; i++)
        {
            var target = poseTargets[i];
            if (target == null) continue;
            target.localPosition = poses[i].Position;
            target.localRotation = poses[i].Rotation;
            target.localScale = poses[i].Scale;
        }
    }

    private void RestoreRestPoses() => ApplyPoses(restPoses);

    private void CacheReferences()
    {
        if (cached) return;
        if (visualRoot == null) visualRoot = transform.Find("DesignV3");
        if (animator == null) animator = GetComponent<Animator>();
        if (visualRoot != null && visualRoot != transform && visualRoot.IsChildOf(transform))
        {
            restScale = visualRoot.localScale;
        }
        else visualRoot = null;
        poseTargets = visualRoot != null ? visualRoot.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
        restPoses = new LocalPose[poseTargets.Length];
        initialPoses = new LocalPose[poseTargets.Length];
        clipStartPoses = new LocalPose[poseTargets.Length];
        clipEndPoses = new LocalPose[poseTargets.Length];
        animatedChannels = new PoseChannels[poseTargets.Length];
        CapturePoses(restPoses);
        animatorRestEnabled = animator != null && animator.enabled;
        colliders = GetComponentsInChildren<Collider2D>(true);
        collisionRest = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) collisionRest[i] = colliders[i] != null && colliders[i].enabled;
        cached = true;
    }

    private void SetCollisions(bool ready)
    {
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = ready && collisionRest[i];
    }

    private bool CanAnimate() => Application.isPlaying && isActiveAndEnabled
        && gameObject.activeInHierarchy && visualRoot != null;
    private static float SafeDuration(float value) => float.IsFinite(value) ? Mathf.Max(0f, value) : 0f;

    private void PauseAnimator()
    {
        // Clip을 직접 샘플링하므로 Animator의 자동 갱신과 기어·케이블의 개별 구동이 경합하지 않는다.
        if (animator != null) animator.enabled = false;
    }

    private void ResumeReadyAnimator()
    {
        if (animator == null) return;
        animator.enabled = animatorRestEnabled;
        if (!animator.enabled || animator.runtimeAnimatorController == null || !animator.gameObject.activeInHierarchy) return;
        int state = Animator.StringToHash(readyState);
        if (!animator.HasState(0, state)) state = Animator.StringToHash("Base Layer." + readyState);
        if (animator.HasState(0, state)) { animator.Play(state, 0, 0f); animator.Update(0f); }
    }

    private void CancelSequence()
    {
        generation++;
        if (sequence != null) StopCoroutine(sequence);
        sequence = null;
        readyCallback = null;
    }

    private void ResetFlags()
    {
        IsReady = IsAppearing = IsDespawning = HasSpawned = false;
        PhaseProgress = 0f;
    }

    private void OnDisable()
    {
        if (!Application.isPlaying || !cached) return;
        var completion = returnCallback;
        returnCallback = null;
        CancelSequence();
        ResetFlags();
        SetCollisions(false);
        RestoreRestPoses();
        if (animator != null) animator.enabled = animatorRestEnabled;
        // 부모 비활성화나 종료 중에는 코루틴이 더 진행되지 않으므로 대기 중인 반환을 즉시 마친다.
        completion?.Invoke();
    }
}
