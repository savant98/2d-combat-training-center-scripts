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
        obj.gameObject.SetActive(true);
        return obj;
    }

    public void Return(FloatingText ft)
    {
        ft.gameObject.SetActive(false);
        pool.Enqueue(ft);
    }

    public void ShowDamage(WeaponBaseSO.DamageInfo damageInfo, Vector3 worldPos, Color? overrideColor = null)
    {
        FloatingText ft = Get();

        worldPos += (Vector3)offSet;

        // 월드 위치 → UI 캔버스 화면 위치 변환
        Vector2 screenPos = Camera.main.WorldToScreenPoint(worldPos);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasParent,
            screenPos,
            Camera.main,
            out Vector2 localPos);

        ft.rect.anchoredPosition = localPos;

        // 데미지 표시
        ft.Show(damageInfo, overrideColor);
    }
}
