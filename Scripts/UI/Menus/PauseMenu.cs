using System;
using UnityEngine;

public class PauseMenu : SelectableMenuUI
{
    [SerializeField] private TitlePanelMotion motion;
    [SerializeField] private RectTransform panelFit;
    [SerializeField] private TMPro.TextMeshProUGUI hint;

    protected override bool CanSelect => IsActive && motion.IsReady;

    protected override void Awake()
    {
        base.Awake();
        InputBindingHint.Bind(hint, "{MenuVertical} 선택   ·   {Confirm} 확인   ·   {Escape} 계속하기");
    }

    private void OnRectTransformDimensionsChange()
    {
        if (IsActive) FitPanel();
    }

    private void FitPanel()
    {
        Vector2 viewport = ((RectTransform)transform).rect.size;
        Vector2 panel = panelFit.rect.size;
        float fit = Mathf.Min(1f, .9f * viewport.x / panel.x, .9f * viewport.y / panel.y);
        panelFit.localScale = Vector3.one * fit;
    }

    public override void Open()
    {
        FitPanel();
        base.Open();
        motion.Open();
    }

    public void Close(Action hidden)
    {
        base.Close();
        motion.Close(hidden);
    }
}