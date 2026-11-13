#if UNITY_EDITOR
using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;

/// <summary>실제 PlayerLoop의 프레임 종료 시점에서 영상과 메인 믹서를 함께 캡처한다.</summary>
public sealed class TerminalCaptureDriver : MonoBehaviour
{
    public int FrameRate=60;
    public Action<NativeArray<float>,int> AudioFrame;
    public Action VideoFrame;
    public bool IsReady { get; private set; }
    public int LastSampleCount { get; private set; }
    public int FrameCount { get; private set; }
    public Action GameAction;
    private void Update(){var action=GameAction;GameAction=null;action?.Invoke();}
    public string Error { get; private set; }
    private bool started;
    private IEnumerator Start()
    {
        Time.captureFramerate=FrameRate;
        yield return new WaitForEndOfFrame();
        started=AudioRenderer.Start();IsReady=started;
        if(!started){Error="오디오 렌더러 초기화 실패";yield break;}
        int channels=AudioSettings.speakerMode switch {AudioSpeakerMode.Mono=>1,AudioSpeakerMode.Quad=>4,AudioSpeakerMode.Surround=>5,AudioSpeakerMode.Mode5point1=>6,AudioSpeakerMode.Mode7point1=>8,_=>2};
        while(started)
        {
            yield return new WaitForEndOfFrame();
            if(!started)yield break;
            int count=AudioRenderer.GetSampleCountForCaptureFrame();LastSampleCount=count;
            using(var samples=new NativeArray<float>(count*channels,Allocator.Temp))
            {if(AudioRenderer.Render(samples))AudioFrame?.Invoke(samples,count);}
            VideoFrame?.Invoke();FrameCount++;
        }
    }
    public void StopCapture(){if(started){AudioRenderer.Stop();started=false;}}
    private void OnDestroy(){StopCapture();}
}

#endif
