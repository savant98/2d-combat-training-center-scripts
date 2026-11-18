using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Lobby 설비(훈련 메뉴·업그레이드·자율훈련장의 홀로그램 송출기) 한 대의 상태를 맡는다(2026-11-14). 표시 전용이며 판정·이동과는 무관하다.
/// - 메뉴: 이 설비의 메뉴가 열리면 Animator의 Open을 켜 장치가 펼쳐지고(상징 판이 누워 투사판이 되고 투사 팔이 선다), 닫히면 다시 접힌다.
///   공회전(기어·전원선·표시등)은 Animator의 다른 층에서 계속 돈다.
/// - 전원: 꺼져 있으면(방전) 공회전이 멈추고 기능부의 빛과 표시선이 꺼지며, 접근 불가 푯말이 서고 입구가 잠긴다(LobbyInteractionLock).
///   나중에 컷씬으로 해금할 때는 SetPowered(true)만 부르면 된다. 사용자 요청: "접근 불가가 풀렸을 때의 디자인을 기준으로 방전된 상태를 보여 주도록".
/// - 조립 자세(2026-11-15 2차): assembleClip이 있으면(업그레이드·자율훈련장) 방전 중에는 그 Clip의 0초 자세(분해·수납: 콘솔이 하우징 속으로 내려가고,
///   타워가 눕고, 갠트리가 내려앉고, 상징판이 얹혀 있다)로 서고, 전원이 들어오면 끝 자세(조립 완료 = Prefab에 저장된 자세)로 선다.
///   사용자 요청: "기계가 조립되는 식으로 만들어줘 … 그렇다면, 접근불가상태일떄의 모습도 달라지겠지". 조립 과정은 LobbyStationUnlock이 재생한다.
///   자세는 Play 중에만 적용한다(Edit Mode에서 바꾸면 MainScene에 Prefab 재정의가 생긴다).
/// - 잠긴 입구의 실제 상호작용은 푯말 흔들림·시설 안내 팝업·기존 실패 Feedback으로 알린다.
/// - 작업 동작(2026-11-18): 메뉴 안에서 설비가 일하는 순간(구매·장착, menus 아래의 ILobbyStationActivity.Performed)에 Animator의 Trigger 'Upgrade'를 건다
///   (업그레이드 장치만 그 파라미터가 있다: 메뉴 층 Opened → Upgrading → Opened). 사용자 요청: "업그레이드 장치의 업그레이드 시 Animation도 추가해줘".
///   작업 도중 메뉴가 닫혀 닫는 동작으로 넘어가면, 닫는 동작이 쓰지 않는 부품(받침 콘솔·렌즈·타워)을 작업 Clip의 끝 자세(쉬는 자세)로 한 번 되돌린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyStation : MonoBehaviour
{
    [Header("시설 이용 상태")]
    [SerializeField, Tooltip("Scene에서 켜면 이용 가능, 끄면 이용 불가입니다. Play 전·실행 중 모두 변경할 수 있으며 빛·애니메이션·푯말·입구 잠금이 함께 바뀝니다. 첫 입소 안내 중에는 환영 메일을 먼저 확인해야 합니다.")] private bool powered = true;
    [SerializeField] private Animator animator;
    [SerializeField, Tooltip("이 설비가 띄우는 메뉴들(IUIInputReceiver를 구현한 컴포넌트. 목록·상세처럼 화면이 여럿이면 모두 넣는다)")] private MonoBehaviour[] menus;
    [SerializeField, Tooltip("방전 중에 잠그는 입구·설비(RoomTrigger·UpgradeTrigger)")] private Component[] lockedTargets;
    [SerializeField, Tooltip("이 설비의 메뉴가 열리면 함께 접히는 다른 장치의 Animator(Lobby 간판: 메뉴 화면과 겹치지 않게 위로 올라간다)")] private Animator[] followers;

    [Header("조립 자세")]
    [SerializeField, Tooltip("조립 Clip(0초 = 분해·수납된 방전 자세, 끝 = 조립 완료 자세). 비우면 방전 중에도 형상이 그대로다(훈련 메뉴 장치). tools/Unity/LobbyStationAssembly.cs가 만든다")]
    private AnimationClip assembleClip;

    [Header("작업 동작")]
    [SerializeField, Tooltip("메뉴 안에서 설비가 일할 때(구매·장착) Animator의 Upgrading 상태가 재생하는 Clip(0초·끝 = 열린 자세). 작업 도중 메뉴가 닫히면 이 Clip의 끝 자세를 한 번 적용해, 닫는 동작이 쓰지 않는 부품(받침 콘솔·렌즈·타워)이 어긋난 채 남지 않게 한다. 비우면 되돌리지 않는다. tools/Unity/LobbyStationAssembly.cs가 연결한다")]
    private AnimationClip activityClip;
    [SerializeField, Tooltip("Upgrading Clip의 시작 이벤트(PlayActivitySound)에서 한 번 재생하는 작업음. 메뉴 닫힘·방전·비활성화·작업 종료 때 멈춘다")]
    private SoundOptions activitySound;

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

    [Header("접근 불가 안내")]
    [SerializeField] private GameObject unavailablePopupPrefab;
    [SerializeField] private FeedbackDataSO unavailableFeedback;
    [SerializeField, TextArea] private string unavailableMessage = "현재 이 시설은 이용할 수 없습니다.\n시설이 활성화된 뒤 다시 방문하십시오.";
    private GameObject unavailablePopupInstance;
    private UpgradePurchaseDialog unavailableDialog;

    private static readonly int OpenId = Animator.StringToHash("Open");
    // 작업 동작(업그레이드 장치의 메뉴 층 Opened → Upgrading → Opened). 파라미터가 없는 설비(훈련 메뉴·자율훈련장)는 아무것도 하지 않는다.
    private static readonly int UpgradeId = Animator.StringToHash("Upgrade");
    private static readonly int UpgradingStateId = Animator.StringToHash("Upgrading");
    private const int MenuLayer = 1;
    private readonly List<ILobbyStationActivity> activities = new();
    private RuntimeAnimatorController upgradeCheckedFor;
    private bool hasUpgrade, inActivity, activitySettlePending;
    private SoundHandle activitySoundHandle;
    private bool activitySoundStarted;
    private bool registered, menuOpen, applied, appliedPowered;
    private float shake;
    private Quaternion barrierRotation;
    private bool barrierCached;
    // 조립 Clip으로 완료 자세가 아닌 자세를 적용했는가(방전 자세, 해금 연출 도중, 꺼진 채 적용해 반영을 장담할 수 없을 때). 전원이 들어오면 완료 자세로 되돌린다.
    private bool poseSampled;

    /// <summary>전원이 들어와 쓸 수 있는 상태인가.</summary>
    public bool IsPowered => powered;
    // 해금 연출(LobbyStationUnlock)이 부품을 하나씩 켜 갈 때 읽는 목록. 값은 바꾸지 않는다.
    public int LitCount => litRenderers != null ? litRenderers.Length : 0;
    public Renderer LitRenderer(int index) => litRenderers != null && index >= 0 && index < litRenderers.Length ? litRenderers[index] : null;
    public Material LitMaterial(int index) => litMaterials != null && index >= 0 && index < litMaterials.Length ? litMaterials[index] : null;
    public GameObject Barrier => barrier;
    /// <summary>지금 이 설비의 메뉴가 떠 있어 장치를 펼쳐 둔 상태인가.</summary>
    public bool IsMenuOpen => menuOpen;
    public Animator Animator => animator;
    /// <summary>조립 Clip(0초 = 분해·수납된 방전 자세, 끝 = 조립 완료 자세). 없으면 null.</summary>
    public AnimationClip AssembleClip => assembleClip;

    /// <summary>해금 연출이 조립 Clip의 자세를 직접 적용했음을 알린다. complete가 아니면 다음 전원 적용 때 완료 자세로 되돌린다.</summary>
    public void NotifyAssemblyPose(bool complete) => poseSampled = !complete;

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
        SubscribeActivities();
        // 꺼지는 동안 작업 동작이 끊겼으면 켜질 때 쉬는 자세로 되돌린다(꺼진 개체에는 Clip 자세 적용을 장담할 수 없다).
        // Apply보다 먼저 둔다: 방전이면 Apply가 조립 Clip의 방전 자세로 다시 덮는다.
        if (activitySettlePending) SettleActivity();
        Apply();
    }

    private void OnDisable()
    {
        StopActivitySound();
        UnsubscribeActivities();
        if (inActivity) { inActivity = false; activitySettlePending = true; }
        Unregister();
        if (unavailableDialog != null) unavailableDialog.Close();
        if (unavailablePopupInstance != null) Destroy(unavailablePopupInstance);
        unavailablePopupInstance = null; unavailableDialog = null;
        shake = 0f;
        menuOpen = false;
        if (barrier != null && barrierCached) barrier.transform.localRotation = barrierRotation;
    }

    private void Apply()
    {
        applied = true;
        appliedPowered = powered;
        if (!powered) StopActivitySound();
        if (powered && unavailableDialog != null && unavailableDialog.IsActive) unavailableDialog.Close();
        if (animator != null)
        {
            // 방전되면 접힌 자세에서 멈춘다(공회전도 멈춘다). 돌던 중에 꺼진 경우에만 자세를 되돌리면 된다(저장된 자세가 접힌 자세다).
            if (!powered && animator.enabled && animator.isInitialized)
            {
                // 작업 동작 도중에 꺼지면 닫는 동작이 쓰지 않는 부품(콘솔·렌즈·타워)을 먼저 쉬는 자세로 돌린다(메뉴 층 부품은 아래 Closed가 다시 쓴다).
                if (inActivity) SettleActivity();
                animator.SetBool(OpenId, false);
                if (HasUpgradeParameter()) animator.ResetTrigger(UpgradeId);
                animator.Play("Closed", 1, 0f);
                animator.Update(0f);
            }
            if (!powered) animator.enabled = false;
        }
        // 조립 자세: 방전이면 Animator를 끈 뒤 분해 자세를, 전원이면 Animator를 켜기 전에 조립 완료 자세를 둔다
        // (Animator가 켜질 때 그 순간 값을 기본값으로 묶으므로 완료 자세가 기본값이 된다).
        ApplyAssemblyPose();
        if (animator != null) animator.enabled = powered;
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

    private void ApplyAssemblyPose()
    {
        if (!Application.isPlaying || assembleClip == null) return;
        if (!powered)
        {
            assembleClip.SampleAnimation(gameObject, 0f);
            poseSampled = true;
        }
        else if (poseSampled || animator == null || !animator.enabled)
        {
            assembleClip.SampleAnimation(gameObject, assembleClip.length);
            // 꺼진 채 적용했다면 켜질 때(OnEnable → Apply) 한 번 더 적용한다.
            poseSampled = !gameObject.activeInHierarchy;
        }
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
        if (!IsLocked(target)) return;
        shake = 1f;
        if (unavailablePopupPrefab == null || UIManager.Instance == null || UIManager.Instance.BlocksGameplayInput
            || UIManager.Instance.IsUIInputBlocked || (unavailableDialog != null && unavailableDialog.IsActive)) return;
        if (unavailableDialog == null)
        {
            unavailablePopupInstance = Instantiate(unavailablePopupPrefab);
            unavailableDialog = unavailablePopupInstance.GetComponentInChildren<UpgradePurchaseDialog>(true);
        }
        if (unavailableDialog != null && unavailableDialog.ShowWarning(unavailableMessage, null, "시설 이용 안내")
            && unavailableFeedback != null && FeedbackPlayer.Instance != null)
            FeedbackPlayer.Instance.Play(unavailableFeedback, transform);
    }

    private void Update()
    {
        if (powered != appliedPowered) Apply();     // Inspector에서 전원을 바꿨을 때

        bool open = MenusOpen();
        if (open != menuOpen)
        {
            menuOpen = open;
            if (!open) StopActivitySound();
            if (animator != null && animator.isActiveAndEnabled)
            {
                animator.SetBool(OpenId, open);
                // 여는 동작(Opening) 중에 받아 아직 쓰지 않은 작업 신호가 다음에 열 때 재생되지 않게 지운다.
                if (!open && HasUpgradeParameter()) animator.ResetTrigger(UpgradeId);
            }
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

    // Animator가 이번 프레임을 평가한 뒤에 본다: 작업 상태(Upgrading)를 막 벗어났으면(끝까지 돌았거나 메뉴가 닫혀 Closing으로 넘어감) 쉬는 자세로 맞춘다.
    private void LateUpdate()
    {
        UpdateActivitySound();
        if (activityClip == null || animator == null || !animator.isActiveAndEnabled || !animator.isInitialized || animator.layerCount <= MenuLayer)
        {
            StopActivitySound();
            return;
        }
        bool now = animator.GetCurrentAnimatorStateInfo(MenuLayer).shortNameHash == UpgradingStateId
            || (animator.IsInTransition(MenuLayer) && animator.GetNextAnimatorStateInfo(MenuLayer).shortNameHash == UpgradingStateId);
        if (inActivity && !now) SettleActivity();
        if (!now) StopActivitySound();
        inActivity = now;
    }

    private bool MenusOpen()
    {
        if (!powered || menus == null) return false;
        for (int i = 0; i < menus.Length; i++)
            if (menus[i] != null && menus[i] is IUIInputReceiver receiver && receiver.IsActive) return true;
        return false;
    }

    // ---- 작업 동작 ----

    /// <summary>Upgrading Clip의 시작 AnimationEvent에서 부른다. Opening 중 대기하는 구매 신호에서는 소리를 먼저 내지 않는다.</summary>
    public void PlayActivitySound()
    {
        if (activitySoundStarted || !powered || !isActiveAndEnabled || !MenusOpen()
            || animator == null || !animator.isActiveAndEnabled || !animator.isInitialized || animator.layerCount <= MenuLayer
            || activitySound == null || activitySound.clip == null || SoundManager.Instance == null) return;
        bool upgrading = animator.GetCurrentAnimatorStateInfo(MenuLayer).shortNameHash == UpgradingStateId
            || (animator.IsInTransition(MenuLayer) && animator.GetNextAnimatorStateInfo(MenuLayer).shortNameHash == UpgradingStateId);
        if (!upgrading) return;

        activitySoundStarted = true;
        // 기존 SoundHandle 경로로 재생하되 반복을 끈다. 끝나기 전 중단해도 풀의 소스를 정확히 반환할 수 있다.
        activitySoundHandle = SoundManager.Instance.PlayLoop(activitySound, transform.position, transform);
        if (activitySoundHandle != null && activitySoundHandle.IsValid) activitySoundHandle.GetSource().loop = false;
    }

    private void UpdateActivitySound()
    {
        if (activitySoundHandle == null) return;
        if (!activitySoundHandle.IsValid) { activitySoundHandle = null; return; }
        if (!activitySoundHandle.GetSource().isPlaying)
        {
            activitySoundHandle.Stop();
            activitySoundHandle = null;
        }
    }

    private void StopActivitySound()
    {
        if (activitySoundHandle != null && activitySoundHandle.IsValid) activitySoundHandle.Stop();
        activitySoundHandle = null;
        activitySoundStarted = false;
    }

    // 메뉴(목록·상세 등) 아래에서 작업 신호를 내는 화면을 모두 찾아 한 번씩만 구독한다.
    private void SubscribeActivities()
    {
        UnsubscribeActivities();
        if (menus == null) return;
        foreach (var menu in menus)
        {
            if (menu == null) continue;
            foreach (var activity in menu.GetComponentsInChildren<ILobbyStationActivity>(true))
            {
                if (activity == null || activities.Contains(activity)) continue;
                activities.Add(activity);
                activity.Performed += HandleActivityPerformed;
            }
        }
    }

    private void UnsubscribeActivities()
    {
        foreach (var activity in activities)
            if (activity is UnityEngine.Object unityObject ? unityObject != null : activity != null) activity.Performed -= HandleActivityPerformed;
        activities.Clear();
    }

    // 메뉴가 구매·장착 연출을 시작했다: 전원이 있고 이 설비의 메뉴가 열려 있으며 Animator에 'Upgrade'가 있을 때만 작업 동작을 건다.
    private void HandleActivityPerformed()
    {
        if (!powered || !isActiveAndEnabled || animator == null || !animator.isActiveAndEnabled || !MenusOpen() || !HasUpgradeParameter()) return;
        animator.SetTrigger(UpgradeId);
    }

    private bool HasUpgradeParameter()
    {
        if (animator == null || !animator.isInitialized) return false;
        var controller = animator.runtimeAnimatorController;
        if (controller != upgradeCheckedFor)
        {
            upgradeCheckedFor = controller;
            hasUpgrade = false;
            if (controller != null)
                foreach (var parameter in animator.parameters)
                    if (parameter.nameHash == UpgradeId && parameter.type == AnimatorControllerParameterType.Trigger) { hasUpgrade = true; break; }
        }
        return hasUpgrade;
    }

    // 작업 Clip의 끝 자세(메뉴 층이 쓰는 부품은 열린 자세, 받침 콘솔·렌즈·타워는 쉬는 자세)를 한 번 적용한다.
    // 메뉴 층이 쓰는 부품은 Animator가 다음 평가에서 다시 쓰므로(닫는 동작은 열린 자세에서 시작한다) 실제로 남는 것은 콘솔·렌즈·타워의 쉬는 자세다.
    private void SettleActivity()
    {
        StopActivitySound();
        inActivity = false;
        if (activityClip == null || !Application.isPlaying) { activitySettlePending = false; return; }
        if (!gameObject.activeInHierarchy) { activitySettlePending = true; return; }
        activitySettlePending = false;
        activityClip.SampleAnimation(gameObject, activityClip.length);
    }
}
