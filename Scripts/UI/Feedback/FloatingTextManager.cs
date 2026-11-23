using UnityEngine;
using System.Collections.Generic;

public class FloatingTextManager : MonoBehaviour
{
    public static FloatingTextManager Instance;

    [SerializeField] private FloatingText prefab;
    [SerializeField] private Vector2 offSet = new (0f, 1f);
    [SerializeField] private int poolSize = 20;
    [SerializeField] private RectTransform canvasParent;

    private readonly Queue<FloatingText> pool = new();

    private struct StackedPopup
    {
        public FloatingText text;
        public FloatingText below;
        public Vector3 origin;
        public int frame;
        public EnemyHealth target;
        public uint life;
        public int group;
        public bool anchor;
    }

    private readonly List<StackedPopup> stacked = new();
    internal Canvas DamageCanvas => canvasParent != null ? canvasParent.GetComponentInParent<Canvas>() : null;

    private void Awake()
    {
        Instance = this;

        for (int i = 0; i < poolSize; i++)
        {
            CreateInstance();
        }
    }

    private FloatingText CreateInstance()
    {
        var obj = Instantiate(prefab, canvasParent);
        obj.gameObject.SetActive(false);
        pool.Enqueue(obj);
        return obj;
    }

    private FloatingText Get()
    {
        if (pool.Count == 0)
            CreateInstance();

        var obj = pool.Dequeue();
        // 반환된 개체를 다시 쓰므로, 예전처럼 새로 만든 개체와 같이 가장 위(마지막 형제)에 그린다.
        // (기존에는 풀 소진 뒤 새 Instantiate가 늘 마지막 형제라 최신 숫자가 위에 겹쳐 보였다.)
        obj.transform.SetAsLastSibling();
        obj.gameObject.SetActive(true);
        return obj;
    }

    private void LateUpdate()
    {
        foreach (var popup in stacked)
            if (popup.below != null) popup.text.PlaceAbove(popup.below);
    }

    public void Return(FloatingText ft)
    {
        for (int i = stacked.Count - 1; i >= 0; i--)
        {
            var popup = stacked[i];
            if (popup.text == ft) stacked.RemoveAt(i);
            else if (popup.below == ft)
            {
                popup.below = null;
                stacked[i] = popup;
            }
        }
        ft.gameObject.SetActive(false);
        pool.Enqueue(ft);
    }

    public void ShowDamage(WeaponBaseSO.DamageInfo damageInfo, Vector3 worldPos, Color? overrideColor = null,
        EnemyHealth target = null, uint targetLife = 0)
    {
        var camera = Camera.main;
        if (camera == null) return;
        FloatingText ft = Get();
        bool stacks = damageInfo.popupGroup != 0 && target != null;
        var previous = stacks && !damageInfo.popupAnchorOnly
            ? FindPredecessor(damageInfo.popupGroup, target, targetLife) : null;
        Vector3 origin = previous?.origin ?? worldPos;
        worldPos = origin + (Vector3)offSet;

        // 월드 위치 → UI 캔버스 화면 위치 변환
        Vector2 screenPos = camera.WorldToScreenPoint(worldPos);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasParent,
            screenPos,
            camera,
            out Vector2 localPos);

        ft.rect.anchoredPosition = localPos;

        // 데미지 표시
        ft.Show(damageInfo, overrideColor);
        if (!stacks) return;
        ft.MeasureStackInk();
        if (previous.HasValue) ft.PlaceAbove(previous.Value.text);
        stacked.Add(new StackedPopup
        {
            text = ft, below = previous?.text, origin = origin, frame = Time.frameCount, group = damageInfo.popupGroup,
            target = target, life = targetLife, anchor = damageInfo.popupAnchorOnly
        });
    }

    internal float GetStackRise(int group, EnemyHealth target, uint life)
    {
        var popup = FindPredecessor(group, target, life);
        return popup.HasValue && popup.Value.frame == Time.frameCount ? popup.Value.text.StackRise : 0f;
    }

    // 레이저 본타는 따로 표시하고, 이어진 추가타가 없을 때 가장 최근의 본타를 기준으로 삼는다.
    private StackedPopup? FindPredecessor(int group, EnemyHealth target, uint life)
    {
        StackedPopup? anchor = null;
        for (int i = stacked.Count - 1; i >= 0; i--)
        {
            var previous = stacked[i];
            if (previous.group != group || previous.target != target || previous.life != life) continue;
            if (!previous.anchor) return previous;
            anchor ??= previous;
        }
        return anchor;
    }
}
