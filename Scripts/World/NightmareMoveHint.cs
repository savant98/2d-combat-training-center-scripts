using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Nightmare 방의 시작 지점 벽면에 흰 발광선으로 그린 좌·우 이동 안내(키캡 두 개와 "이동").
/// 방의 바닥선과 같은 Material(Nightmare/Geometry)로 그려 같은 빛으로 보이고, 벽에 고정돼 있어 걸어 나가면 화면 밖으로 사라진다.
/// 이동 키를 바꿨으면 화살표 대신 그 키의 이름을 보여 준다. 켜고 끄는 것은 NightmareBackdrop이 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class NightmareMoveHint : MonoBehaviour
{
    [SerializeField, Tooltip("키캡 외곽선(왼쪽, 오른쪽)")] private Renderer[] keycaps;
    [SerializeField, Tooltip("기본 방향키일 때 보이는 화살표(왼쪽, 오른쪽)")] private Renderer[] arrows;
    [SerializeField, Tooltip("다른 키로 바꿨을 때 보이는 키 이름(왼쪽, 오른쪽)")] private TextMeshPro[] keyLabels;
    [SerializeField] private TextMeshPro caption;
    [SerializeField, Min(0f), Tooltip("켜라는 요청을 받은 뒤 켜지기 시작할 때까지의 시간")] private float showDelay = .7f;
    [SerializeField, Min(.05f)] private float fadeInDuration = .9f;
    [SerializeField, Min(.05f)] private float fadeOutDuration = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("가장 밝을 때의 밝기")] private float brightness = .35f;
    [SerializeField, Range(0f, .5f), Tooltip("켜진 뒤 숨 쉬듯 밝기가 변하는 폭")] private float pulse = .1f;

    private static readonly int VisibilityId = Shader.PropertyToID("_Visibility");
    private static readonly string[] MoveParts = { "left", "right" };
    private static readonly string[] DefaultPaths = { "<Keyboard>/leftArrow", "<Keyboard>/rightArrow" };

    private MaterialPropertyBlock block;
    private readonly bool[] usesArrow = { true, true };
    private float visibility, delay, shownTime;
    private bool wanted;

    /// <summary>지금 화면에 보이는 정도(0~1).</summary>
    public float Visibility => visibility;
    public bool IsShown => wanted;

    private void Awake()
    {
        Apply(0f);
    }

    /// <summary>잠시 뒤 서서히 켠다.</summary>
    public void Show()
    {
        RefreshKeys();
        wanted = true;
        delay = showDelay;
        shownTime = 0f;
    }

    /// <summary>서서히 끈다.</summary>
    public void Hide()
    {
        wanted = false;
    }

    public void HideImmediately()
    {
        wanted = false;
        visibility = 0f;
        Apply(0f);
    }

    // 이동 키가 기본 방향키면 화살표를, 바꿨으면 그 키의 이름을 보여 준다.
    private void RefreshKeys()
    {
        InputAction move = InputManager.Instance != null ? InputManager.Instance.Actions.FindAction("Player/Move") : null;
        for (int i = 0; i < 2; i++)
        {
            int index = OptionMenu.FindBinding(move, MoveParts[i]);
            bool arrow = index < 0 || string.Equals(move.bindings[index].effectivePath, DefaultPaths[i], System.StringComparison.OrdinalIgnoreCase);
            usesArrow[i] = arrow;
            if (!arrow && keyLabels != null && i < keyLabels.Length && keyLabels[i] != null)
                keyLabels[i].text = move.GetBindingDisplayString(index).ToUpperInvariant();
        }
    }

    private void Update()
    {
        if (wanted && delay > 0f)
        {
            delay -= Time.deltaTime;
            return;
        }
        float target = wanted ? 1f : 0f;
        if (Mathf.Approximately(visibility, target) && !wanted) return;
        visibility = Mathf.MoveTowards(visibility, target, Time.deltaTime / (wanted ? fadeInDuration : fadeOutDuration));
        if (wanted) shownTime += Time.deltaTime;

        // 오래된 등이 켜지듯 처음에는 두 번 깜빡이고, 켜진 뒤에는 천천히 숨 쉰다.
        float flicker = 1f;
        if (wanted && shownTime < .5f)
            flicker = shownTime < .07f || (shownTime > .15f && shownTime < .2f) || (shownTime > .3f && shownTime < .33f) ? .35f : 1f;
        float breath = 1f - pulse * (.5f + .5f * Mathf.Sin(shownTime * 1.9f));
        Apply(Mathf.SmoothStep(0f, 1f, visibility) * flicker * breath * brightness);
    }

    private void Apply(float value)
    {
        block ??= new MaterialPropertyBlock();
        block.SetFloat(VisibilityId, value);
        SetLines(keycaps, value, null);
        SetLines(arrows, value, true);
        if (keyLabels != null)
            for (int i = 0; i < keyLabels.Length; i++)
                SetLabel(keyLabels[i], i < usesArrow.Length && usesArrow[i] ? 0f : value);
        SetLabel(caption, value);
    }

    private void SetLines(Renderer[] renderers, float value, bool? arrowState)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            bool visible = value > .001f && (arrowState == null || (i < usesArrow.Length && usesArrow[i]));
            if (renderers[i].enabled != visible) renderers[i].enabled = visible;
            if (visible) renderers[i].SetPropertyBlock(block);
        }
    }

    private static void SetLabel(TextMeshPro label, float value)
    {
        if (label == null) return;
        bool visible = value > .001f;
        if (label.enabled != visible) label.enabled = visible;
        if (visible) label.alpha = value;
    }
}
