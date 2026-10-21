using DG.Tweening;
using UnityEngine;

public class DeathMenuController : MonoBehaviour
{
    [SerializeField] private GameObject rootObject;
    [SerializeField] private SelectableMenuUI menu;
    [SerializeField] private Animator animator;
    [SerializeField] private CanvasGroup canvasGroup;

    private void Start()
    {
        rootObject.SetActive(false);
    }
    public void Show()
    {
        menu.OnSelected += HandleSelect;

        //animator.SetTrigger("Open");
        menu.Open();
        canvasGroup.DOFade(1f, 0.3f);
        rootObject.SetActive(true);
    }
    public void Hide()
    {
        HandleCancel();
    }
    private void HandleSelect(int index)
    {
        switch (index)
        {
            case 0:
                GameManager.Instance.RequestRetry();
                break;
            case 1:
                GameManager.Instance.RequestReturnToLobby();
                break;
        }
    }
    private void HandleCancel()
    {
        Cleanup();
    }
    private void Cleanup()
    {
        menu.OnSelected -= HandleSelect;

        menu.Close();
        canvasGroup.DOFade(0f, 0.3f).OnComplete(() => rootObject.SetActive(false));
    }
}
