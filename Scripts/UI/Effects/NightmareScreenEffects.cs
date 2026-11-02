using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 실제 카메라 출력을 임시 RT로 표시한다. 연출용 버퍼이며 촬영 파일을 저장하지 않는다.
[DisallowMultipleComponent]
public sealed class NightmareScreenEffects : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.RawImage display;
    [SerializeField] private Shader screenShader;
    [SerializeField] private Shader gaussianShader;
    [SerializeField] private SoundOptions staticSound;
    [SerializeField] private SoundOptions tinnitusSound;
    private Camera source;
    private Camera displayCamera;
    private RenderTexture originalTarget, raw, ping, blurred;
    private Material screenMaterial, gaussianMaterial;
    private SoundHandle sound;
    private uint version;
    private float noise, red, blur;
    public bool IsCapturing => raw != null && source != null && source.targetTexture == raw;
    public float BlurRadius => blur;
    public float NoiseIntensity => noise;
    public int ProcessedFrames { get; private set; }
    public AudioClip TinnitusClip => tinnitusSound != null ? tinnitusSound.clip : null;

    private void Awake() { if(display!=null)display.enabled=false; }

    public void BeginStatic(float volumeScale=1f)
    {
        Begin();
        StopSound();
        if(SoundManager.Instance!=null&&staticSound!=null&&staticSound.clip!=null)
        {
            var options=JsonUtility.FromJson<SoundOptions>(JsonUtility.ToJson(staticSound));
            options.volume*=volumeScale;
            sound=SoundManager.Instance.PlayLoop(options,Vector3.zero);
        }
        SetSignal(1,.6f);
    }

    public void SetSignal(float strength,float redStrength)
    {
        noise=Mathf.Clamp01(strength);red=Mathf.Clamp01(redStrength);
        ApplyMaterial();
    }

    private void Begin()
    {
        if(IsCapturing)return;
        source=Camera.main;
        if(source==null||display==null||screenShader==null||gaussianShader==null)
            throw new InvalidOperationException("악몽 화면 연출의 카메라·표시·Shader 참조가 없습니다.");
        originalTarget=source.targetTexture;
        int width=Mathf.Max(32,Screen.width/2),height=Mathf.Max(32,Screen.height/2);
        raw=Texture(width,height,24,"NightmareCameraOutput");
        ping=Texture(width,height,0,"NightmareGaussianPing");
        blurred=Texture(width,height,0,"NightmareGaussianOutput");
        screenMaterial=new Material(screenShader){hideFlags=HideFlags.HideAndDontSave};
        gaussianMaterial=new Material(gaussianShader){hideFlags=HideFlags.HideAndDontSave};
        source.targetTexture=raw;display.texture=raw;display.material=screenMaterial;display.enabled=true;
        // 기존 Title의 라이브 화면처럼 RT 카메라와 실제 Display 출력을 함께 유지한다.
        var output=new GameObject("NightmareDisplayCamera"){hideFlags=HideFlags.HideAndDontSave};
        displayCamera=output.AddComponent<Camera>();displayCamera.targetDisplay=source.targetDisplay;
        displayCamera.cullingMask=0;displayCamera.clearFlags=CameraClearFlags.SolidColor;
        displayCamera.backgroundColor=Color.black;displayCamera.depth=-1000;
        displayCamera.allowHDR=displayCamera.allowMSAA=false;displayCamera.useOcclusionCulling=false;
        var additional=displayCamera.GetUniversalAdditionalCameraData();
        additional.renderPostProcessing=additional.renderShadows=false;additional.volumeLayerMask=0;
        additional.requiresColorOption=additional.requiresDepthOption=CameraOverrideOption.Off;
        RenderPipelineManager.endCameraRendering+=ProcessCamera;
        ProcessedFrames=0;
    }

    private void LateUpdate()
    {
        if(!IsCapturing)return;
        int width=Mathf.Max(32,Screen.width/2),height=Mathf.Max(32,Screen.height/2);
        if(raw.width!=width||raw.height!=height)
        {
            source.targetTexture=null;
            Release(raw);Release(ping);Release(blurred);
            raw=Texture(width,height,24,"NightmareCameraOutput");
            ping=Texture(width,height,0,"NightmareGaussianPing");
            blurred=Texture(width,height,0,"NightmareGaussianOutput");
            source.targetTexture=raw;display.texture=raw;
        }
        ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        if(screenMaterial==null)return;
        screenMaterial.SetFloat("_Noise",noise);
        screenMaterial.SetFloat("_RedFlash",red*(.35f+.65f*Mathf.Abs(Mathf.Sin(Time.unscaledTime*19.7f))));
        screenMaterial.SetFloat("_Clock",Time.unscaledTime);
    }

    private void ProcessCamera(ScriptableRenderContext context,Camera camera)
    {
        if(camera!=source||raw==null)return;
        ProcessedFrames++;
        if(blur>.01f)
        {
            gaussianMaterial.SetFloat("_BlurRadius",blur*raw.height/1080f);
            var command=CommandBufferPool.Get("Nightmare Gaussian Blur");
            command.Blit(raw,ping,gaussianMaterial,0);
            command.Blit(ping,blurred,gaussianMaterial,0);
            context.ExecuteCommandBuffer(command);CommandBufferPool.Release(command);
            display.texture=blurred;
        }
        else display.texture=raw;
    }

    public IEnumerator PlayArrival(Action switchRoom,float duration)
    {
        uint current=++version;
        BeginStatic(.45f);
        bool switched=false;
        double started=Time.unscaledTimeAsDouble;
        while(current==version&&Time.unscaledTimeAsDouble-started<duration)
        {
            float t=(float)((Time.unscaledTimeAsDouble-started)/duration);
            if(!switched&&t>=.2f){switchRoom();switched=true;UIManager.Instance.StartScreenFade(0,0);}
            float envelope=t<.65f?1:Mathf.SmoothStep(1,0,(t-.65f)/.35f);
            SetSignal(.32f*envelope,.24f*envelope);
            yield return null;
        }
        if(current!=version)yield break;
        if(!switched)switchRoom();
        End();
    }

    public IEnumerator PlayDefeat(float duration=3.2f)
    {
        uint current=++version;
        Begin();StopSound();
        if(SoundManager.Instance!=null&&tinnitusSound!=null&&tinnitusSound.clip!=null)
            sound=SoundManager.Instance.PlayLoop(tinnitusSound,Vector3.zero);
        double started=Time.unscaledTimeAsDouble;
        while(current==version&&Time.unscaledTimeAsDouble-started<duration)
        {
            float t=(float)((Time.unscaledTimeAsDouble-started)/duration);
            blur=Mathf.SmoothStep(0,22,Mathf.Clamp01(t*2.2f));
            SetSignal(.13f*(1-t),.13f*(1-t));
            yield return null;
        }
        if(current==version)StopSound();
    }

    public void End()
    {
        ++version;StopSound();noise=red=blur=0;
        RenderPipelineManager.endCameraRendering-=ProcessCamera;
        if(source!=null&&source.targetTexture==raw)source.targetTexture=originalTarget;
        if(display!=null){display.enabled=false;display.texture=null;display.material=null;}
        if(displayCamera!=null){displayCamera.enabled=false;Destroy(displayCamera.gameObject);displayCamera=null;}
        Release(raw);Release(ping);Release(blurred);raw=ping=blurred=null;
        if(screenMaterial!=null)Destroy(screenMaterial);if(gaussianMaterial!=null)Destroy(gaussianMaterial);
        screenMaterial=gaussianMaterial=null;source=null;originalTarget=null;
    }

    private void StopSound(){if(sound!=null)sound.Stop();sound=null;}
    private static RenderTexture Texture(int width,int height,int depth,string name)
    {
        var texture=new RenderTexture(width,height,depth,RenderTextureFormat.ARGBHalf){name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        texture.Create();return texture;
    }
    private static void Release(RenderTexture texture){if(texture==null)return;texture.Release();Destroy(texture);}
    private void OnDisable()=>End();
}
