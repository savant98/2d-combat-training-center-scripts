using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무기 상세 페이지의 구매 줄 하나(무기 해금 · A 모듈 · B 모듈, 2026-11-18).
/// 기존 강화 슬롯처럼 원 노드의 색(구매 완료 = 청록, 미구매 = 붉은색)과 켜짐 표시로 상태를 보이고, 설명·가격·상태 글자를 항상 함께 보여 준다.
/// 선택 대상(hitArea)은 줄보다 1/1.15 작게 두어, 선택되어 커지면 외곽선이 줄 바로 바깥에 맞는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponModuleRow : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private RectTransform hitArea;
    [SerializeField] private Image highlight;

    [Header("Visuals")]
    [SerializeField] private Image node;
    [Tooltip("구매를 마친 노드에 켜지는 표시")]
    [SerializeField] private Image indicator;
    [SerializeField] private Graphic contact;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI statusText;

    public RectTransform HitArea => hitArea;
    public Image Highlight => highlight;
    public Transform FeedbackPoint => node != null ? node.transform : transform;

    public void Show(string title, string description, Color descriptionColor, string cost, Color costColor,
        string status, Color statusColor, Color nodeColor, bool purchased)
    {
        if (titleText != null) titleText.text = title;
        if (descriptionText != null)
        {
            descriptionText.text = description;
            descriptionText.color = descriptionColor;
        }
        if (costText != null)
        {
            costText.text = cost;
            costText.color = costColor;
        }
        if (statusText != null)
        {
            statusText.text = status;
            statusText.color = statusColor;
        }
        if (node != null) node.color = nodeColor;
        if (indicator != null) indicator.enabled = purchased;
        if (contact != null) contact.color = statusColor;
    }
}
