using System;
using TMPro;
using UnityEngine;

/// <summary>배포 고지 정본을 읽고, 설정 메뉴를 유지한 채 음원 출처를 스크롤한다.</summary>
public sealed class AudioCreditsPanel : MonoBehaviour, IUIInputReceiver
{
    [SerializeField] private GameObject root;
    [SerializeField] private TextAsset notice;
    [SerializeField] private TextMeshProUGUI body;
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;

    private const float KeyboardScrollDistance = 160f;
    private UIManager inputOwner;
    private FeedbackDataSO confirmFeedback;
    private FeedbackDataSO cancelFeedback;

    public bool IsActive { get; private set; }

    // 마우스 버튼의 확인음은 부모 OptionMenu가 붙인 기존 리스너가 담당한다.
    // 키보드 확인·취소에만 같은 Feedback을 직접 한 번 재생한다.
    public void ConfigureFeedback(FeedbackDataSO confirm, FeedbackDataSO cancel)
    {
        confirmFeedback = confirm;
        cancelFeedback = cancel;
    }

    public void Open()
    {
        if (IsActive) return;
        if (root == null || notice == null || body == null || scroll == null ||
            scroll.content == null || scroll.viewport == null || UIManager.Instance == null)
        {
            Debug.LogError("음원 출처 패널의 루트·고지 원고·본문·스크롤 연결을 확인해 주세요.", this);
            return;
        }

        InputBindingHint.Bind(root.transform, "ScrollHint", "{MenuVertical} 스크롤    {Cancel} 돌아가기");
        root.SetActive(true);
        if (!isActiveAndEnabled || !root.activeInHierarchy) return;

        // TXT 정본을 그대로 표시하며 꺾쇠나 역슬래시를 TMP 명령으로 해석하지 않는다.
        body.richText = false;
        body.parseCtrlCharacters = false;
        body.overflowMode = TextOverflowModes.Overflow;
        body.text = notice.text;
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        body.ForceMeshUpdate();
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;

        IsActive = true;
        inputOwner = UIManager.Instance;
        inputOwner.SetInputTarget(this, UIInputLayer.Pause);
    }

    public void Close()
    {
        ReleaseInput();
        if (scroll != null) scroll.StopMovement();
        if (root != null) root.SetActive(false);
    }

    private void OnDisable()
    {
        // 부모 Options나 Scene이 닫혀도 다른 메뉴의 입력 대상은 제거하지 않는다.
        ReleaseInput();
    }

    private void ReleaseInput()
    {
        bool wasActive = IsActive;
        IsActive = false;
        if (wasActive && inputOwner != null) inputOwner.ClearInputTarget(this);
        inputOwner = null;
    }

    private bool CanReceiveInput => IsActive && isActiveAndEnabled && inputOwner != null &&
        !inputOwner.IsUIInputBlocked && inputOwner.IsCurrentInputTarget(this);

    public void OnMove(Vector2 direction)
    {
        if (!CanReceiveInput || Mathf.Abs(direction.y) < .5f) return;
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
        if (overflow <= 0f) return;
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition +
            Mathf.Sign(direction.y) * KeyboardScrollDistance / overflow);
    }

    public void OnConfirm()
    {
        if (!CanReceiveInput) return;
        PlayFeedback(confirmFeedback);
        Close();
    }

    public void OnCancel()
    {
        if (!CanReceiveInput) return;
        PlayFeedback(cancelFeedback);
        Close();
    }

    /// <summary>사용자가 출처 버튼을 눌렀을 때만 호출하며 웹 URL만 허용한다.</summary>
    public void OpenUrl(string url)
    {
        if (!CanReceiveInput || !Uri.TryCreate(url, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(address.Host)) return;
        Application.OpenURL(address.AbsoluteUri);
    }

    private static void PlayFeedback(FeedbackDataSO feedback)
    {
        if (feedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.PlayExact(feedback, Vector3.zero, Quaternion.identity);
    }
}
