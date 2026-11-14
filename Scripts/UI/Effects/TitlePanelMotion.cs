using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀의 메뉴 판(타이틀 메뉴·세이브 선택·삭제 확인)이 함께 쓰는 열기·닫기와 공회전 연출.
/// 매 프레임 "열린 지 t초의 모습"을 계산해 적용하고, 일시정지와 무관하게 실제 시간으로 움직인다.
/// 열기: 가로선이 먼저 그어지고 세로로 펼쳐진 뒤 빛줄기가 한 번 지나가며, 안쪽 부분이 차례로 들어온다. 닫기는 그 반대다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TitlePanelMotion : MonoBehaviour
{
    public const float OpenDuration = .34f;    // 판이 다 펼쳐지는 시간. 이때부터 입력을 받는다.
    public const float CloseDuration = .26f;

    [Header("판")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform plate;
    [SerializeField, Tooltip("뒤 배경을 어둡게 덮는 판(없어도 된다)")] private CanvasGroup blinder;
    [SerializeField, Range(0f, 1f)] private float blinderAlpha = .62f;
    [SerializeField, Tooltip("펼쳐진 직후 위에서 아래로 한 번 지나가는 빛줄기")] private RectTransform scanBeam;

    [Header("차례로 들어오는 부분")]
    [SerializeField] private RectTransform[] sections = Array.Empty<RectTransform>();
    [SerializeField] private float[] sectionDelays = Array.Empty<float>();
    [SerializeField] private Vector2 sectionOffset = new Vector2(-40f, 0f);

    [Header("공회전")]
    [SerializeField, Tooltip("계속 도는 기어·고리")] private RectTransform[] rotors = Array.Empty<RectTransform>();
    [SerializeField, Tooltip("도/초. 음수는 시계 방향")] private float[] rotorSpeeds = Array.Empty<float>();
    [SerializeField, Tooltip("신호가 흐르는 선(uv가 흐른다)")] private RawImage[] flows = Array.Empty<RawImage>();
    [SerializeField] private float[] flowSpeeds = Array.Empty<float>();
    [SerializeField, Tooltip("천천히 숨 쉬는 신호등")] private Graphic[] blinkers = Array.Empty<Graphic>();
    [SerializeField, Tooltip("차례로 깜빡이는 화살표")] private Graphic[] chevrons = Array.Empty<Graphic>();
    [SerializeField, Tooltip("일정 주기로 판을 훑는 가는 선")] private RectTransform sweep;
    [SerializeField, Min(1f)] private float sweepPeriod = 6f;

    private enum State { Hidden, Opening, Closing }

    private State state = State.Hidden;
    private float time, idleTime;
    private bool cached;
    private Vector2[] sectionHomes;
    private CanvasGroup[] sectionGroups;
    private float[] sectionAlphas;
    private Quaternion[] rotorHomes;
    private Rect[] flowHomes;
    private Color[] blinkerColors, chevronColors;
    private CanvasGroup scanGroup, sweepGroup;
    private float scanAlpha, sweepAlpha;
    private Action onHidden;

    /// <summary>보이고 있는가(열리는 중·열림·닫히는 중).</summary>
    public bool IsVisible => state != State.Hidden;
    /// <summary>다 펼쳐져 입력을 받아도 되는가.</summary>
    public bool IsReady => state == State.Opening && time >= OpenDuration;
    /// <summary>열리거나 닫히는 도중인가.</summary>
    public bool IsBusy => state == State.Closing || (state == State.Opening && time < OpenDuration);
    /// <summary>열린 뒤 흐른 시간(초). 닫혀 있으면 0.</summary>
    public float OpenTime => state == State.Opening ? time : 0f;

    private void Awake()
    {
        Cache();
        SnapHidden();
    }

    private void Cache()
    {
        if (cached) return;
        cached = true;
        sectionHomes = new Vector2[sections.Length];
        sectionGroups = new CanvasGroup[sections.Length];
        sectionAlphas = new float[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null) continue;
            sectionHomes[i] = sections[i].anchoredPosition;
            sectionGroups[i] = sections[i].GetComponent<CanvasGroup>();
            sectionAlphas[i] = sectionGroups[i] != null ? sectionGroups[i].alpha : 1f;
        }
        rotorHomes = new Quaternion[rotors.Length];
        for (int i = 0; i < rotors.Length; i++)
            if (rotors[i] != null) rotorHomes[i] = rotors[i].localRotation;
        flowHomes = new Rect[flows.Length];
        for (int i = 0; i < flows.Length; i++)
            if (flows[i] != null) flowHomes[i] = flows[i].uvRect;
        blinkerColors = new Color[blinkers.Length];
        for (int i = 0; i < blinkers.Length; i++)
            if (blinkers[i] != null) blinkerColors[i] = blinkers[i].color;
        chevronColors = new Color[chevrons.Length];
        for (int i = 0; i < chevrons.Length; i++)
            if (chevrons[i] != null) chevronColors[i] = chevrons[i].color;
        if (scanBeam != null)
        {
            scanGroup = scanBeam.GetComponent<CanvasGroup>();
            scanAlpha = scanGroup != null ? scanGroup.alpha : 1f;
        }
        if (sweep != null)
        {
            sweepGroup = sweep.GetComponent<CanvasGroup>();
            sweepAlpha = sweepGroup != null ? sweepGroup.alpha : 1f;
        }
    }

    /// <summary>닫혀 있으면 펼친다. 이미 열려 있으면 그대로 둔다.</summary>
    public void Open()
    {
        Cache();
        if (state == State.Opening) return;
        state = State.Opening;
        time = 0f;
        onHidden = null;
        Apply();
    }

    /// <summary>접어서 숨긴다. 다 접히면 hidden을 한 번 부른다.</summary>
    public void Close(Action hidden = null)
    {
        Cache();
        if (state == State.Hidden)
        {
            hidden?.Invoke();
            return;
        }
        onHidden = hidden;
        if (state == State.Closing) return;
        state = State.Closing;
        time = 0f;
        Apply();
    }

    /// <summary>연출 없이 바로 숨긴다.</summary>
    public void SnapHidden()
    {
        Cache();
        state = State.Hidden;
        time = 0f;
        onHidden = null;
        Apply();
    }

    private void Update()
    {
        if (state == State.Hidden) return;
        float delta = Time.unscaledDeltaTime;
        time += delta;
        idleTime += delta;
        if (state == State.Closing && time >= CloseDuration)
        {
            state = State.Hidden;
            time = 0f;
            Apply();
            var callback = onHidden;
            onHidden = null;
            callback?.Invoke();
            return;
        }
        Apply();
    }

    private void Apply()
    {
        bool hidden = state == State.Hidden, closing = state == State.Closing;
        float t = time;

        if (group != null)
        {
            group.alpha = hidden ? 0f : closing ? 1f - TrainingBoardMotion.Seg(t, .2f, CloseDuration) : TrainingBoardMotion.Seg(t, 0f, .06f);
            group.interactable = group.blocksRaycasts = state == State.Opening;
        }
        if (blinder != null)
        {
            float shade = hidden ? 0f : closing ? 1f - TrainingBoardMotion.Seg(t, 0f, CloseDuration) : TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(t, 0f, .22f));
            blinder.alpha = blinderAlpha * shade;
            blinder.blocksRaycasts = state == State.Opening;
        }
        if (plate != null)
        {
            // 가로선이 먼저 그어지고 세로로 펼쳐진다. 닫을 때는 세로로 접힌 뒤 가로선이 사라진다.
            float x, y;
            if (hidden) { x = 0f; y = .02f; }
            else if (closing)
            {
                y = Mathf.Lerp(1f, .02f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0f, .16f)));
                x = 1f - TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(t, .12f, CloseDuration));
            }
            else
            {
                x = TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, 0f, .16f));
                y = Mathf.Lerp(.02f, 1f, TrainingBoardMotion.OutCubic(TrainingBoardMotion.Seg(t, .12f, OpenDuration)));
            }
            plate.localScale = new Vector3(x, y, 1f);
        }
        if (scanBeam != null)
        {
            float k = state == State.Opening ? TrainingBoardMotion.Seg(t, .3f, .85f) : 1f;
            float half = plate != null ? plate.rect.height * .5f : 0f;
            scanBeam.anchoredPosition = new Vector2(scanBeam.anchoredPosition.x, Mathf.Lerp(half, -half, k));
            if (scanGroup != null) scanGroup.alpha = k > 0f && k < 1f ? scanAlpha * Mathf.Sin(k * Mathf.PI) : 0f;
        }
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null) continue;
            float s;
            if (hidden) s = 0f;
            else if (closing) s = 1f - TrainingBoardMotion.Seg(t, 0f, .08f);
            else
            {
                float delay = i < sectionDelays.Length ? sectionDelays[i] : .3f + i * .06f;
                s = TrainingBoardMotion.Smooth(TrainingBoardMotion.Seg(t, delay, delay + .26f));
            }
            sections[i].anchoredPosition = sectionHomes[i] + (closing ? Vector2.zero : sectionOffset * (1f - s));
            if (sectionGroups[i] != null) sectionGroups[i].alpha = sectionAlphas[i] * s;
        }
        if (hidden) return;

        for (int i = 0; i < rotors.Length; i++)
        {
            if (rotors[i] == null) continue;
            float speed = i < rotorSpeeds.Length ? rotorSpeeds[i] : 12f;
            rotors[i].localRotation = rotorHomes[i] * Quaternion.Euler(0f, 0f, idleTime * speed);
        }
        for (int i = 0; i < flows.Length; i++)
        {
            if (flows[i] == null) continue;
            float speed = i < flowSpeeds.Length ? flowSpeeds[i] : .18f;
            Rect uv = flowHomes[i];
            uv.x = Mathf.Repeat(flowHomes[i].x + idleTime * speed, 1f);
            flows[i].uvRect = uv;
        }
        for (int i = 0; i < blinkers.Length; i++)
        {
            if (blinkers[i] == null) continue;
            Color color = blinkerColors[i];
            color.a *= .72f + .28f * Mathf.Sin(idleTime * 1.7f + i * .8f);
            blinkers[i].color = color;
        }
        for (int i = 0; i < chevrons.Length; i++)
        {
            if (chevrons[i] == null) continue;
            Color color = chevronColors[i];
            color.a *= .3f + .7f * (.5f + .5f * Mathf.Sin(idleTime * 5f - i * .9f));
            chevrons[i].color = color;
        }
        if (sweep != null)
        {
            // 글자를 가리지 않는 낮은 알파로 한 번 훑고 다음 주기를 기다린다.
            float phase = Mathf.Repeat(idleTime / sweepPeriod, 1f);
            float k = Mathf.Clamp01(phase / .8f);
            float half = plate != null ? plate.rect.height * .5f : 0f;
            sweep.anchoredPosition = new Vector2(sweep.anchoredPosition.x, Mathf.Lerp(half, -half, k));
            if (sweepGroup != null) sweepGroup.alpha = phase < .8f && IsReady ? sweepAlpha * Mathf.Sin(k * Mathf.PI) : 0f;
        }
    }

    private void OnDisable()
    {
        if (!cached) return;
        // Scene 다시 읽기가 꺼진 편집기에서도 다음 실행이 움직이던 자세를 기준으로 삼지 않게 되돌린다.
        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i] == null) continue;
            sections[i].anchoredPosition = sectionHomes[i];
            if (sectionGroups[i] != null) sectionGroups[i].alpha = sectionAlphas[i];
        }
        for (int i = 0; i < rotors.Length; i++)
            if (rotors[i] != null) rotors[i].localRotation = rotorHomes[i];
        for (int i = 0; i < flows.Length; i++)
            if (flows[i] != null) flows[i].uvRect = flowHomes[i];
        for (int i = 0; i < blinkers.Length; i++)
            if (blinkers[i] != null) blinkers[i].color = blinkerColors[i];
        for (int i = 0; i < chevrons.Length; i++)
            if (chevrons[i] != null) chevrons[i].color = chevronColors[i];
        if (plate != null) plate.localScale = Vector3.one;
        if (scanGroup != null) scanGroup.alpha = scanAlpha;
        if (sweepGroup != null) sweepGroup.alpha = sweepAlpha;
        state = State.Hidden;
    }
}
