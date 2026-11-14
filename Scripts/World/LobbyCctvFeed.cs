using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Lobby의 CCTV 중계 화면(2026-11-15). 다른 훈련생의 훈련 모습을 중계하는 설정이며, 지금은 TitleScene에서 쓴 훈련 영상(RawImage01~04)을
/// 차례로 이어서 반복한다(나중에 최신 영상으로 바꾼다). 실제 게임을 녹화한 영상만 쓴다(docs/ROOM_BACKGROUND_DESIGN.md: 가짜 중계 금지).
/// - 화면이 TV 액정처럼 보이는 효과(주사선·RGB 화소 격자·필름 그레인·가장자리 감광·약한 곡률·색수차·굴러가는 띠·깜빡임·채도 낮춤)는
///   Lobby/CctvScreen 셰이더가 맡는다. 이 컴포넌트는 영상 RenderTexture와 _Switch(전환 잡음)·_Signal(신호)·_Clock(시계)을 넘겨준다.
/// - 끊김을 줄이려고 VideoPlayer 둘을 번갈아 쓴다. 하나가 재생하는 동안 다른 하나가 다음 영상을 미리 준비(Prepare)하고,
///   앞 영상이 끝나면 넘겨받는다. 넘겨받는 동안에는 _Switch를 올려 채널이 바뀌는 것처럼 정적 잡음이 지나간다.
/// - VideoPlayer마다 RenderTexture(기본 960×540)를 하나씩 만들고 MaterialPropertyBlock으로 화면에 준다(공유 재질은 바꾸지 않는다).
/// - 켜질 때 재생을 시작하고, 꺼질 때(방이 비활성화될 때) 멈추고 RenderTexture를 해제한다. 화면이 어느 카메라에도 보이지 않으면 잠시 뒤 일시정지한다.
/// - 표시 전용이며 판정·입력과 무관하다. 검증용으로 IsPlaying·CurrentClipIndex·FramesShown을 읽을 수 있다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyCctvFeed : MonoBehaviour
{
    [SerializeField, Tooltip("차례로 이어서 반복할 영상(실제 게임을 녹화한 것)")] private VideoClip[] playlist = Array.Empty<VideoClip>();
    [SerializeField, Tooltip("영상을 그릴 화면(Lobby/CctvScreen 재질의 MeshRenderer)")] private Renderer screen;
    [SerializeField, Min(16), Tooltip("영상 RenderTexture의 가로 화소 수")] private int width = 960;
    [SerializeField, Min(16), Tooltip("영상 RenderTexture의 세로 화소 수")] private int height = 540;
    [SerializeField, Min(.05f), Tooltip("영상이 바뀐 뒤 정적 잡음이 잦아드는 시간(초)")] private float switchFade = .45f;
    [SerializeField, Min(.1f), Tooltip("다음 영상이 첫 프레임을 내지 못해도 화면을 넘기는 시간(초)")] private float handOverTimeout = 1.5f;
    [SerializeField, Min(0f), Tooltip("화면이 카메라에서 벗어난 뒤 일시정지할 때까지의 시간(초)")] private float hideDelay = .5f;

    private static readonly int FeedId = Shader.PropertyToID("_MainTex");
    private static readonly int SwitchId = Shader.PropertyToID("_Switch");
    private static readonly int SignalId = Shader.PropertyToID("_Signal");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");

    private readonly VideoPlayer[] players = new VideoPlayer[2];
    private readonly RenderTexture[] textures = new RenderTexture[2];
    private readonly List<VideoClip> clips = new List<VideoClip>();
    private MaterialPropertyBlock block;
    private int current;            // 화면에 보이는 VideoPlayer(0 또는 1)
    private int index;              // 지금 화면에 나오는(또는 넘겨받는 중인) 영상 번호. 다시 켜지면 이 영상부터 이어 간다
    private int failures, framesShown;
    private bool running, handing, paused, broken;
    private float handStarted, switchAmount, signal, hiddenFor, stalledFor;
    private long lastFrame = -1;

    /// <summary>화면에 보이는 영상이 지금 재생 중인가.</summary>
    public bool IsPlaying => running && !broken && players[current] != null && players[current].isPlaying;
    /// <summary>화면이 보이지 않아 일시정지한 상태인가.</summary>
    public bool IsPaused => paused;
    /// <summary>지금 화면에 나오는 영상의 번호(playlist의 빈 칸을 뺀 순서).</summary>
    public int CurrentClipIndex => index;
    /// <summary>켜진 뒤 화면에 새로 나온 프레임의 수(검증용).</summary>
    public int FramesShown => framesShown;
    public int ClipCount => clips.Count;

    /// <summary>생성 도구가 Prefab에 영상 목록과 화면을 적을 때 쓴다(Edit Mode).</summary>
    public void Configure(VideoClip[] videos, Renderer target)
    {
        playlist = videos != null ? videos : Array.Empty<VideoClip>();
        screen = target;
    }

    private void OnEnable()
    {
        clips.Clear();
        if (playlist != null)
            foreach (var clip in playlist)
                if (clip != null) clips.Add(clip);
        if (screen == null || clips.Count == 0) return;
        if (block == null) block = new MaterialPropertyBlock();
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = CreatePlayer(i);
            textures[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "LobbyCctvFeed" + i,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            textures[i].Create();
            players[i].targetTexture = textures[i];
        }
        running = true;
        handing = paused = broken = false;
        failures = 0;
        hiddenFor = stalledFor = 0f;
        signal = 0f;
        switchAmount = 1f;
        lastFrame = -1;
        current = 0;
        index = Wrap(index);
        Load(current, index, true);
        Load(1 - current, index + 1, false);
        ApplyScreen();
    }

    private void OnDisable()
    {
        running = handing = paused = false;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
            {
                players[i].Stop();
                players[i].targetTexture = null;
            }
            if (textures[i] != null)
            {
                textures[i].Release();
                Destroy(textures[i]);
                textures[i] = null;
            }
        }
        // 실행 중에 넘긴 영상·잡음 값을 지운다(꺼진 화면이 해제된 텍스처를 가리키지 않게 한다).
        if (screen != null) screen.SetPropertyBlock(null);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < players.Length; i++)
            if (players[i] != null) Destroy(players[i].gameObject);
    }

    private VideoPlayer CreatePlayer(int slot)
    {
        // 영상마다 따로 디코딩하도록 자식 개체에 하나씩 둔다(실행 중에만 생긴다).
        var host = new GameObject("CctvDecoder" + slot);
        host.transform.SetParent(transform, false);
        var player = host.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.isLooping = false;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.loopPointReached += ReachedEnd;
        player.errorReceived += Failed;
        return player;
    }

    private int Wrap(int value) => clips.Count == 0 ? 0 : (value % clips.Count + clips.Count) % clips.Count;

    // slot의 VideoPlayer에 clipIndex번 영상을 싣는다. play가 아니면 준비만 해 둔다(넘겨받을 때 바로 나오게).
    private void Load(int slot, int clipIndex, bool play)
    {
        var player = players[slot];
        player.Stop();
        player.clip = clips[Wrap(clipIndex)];
        if (play) player.Play();
        else player.Prepare();
    }

    private void Update()
    {
        if (!running) return;
        float dt = Time.unscaledDeltaTime;

        // 화면이 어느 카메라에도 보이지 않으면 잠시 뒤 멈춘다(디코딩 비용을 아낀다). 다시 보이면 이어서 재생한다.
        hiddenFor = screen.isVisible ? 0f : hiddenFor + dt;
        bool shouldPause = hiddenFor > hideDelay;
        if (shouldPause != paused) SetPaused(shouldPause);
        if (paused) return;

        if (handing)
        {
            var next = players[1 - current];
            // 다음 영상이 첫 프레임을 낸 뒤에 화면을 넘긴다(그 전까지는 앞 영상의 마지막 프레임 위로 정적 잡음이 지난다).
            if ((next.isPlaying && next.frame > 0) || Time.unscaledTime - handStarted > handOverTimeout) FinishHandOver();
        }
        else if (!broken)
        {
            var shown = players[current];
            if (shown.isPlaying && shown.frame >= 0 && shown.frame != lastFrame)
            {
                lastFrame = shown.frame;
                framesShown++;
                failures = 0;
            }
            // 넘겨받은 영상이 끝내 재생되지 않으면(디코더가 멈춘 경우) 다음 영상으로 넘어간다.
            stalledFor = shown.isPlaying ? 0f : stalledFor + dt;
            if (stalledFor > handOverTimeout * 2f)
            {
                stalledFor = 0f;
                BeginHandOver();
            }
        }

        switchAmount = handing ? 1f : Mathf.MoveTowards(switchAmount, 0f, dt / switchFade);
        signal = Mathf.MoveTowards(signal, !broken && framesShown > 0 ? 1f : 0f, dt / .3f);
        ApplyScreen();
    }

    private void ApplyScreen()
    {
        if (screen == null || block == null) return;
        screen.GetPropertyBlock(block);
        if (textures[current] != null) block.SetTexture(FeedId, textures[current]);
        block.SetFloat(SwitchId, switchAmount);
        block.SetFloat(SignalId, signal);
        block.SetFloat(ClockId, Mathf.Repeat(Time.unscaledTime, 3600f));
        screen.SetPropertyBlock(block);
    }

    private void SetPaused(bool value)
    {
        paused = value;
        if (broken) return;
        var shown = players[current];
        var next = players[1 - current];
        if (paused)
        {
            if (shown.isPlaying) shown.Pause();
            if (handing && next.isPlaying) next.Pause();
            return;
        }
        shown.Play();
        if (handing) next.Play();
    }

    private void ReachedEnd(VideoPlayer source)
    {
        if (!running || handing || source != players[current]) return;
        BeginHandOver();
    }

    // 다음 영상(미리 준비해 둔 VideoPlayer)을 재생하기 시작한다. 화면은 첫 프레임이 나온 뒤에 넘긴다.
    private void BeginHandOver()
    {
        handing = true;
        handStarted = Time.unscaledTime;
        switchAmount = 1f;
        index = Wrap(index + 1);
        var next = players[1 - current];
        if (next.clip != clips[index])
        {
            next.Stop();
            next.clip = clips[index];
        }
        if (!paused) next.Play();
    }

    private void FinishHandOver()
    {
        handing = false;
        int previous = current;
        current = 1 - current;
        lastFrame = -1;
        stalledFor = 0f;
        // 끝난 영상은 멈추고, 그 VideoPlayer에 그다음 영상을 미리 준비해 둔다.
        Load(previous, index + 1, false);
    }

    private void Failed(VideoPlayer source, string message)
    {
        Debug.LogWarning("CCTV 중계 영상 재생 실패: " + message, this);
        if (!running || broken) return;
        failures++;
        if (failures >= clips.Count * 2)
        {
            // 모든 영상이 거듭 실패하면 더 시도하지 않고 대기 화면(신호 없음)으로 둔다.
            broken = true;
            handing = false;
            foreach (var player in players)
                if (player != null) player.Stop();
            return;
        }
        if (source == players[current] && !handing) BeginHandOver();
        else if (handing && source == players[1 - current])
        {
            // 넘겨받던 영상도 실패하면 그다음 영상으로 건너뛴다.
            index = Wrap(index + 1);
            source.Stop();
            source.clip = clips[index];
            handStarted = Time.unscaledTime;
            if (!paused) source.Play();
        }
    }
}
