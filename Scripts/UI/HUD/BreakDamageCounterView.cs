using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// 보스가 아닌 쉴드 적이 브레이크 동안 받은 피해의 합을 몸 옆에 띄운다.
// 2026-11-17 사용자 지시: "보스 판정인 Enemy가 아니더라도, 쉴드가 있는 Enemy가 브레이크 상태에서 피해를 입을 때 마찬가지로 합산 피해를 보여 줘."
// 보스는 StageHUD가 같은 Prefab(Assets/Prefabs/UI/BreakDamageCounter)을 하나 만들어 띄운다. 보스가 아닌 적은 여럿이 함께 브레이크될 수 있으므로,
// 브레이크가 시작될 때마다 이 표시를 풀에서 하나 꺼내 그 적에게 붙이고(EnemyUI), 사라지면 풀로 돌려놓는다.
// - 세는 동안: '브레이크 피해' + 합계. 첫 피해가 들어오면 나타나고, 맞을 때마다 숫자가 올라가며 튄다.
// - 끝나면(브레이크 종료, 또는 그 적이 처치되어 꺼짐): '브레이크 총 피해'로 바꿔 잠시 보여 준 뒤 사라진다. 적의 자식이 아니라서 처치된 뒤에도 남는다.
// - 자리: 몸 옆, Player 반대쪽(그쪽이 화면 밖이고 Player 쪽은 화면 안이면 Player 쪽). 보스 표시와 같은 규칙이고 크기만 작다.
// 읽기만 하고 게임 상태는 바꾸지 않는다. 일시정지·히트스톱과 무관하게 실제 시간으로 움직인다(StageHUD의 표시와 같다).
public class BreakDamageCounterView : MonoBehaviour
{
    private const string CountingLabel = "브레이크 피해";
    private const string TotalLabel = "브레이크 총 피해";
    private const float FadeIn = 0.12f;            // 나타나는 시간(초)
    private const float Slide = 0.12f;             // Player가 적을 넘어가 반대쪽으로 옮길 때 미끄러지는 시간(초)
    private const float SideDeadZone = 0.3f;       // Player가 적 중심에서 이만큼(칸) 넘어가야 쪽을 바꾼다
    private const float ViewMargin = 0.25f;        // 화면 가장자리 여유(칸)
    private const float Gap = 0.5f;                // 몸에서 표시 가장자리까지(칸)
    private const float Rise = 0.3f;               // 몸 중심보다 올리는 높이(칸)
    private const float CountTime = 0.25f;         // 숫자가 따라 올라가는 시간(초)
    private const float HitPunch = 0.18f, TotalPunch = 0.35f;
    private const float HoldTime = 1.6f, FadeOut = 0.4f;

    private static readonly Dictionary<CanvasGroup, Stack<BreakDamageCounterView>> Pools = new();
    private static readonly List<BreakDamageCounterView> Live = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPools()
    {
        Pools.Clear();
        Live.Clear();
    }

    // 지금 화면에 떠 있는 표시(검증·연출이 읽기만 한다).
    public static IReadOnlyList<BreakDamageCounterView> Active => Live;

    private enum State { Idle, Counting, Total }

    private CanvasGroup source;
    private CanvasGroup group;
    private Canvas canvas;
    private TMP_Text label, value;
    private Vector3 baseScale;
    private State state;
    private EnemyController owner;
    private Collider2D body;
    private Vector2 bodyOffset = new Vector2(0f, 1f), bodyExtents = new Vector2(0.75f, 0.75f);
    private float total, shown, hideTime, halfWidth;
    private int shownInt = -1;
    private Tween countTween;
    private float farSide = 1f, side = 1f, sideVelocity;
    private Vector3 position;

    public EnemyController Owner => owner;
    public bool IsCounting => state == State.Counting;
    public bool IsShowingTotal => state == State.Total;
    public float Total => total;
    public string LabelText => label != null ? label.text : string.Empty;
    public string ValueText => value != null ? value.text : string.Empty;
    public float Alpha => group != null ? group.alpha : 0f;

    // 그 적의 브레이크 피해를 세기 시작한다. scale = Prefab 크기에 곱하는 배율(보스 표시보다 작게).
    public static BreakDamageCounterView Begin(CanvasGroup prefab, EnemyController owner, float scale)
    {
        if (prefab == null || owner == null) return null;
        if (!Pools.TryGetValue(prefab, out var pool)) Pools[prefab] = pool = new Stack<BreakDamageCounterView>();
        BreakDamageCounterView view = null;
        while (pool.Count > 0 && view == null) view = pool.Pop();       // Scene이 내려가며 사라진 것은 버린다
        if (view == null)
        {
            var instance = Instantiate(prefab);
            instance.name = prefab.name + "(Enemy)";
            instance.interactable = false;
            instance.blocksRaycasts = false;
            view = instance.gameObject.AddComponent<BreakDamageCounterView>();
            view.source = prefab;
            view.group = instance;
            view.canvas = instance.GetComponent<Canvas>();
            view.label = FindText(instance.transform, "Label");
            view.value = FindText(instance.transform, "Value");
            view.baseScale = instance.transform.localScale;
        }
        view.Open(owner, scale);
        return view;
    }

    private static TMP_Text FindText(Transform root, string name)
    {
        var child = root.Find(name);
        return child != null ? child.GetComponent<TMP_Text>() : null;
    }

    private void Open(EnemyController target, float scale)
    {
        owner = target;
        // 그 적과 같은 Scene에 둔다(Scene이 내려가면 함께 사라진다).
        var scene = target.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && gameObject.scene != scene && scene.name != "DontDestroyOnLoad")
            SceneManager.MoveGameObjectToScene(gameObject, scene);
        transform.localScale = baseScale * Mathf.Max(0.1f, scale);
        body = FindBody(target);
        bodyOffset = new Vector2(0f, 1f);
        bodyExtents = new Vector2(0.75f, 0.75f);
        total = shown = 0f;
        shownInt = -1;
        countTween?.Kill();
        ResetPunch();
        SetLabel(CountingLabel);
        SetValue(0f);
        state = State.Counting;
        group.alpha = 0f;
        if (canvas != null) canvas.enabled = true;
        gameObject.SetActive(true);
        enabled = true;
        if (!Live.Contains(this)) Live.Add(this);
        Place(0f, true);
    }

    // 그 적이 이번에 실제로 잃은 체력을 더한다.
    public void Add(EnemyController from, float amount)
    {
        if (state != State.Counting || from != owner || amount <= 0f) return;
        total += amount;
        countTween?.Kill();
        countTween = DOTween.To(() => shown, SetShown, total, CountTime).SetEase(Ease.OutCubic).SetUpdate(true);
        Punch(value, HitPunch, 0.2f);
    }

    // 세기를 끝낸다: 총 피해로 바꿔 잠시 보여 준 뒤 사라진다. 피해가 없었으면 바로 사라진다.
    public void End(EnemyController from)
    {
        if (state != State.Counting || from != owner) return;
        Finish();
    }

    private void Finish()
    {
        countTween?.Kill();
        if (total <= 0f) { Release(); return; }
        shown = total;
        SetValue(shown);
        SetLabel(TotalLabel);
        Punch(value, TotalPunch, 0.32f);
        Punch(label, TotalPunch * 0.5f, 0.32f);
        state = State.Total;
        hideTime = Time.unscaledTime + HoldTime;
    }

    private void LateUpdate()
    {
        if (state == State.Idle) return;
        float dt = Time.unscaledDeltaTime;
        // 세는 중에 그 적이 꺼졌다(처치·풀 반환): 마지막 자리에서 총 피해를 보여 준다
        if (state == State.Counting && (owner == null || !owner.isActiveAndEnabled)) Finish();
        if (state == State.Idle) return;
        Place(dt, false);
        // 세는 동안에는 첫 피해가 들어온 뒤부터 보인다(브레이크만 걸리고 맞지 않은 적 옆에 0을 띄우지 않는다)
        bool visible = state == State.Counting ? total > 0f : Time.unscaledTime < hideTime;
        if (visible)
            group.alpha = Mathf.MoveTowards(group.alpha, 1f, dt / FadeIn);
        else if (state == State.Counting)
            group.alpha = 0f;
        else
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, dt / FadeOut);
            if (group.alpha <= 0f) Release();
        }
    }

    private void Release()
    {
        countTween?.Kill();
        ResetPunch();
        state = State.Idle;
        owner = null;
        body = null;
        group.alpha = 0f;
        if (canvas != null) canvas.enabled = false;
        enabled = false;
        Live.Remove(this);
        gameObject.SetActive(false);
        if (source != null && Pools.TryGetValue(source, out var pool)) pool.Push(this);
    }

    private void OnDestroy()
    {
        countTween?.Kill();
        Live.Remove(this);
    }

    private void SetShown(float amount)
    {
        shown = amount;
        SetValue(amount);
    }

    private void SetValue(float amount)
    {
        int rounded = Mathf.RoundToInt(amount);
        if (value == null || rounded == shownInt) return;
        shownInt = rounded;
        value.text = rounded.ToString("N0");
        UpdateWidth();
    }

    private void SetLabel(string text)
    {
        if (label == null || label.text == text) return;
        label.text = text;
        UpdateWidth();
    }

    // 글자 폭(칸)의 절반. 몸에서 표시 가장자리까지의 거리를 지키고 화면 안에 드는지 볼 때 쓴다.
    private void UpdateWidth()
    {
        float width = 0f;
        if (label != null) width = Mathf.Max(width, label.preferredWidth);
        if (value != null) width = Mathf.Max(width, value.preferredWidth);
        halfWidth = width * 0.5f * Mathf.Abs(transform.lossyScale.x);
    }

    private static void Punch(TMP_Text target, float amount, float duration)
    {
        if (target == null) return;
        var rect = target.rectTransform;
        rect.DOKill(true);
        rect.localScale = Vector3.one;
        rect.DOPunchScale(Vector3.one * amount, duration, 6, 0.6f).SetUpdate(true);
    }

    private void ResetPunch()
    {
        foreach (var text in new[] { label, value })
        {
            if (text == null) continue;
            text.rectTransform.DOKill(true);
            text.rectTransform.localScale = Vector3.one;
        }
    }

    // 몸 옆, Player 반대쪽에 둔다. 그 적이 꺼지면 마지막 자리에 둔다.
    private void Place(float dt, bool snap)
    {
        var cam = Camera.main;
        if (canvas != null && cam != null && canvas.worldCamera != cam) canvas.worldCamera = cam;
        if (owner != null && owner.isActiveAndEnabled)
        {
            if (body != null && body.enabled && body.gameObject.activeInHierarchy)
            {
                var bounds = body.bounds;
                bodyOffset = bounds.center - owner.transform.position;
                bodyExtents = bounds.extents;
            }
            Vector2 center = (Vector2)owner.transform.position + bodyOffset;
            var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player != null)
            {
                float dx = center.x - player.transform.position.x;
                if (snap || Mathf.Abs(dx) > SideDeadZone) farSide = dx >= 0f ? 1f : -1f;
            }
            float reach = bodyExtents.x + Gap + halfWidth;
            float wanted = farSide;
            if (cam != null && cam.orthographic && !Fits(cam, center.x + wanted * reach) && Fits(cam, center.x - wanted * reach))
                wanted = -wanted;
            if (snap)
            {
                side = wanted;
                sideVelocity = 0f;
            }
            else
                side = Mathf.SmoothDamp(side, wanted, ref sideVelocity, Slide, Mathf.Infinity, Mathf.Max(dt, 1e-5f));
            position = new Vector3(center.x + side * reach, center.y + Rise, owner.transform.position.z);
        }
        transform.position = position;
    }

    private bool Fits(Camera cam, float x)
    {
        float half = cam.orthographicSize * cam.aspect - ViewMargin;
        float cx = cam.transform.position.x;
        return x - halfWidth >= cx - half && x + halfWidth <= cx + half;
    }

    // 몸 크기를 읽을 Collider2D: 루트(모터 몸통) → 피격 판정(EnemyHealth 개체) → 켜진 자식 Collider 가운데 가장 큰 것. StageHUD와 같은 순서다.
    private static Collider2D FindBody(EnemyController target)
    {
        var found = target.GetComponent<Collider2D>();
        if (found != null) return found;
        if (target.Health != null)
        {
            found = target.Health.GetComponent<Collider2D>();
            if (found != null) return found;
        }
        float largest = 0f;
        foreach (var each in target.GetComponentsInChildren<Collider2D>())
        {
            if (!each.enabled) continue;
            var size = each.bounds.size;
            if (size.x * size.y <= largest) continue;
            largest = size.x * size.y;
            found = each;
        }
        return found;
    }
}
