using System;
using TMPro;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.InputSystem;

/// <summary>연속 교본의 재생 위치와 지속 일시정지 상태를 하나의 버튼으로 제어한다.</summary>
public sealed class TerminalTutorialVideo : MonoBehaviour
{
    [Serializable] public sealed class LessonStep { public string title, explanation, keys; public double start, end; }
    [SerializeField] private VideoClip clip;
    [SerializeField] private UnityEngine.UI.RawImage image;
    [SerializeField] private TextMeshProUGUI stateLabel, bindingLabel, timeLabel;
    [SerializeField] private string[] actions;
    [SerializeField] private TerminalVideoSeekBar seekBar;
    [UnityEngine.Serialization.FormerlySerializedAs("pauseButton")]
    [SerializeField] private UnityEngine.UI.Button playbackButton;
    [SerializeField] private UnityEngine.UI.Button restartButton;
    [SerializeField] private TerminalPlaybackButton playbackView;
    [SerializeField] private FeedbackDataSO controlFeedback;
    [SerializeField] private AudioSource audioOutput;
    private VideoPlayer player;
    private RenderTexture texture;
    private bool wantsPlay, seeking, scrubbing, ended, hasPendingSeek, seekCompleted, seekMuted, previousMute, playbackFailed;
    private bool decodedSeekFrame, preparing, hasPrepared, hasDecodedFrame;
    private double pendingTime, activeSeekTime, seekDeadline;
    private InventoryTerminalMenu menu;
    public VideoClip Clip => clip;
    public bool IsPlaying => player != null && player.isPlaying;
    public bool IsSeeking => seeking || hasPendingSeek;
    public bool IsScrubbing => scrubbing;
    public bool IsPaused => !wantsPlay;
    public double Duration => player != null && player.isPrepared ? player.length : clip != null ? clip.length : 0;
    public double Position => ended ? Duration : player != null && player.isPrepared ? player.time : 0;

    private void OnEnable()
    {
        if (clip == null || image == null) return;
        menu = GetComponentInParent<InventoryTerminalMenu>();
        if (player == null)
        {
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false; player.isLooping = false;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.waitForFirstFrame = true; player.skipOnDrop = false;
            player.prepareCompleted += Prepared; player.seekCompleted += SeekCompleted;
            player.errorReceived += Failed; player.loopPointReached += ReachedEnd;
            player.sendFrameReadyEvents = true; player.frameReady += SeekFrameReady;
        }
        player.audioOutputMode = VideoAudioOutputMode.AudioSource; player.controlledAudioTrackCount = 1;
        if (audioOutput == null) { audioOutput = gameObject.AddComponent<AudioSource>(); audioOutput.playOnAwake = false; audioOutput.ignoreListenerPause = true; }
        player.SetTargetAudioSource(0, audioOutput);
        texture = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32); texture.Create();
        image.texture = texture; image.enabled = false; player.clip = clip; player.targetTexture = texture;
        wantsPlay = true; seeking = scrubbing = ended = hasPendingSeek = seekCompleted = playbackFailed = false;
        preparing = hasPrepared = decodedSeekFrame = hasDecodedFrame = false;
        if (seekBar != null) { seekBar.interactable = false; seekBar.SetValueWithoutNotify(0); seekBar.onValueChanged.AddListener(SeekValueChanged); }
        RefreshBindings(); RefreshControls(); EnsurePrepared();
    }
    private void RefreshBindings()
    {
        if (bindingLabel == null || InputManager.Instance == null) return;
        var labels = new System.Collections.Generic.List<string>();
        foreach (string name in actions ?? Array.Empty<string>())
        {
            var action = InputManager.Instance.Actions.FindAction("Player/" + name);
            int index = OptionMenu.FindBinding(action, name == "Move" ? "left" : "");
            if (index >= 0) labels.Add(action.GetBindingDisplayString(index));
            if (name == "Move") { index = OptionMenu.FindBinding(action, "right"); if (index >= 0) labels.Add(action.GetBindingDisplayString(index)); }
        }
        bindingLabel.text = "현재 설정 키  " + string.Join(" / ", labels) + "  ·  영상의 키는 기본 설정 기준";
    }
    private void Prepared(VideoPlayer source)
    {
        if (!isActiveAndEnabled) return;
        preparing = false; hasPrepared = true; image.enabled = true;
        if (hasPendingSeek) ApplyPendingSeek(); else ApplyPlaybackState();
        RefreshControls();
    }
    private void Update()
    {
        if (player == null) return;
        // 콜백 순서나 디코더 종료 상태 때문에 탐색·음소거가 무한히 남지 않게 다시 준비한다.
        if (seeking && Time.realtimeSinceStartupAsDouble > seekDeadline)
        {
            seeking = seekCompleted = decodedSeekFrame = false; hasPendingSeek = true;
            RestoreSeekAudio(); player.Stop(); preparing = false; EnsurePrepared();
        }
        if (!scrubbing && !IsSeeking && seekBar != null) seekBar.SetValueWithoutNotify(Duration > 0 ? (float)(Position / Duration) : 0);
        RefreshControls();
    }
    public void Play()
    {
        if (player == null) return;
        if (playbackFailed) { Replay(); return; }
        PlayCue(); wantsPlay = true;
        if (ended || Position >= Duration - .04) SeekToNormalized(0);
        else ApplyPlaybackState();
        RefreshControls();
    }
    public void Pause()
    {
        if (player == null) return;
        PlayCue(); wantsPlay = false; if (!seeking && player.isPrepared) player.Pause(); RefreshControls();
    }
    public void TogglePlayback() { if (wantsPlay) Pause(); else Play(); }
    public void Replay()
    {
        if (player == null) return;
        wantsPlay = true; PlayCue();
        if (playbackFailed) { playbackFailed = false; player.Stop(); preparing = false; }
        SeekToNormalized(0);
    }
    public void BeginScrub()
    {
        if (player == null || !hasPrepared || playbackFailed) return;
        scrubbing = true; if (!seeking && player.isPrepared) player.Pause(); RefreshControls();
    }
    public void EndScrub()
    {
        if (!scrubbing) return;
        scrubbing = false;
        if (seekBar != null) SeekToNormalized(seekBar.value);
    }
    private void SeekValueChanged(float value)
    {
        if (!isActiveAndEnabled) return;
        if (scrubbing) RefreshControls(); else SeekToNormalized(value);
    }
    public void SeekBySeconds(int seconds)
    {
        if (scrubbing || playbackFailed || Duration <= 0) return;
        double origin = IsSeeking ? pendingTime : Position;
        SeekToTime(origin + seconds);
    }
    public void SeekToNormalized(float value) => SeekToTime(Mathf.Clamp01(value) * Duration);
    private void SeekToTime(double target)
    {
        if (player == null || clip == null || playbackFailed) return;
        double lastFrame = Math.Max(0, Math.Min(Duration - 1.0 / Math.Max(1, clip.frameRate), ((double)clip.frameCount - 1) / Math.Max(1, clip.frameRate)));
        pendingTime = Math.Max(0, Math.Min(lastFrame, target));
        ended = false; hasPendingSeek = true;
        if (seekBar != null) seekBar.SetValueWithoutNotify(Duration > 0 ? (float)(pendingTime / Duration) : 0);
        if (!player.isPrepared) EnsurePrepared(); else if (!seeking) ApplyPendingSeek();
        RefreshControls();
    }
    private void ApplyPendingSeek()
    {
        double target = pendingTime; hasPendingSeek = false;
        bool sameTime = Math.Abs(player.time - target) < .001;
        if (!seekMuted && sameTime && hasDecodedFrame) { ApplyPlaybackState(); return; }
        player.Pause(); seeking = true; seekCompleted = decodedSeekFrame = false; activeSeekTime = target;
        seekDeadline = Time.realtimeSinceStartupAsDouble + 5;
        if (audioOutput != null && !seekMuted) { previousMute = audioOutput.mute; audioOutput.mute = true; seekMuted = true; }
        if (sameTime) { seekCompleted = true; player.Play(); }
        else player.time = target;
    }
    private void SeekCompleted(VideoPlayer source)
    {
        if (!isActiveAndEnabled || !seeking) return;
        if (hasPendingSeek && Math.Abs(pendingTime - activeSeekTime) >= .001) { seeking = false; ApplyPendingSeek(); return; }
        hasPendingSeek = false;
        seekCompleted = true;
        if (TryCompleteSeek()) return;
        // 정지 중 탐색도 새 프레임이 실제 출력된 뒤 멈춘다. 이 짧은 디코딩 동안 소리는 음소거한다.
        source.Play(); RefreshControls();
    }
    private void SeekFrameReady(VideoPlayer source, long frame)
    {
        if (!isActiveAndEnabled) return;
        hasDecodedFrame = true;
        if (!seeking) return;
        long target = (long)Math.Floor(activeSeekTime * clip.frameRate);
        if (Math.Abs(frame - target) > 3) return;
        decodedSeekFrame = true; TryCompleteSeek();
    }
    private bool TryCompleteSeek()
    {
        if (!seeking || !seekCompleted || !decodedSeekFrame) return false;
        seeking = false; seekCompleted = decodedSeekFrame = false;
        if (hasPendingSeek) ApplyPendingSeek();
        else { RestoreSeekAudio(); ApplyPlaybackState(); }
        RefreshControls();
        return true;
    }
    private void RestoreSeekAudio()
    {
        if (!seekMuted) return;
        if (audioOutput != null) audioOutput.mute = previousMute;
        seekMuted = false;
    }
    private void ApplyPlaybackState()
    {
        if (!isActiveAndEnabled || player == null || IsSeeking) return;
        if (!player.isPrepared) { EnsurePrepared(); return; }
        if (wantsPlay && !scrubbing) player.Play(); else player.Pause();
    }
    private void ReachedEnd(VideoPlayer source)
    {
        if (!isActiveAndEnabled || scrubbing) return;
        if (IsSeeking)
        {
            // 마지막 프레임에서는 새 frameReady가 더 오지 않을 수 있다.
            if (seeking && activeSeekTime >= Duration - 4.0 / Math.Max(1,clip.frameRate)
                && Math.Abs(source.frame - Math.Floor(activeSeekTime * clip.frameRate)) <= 3)
            { seekCompleted = decodedSeekFrame = true; TryCompleteSeek(); }
            if (IsSeeking) return;
        }
        wantsPlay = false; ended = true; source.Pause();
        if (seekBar != null) seekBar.SetValueWithoutNotify(1);
        RefreshControls();
    }
    private void RefreshControls()
    {
        bool ready = player != null && player.isPrepared && !playbackFailed;
        bool input = menu == null || menu.CanControlTutorialVideo;
        if (playbackButton != null) playbackButton.interactable = input && clip != null;
        if (restartButton != null) restartButton.interactable = input && clip != null;
        if (seekBar != null) seekBar.interactable = input && hasPrepared && !playbackFailed;
        if (playbackView != null) playbackView.SetState(IsPaused, ended, ready);
        if (stateLabel != null) stateLabel.text = playbackFailed ? "영상 재생 실패 · 처음부터를 눌러 주세요" : ended ? "재생 완료 · 확인: 처음부터 · ←/→: 1초 이동" : !ready ? "영상 준비 중…" : scrubbing ? "재생 위치 선택 중" : IsSeeking ? "선택한 위치로 이동 중…" : wantsPlay ? "재생 중  ·  확인: 일시정지  ·  ←/→: 1초 이동" : "일시정지  ·  확인: 재생  ·  ←/→: 1초 이동";
        double time = scrubbing && seekBar != null ? seekBar.value * Duration : IsSeeking ? pendingTime : Position;
        if (timeLabel != null) timeLabel.text = FormatTime(time) + " / " + FormatTime(Duration);
    }
    private static string FormatTime(double seconds) { int total = Math.Max(0, (int)seconds); return (total / 60).ToString("00") + ":" + (total % 60).ToString("00"); }
    private void PlayCue() { if (controlFeedback != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.Play(controlFeedback, transform); }
    private void EnsurePrepared()
    {
        if (player == null || preparing || player.isPrepared || playbackFailed) return;
        preparing = true; hasDecodedFrame = false; player.Prepare();
    }
    private void Failed(VideoPlayer source, string message)
    {
        wantsPlay = seeking = scrubbing = hasPendingSeek = seekCompleted = preparing = decodedSeekFrame = false; playbackFailed = true;
        source.Pause(); RestoreSeekAudio(); image.enabled = false;
        if (seekBar != null) seekBar.interactable = false;
        RefreshControls();
        Debug.LogWarning("교본 영상 재생 실패: " + message, this);
    }
    private void OnDisable()
    {
        wantsPlay = seeking = scrubbing = ended = hasPendingSeek = seekCompleted = preparing = hasPrepared = decodedSeekFrame = hasDecodedFrame = false;
        RestoreSeekAudio();
        if (seekBar != null) { seekBar.onValueChanged.RemoveListener(SeekValueChanged); seekBar.interactable = false; }
        if (player != null) { player.Stop(); player.targetTexture = null; }
        if (audioOutput != null) audioOutput.Stop();
        if (image != null) { image.texture = null; image.enabled = false; }
        if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
    }
}
