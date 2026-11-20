using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 각개 전투 엘리베이터의 탑승 입구(2026-11-20). 케이지 앞의 상호작용 영역(Layer 8 Interact, Trigger)이다.
/// - 상호작용 표시와 Lobby 잠금(LobbyInteractionLock)은 RoomTrigger와 같은 방식으로 다룬다. RoomTrigger가 아니므로
///   Lobby 안내(LobbyOnboarding.FindEntrance)가 훈련 입구를 고를 때 섞이지 않고, 훈련 메뉴도 열지 않는다.
/// - 예/아니오 창을 쓰지 않고 두 번 누르기로 확인한다: 첫 입력은 케이지 위에 "한 번 더 누르면 출발" 안내를 띄우고,
///   confirmWindow초 안에 다시 상호작용하면 출발한다(FieldElevator.RequestRide). 영역을 나가거나 시간이 지나면 처음으로 돌아간다.
///   첫 입력 뒤 InteractAbility를 끝내 두어야 두 번째 입력이 이 입구에 다시 들어온다(SelfTrainingEntrancePanel과 같은 방식).
/// </summary>
[DisallowMultipleComponent]
public sealed class FieldElevatorTrigger : MonoBehaviour, IInteractable
{
    [SerializeField] private FieldElevator elevator;
    [SerializeField, Tooltip("상호작용 표시(RoomTrigger의 버튼 그림 사본)")] private CanvasGroup interactUI;
    [SerializeField, Tooltip("케이지 위 안내 글자")] private TMP_Text noticeLabel;
    [SerializeField, Min(.5f), Tooltip("첫 입력 뒤 두 번째 입력을 기다리는 시간(초)")] private float confirmWindow = 3f;
    [SerializeField, Min(.01f)] private float noticeFade = .2f;
    [SerializeField, TextArea] private string confirmText = "각개 전투 교장행\n한 번 더 누르면 출발합니다";
    [SerializeField, TextArea] private string readyText = "각개 전투 교장행 승강기\n앞에서 상호작용하면 탑승합니다";

    [Header("소리")]
    [SerializeField, Tooltip("첫 입력(출발 확인 대기)")] private SoundOptions armSound;
    [SerializeField, Tooltip("출발할 수 없을 때")] private SoundOptions rejectSound;

    private bool playerInside;
    private bool blocked;
    private bool armed;
    private float armedUntil;
    private float noticeUntil;
    private Color noticeColor = Color.white;

    public FieldElevator Elevator => elevator;
    public bool IsArmed => armed;
    public bool PlayerInside => playerInside;
    public string ReadyText => readyText;
    public float NoticeAlpha => noticeLabel != null ? noticeLabel.alpha : 0f;

    private void Awake()
    {
        if (interactUI != null) interactUI.alpha = 0f;
        if (noticeLabel != null) { noticeColor = noticeLabel.color; noticeLabel.alpha = 0f; }
    }

    private void OnDisable()
    {
        Disarm();
        noticeUntil = 0f;
        playerInside = false;
        if (interactUI != null) { interactUI.DOKill(); interactUI.alpha = 0f; }
        if (noticeLabel != null) noticeLabel.alpha = 0f;
    }

    private void Update()
    {
        // 잠금이 걸리거나 풀리는 순간에만 표시를 바꾼다(RoomTrigger와 같다).
        bool now = LobbyInteractionLock.IsBlocked(this);
        if (now != blocked)
        {
            blocked = now;
            if (playerInside && interactUI != null) interactUI.DOFade(blocked || !Boardable ? 0f : 1f, .3f);
        }
        if (armed && Time.time > armedUntil) Disarm();
        if (noticeLabel != null)
        {
            float target = Time.time < noticeUntil ? 1f : 0f;
            float alpha = Mathf.MoveTowards(noticeLabel.alpha, target, Time.deltaTime / noticeFade);
            if (!Mathf.Approximately(alpha, noticeLabel.alpha)) noticeLabel.alpha = alpha;
        }
    }

    private bool Boardable => elevator != null && elevator.CanBoard;

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (!col.TryGetComponent<PlayerController>(out _)) return;
        playerInside = true;
        blocked = LobbyInteractionLock.IsBlocked(this);
        if (interactUI != null && !blocked && Boardable) interactUI.DOFade(1f, .3f);
    }

    private void OnTriggerExit2D(Collider2D col)
    {
        if (!col.TryGetComponent<PlayerController>(out _)) return;
        playerInside = false;
        Disarm();
        if (interactUI != null) interactUI.DOFade(0f, .3f);
    }

    public bool CanInteract(PlayerController player)
    {
        if (!playerInside || elevator == null || !elevator.CanBoard) return false;
        if (LobbyInteractionLock.IsBlocked(this))
        {
            LobbyInteractionLock.NotifyRejected(this);
            return false;
        }
        return true;
    }

    public void Interact(PlayerController player)
    {
        if (!armed)
        {
            armed = true;
            armedUntil = Time.time + confirmWindow;
            PlaySound(armSound);
            ShowNotice(confirmText, confirmWindow);
            // 상호작용 상태를 바로 끝내 두 번째 입력을 받는다.
            if (player != null && player.AbilityManager != null && player.AbilityManager.InteractAbility != null)
                player.AbilityManager.InteractAbility.Interrupt();
            return;
        }
        Disarm();
        noticeUntil = 0f;
        if (interactUI != null) interactUI.DOFade(0f, .3f);
        if (elevator == null || !elevator.RequestRide())
        {
            PlaySound(rejectSound);
            if (player != null && player.AbilityManager != null && player.AbilityManager.InteractAbility != null)
                player.AbilityManager.InteractAbility.Interrupt();
        }
    }

    public void CancelInteract(PlayerController player)
    {
        // 무장은 시간·이탈로만 풀린다(출발 뒤의 정리는 방 전환이 맡는다).
    }

    /// <summary>케이지 위에 안내 글자를 seconds초 동안 띄운다.</summary>
    public void ShowNotice(string text, float seconds)
    {
        if (noticeLabel == null) return;
        noticeLabel.text = text;
        noticeLabel.color = new Color(noticeColor.r, noticeColor.g, noticeColor.b, noticeLabel.alpha);
        noticeUntil = Time.time + Mathf.Max(0f, seconds);
    }

    /// <summary>등장 연출 끝의 탑승 안내.</summary>
    public void ShowReadyNotice(float seconds) => ShowNotice(readyText, seconds);

    private void Disarm()
    {
        if (!armed) return;
        armed = false;
        if (noticeUntil > Time.time && noticeLabel != null && noticeLabel.text == confirmText) noticeUntil = 0f;
    }

    private void PlaySound(SoundOptions sound)
    {
        if (sound != null && sound.clip != null && SoundManager.Instance != null) SoundManager.Instance.PlaySound(sound, transform.position);
    }
}
