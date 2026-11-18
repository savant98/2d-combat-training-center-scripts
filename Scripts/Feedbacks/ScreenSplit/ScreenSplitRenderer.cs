using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 화면 가르기: 메인 카메라 화면을 한 줄(월드의 직선)을 따라 둘로 갈라, 두 조각이 선 방향으로 서로 어긋나 보이게 다시 그린다(2026-11-18).
// 사용자 지시: "Sword의 일격필살의 경우 화면을 반으로 가르는 연출(normal map이나 카메라 활용)을 하고 ... 화면을 반으로 가름, 둘로 쪼개지는게 보임.
//   그 후 자연스럽게 참격이 보임. 화면을 15도 대각선으로 자르면 됨." — 일격필살(SwordFinisherSlash)이 원천 하나로 쓴다.
// 방식은 블랙홀 화면 왜곡(ScreenDistortionRenderer)과 같다.
// - 공용 렌더 설정(Renderer2D.asset의 Renderer Feature·URP 에셋)은 바꾸지 않는다. 원천이 있는 동안만 beginCameraRendering을 구독하고,
//   MainCamera 태그의 Game 카메라에만 ScreenSplitPass를 EnqueuePass한다(2D 렌더러는 BeforeRenderingPostProcessing, Bloom보다 먼저 —
//   틈 가운데의 HDR 빛 선이 Bloom으로 번진다). 원천이 없으면 구독을 끊는다.
// - HUD는 UICamera(Overlay)가 나중에 그리므로 갈라지지 않는다(메인 카메라만).
// - 원천은 하나다. Begin(주인, 재질, 상태) → 프레임마다 UpdateState → End(주인). 다른 주인의 End는 무시한다(늦게 정리되는 옛 개체가 새 연출을 끄지 않게).
//   주인이 파괴됐거나 Play가 아니면(도메인 리로드를 끈 Play 종료 포함) 다음 렌더에서 스스로 정리해 화면이 갈린 채 남지 않게 한다.
// - 상태는 월드 좌표(선 위의 점·방향)로 받고, 렌더 직전에 그 카메라의 투영으로 화면 좌표를 구한다(흔들림으로 카메라가 움직여도 선이 월드에 붙어 있다).
public struct ScreenSplitState
{
    public Vector2 point;       // 가르는 선 위의 월드 점
    public Vector2 direction;   // 선 방향(월드, 길이 1)
    public float gap;           // 틈 반폭(화면 높이 비율)
    public float slide;         // 두 조각이 선 방향으로 서로 어긋난 거리(화면 높이 비율). 조각마다 절반씩 반대로 미끄러진다
    public float jag;           // 자른 면이 들쭉날쭉한 정도(화면 높이 비율)
    public float strength;      // 틈 가운데 빛 선·자른 면 테두리 빛의 세기(0~1)
}

public static class ScreenSplitRenderer
{
    private static object owner;
    private static Material template;
    private static ScreenSplitState state;
    private static ScreenSplitPass pass;
    private static bool subscribed;

    public static bool IsActive => owner != null;
    public static ScreenSplitState State => state;
    // 검증용: true인 동안 패스를 넣지 않는다(같은 프레임에 갈린 화면·원래 화면을 한 장씩 렌더해 비교할 때만 쓴다).
    public static bool SuppressedForVerification { get; set; }
    // 검증용: 패스를 기록한 누적 횟수, Begin·End 누적 횟수, 마지막으로 기록한 선(UV x·y, 화면 방향 x·y)·모양(틈 반폭, 어긋남, 들쭉날쭉, 세기)·가로세로비
    public static int RecordedPassCount { get; internal set; }
    public static int BeginCount { get; private set; }
    public static int EndCount { get; private set; }
    public static Vector4 LastLine => pass != null ? pass.Line : Vector4.zero;
    public static Vector4 LastShape => pass != null ? pass.Shape : Vector4.zero;
    public static float LastAspect => pass != null ? pass.Aspect : 0f;

    // Domain Reload를 끈 Play 설정에서도 이전 Play의 구독·원천이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        owner = null;
        template = null;
        state = default;
        SetSubscribed(false);
        SuppressedForVerification = false;
        RecordedPassCount = 0;
        BeginCount = 0;
        EndCount = 0;
        pass?.Dispose();
        pass = null;
    }

    // 화면 가르기를 시작한다. 이미 다른 주인이 쓰고 있으면 새 주인이 넘겨받는다.
    public static void Begin(object newOwner, Material material, in ScreenSplitState newState)
    {
        if (newOwner == null || material == null)
            return;

        owner = newOwner;
        template = material;
        state = newState;
        BeginCount++;
        SetSubscribed(true);
    }

    public static void UpdateState(object currentOwner, in ScreenSplitState newState)
    {
        if (currentOwner == null || !ReferenceEquals(currentOwner, owner))
            return;

        state = newState;
    }

    public static void End(object currentOwner)
    {
        if (currentOwner == null || !ReferenceEquals(currentOwner, owner))
            return;

        Clear();
    }

    private static void Clear()
    {
        if (owner != null)
            EndCount++;
        owner = null;
        template = null;
        state = default;
        SetSubscribed(false);
    }

    private static void SetSubscribed(bool value)
    {
        if (subscribed == value)
            return;

        subscribed = value;
        if (value)
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        else
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // Play가 끝났거나 주인이 파괴됐다(정리 경로를 거치지 못했다). 화면이 갈린 채 남지 않게 스스로 끝낸다.
        if (!Application.isPlaying || owner == null || (owner is Object unityOwner && unityOwner == null) || template == null)
        {
            Clear();
            return;
        }

        // 게임 화면(메인 카메라)만 가른다. Scene 뷰·미리보기·화면 안 모니터용 카메라와 UICamera는 제외한다.
        if (SuppressedForVerification || camera.cameraType != CameraType.Game || !camera.CompareTag("MainCamera"))
            return;

        var cameraData = camera.GetUniversalAdditionalCameraData();
        if (cameraData == null || cameraData.scriptableRenderer == null)
            return;

        pass ??= new ScreenSplitPass();
        if (pass.Setup(camera, template, state))
            cameraData.scriptableRenderer.EnqueuePass(pass);
    }
}
