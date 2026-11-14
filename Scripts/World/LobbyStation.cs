using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Lobby 설비(훈련 메뉴·업그레이드·자율훈련장의 홀로그램 송출기) 한 대의 상태를 맡는다(2026-11-14). 표시 전용이며 판정·이동과는 무관하다.
/// - 메뉴: 이 설비의 메뉴가 열리면 Animator의 Open을 켜 장치가 펼쳐지고(상징 판이 누워 투사판이 되고 투사 팔이 선다), 닫히면 다시 접힌다.
///   공회전(기어·전원선·표시등)은 Animator의 다른 층에서 계속 돈다.
/// - 전원: 꺼져 있으면(방전) 공회전이 멈추고 기능부의 빛과 표시선이 꺼지며, 접근 불가 푯말이 서고 입구가 잠긴다(LobbyInteractionLock).
///   나중에 컷씬으로 해금할 때는 SetPowered(true)만 부르면 된다. 사용자 요청: "접근 불가가 풀렸을 때의 디자인을 기준으로 방전된 상태를 보여 주도록".
/// - 잠긴 입구에서 상호작용하려 하면 푯말만 흔들린다(소리는 내지 않는다).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyStation : MonoBehaviour
{
    [SerializeField, Tooltip("전원. 끄면 방전된 모습으로 서고 입구가 잠긴다")] private bool powered = true;
    [SerializeField] private Animator animator;
    [SerializeField, Tooltip("이 설비가 띄우는 메뉴들(IUIInputReceiver를 구현한 컴포넌트. 목록·상세처럼 화면이 여럿이면 모두 넣는다)")] private MonoBehaviour[] menus;
    [SerializeField, Tooltip("방전 중에 잠그는 입구·설비(RoomTrigger·UpgradeTrigger)")] private Component[] lockedTargets;
    [SerializeField, Tooltip("이 설비의 메뉴가 열리면 함께 접히는 다른 장치의 Animator(Lobby 간판: 메뉴 화면과 겹치지 않게 위로 올라간다)")] private Animator[] followers;

    [Header("방전될 때 꺼지는 빛")]
    [SerializeField, Tooltip("전원이 있을 때만 빛나는 부품(기능부·표시선)")] private Renderer[] litRenderers;
    [SerializeField, Tooltip("켜졌을 때의 재질(위와 같은 순서)")] private Material[] litMaterials;
    [SerializeField, Tooltip("꺼졌을 때의 재질(위와 같은 순서). 비어 있는 칸의 부품은 꺼졌을 때 그리지 않는다")] private Material[] dormantMaterials;
    [SerializeField, Tooltip("글씨(전원이 꺼지면 어두워진다)")] private TMP_Text[] labels;
    [SerializeField] private Color litLabelColor = Color.white;
    [SerializeField] private Color dormantLabelColor = new Color(.2f, .22f, .25f, 1f);

    [Header("접근 불가 푯말")]
    [SerializeField, Tooltip("방전 중에만 세우는 푯말")] private GameObject barrier;
    [SerializeField, Min(0f), Tooltip("막힌 시도가 있을 때 푯말이 흔들리는 각도(도)와 시간(초)")] private float shakeAngle = 6f;
    [SerializeField, Min(.05f)] private float shakeTime = .55f;

    private static readonly int OpenId = Animator.StringToHash("Open");
    private bool registered, menuOpen, applied, appliedPowered;
    private float shake;
    private Quaternion barrierRotation;
    private bool barrierCached;

    /// <summary>전원이 들어와 쓸 수 있는 상태인가.</summary>
    public bool IsPowered => powered;
    /// <summary>지금 이 설비의 메뉴가 떠 있어 장치를 펼쳐 둔 상태인가.</summary>
    public bool IsMenuOpen => menuOpen;
    public Animator Animator => animator;

    /// <summary>전원을 켜거나 끈다(빛·공회전·푯말·입구의 잠금이 함께 바뀐다). 해금 컷씬이 부른다.</summary>
    public void SetPowered(bool value)
    {
        if (powered == value && applied) return;
        powered = value;
        Apply();
    }

    private void OnEnable()
    {
        if (barrier != null && !barrierCached)
        {
            barrierCached = true;
            barrierRotation = barrier.transform.localRotation;
        }
        Apply();
    }

    private void OnDisable()
    {
        Unregister();
        shake = 0f;
        menuOpen = false;
        if (barrier != null && barrierCached) barrier.transform.localRotation = barrierRotation;
    }

    private void Apply()
    {
        applied = true;
        appliedPowered = powered;
        if (animator != null)
        {
            // 방전되면 접힌 자세에서 멈춘다(공회전도 멈춘다). 돌던 중에 꺼진 경우에만 자세를 되돌리면 된다(저장된 자세가 접힌 자세다).
            if (!powered && animator.enabled && animator.isInitialized)
            {
                animator.SetBool(OpenId, false);
                animator.Play("Closed", 1, 0f);
                animator.Update(0f);
            }
            animator.enabled = powered;
        }
        if (litRenderers != null)
            for (int i = 0; i < litRenderers.Length; i++)
            {
                var target = litRenderers[i];
                if (target == null) continue;
                Material lit = litMaterials != null && i < litMaterials.Length ? litMaterials[i] : null;
                Material dormant = dormantMaterials != null && i < dormantMaterials.Length ? dormantMaterials[i] : null;
                Material wanted = powered ? lit : dormant;
                target.enabled = wanted != null;
                if (wanted != null && target.sharedMaterial != wanted) target.sharedMaterial = wanted;
                // 공회전이 남긴 밝기·흐름 값(Renderer별 값)을 지운다. 꺼진 재질이 그 값으로 다시 밝아지지 않게 한다.
                if (!powered) target.SetPropertyBlock(null);
            }
        if (labels != null)
            foreach (var label in labels)
                if (label != null) label.color = powered ? litLabelColor : dormantLabelColor;
        if (barrier != null && barrier.activeSelf == powered) barrier.SetActive(!powered);
        if (!powered && isActiveAndEnabled) Register();
        else Unregister();
    }

    private void Register()
    {
        if (registered) return;
        registered = true;
        LobbyInteractionLock.AddBlocker(IsLocked);
        LobbyInteractionLock.Rejected += HandleRejected;
    }

    private void Unregister()
    {
        if (!registered) return;
        registered = false;
        LobbyInteractionLock.RemoveBlocker(IsLocked);
        LobbyInteractionLock.Rejected -= HandleRejected;
    }

    private bool IsLocked(Component target) => !powered && target != null && lockedTargets != null && Array.IndexOf(lockedTargets, target) >= 0;

    private void HandleRejected(Component target)
    {
        if (IsLocked(target)) shake = 1f;
    }

    private void Update()
    {
        if (powered != appliedPowered) Apply();     // Inspector에서 전원을 바꿨을 때

        bool open = false;
        if (powered && menus != null)
            for (int i = 0; i < menus.Length && !open; i++)
                open = menus[i] != null && menus[i] is IUIInputReceiver receiver && receiver.IsActive;
        if (open != menuOpen)
        {
            menuOpen = open;
            if (animator != null && animator.isActiveAndEnabled) animator.SetBool(OpenId, open);
            if (followers != null)
                foreach (var follower in followers)
                    if (follower != null && follower.isActiveAndEnabled) follower.SetBool(OpenId, open);
        }

        if (shake > 0f && barrier != null)
        {
            // 세워 둔 푯말을 툭 친 것처럼 좌우로 흔들리다 멎는다.
            shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime / shakeTime);
            float angle = Mathf.Sin((1f - shake) * Mathf.PI * 5f) * shakeAngle * shake * shake;
            barrier.transform.localRotation = barrierRotation * Quaternion.Euler(0f, 0f, angle);
        }
    }
}
