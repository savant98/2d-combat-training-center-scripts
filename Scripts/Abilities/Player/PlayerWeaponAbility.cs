using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
public class PlayerWeaponAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Attack;
    public override bool IsActive => abilityState != AbilityState.None;
    public AbilityState AttackPhase => abilityState;
    public PlayerWeaponTraits Traits { get; private set; }

    public enum RejectionReason
    {
        None, Unavailable, Cooldown, Gauge, Resolve, Reloading, EmptyMagazine, Overheated, GearCharging, Busy
    }

    /// <summary>사용할 수 없는 공격을 눌렀을 때 안내 사유를 HUD에 전달합니다.</summary>
    public event System.Action<RejectionReason> AttackRejected;
    private RejectionReason lastAttackRejection;
    private float nextRepeatedRejectionTime;

    /// <summary>일격필살에 스택이 부족하면 현재 스택을, 요구 표시가 필요 없으면 -1을 반환합니다.</summary>
    public int GetFinisherRequirementStacks(WeaponBaseSO weapon) =>
        weapon is SwordFinisherWeaponSO && Traits != null && Traits.Resolve < PlayerWeaponTraits.ResolveCapacity
            ? Traits.Resolve : -1;
    public int GetResolveRequirementStacks(WeaponBaseSO weapon, out int required)
    {
        required = weapon is SwordWaveWeaponSO wave ? wave.EmpowerCost : PlayerWeaponTraits.ResolveCapacity;
        if (weapon is not SwordWaveWeaponSO) return GetFinisherRequirementStacks(weapon);
        return Traits != null && Traits.Resolve < required ? Traits.Resolve : -1;
    }
    public string ActiveArmamentId => GetEquippedArmament(currentSetIndex)?.armamentId;
    public bool IsMachineGunAttacking => IsNormalAttackRunning && selectedWeapon is MachineGunWeaponSO;
    // 점프가 지금 공격을 끊고 나갈 수 있는지(2026-11-21 사용자 지시 "공격 중 점프도 대시와 똑같이", JumpAbility.TryJump).
    // 대시와 같은 범위다: 회수 단계(PlayerStateMachine.CanPerform의 대시 조건)와 기술 저장 중. 기관총 연사는 끊지 않고 쏘면서 뛰므로 제외한다.
    public bool CanJumpCancel => IsActive && !IsMachineGunAttacking && (isStoringSkill || abilityState == AbilityState.Recover);
    public Vector2 PrimaryFireDirection { get; private set; } = Vector2.right;
    public RangedWeaponSO EquippedStarShot => normalWeapon is RangedWeaponSO ranged && ranged.projectilePrefab is StarShot ? ranged : null;
    public HandCannonWeaponSO ActiveHandCannon => IsActive && !isStoringSkill ? selectedWeapon as HandCannonWeaponSO : null;
    public bool IsRailgunPrimaryActive => IsActive && !isStoringSkill && selectedWeapon == normalWeapon && normalWeapon is RailgunWeaponSO;
    public bool IsStarShotActive => !isStoringSkill && IsActive && selectedWeapon == normalWeapon && EquippedStarShot != null;
    private PlayerStarShotAim starAim;
    // 라이플 실행·회수 중에 새로 누른 공격(연타). 회수가 끝나는 순간 다음 한 발로 잇는다(TryContinueRifleFire, 2026-11-21).
    private bool rifleShotQueued;
    private bool restoreStorageIdle;
    public bool QueuePrimaryStarShot(RangedWeaponSO weapon, WeaponBaseSO.DamageInfo damage, Vector2 direction)
        => starAim != null && starAim.Queue(weapon, damage, direction);
    public const int StoredSkillCapacity = 3;
    private readonly List<StoredGearSkill> storedSkills = new();
    public IReadOnlyList<StoredGearSkill> StoredSkills => storedSkills;
    public float GetStoredSkillCooldownRemaining(int slotIndex)
    {
        return (uint)slotIndex < (uint)storedSkills.Count
            ? GetWeaponCooldownRemaining(storedSkills[slotIndex].Weapon) : 0f;
    }
    public bool IsStoringSkill => isStoringSkill;
    public float SkillStorageProgress => isStoringSkill ? 1f - Mathf.Clamp01(timer / weaponData.skillStoreDuration) : 0f;
    public event System.Action StoredSkillsChanged;
    public event System.Action<StoredGearSkill> SkillStorageStarted;
    public event System.Action<bool> SkillStorageFinished;
    public event System.Action StoredSkillUsed;
    private StoredGearSkill pendingSkill;
    private bool isStoringSkill;
    private int executionSetIndex = -1;
    private bool suppressHeldPrimaryUntilRelease;
    private readonly Dictionary<WeaponBaseSO, System.Action<CharacterController2D, Vector2>> hitHandlers = new();

    [Header("Melee Hitboxes")]
    [SerializeField] private string hammerWeaponName = "Hammer";
    [SerializeField] private string hammerSkillName = "HammerSkill";
    [SerializeField] private MeleeHitbox[] hammerHitBoxes;
    [SerializeField] private MeleeHitbox[] hammerSkillBoxes;
    [Tooltip("세트별 기술용 모형(0 = 해머 계열 기술, 1 = 별 계열 기술). 그 세트의 Gear·보조 무기는 이 모형을 움직이는 Clip으로 연출한다.")]
    [SerializeField] private GameObject[] weaponModels;
    [Tooltip("기본 공격 자리에 넣을 수 있는 무기별 대기 모형. 어느 세트에 넣든 그 무기의 모형이 보인다(표에 없는 무기는 세트의 기술용 모형을 쓴다).")]
    [SerializeField] private IdleWeaponModel[] idleModels;

    [System.Serializable]
    private class IdleWeaponModel
    {
        public string weaponName;
        public GameObject model;
    }

    [Header("Body Core")]
    [Tooltip("몸 중심의 코어(WeaponModel/Core). 해머의 손잡이 끝 고리가 여기에 물려 있다. WeaponModel의 자식이라 무기와 함께 움직인다.")]
    [SerializeField] private GameObject bodyCore;
    [Tooltip("이 모형이 보이는 동안에는 코어를 숨긴다. 그 자리에 자기 코어를 가진 무기(별 총의 소켓과 별 코어)를 넣는다.")]
    [SerializeField] private GameObject[] coreHiddenBy;

    [Header("Sword")]
    [Tooltip("칼의 weaponName. 이 무기가 기본 공격이면 아래 판정을 쓴다(콤보 단계 × MeleeWeaponSO.hitsPerStep개, 단계 순서대로).")]
    [SerializeField] private string swordWeaponName = "Sword";
    [SerializeField] private MeleeHitbox[] swordHitBoxes;

    [Header("Gear UI")]
    [SerializeField] private PlayerGearUI gearUI;

    // Weapons
    private WeaponBaseSO normalWeapon;
    private WeaponBaseSO secondaryWeapon;
    private int currentSetIndex = 0;

    // SkillGauge
    private float maxSkillGauge;

    private float timer = 0f;
    private float weaponSwapCoolTime = 0f;
    private float skillGauge = 0f;
    private AbilityState abilityState = AbilityState.None;
    private WeaponBaseSO selectedWeapon;

    private readonly InputBuffer normalWeaponBuffer = new(0f);
    private readonly InputBuffer secondaryWeaponBuffer = new(0f);
    // 저장 기술 사용 입력(아래 + 공격). 누른 순간 아래가 들어 있고 저장해 둔 기술이 있으면 기본 공격 버퍼 대신 여기에 넣는다(2026-11-17).
    // 기본 공격 중이면 곧바로 그 공격을 끊고 쓰고, 다른 기술 중이면 남겨 두었다가 그 기술이 끝나는 프레임에 쓴다(회수 중에 누른 것은 끝날 때까지 유지).
    private readonly InputBuffer storedSkillBuffer = new(0f);
    // 아래와 공격을 거의 함께 눌렀는데 공격이 한두 프레임 먼저 들어온 경우: 그 공격 키를 누르고 있는 채 이 시간 안에 아래가 들어오면 아래 + 공격으로 본다.
    private const float SimultaneousInputWindow = 0.1f;
    private float primaryPressTime = -999f;
    private bool primaryPressPending;
    private bool primaryPressDown;
    // 공격·기술 중에 새로 누른 Gear 키(2026-11-17). 끝났을 때 키를 아직 누르고 있으면 충전을 시작한다.
    private bool gearPressQueued;
    private bool gearKeyDown;
    // 공격마다 하나씩 늘리는 번호. 지난 공격의 코루틴이 다음 공격을 회수로 넘기지 못하게 한다(기술을 끝나는 프레임에 바로 이을 때).
    private int attackRun;
    private readonly Dictionary<WeaponBaseSO, float> weaponLastUseTimes = new();

    private PlayerWeaponDataSO weaponData;
    // Initialize가 만든 실행용 사본(에셋을 직접 고치지 않는다). OnDestroy에서 지운다.
    private PlayerWeaponDataSO runtimeWeaponData;
    // 자리(weaponSets 번호)마다 장착한 무장. 무장을 정하지 않은 자리는 null이고 무기 칸을 그대로 쓴다.
    private PlayerArmamentSO[] equippedArmaments = new PlayerArmamentSO[0];
    // 에셋(Player_Weapon_Data)의 자리별 기본 보조 무기. 실행용 사본의 secondary는 장착·잠금으로 바뀌므로 원래 값을 따로 둔다(2026-11-20 훈련 개편).
    private WeaponBaseSO[] defaultSecondaries = new WeaponBaseSO[0];

    // 기본 공격 자리를 잠시 다른 무기로 바꾼 상태(Rifle Gear 3단계 '머신건 장착', 2026-11-18). 그 자리를 든 동안만 기본 공격이 바뀌고 시간이 지나면 돌아온다.
    private WeaponBaseSO primaryOverride;
    private int primaryOverrideSet = -1;
    private float primaryOverrideEnd = -1f;
    private float primaryOverrideDuration;
    private FeedbackDataSO primaryOverrideExpirationFeedback;

    private int meleeComboIndex = 0;
    private float comboQueueWindow = 0f;
    private float lastMeleeAttackTime = -999f;
    private float lastWeaponSwapTime = -999f;
    private bool comboQueuedThisStep;
    private bool comboQueuedByHold;
    // 피격 경직과 겹친 기본 공격을 경직이 끝난 첫 행동 가능 프레임에 1타부터 내보내기 위한 표시(HoldAttackThroughHit).
    // restartAfterHit: 판정이 켜지기 전에 끊긴 첫 타격(효과 없음)을 다시 시작할지. currentHitboxOpened: 이번 타격의 판정이 한 번이라도 켜졌는지.
    private bool restartAfterHit;
    private bool currentHitboxOpened;
    // 기본 공격 자리의 근접 무기(해머·칼)를 쓰는 중인지. 유지 입력 콤보·판정 정리처럼 두 무기가 함께 쓰는 처리에 쓴다.
    private bool IsNormalMelee => !isStoringSkill && selectedWeapon == normalWeapon && selectedWeapon is MeleeWeaponSO;
    // 실행 끝·회수 중의 다음 타격 입력을 남은 시간을 기다리지 않고 잇는 무기인지(MeleeWeaponSO.chainWithoutRecover).
    private bool ChainsWithoutRecover => IsNormalMelee && ((MeleeWeaponSO)selectedWeapon).chainWithoutRecover;
    // 기본 근접 공격이 끊기거나 끝날 때 끌 궤적. Initialize에서 한 번 찾아 둔다. 칼은 두 자루라 궤적도 둘이다.
    private Renderer hammerArc, swordArc, swordArcOff;
    // 대기 모형이 세트의 기술용 모형과 다른 세트(예: 칼)에서 Gear·보조 무기를 쓰는 동안 기술용 모형으로 바꿔 들었는지(끝나면 되돌린다).
    private bool skillModelShown;
    // 해머 모형으로 연출한 기술이 끝나면 Animator가 WeaponRecover → Idle을 이 시간 동안 섞는다(큰 해머가 줄어들며 사라진다).
    // 그동안 칼 모형을 켜 두면 두 무기가 겹쳐 보이므로 섞임이 끝나는 시각에 켠다. PlayerBase.controller의 전이 시간과 같아야 한다.
    private const float SkillModelReturnBlend = 0.1f;
    private float modelReturnTime = -1f;   // 미뤄 둔 대기 모형을 켤 시각(-1 = 없음)
    private GameObject hammerAttackRig;    // 공격용 해머(WeaponModel/MeleeWeapon). 해머 기술의 Clip이 켠다
    private SpriteRenderer hammerAttackBlade;   // 그 안의 해머 그림(WeaponModel/MeleeWeapon/Hammer)

    //Gear
    private enum GearState
    {
        Inactive,
        Charging,
        Waiting
    }

    private GearState gearState = GearState.Inactive;
    private int gearStage = 0;
    private int chargingTargetStage = 0;
    private float gearTimer = 0f;
    private bool isGearHeld = false;

    // Gear·무기 변경의 몸 둘레 표시. DashAbility처럼 실행 중에 Player 아래에 만든다.
    private PlayerGearVisual gearVisual;
    private PlayerWeaponSwapVisual weaponSwapVisual;
    // 스킬 게이지를 몸 옆 배터리로 보여 주는 표시(2026-11-15, 날아오는 구슬 대신). 게이지는 적중 즉시 오른다.
    private PlayerSkillBatteryVisual skillBattery;

    public override void Initialize(AbilityManager manager)
    {
        base.Initialize(manager);

        Traits = new PlayerWeaponTraits(controller);
        // 장전이 끝날 때의 재장전 소리는 라이플 자리를 든 동안만 낸다(기관총 장착 중에는 라이플 탄창을 쓰지 않는다).
        Traits.IsRifleHeld = () => IsRifleSlotHeld;
        EnemyHealth.DamageApplied += OnEnemyDamageApplied;
        EnemyShieldSystem.ShieldDamageApplied += OnEnemyDamageApplied;
        if (controller.Health != null) controller.Health.OnDamageApplied += OnPlayerDamageApplied;
        weaponData = CreateRuntimeData((PlayerWeaponDataSO)data);
        maxSkillGauge = weaponData.maxSkillGauge;
        weaponSwapCoolTime = weaponData.swapCoolTime;

        normalWeaponBuffer.SetBufferTime(data.inputBufferTime);
        storedSkillBuffer.SetBufferTime(data.inputBufferTime);
        secondaryWeaponBuffer.SetBufferTime(data.inputBufferTime);

        controller.OnNormalWeapon += RegisterNormalAttack;
        controller.OnSecondaryWeapon += RegisterSecondaryAttack;
        controller.OnSwapWeaponSet += SwapWeaponSet;

        // 다음 단계에서 PlayerController에 추가할 이벤트
        controller.OnGearPressed += HandleGearPressed;
        controller.OnGearReleased += HandleGearReleased;
        // 쓰러지면 기본 공격 자리 바꾸기(머신건 장착)도 끝난다. 패배 뒤 Lobby로 돌아갈 때 장착 표시만 회수되고 장착이 남지 않게 한다.
        if (controller.Health != null) controller.Health.OnPlayerDeath += HandlePlayerDeath;

        if (weaponData.gearVisual != null)
        {
            gearVisual = Instantiate(weaponData.gearVisual, transform);
            gearVisual.Initialize(weaponData, gearUI, feedbackPlayer);
        }

        if (weaponData.weaponSwapVisual != null)
        {
            weaponSwapVisual = Instantiate(weaponData.weaponSwapVisual, transform);
            weaponSwapVisual.Initialize(controller.model.transform.Find("Circle"));
        }

        if (weaponData.skillBatteryVisual != null)
        {
            skillBattery = Instantiate(weaponData.skillBatteryVisual, transform);
            skillBattery.Initialize(controller);
        }

        // HUD의 기어 슬롯·눈금은 머리 위 GearUI의 단계 색을 그대로 쓴다.
        if (gearUI != null)
            UIManager.Instance.SetGearStageColors(gearUI.GetStageColor(1), gearUI.GetStageColor(2), gearUI.GetStageColor(3));

        if (weaponData.skillMemoryVisual != null)
        {
            var memoryVisual = Instantiate(weaponData.skillMemoryVisual, transform);
            memoryVisual.Initialize(this, weaponData);
        }
        starAim = gameObject.AddComponent<PlayerStarShotAim>();
        starAim.Initialize(controller, this);
        gameObject.AddComponent<PlayerWeaponTraitVisual>().Bind(controller, this, starAim);
        hammerArc = FindArc("HammerArc");
        swordArc = FindArc("SwordArc");
        swordArcOff = FindArc("SwordArcOff");
        var attackRig = controller.model.transform.Find("WeaponModel/MeleeWeapon");
        hammerAttackRig = attackRig != null ? attackRig.gameObject : null;
        var attackBlade = attackRig != null ? attackRig.Find("Hammer") : null;
        hammerAttackBlade = attackBlade != null ? attackBlade.GetComponent<SpriteRenderer>() : null;
        // 시작 능력 제한(2026-11-20 훈련 개편): 해금하지 않은 무장·Gear 기술·보조 무기는 실행용 사본에서 비운다(ArmamentUnlocks).
        CaptureDefaultSecondaries();
        LoadEquippedArmaments();
        ResolveSecondaryWeapons();
        ArmamentUnlocks.Changed -= RefreshUnlocks;
        ArmamentUnlocks.Changed += RefreshUnlocks;
        ApplyWeaponSet(0, true);

        selectedWeapon = normalWeapon;
        skillGauge = 0;
        if (skillBattery != null) skillBattery.SetGauge(0f, maxSkillGauge, true);

        CancelGear();

        if (gearUI != null)
        {
            gearUI.SetGearUI(false, 0, 0f, false);
        }
    }

    public void ResetSkillGauge(bool preserveExecutingAttack = false)
    {
        if (isStoringSkill || (!preserveExecutingAttack && executionSetIndex >= 0)) Interrupt();
        EndPrimaryOverride();
        storedSkills.Clear();
        StoredSkillsChanged?.Invoke();
        skillGauge = 0;
        PublishSkillGauge(true);
    }

    // 게이지가 바뀌는 곳은 모두 여기로 모아 HUD와 몸 옆 배터리를 함께 갱신한다.
    // silent면 칸이 다 차도 'Charge!'와 소리를 내지 않는다(방 초기화 등).
    private void PublishSkillGauge(bool silent = false)
    {
        UIManager.Instance.UpdateSkillGauge(skillGauge, maxSkillGauge, weaponData.weaponSets[currentSetIndex]);
        if (skillBattery != null) skillBattery.SetGauge(skillGauge, maxSkillGauge, silent);
    }
    // 이동 입력에 아래가 들어 있는지(대각선 아래, 다른 방향을 함께 누른 것 포함)
    private bool IsDownHeld() => InputManager.Instance != null ? InputManager.Instance.MoveDownHeld : InputManager.IsDownward(controller.MoveInput);

    private bool UseStorageChord => InputManager.Instance == null || !InputManager.Instance.HasStoreSkillBinding;

    private void RegisterNormalAttack()
    {
        suppressHeldPrimaryUntilRelease = gearState != GearState.Inactive;
        if (isStoringSkill)
        {
            normalWeaponBuffer.Reset();
            RejectAttack(RejectionReason.Busy);
            return;
        }
        bool down = UseStorageChord && IsDownHeld();
        primaryPressPending = false;
        if (UseStorageChord && gearState == GearState.Inactive && storedSkills.Count > 0)
        {
            if (down)
            {
                // 아래 + 공격: 저장 기술. 기본 공격 버퍼에는 넣지 않아 콤보 이어 치기가 이 입력을 가져가지 않는다.
                suppressHeldPrimaryUntilRelease = true;
                storedSkillBuffer.RegisterInput();
                return;
            }
            primaryPressTime = Time.time;
            primaryPressPending = true;
        }
        primaryPressDown = down;
        if (gearState == GearState.Waiting && !down)
        {
            var target = weaponData.weaponSets[currentSetIndex].GetGearWeapon(gearStage);
            if (RejectAttack(GetWeaponReadinessBlock(target))) { normalWeaponBuffer.Reset(); return; }
        }
        // Gear+공격 동시 입력은 모든 입력 이벤트가 끝난 Tick에서 해석한다.
        normalWeaponBuffer.RegisterInput();
    }
    private void RegisterSecondaryAttack()
    {
        var restriction = isStoringSkill ? RejectionReason.Busy : GetWeaponReadinessBlock(secondaryWeapon);
        if (RejectAttack(restriction)) { secondaryWeaponBuffer.Reset(); return; }
        secondaryWeaponBuffer.RegisterInput();
    }
    // 작열탄도 누르고 있으면 반복한다(2026-11-21 사용자 지시: 레일건과 같은 조작, 매번 준비 단계부터).
    private bool CanRepeatPrimary => !suppressHeldPrimaryUntilRelease && gearState == GearState.Inactive && controller.CanReceiveInput
        && InputManager.Held(Button.Attack) && (!IsActive || selectedWeapon == normalWeapon);
    private void RepeatPrimaryInput()
    {
        // 유지 입력은 실제 선입력 버퍼에 남기지 않아 키를 놓으면 다음 자동 타격이 취소된다.
        if (!InputManager.Held(Button.Attack)) suppressHeldPrimaryUntilRelease = false;
        if (comboQueuedByHold && !InputManager.Held(Button.Attack))
        { comboQueuedThisStep = comboQueuedByHold = false; }
    }
    private void TryAttack(bool storePreparedGear)
    {
        if (!CanDo()) { HoldAttackThroughHit(); return; }
        restartAfterHit = false;
        // 기술 저장과 사용은 모두 '아래 + 공격'이다(아래를 누른 채 공격을 눌러도 되고, 대각선 아래처럼 다른 방향이 함께 눌려도 된다).
        // Gear 대기 중이면 그 단계의 기술을 저장하고, Gear를 쓰지 않을 때는 저장해 둔 기술을 꺼내 쓴다(누른 순간 아래가 들어 있었으면 storedSkillBuffer).
        if (storePreparedGear)
        {
            normalWeaponBuffer.Reset();
            if (gearState == GearState.Waiting) TryStoreGearSkill();
            else RejectAttack(gearState == GearState.Charging ? RejectionReason.GearCharging : RejectionReason.Unavailable);
            return;
        }
        bool down = UseStorageChord && (primaryPressDown || IsDownHeld());
        if (gearState != GearState.Inactive)
        {
            if (normalWeaponBuffer.ConsumeIfValid())
            {
                suppressHeldPrimaryUntilRelease = true;
                // Gear를 충전하는 동안에는 아무 일도 하지 않는다(TryStartGearWeapon은 대기 중에만 쏜다).
                // 저장하려다 조금 일찍 눌러, 저장해 둔 기술이 대신 나가 버리지 않게 한다.
                if (down && gearState == GearState.Waiting)
                    TryStoreGearSkill();
                else if (gearState == GearState.Charging)
                    RejectAttack(RejectionReason.GearCharging);
                else if (!down) TryStartGearWeapon();
                if (IsActive) { secondaryWeaponBuffer.Reset(); return; }
            }
            if (secondaryWeaponBuffer.ConsumeIfValid()) StartSecondaryWeapon();
            return;
        }
        // 보조무기는 기본 공격 키 유지 중에도 새 입력으로 우선 사용할 수 있다.
        if (secondaryWeaponBuffer.ConsumeIfValid()) { StartSecondaryWeapon(); return; }
        // 입력이 막힌 연출(해금 연출 등) 중에는 회수 때 붙잡아 둔 아래 + 공격도 버린다.
        if (!controller.CanReceiveInput) storedSkillBuffer.Reset();
        if (storedSkillBuffer.ConsumeIfValid())
        {
            TryUseStoredSkill();
            if (IsActive) return;
            if (storedSkills.Count > 0 || !UseStorageChord) return;
            StartNormalWeapon();        // 그사이 저장 목록이 비었으면 평소 공격
            return;
        }
        if (normalWeaponBuffer.ConsumeIfValid())
        {
            // 새로 누른 공격만 저장 기술을 꺼낸다. 공격 키를 누르고 있어 이어지는 반복 공격으로는 꺼내지 않는다.
            if (down && storedSkills.Count > 0)
            {
                suppressHeldPrimaryUntilRelease = true;
                TryUseStoredSkill();
                return;
            }
            StartNormalWeapon();
            return;
        }
        // 누른 채 반복되는 기본 공격은 실패 안내를 다시 재생하지 않는다.
        if (CanRepeatPrimary && !WaitsForJumpRise && GetAttackBlock(normalWeapon, true) == RejectionReason.None)
            StartNormalWeapon();
    }
    // 누르고 있어 이어지는 공격이 막 뛴 점프를 그 자리에서 멈추지 않게 한다(2026-11-21 "공격 중 점프").
    // 시작할 때 세로 속도를 지우고 중력을 끄는 기본 무기(해머·칼·레일건·작열탄, StartWindup)는 점프 상승이 끝날 때(최고점)까지 반복을 미룬다.
    // 라이플·기관총은 세로 속도를 유지하므로 바로 잇는다. 새로 누른 공격은 예전처럼 바로 나간다(공중 공격).
    private bool WaitsForJumpRise => manager.JumpAbility != null && manager.JumpAbility.IsJumpRising
        && !(normalWeapon is MachineGunWeaponSO || IsRifle(normalWeapon));
    // 피격 경직(0.3초) 동안은 행동할 수 없는데 입력 버퍼(0.15초)가 그보다 먼저 만료되어, 피격 직후에 누른 공격은 버려지고
    // 경직 끝 무렵에 누른 공격만 나갔다. 같은 조작에도 1타가 나가거나 안 나가는 원인이었다. 경직 동안 입력을 붙잡아 두고,
    // 판정 전에 피격으로 끊긴 첫 타격도 다시 맡겨, 경직이 끝난 첫 행동 가능 프레임에 1타부터 시작한다(피격이 공격을 끊는 규칙은 그대로).
    private void HoldAttackThroughHit()
    {
        if (manager.StateMachine.CurrentState != PlayerState.Hit)
        {
            restartAfterHit = false;
            return;
        }

        if (restartAfterHit)
        {
            restartAfterHit = false;
            primaryPressDown = false;    // 다시 맡긴 첫 타격은 아래 없이 누른 공격이다
            normalWeaponBuffer.RegisterInput();
        }
        normalWeaponBuffer.Hold();
        storedSkillBuffer.Hold();
    }

    // 끊긴 공격이 아직 아무 효과도 내지 않은 기본 무기의 첫 타격인지. 게이지를 쓰는 무기는 두 번 차감되지 않게 제외한다.
    private bool IsUnresolvedFirstStrike()
    {
        if (normalWeapon == null || selectedWeapon != normalWeapon || normalWeapon.skillGauge > 0f)
            return false;
        if (selectedWeapon is MeleeWeaponSO && meleeComboIndex != 0)
            return false;
        if (abilityState == AbilityState.Windup)
            return true;
        // 근접 무기는 실행 Clip이 판정을 켜므로, 실행 단계여도 판정이 켜지기 전이면 효과가 없었다.
        return abilityState == AbilityState.Execute && selectedWeapon is MeleeWeaponSO && !currentHitboxOpened && !IsCurrentHitboxOpen();
    }

    // 지금 타격의 판정 Collider가 켜져 있는지(실행 Clip의 m_IsActive 곡선이 켠다). StartExecute와 같은 판정 묶음을 본다.
    private bool IsCurrentHitboxOpen()
    {
        if (selectedWeapon is not MeleeWeaponSO melee)
            return false;
        var boxes = GetHitBoxes(melee);
        if (boxes == null || meleeComboIndex < 0)
            return false;
        int hits = Mathf.Max(1, melee.hitsPerStep);
        for (int h = 0; h < hits; h++)
        {
            int index = meleeComboIndex * hits + h;
            if (index < boxes.Length && boxes[index] != null && boxes[index].gameObject.activeInHierarchy)
                return true;
        }
        return false;
    }

    // 근접 무기가 쓰는 판정 묶음. 기본 공격 자리의 해머는 콤보 단계마다 하나씩, 칼은 단계마다 hitsPerStep개씩, 그 밖의 근접 무기(HammerSkill)는 전용 판정을 쓴다.
    private MeleeHitbox[] GetHitBoxes(MeleeWeaponSO melee)
    {
        if (melee.weaponName == swordWeaponName) return swordHitBoxes;
        return melee.weaponName == hammerWeaponName ? hammerHitBoxes : hammerSkillBoxes;
    }

    private Renderer FindArc(string arcName)
    {
        var arc = controller.model.transform.Find(arcName);
        return arc != null ? arc.GetComponent<Renderer>() : null;
    }

    // 세트의 대기 모형. 그 세트의 기본 공격 자리에 든 무기의 모형을 쓴다(해머·칼·별 총을 어느 세트에 넣어도 된다).
    // 표에 없는 무기는 그 세트의 기술용 모형을 쓴다.
    private GameObject GetSetModel(int setIndex)
    {
        var normal = weaponData.weaponSets[setIndex].normal;
        if (normal != null && idleModels != null)
        {
            foreach (var entry in idleModels)
            {
                if (entry != null && entry.model != null && entry.weaponName == normal.weaponName)
                    return entry.model;
            }
        }
        return weaponModels[setIndex];
    }

    // 기술(Gear·보조 무기·저장해 둔 기술)을 연출하는 모형. 무기가 정한 모형이 표에 있으면 그것을(칼로 연출하는 기술은 해머 세트에서도 칼),
    // 없으면 그 세트의 기술용 모형을 쓴다.
    private GameObject GetSkillModel(WeaponBaseSO weapon, int setIndex)
    {
        string key = weapon != null ? weapon.PresentationWeaponName : null;
        if (!string.IsNullOrEmpty(key) && idleModels != null)
        {
            foreach (var entry in idleModels)
            {
                if (entry != null && entry.model != null && entry.weaponName == key)
                    return entry.model;
            }
        }
        return weaponModels[setIndex];
    }

    private void ShowModel(GameObject target)
    {
        foreach (GameObject weaponModel in weaponModels)
        {
            if (weaponModel != null) weaponModel.SetActive(weaponModel == target);
        }
        if (idleModels != null)
        {
            foreach (var entry in idleModels)
            {
                if (entry != null && entry.model != null) entry.model.SetActive(entry.model == target);
            }
        }
        modelReturnTime = -1f;
        if (bodyCore != null) bodyCore.SetActive(!HidesCore(target));
    }

    // 그 모형이 코어 자리를 차지하는지(별 총은 소켓에 자기 별 코어를 품고 있다). 아무 모형도 켜지 않은 동안에는 코어가 보인다.
    private bool HidesCore(GameObject target)
    {
        if (target == null || coreHiddenBy == null) return false;
        foreach (GameObject model in coreHiddenBy)
        {
            if (model == target) return true;
        }
        return false;
    }

    // 공격용 해머가 지금 화면에 보이는지(리그가 켜져 있고 해머 그림이 크기를 갖고 그려진다).
    // 해머를 던져 손에 없는 채 끝나는 기술은 리그가 켜져 있어도 보이는 해머가 없다.
    private bool IsAttackHammerShown()
    {
        if (hammerAttackRig == null || !hammerAttackRig.activeInHierarchy) return false;
        if (hammerAttackBlade == null) return true;
        return hammerAttackBlade.enabled && hammerAttackBlade.gameObject.activeInHierarchy
            && hammerAttackBlade.transform.lossyScale.sqrMagnitude > 0.0025f;
    }

    // 미뤄 둔 대기 모형이 있으면 지금 켠다(새 동작을 시작하거나 끊길 때는 기다리지 않는다).
    private void FinishModelReturn()
    {
        if (modelReturnTime >= 0f) ShowModel(GetSetModel(currentSetIndex));
    }

    private void TryQueueComboInput()
    {
        if (comboQueuedThisStep) // 이미 예약됐으면 금지
            return;

        if (selectedWeapon is not MeleeWeaponSO melee)
            return;

        // 허용 구간 전의 짧은 선입력은 버퍼 수명 동안 유지한다.
        if (!IsInComboQueueWindow(melee)) return;
        bool freshInput = normalWeaponBuffer.ConsumeIfValid();
        if (!freshInput && !(IsNormalMelee && CanRepeatPrimary))
            return;

        comboQueuedThisStep = true;
        comboQueuedByHold = !freshInput;
    }
    private bool IsInComboQueueWindow(MeleeWeaponSO melee)
    {
        // Recover에서는 항상 예약 허용
        if (abilityState == AbilityState.Recover)
            return true;

        // Execute에서는 끝나기 직전 comboQueueWindow에만 예약 허용
        if (abilityState == AbilityState.Execute)
            return timer <= comboQueueWindow;

        return false;
    }

    private bool CancelAttack()
    {
        if (comboQueuedThisStep && selectedWeapon is MeleeWeaponSO melee)
        {
            comboQueuedThisStep = false;

            // 다음 콤보로 인덱스 증가
            meleeComboIndex++;

            //combo 종료
            if (meleeComboIndex >= melee.comboSteps.Length)
            {
                meleeComboIndex--;
                return false;
            }

            // 바로 다음 공격으로 이어가기
            normalWeaponBuffer.Reset();
            StartWindup();
            return true;
        }

        return false;
    }
    private void StartNormalWeapon()
    {
        normalWeaponBuffer.Reset();
        if (normalWeapon == null) return;   // 기본 무기가 없는 자리(데이터 누락)는 공격하지 않는다

        if (RejectAttack(GetAttackBlock(normalWeapon, true)))
            return;

        selectedWeapon = normalWeapon;

        if (selectedWeapon is MeleeWeaponSO melee)
        {
            float now = Time.time;

            if (now > lastMeleeAttackTime + melee.comboResetTime)
                meleeComboIndex = 0;

            meleeComboIndex = Mathf.Clamp(
                meleeComboIndex,
                0,
                melee.comboSteps.Length - 1
            );

            lastMeleeAttackTime = now;
        }

        skillGauge -= normalWeapon.skillGauge;
        MarkWeaponUsed(normalWeapon);

        PublishSkillGauge();

        CancelGear();
        StartWindup();
    }
    private void TryStartGearWeapon()
    {
        // Gear 대기 상태가 아니면 아무 일도 하지 않는다.
        // 예: Charging 중 Normal 입력
        if (gearState != GearState.Waiting || gearStage <= 0)
            return;

        WeaponBaseSO gearWeapon = weaponData
            .weaponSets[currentSetIndex]
            .GetGearWeapon(gearStage);

        // 해당 단계 무기가 없으면 아무 일도 하지 않는다.
        if (RejectAttack(GetAttackBlock(gearWeapon)))
            return;

        selectedWeapon = gearWeapon;

        // 1차 구현에서는 Gear Weapon 콤보를 쓰지 않음
        meleeComboIndex = 0;
        lastMeleeAttackTime = Time.time;

        skillGauge -= selectedWeapon.skillGauge;
        MarkWeaponUsed(gearWeapon);

        PublishSkillGauge();

        CancelGear(selectedWeapon is HandCannonWeaponSO
            ? PlayerGearVisual.EndReason.Silent : PlayerGearVisual.EndReason.Fired);
        StartWindup();
    }

    private void StartSecondaryWeapon()
    {
        if (RejectAttack(GetAttackBlock(secondaryWeapon)))
            return;

        selectedWeapon = secondaryWeapon;
        skillGauge -= secondaryWeapon.skillGauge;
        MarkWeaponUsed(secondaryWeapon);

        PublishSkillGauge();

        CancelGear();
        StartWindup();
    }
    public override void Tick()
    {
        if (modelReturnTime >= 0f && Time.time >= modelReturnTime - 0.0001f) FinishModelReturn();
        Traits.Tick();
        TickPrimaryOverride();
        bool storageRequested = controller.CanReceiveInput && InputManager.StoreSkillPressed();
        if (isStoringSkill)
        {
            if (storageRequested) RejectAttack(RejectionReason.Busy);
            TickSkillStorage(); UpdateWeaponCooldownUI(); UpdateGearStageCooldownUI(); return;
        }
        bool storePreparedGear = storageRequested && gearState != GearState.Inactive;
        if (storageRequested && !storePreparedGear)
        {
            if (storedSkills.Count > 0) storedSkillBuffer.RegisterInput();
            else RejectAttack(RejectionReason.Unavailable);
        }
        RepeatPrimaryInput();
        if (abilityState != AbilityState.None)
        {
            // 아래 + 공격: 기본 공격 중이면 곧바로 끊고 저장 기술, 다른 기술의 회수 중이면 끝날 때까지 남겨 둔다
            if (!manager.DashAbility.IsActive && TryCancelIntoStoredSkill())
            {
                UpdateWeaponCooldownUI();
                UpdateGearStageCooldownUI();
                return;
            }
            bool mountEnded = IsNormalAttackRunning
                && (selectedWeapon is IncendiaryRifleWeaponSO || selectedWeapon is MachineGunWeaponSO)
                && ResolveNormalWeapon(currentSetIndex) != selectedWeapon;
            if (mountEnded || (IsMachineGunAttacking && InputManager.Released(Button.Attack)))
            {
                StopAllCoroutines();
                starAim?.EndBurst();
                EndAttack();
                UpdateWeaponCooldownUI();
                UpdateGearStageCooldownUI();
                return;
            }
            // 기관총 연사 중에는 몸이 이동·대시 방향으로 돌아선다(2026-11-21 방향 전환 제한 해제). 좌우는 지금 보는 방향, 위 입력은 매 프레임 반영한다.
            // 대시가 방향만 바꾼 프레임에도 총구가 반대쪽에 남지 않게 모형 좌우를 함께 맞춘다.
            if (IsMachineGunAttacking)
            {
                SnapFacingModel();
                PrimaryFireDirection = ReadPrimaryAim(controller.Facing);
            }

            if (abilityState == AbilityState.Recover && !IsNormalAttackRunning && controller.CanReceiveInput) storedSkillBuffer.Hold();
            if (abilityState == AbilityState.Execute && !currentHitboxOpened && IsCurrentHitboxOpen())
                currentHitboxOpened = true;
            TryQueueComboInput();
            TryQueueRifleShot();
            timer -= SwordFinisherCinematic.DeltaFor(controller);

            // 빠르게 잇는 무기(칼)는 후속 입력이 회수 중 들어와도 남은 회수 시간을 기다리지 않는다.
            if (ChainsWithoutRecover && abilityState == AbilityState.Recover && CancelAttack())
                return;

            bool rifleCadence = IsNormalAttackRunning && IsRifle(selectedWeapon);
            if (timer > (rifleCadence ? 0.00001f : 0f))
            {
                UpdateWeaponCooldownUI();
                UpdateGearStageCooldownUI();
                return;
            }

            switch (abilityState)
            {
                case AbilityState.Windup:
                    StartExecute();
                    break;

                case AbilityState.Execute:
                    if (ChainsWithoutRecover && CancelAttack())
                        break;
                    if (selectedWeapon.ShouldEnterRecover())
                    {
                        float carry = rifleCadence ? timer : 0f;
                        StartRecover();
                        // 라이플 연사 주기에 단계 전환의 프레임 오차가 쌓이지 않게 한다.
                        if (rifleCadence) timer = Mathf.Max(0f, timer + carry);
                    }
                    else if (!TryContinueMachineGunBurst())
                    {
                        EndAttack();
                        TryChainStoredSkill();
                    }
                    break;

                case AbilityState.Recover:
                    if (TryContinueMachineGunBurst() || TryContinueRifleFire() || CancelAttack())
                        break;

                    EndAttack();
                    TryChainStoredSkill();
                    break;
            }

            UpdateWeaponCooldownUI();
            UpdateGearStageCooldownUI();
            return;
        }

        StartQueuedGearCharge();
        UpdateGear();
        TryAttack(storePreparedGear);

        UpdateWeaponCooldownUI();
        UpdateGearStageCooldownUI();
    }

    private bool TryContinueMachineGunBurst()
    {
        if (!IsMachineGunAttacking || !CanRepeatPrimary || skillGauge < selectedWeapon.skillGauge
            || !IsWeaponCooldownReady(selectedWeapon))
            return false;

        // 지금 보는 방향(이동 방향을 따라 돈다)으로 계속 연사하며 위 입력은 발사마다 반영한다.
        skillGauge -= selectedWeapon.skillGauge;
        MarkWeaponUsed(selectedWeapon);
        PublishSkillGauge();
        StopAllCoroutines();
        StartExecute();
        if (!manager.DashAbility.IsActive) controller.animator.Play("WeaponExecute", 0, 0f);
        return true;
    }

    // 라이플 단발 연사(2026-11-21 사용자 지시 "3점사 → 지속 공격: 한 번 누르면 1발, 연타·누르고 있으면 계속").
    // 회수가 끝나는 순간 공격 키를 누르고 있거나(CanRepeatPrimary) 실행·회수 중에 다시 눌렀으면(rifleShotQueued)
    // 준비 단계 없이 다음 한 발의 실행으로 바로 잇는다(TryContinueMachineGunBurst와 같은 자리). 회수 단계를 거치므로 그동안 대시할 수 있다.
    // 탄이 없거나 장전 중이면 잇지 않고 끝낸다. 누르고 있으면 장전이 끝나는 대로 TryAttack이 다시 쏜다.
    private bool TryContinueRifleFire()
    {
        bool queued = rifleShotQueued;
        rifleShotQueued = false;
        if (!IsNormalAttackRunning || !IsRifle(selectedWeapon)) return false;
        // 공격 중에 누른 Gear 키는 공격이 끝나야 충전을 시작한다(StartQueuedGearCharge). 이어 쏘면 Gear를 쓸 수 없으므로 여기서 끊는다.
        if (gearPressQueued && gearKeyDown) return false;
        bool wantsShot = CanRepeatPrimary || (queued && gearState == GearState.Inactive && controller.CanReceiveInput);
        if (!wantsShot) return false;
        var restriction = GetAttackBlock(selectedWeapon, true);
        if (restriction != RejectionReason.None)
        {
            if (queued) RejectAttack(restriction);
            return false;
        }

        if (selectedWeapon.skillGauge > 0f)
        {
            skillGauge -= selectedWeapon.skillGauge;
            PublishSkillGauge();
        }
        MarkWeaponUsed(selectedWeapon);
        // 이어 쏠 때마다 좌우 입력이 있으면 그쪽으로 돌아서고(모형도 바로 맞춘다), 위 입력이면 위 30도로 쏜다.
        int inputDir = (int)controller.MoveInput.x;
        if (inputDir != 0 && inputDir != controller.Facing)
        {
            controller.FacingUpdate(inputDir);
            SnapFacingModel();
        }
        PrimaryFireDirection = ReadPrimaryAim(controller.Facing);
        float carry = timer;
        StopAllCoroutines();
        StartExecute();
        timer = Mathf.Max(0f, timer + carry);
        if (!manager.DashAbility.IsActive) controller.animator.Play("WeaponExecute", 0, 0f);
        return true;
    }

    // 라이플 기본 공격의 실행·회수 중에 새로 누른 공격을 다음 한 발로 남긴다.
    // 입력 버퍼(0.15초)가 회수 끝보다 먼저 지나도 잃지 않게 여기서 꺼내 둔다. 연타가 누르고 있기보다 빨라지지는 않는다(한 번에 하나만 남긴다).
    private void TryQueueRifleShot()
    {
        if (!IsNormalAttackRunning || !IsRifle(selectedWeapon)) return;
        if (normalWeaponBuffer.ConsumeIfValid()) rifleShotQueued = true;
    }

    // 지금 기본 공격(기본 공격 자리의 무기)을 쓰는 중인지. Gear·보조 무기·저장 기술은 아니다.
    private bool IsNormalAttackRunning => abilityState != AbilityState.None && !isStoringSkill && executionSetIndex < 0 && selectedWeapon == normalWeapon;

    // 아래 + 공격이 들어와 있고 기본 공격 중이면 그 공격을 곧바로 끊고 저장 기술을 시작한다(2026-11-17 사용자 지시 "공격 중에 아래 + 공격을 하면 곧바로").
    // 공격 키가 아래보다 조금 먼저 들어온 경우(SimultaneousInputWindow 안에 공격 키를 누른 채 아래가 들어옴)도 아래 + 공격으로 본다.
    private bool TryCancelIntoStoredSkill()
    {
        if (!controller.CanReceiveInput) { storedSkillBuffer.Reset(); primaryPressPending = false; return false; }
        if (primaryPressPending)
        {
            if (Time.time - primaryPressTime > SimultaneousInputWindow) primaryPressPending = false;
            else if (UseStorageChord && IsDownHeld() && InputManager.Held(Button.Attack))
            {
                primaryPressPending = false;
                suppressHeldPrimaryUntilRelease = true;
                storedSkillBuffer.RegisterInput();
            }
        }
        if (!IsNormalAttackRunning || !storedSkillBuffer.IsValid || storedSkills.Count == 0 || !controller.CanReceiveInput)
            return false;
        if (RejectAttack(GetWeaponReadinessBlock(storedSkills[0].Weapon))) { storedSkillBuffer.Reset(); return false; }
        storedSkillBuffer.Reset();
        // 끊는 기본 공격의 모습·판정·남은 연사를 정리한다(selectedWeapon을 바꾸기 전에 해야 기본 근접 공격으로 알아본다)
        FinishModelReturn();
        ClearNormalMeleePresentation();
        StopAllCoroutines();
        if (starAim != null) starAim.Restore();
        controller.PlayerCounterState.EndCounter();
        comboQueuedThisStep = comboQueuedByHold = false;
        restartAfterHit = false;
        normalWeaponBuffer.Reset();
        if (!StartStoredSkillNow())
        {
            EndAttack();
            return true;
        }
        return true;
    }

    // 다른 기술이 끝난 프레임: 그사이 들어와 남아 있는 아래 + 공격이 있으면 같은 프레임에 저장 기술을 이어 쓴다(스킬 공격 뒤 스킬 공격의 선입력).
    // 이 프레임의 상태 기계는 아직 공격(지난 프레임 값)이라 CanDo로 묻지 않는다. 공격을 이어 가는 것이고, 피격·잡기·대시는 공격을 먼저 끊으므로 여기에 오지 않는다.
    private void TryChainStoredSkill()
    {
        if (manager.DashAbility.IsActive) return;
        if (!controller.CanReceiveInput) { storedSkillBuffer.Reset(); return; }
        if (abilityState != AbilityState.None || storedSkills.Count == 0 || !storedSkillBuffer.IsValid)
            return;
        storedSkillBuffer.Reset();
        StartStoredSkillNow();
    }

    // 저장 목록 맨 앞의 기술을 지금 시작하고, Animator를 그 기술의 준비 Clip 첫 프레임부터 돌린다.
    // (Any State에서 WeaponWindup으로 가는 전이는 자기 자신으로는 가지 않으므로, 준비 중에 끊고 시작하면 Clip이 처음부터 다시 돌지 않는다)
    private bool StartStoredSkillNow()
    {
        TryUseStoredSkill();
        if (abilityState != AbilityState.Windup) return false;
        suppressHeldPrimaryUntilRelease = true;
        if (controller.animator.isActiveAndEnabled)
        {
            controller.animator.Play("WeaponWindup", 0, 0f);
            controller.animator.Update(0f);
        }
        return true;
    }

    // 공격·기술 중에 새로 누른 Gear 키를 끝난 뒤에 받는다(키를 아직 누르고 있을 때만 충전을 시작한다).
    private void StartQueuedGearCharge()
    {
        if (!gearPressQueued) return;
        // 공격이 끝난 첫 프레임은 상태 기계가 아직 공격(지난 프레임 값)이다. 그때는 남겨 두고 다음 프레임에 본다.
        if (manager.StateMachine.CurrentState == PlayerState.Attack) return;
        gearPressQueued = false;
        if (!gearKeyDown || gearState != GearState.Inactive || !CanDo()) return;
        isGearHeld = true;
        StartChargingGear(1);
    }

    #region SwapWeapon
    private void SwapWeaponSet()
    {
        if (!CanDo())
            return;

        float now = Time.time;

        if (now <= lastWeaponSwapTime + weaponSwapCoolTime)
            return;

        // 공격 중 스왑 금지
        if (abilityState != AbilityState.None)
            return;

        int next = currentSetIndex == 0 ? 1 : 0;
        ApplyWeaponSet(next);
    }
    private void ApplyWeaponSet(int setIndex, bool firstInit = false)
    {
        // 바꾸기 전 세트의 기본 무기(교체 아이콘에 나가는 쪽으로 표시)
        WeaponBaseSO previousWeapon = firstInit ? null : normalWeapon;

        // 2026-11-21 사용자 지시 3차: 무기 교체는 장전 조건이 아니다(탄창 소진 또는 3.5초 무발사: PlayerWeaponTraits.Tick).
        currentSetIndex = setIndex;

        var set = weaponData.weaponSets[currentSetIndex];
        normalWeapon = ResolveNormalWeapon(currentSetIndex);
        secondaryWeapon = set.secondary;

        BindWeaponEvents();

        selectedWeapon = normalWeapon;

        meleeComboIndex = 0;
        lastMeleeAttackTime = -999f;
        lastWeaponSwapTime = Time.time;
        comboQueuedThisStep = false;

        normalWeaponBuffer.Reset();
        secondaryWeaponBuffer.Reset();

        CancelGear();

        skillModelShown = false;
        ShowModel(GetSetModel(currentSetIndex));

        UIManager.Instance.StartWeaponSwap(set, skillGauge, !firstInit);

        if (!firstInit)
        {
            FeedbackPlayer.Instance.Play(weaponData.swapFeedback, transform);

            if (weaponSwapVisual != null)
            {
                weaponSwapVisual.Play(
                    previousWeapon != null ? previousWeapon.weaponSprite : null,
                    normalWeapon != null ? normalWeapon.weaponSprite : null,
                    set.swapColor,
                    GetSetModel(currentSetIndex));
            }
        }
    }
    private void BindWeaponEvents()
    {
        var set = weaponData.weaponSets[currentSetIndex];

        BindWeaponEvent(set.normal);
        BindWeaponEvent(normalWeapon);
        BindWeaponEvent(set.gearStage1);
        BindWeaponEvent(set.gearStage2);
        BindWeaponEvent(set.gearStage3);
        BindWeaponEvent(set.secondary);
    }

    private void BindWeaponEvent(WeaponBaseSO weapon)
    {
        if (weapon == null || hitHandlers.ContainsKey(weapon)) return;
        // 회수 뒤에도 살아 있는 저장 투사체의 보상은 발사한 원본 무기를 기준으로 한다.
        System.Action<CharacterController2D, Vector2> handler = (owner, point) => OnWeaponHitEnemy(weapon, owner, point);
        hitHandlers.Add(weapon, handler);
        weapon.OnHitEnemy += handler;
    }
    private void OnDisable()
    {
        if (controller == null || controller.animator == null || selectedWeapon == null) return;
        Interrupt();
    }
    private void OnDestroy()
    {
        foreach (var pair in hitHandlers) if (pair.Key != null) pair.Key.OnHitEnemy -= pair.Value;
        hitHandlers.Clear();
        if (runtimeWeaponData != null) Destroy(runtimeWeaponData);
        runtimeWeaponData = null;
        ArmamentUnlocks.Changed -= RefreshUnlocks;
        EnemyHealth.DamageApplied -= OnEnemyDamageApplied;
        EnemyShieldSystem.ShieldDamageApplied -= OnEnemyDamageApplied;
        if (controller != null && controller.Health != null)
        {
            controller.Health.OnPlayerDeath -= HandlePlayerDeath;
            controller.Health.OnDamageApplied -= OnPlayerDamageApplied;
        }
    }
    private void HandlePlayerDeath()
    {
        EndPrimaryOverride();
        Traits.Reset();
    }

    private void OnPlayerDamageApplied(WeaponBaseSO.DamageInfo info, float amount)
    {
        if (amount > 0f && Traits.Resolve > 0) Traits.ChangeResolve(-1);
    }

    private void OnEnemyDamageApplied(EnemyHealth enemy, WeaponBaseSO.DamageInfo info, float amount)
    {
        if (info.sourceOwner != controller || info.sourceWeapon == null || controller.Health == null || controller.Health.IsDead) return;
        foreach (var armament in Armaments)
        {
            if (armament == null || armament.armamentId != "Hammer") continue;
            var source = info.sourceWeapon;
            // 평타 환산(5차 지시): 평타 1타 = 1, 회전 망치 한 틱 = 1/3, 지면 평탄화 적중 = 5(한 번 쓸 때 한 번), 반격은 성공 때 FillBarrier로 즉시.
            if (source == armament.normal) Traits.AddBarrierHits(1f, source);
            else if (source == armament.gearStage2) Traits.AddBarrierHits(1f / 3f, source);
            else if (source == armament.gearStage3) Traits.AddBarrierHits(5f, source, 1.5f);
            break;
        }
    }

    private static bool IsRifle(WeaponBaseSO weapon) =>
        weapon is RangedWeaponSO && weapon.weaponName == "StarShot";

    // 지금 든 자리가 라이플 무장인지(그 자리에 기관총을 장착한 동안은 아니다). PlayerWeaponTraits.IsRifleHeld가 쓴다.
    private bool IsRifleSlotHeld
    {
        get
        {
            if (weaponData == null || weaponData.weaponSets == null) return true;
            bool rifle = ActiveArmamentId == "Rifle" || IsRifle(GetWeaponSet(currentSetIndex)?.normal);
            bool machineGunMounted = primaryOverride is MachineGunWeaponSO && primaryOverrideSet == currentSetIndex;
            return rifle && !machineGunMounted;
        }
    }

    private RejectionReason GetAttackBlock(WeaponBaseSO weapon, bool isPrimary = false)
    {
        var restriction = GetWeaponReadinessBlock(weapon);
        if (restriction != RejectionReason.None) return restriction;
        if (skillGauge < weapon.skillGauge) return RejectionReason.Gauge;
        if (!isPrimary) return RejectionReason.None;
        if (IsRifle(weapon))
        {
            if (Traits.ReloadRemaining > 0f) return RejectionReason.Reloading;
            if (Traits.RifleAmmo <= 0) return RejectionReason.EmptyMagazine;
        }
        if (weapon is IncendiaryRifleWeaponSO && Traits.IncendiaryAmmo <= 0)
            return RejectionReason.EmptyMagazine;
        return RejectionReason.None;
    }

    private RejectionReason GetWeaponReadinessBlock(WeaponBaseSO weapon)
    {
        if (weapon == null) return RejectionReason.Unavailable;
        var restriction = GetTraitBlock(weapon);
        return restriction == RejectionReason.None && GetWeaponCooldownRemaining(weapon) > 0f
            ? RejectionReason.Cooldown : restriction;
    }

    private bool RejectAttack(RejectionReason reason)
    {
        if (reason == RejectionReason.None) return false;

        float now = Time.unscaledTime;
        if (reason != lastAttackRejection || now >= nextRepeatedRejectionTime)
        {
            lastAttackRejection = reason;
            nextRepeatedRejectionTime = now + 0.30f;
            SoundManager.Instance?.PlaySFX(weaponData.actionRejectedSound, transform.position);
            AttackRejected?.Invoke(reason);
        }
        return true;
    }

    public bool UsePrimaryRound(RangedWeaponSO weapon)
    {
        if (IsRifle(weapon)) return Traits.UseRifleRound();
        if (weapon is IncendiaryRifleWeaponSO)
            return primaryOverride == weapon && PrimaryOverrideRemaining > 0f && Traits.UseIncendiaryRound();
        return true;
    }

    public bool IsWeaponTraitReady(WeaponBaseSO weapon) => GetTraitBlock(weapon) == RejectionReason.None;

    private RejectionReason GetTraitBlock(WeaponBaseSO weapon)
    {
        if (GetFinisherRequirementStacks(weapon) >= 0) return RejectionReason.Resolve;
        if (Traits != null && Traits.OverheatRemaining > 0f
            && (weapon is RailgunWeaponSO || weapon is RailgunChargeWeaponSO
                || weapon is StarSpearWeaponSO || weapon is LaserWeaponSO))
            return RejectionReason.Overheated;
        return RejectionReason.None;
    }
    #endregion

    #region OnWeaponHit
    private void OnWeaponHitEnemy(WeaponBaseSO source, CharacterController2D owner, Vector2 hitPoint)
    {
        if (owner != controller)
            return;
        if (DestructibleProp.ConsumeRewardHit(source, owner, hitPoint)) return;   // 파괴형 던전 Prop 적중은 기술 게이지를 주지 않는다

        if (source.skillGainPerHit <= 0)
            return;

        // 예전에는 적중 지점에서 구슬이 날아와 도착할 때 게이지가 올랐다(투사체와 헷갈린다는 지적). 이제 바로 오른다.
        AddSkillGauge(source.skillGainPerHit);
    }
    private void AddSkillGauge(float amount)
    {
        skillGauge += amount;
        skillGauge = Mathf.Min(skillGauge, maxSkillGauge);

        PublishSkillGauge();
    }
    #endregion

    #region Gear
    private void HandleGearPressed()
    {
        gearKeyDown = true;
        // 공격·기술 중에 새로 누른 Gear 키는 남겨 두었다가 끝났을 때 키를 아직 누르고 있으면 충전을 시작한다(StartQueuedGearCharge)
        if (abilityState != AbilityState.None)
        {
            gearPressQueued = true;
            return;
        }

        if (!CanDo())
            return;

        if (gearState == GearState.Inactive)
        {
            isGearHeld = true;
            StartChargingGear(1);
            return;
        }

        if (gearState == GearState.Waiting)
        {
            // 3단계 대기 중에는 더 이상 충전할 단계가 없다.
            if (gearStage >= 3)
                return;

            isGearHeld = true;
            StartChargingGear(gearStage + 1);
        }
    }

    private void HandleGearReleased()
    {
        isGearHeld = false;
        gearKeyDown = false;
        gearPressQueued = false;

        // 확정 규칙:
        // 충전 중 키를 떼면 어떤 단계든 Gear 전체 취소.
        if (gearState == GearState.Charging)
        {
            CancelGear(PlayerGearVisual.EndReason.Disengaged);
        }

        // Waiting 중 Release는 허용.
        // 즉, 이미 도달한 단계의 대기 시간은 유지된다.
    }

    private void StartChargingGear(int targetStage)
    {
        chargingTargetStage = Mathf.Clamp(targetStage, 1, 3);
        gearTimer = 0f;
        gearState = GearState.Charging;

        UpdateGearUI();
    }

    private void EnterGearWaiting(int stage)
    {
        gearStage = Mathf.Clamp(stage, 1, 3);
        chargingTargetStage = 0;
        gearTimer = weaponData.gearWaitTime;
        gearState = GearState.Waiting;

        // 여기서 isGearHeld를 false로 만들지 않는다.
        // 키를 계속 누르고 있어도, 다음 단계로 넘어가려면
        // 대기 시간 안에 Gear 키를 다시 눌러야 하기 때문.
        // 즉, Press 이벤트 기반 리듬 입력을 유지한다.

        UpdateGearUI();
    }

    private void UpdateGear()
    {
        switch (gearState)
        {
            case GearState.Inactive:
                return;

            case GearState.Charging:
                UpdateGearCharging();
                break;

            case GearState.Waiting:
                UpdateGearWaiting();
                break;
        }
    }

    private void UpdateGearCharging()
    {
        if (!isGearHeld)
        {
            CancelGear(PlayerGearVisual.EndReason.Disengaged);
            return;
        }

        gearTimer += Time.deltaTime;

        float chargeTime = Mathf.Max(0.0001f, weaponData.gearStageChargeTime);

        if (gearTimer >= chargeTime)
        {
            EnterGearWaiting(chargingTargetStage);
            return;
        }

        UpdateGearUI();
    }

    private void UpdateGearWaiting()
    {
        gearTimer -= Time.deltaTime;

        if (gearTimer > 0f)
        {
            UpdateGearUI();
            return;
        }

        // 3단계는 최종 단계라서 더 이상 다음 단계로 가지 않는다.
        // 대기 시간이 끝나면 해제.
        if (gearStage >= 3)
        {
            CancelGear(PlayerGearVisual.EndReason.Disengaged);
            return;
        }

        // Waiting 시간이 끝났을 때 Gear 키를 계속 누르고 있다면,
        // 느리더라도 자동으로 다음 단계 충전을 시작한다.
        if (isGearHeld)
        {
            StartChargingGear(gearStage + 1);
            return;
        }

        // 키를 누르고 있지 않다면 기존처럼 Gear 해제.
        CancelGear(PlayerGearVisual.EndReason.Disengaged);
    }

    private void CancelGear(PlayerGearVisual.EndReason reason = PlayerGearVisual.EndReason.Silent)
    {
        gearState = GearState.Inactive;
        gearStage = 0;
        chargingTargetStage = 0;
        gearTimer = 0f;
        isGearHeld = false;

        // Gear 상태에서 눌렀던 Normal 입력이
        // Gear 해제 후 기본공격으로 발동되는 것을 방지
        normalWeaponBuffer.Reset();

        if (gearVisual != null)
            gearVisual.End(reason);

        UpdateGearUI();
    }

    private void UpdateGearUI()
    {
        UpdateGearVisual();

        if (gearUI == null)
            return;

        if (gearState == GearState.Inactive)
        {
            gearUI.SetGearUI(false, 0, 0f, false);
            gearUI.SetStageWeaponCooldown(null, 0f, 0f, false);
            return;
        }

        if (gearState == GearState.Charging)
        {
            float chargeTime = Mathf.Max(0.0001f, weaponData.gearStageChargeTime);
            float fill = Mathf.Clamp01(gearTimer / chargeTime);

            // 충전 중에는 아직 도달한 stage가 아니라 목표 stage를 보여주는 편이 확인하기 좋음.
            gearUI.SetGearUI(true, chargingTargetStage, fill, false);
            UpdateGearStageCooldownUI();
            return;
        }

        if (gearState == GearState.Waiting)
        {
            float waitTime = Mathf.Max(0.0001f, weaponData.gearWaitTime);
            float fill = Mathf.Clamp01(gearTimer / waitTime);

            gearUI.SetGearUI(true, gearStage, fill, true);
            UpdateGearStageCooldownUI();
        }
    }
    private void UpdateGearVisual()
    {
        if (gearVisual == null)
            return;

        if (gearState == GearState.Charging)
        {
            float chargeTime = Mathf.Max(0.0001f, weaponData.gearStageChargeTime);
            gearVisual.SetCharging(chargingTargetStage, Mathf.Clamp01(gearTimer / chargeTime));
        }
        else if (gearState == GearState.Waiting)
        {
            float waitTime = Mathf.Max(0.0001f, weaponData.gearWaitTime);
            gearVisual.SetWaiting(gearStage, Mathf.Clamp01(gearTimer / waitTime));
        }
    }

    #endregion

    #region CoolTime Helper
    private float GetWeaponCooldown(WeaponBaseSO weapon)
    {
        if (weapon == null)
            return 0f;

        // 실제 WeaponBase의 쿨타임 필드명이 다르면 여기만 바꾸면 됨.
        return Mathf.Max(0f, weapon.cooldown);
    }

    private float GetWeaponLastUseTime(WeaponBaseSO weapon)
    {
        if (weapon == null)
            return -999f;

        if (weaponLastUseTimes.TryGetValue(weapon, out float lastUseTime))
            return lastUseTime;

        return -999f;
    }

    private float GetWeaponCooldownRemaining(WeaponBaseSO weapon)
    {
        if (weapon == null)
            return 0f;

        float cooldown = GetWeaponCooldown(weapon);

        if (cooldown <= 0f)
            return 0f;

        float lastUseTime = GetWeaponLastUseTime(weapon);
        float remaining = lastUseTime + cooldown - Time.time;

        return Mathf.Max(0f, remaining);
    }

    private bool IsWeaponCooldownReady(WeaponBaseSO weapon)
    {
        return GetWeaponCooldownRemaining(weapon) <= 0f && IsWeaponTraitReady(weapon);
    }

    private void MarkWeaponUsed(WeaponBaseSO weapon)
    {
        if (weapon == null)
            return;

        weaponLastUseTimes[weapon] = Time.time;
    }
    private void UpdateWeaponCooldownUI()
    {
        if (weaponData == null || weaponData.weaponSets == null)
            return;

        WeaponSet set = weaponData.weaponSets[currentSetIndex];

        WeaponBaseSO normal = set.normal;
        WeaponBaseSO gear1 = set.gearStage1;
        WeaponBaseSO gear2 = set.gearStage2;
        WeaponBaseSO gear3 = set.gearStage3;
        WeaponBaseSO secondary = set.secondary;

        UIManager.Instance.UpdateWeaponCooldowns(
            GetWeaponCooldownRemaining(normal),
            GetWeaponCooldown(normal),

            GetWeaponCooldownRemaining(gear1),
            GetWeaponCooldown(gear1),

            GetWeaponCooldownRemaining(gear2),
            GetWeaponCooldown(gear2),

            GetWeaponCooldownRemaining(gear3),
            GetWeaponCooldown(gear3),

            GetWeaponCooldownRemaining(secondary),
            GetWeaponCooldown(secondary));
    }

    private void UpdateGearStageCooldownUI()
    {
        if (gearUI == null)
            return;

        if (gearState == GearState.Inactive || currentSetIndex < 0)
        {
            gearUI.SetStageWeaponCooldown(null, 0f, 0f, false);
            return;
        }

        int displayStage = gearState == GearState.Charging
            ? chargingTargetStage
            : gearStage;

        WeaponBaseSO gearWeapon = weaponData
            .weaponSets[currentSetIndex]
            .GetGearWeapon(displayStage);

        if (gearWeapon == null)
        {
            gearUI.SetStageWeaponCooldown(null, 0f, 0f, false);
            return;
        }


        bool gaugeReady = skillGauge >= gearWeapon.skillGauge;
        bool cooldownReady = IsWeaponCooldownReady(gearWeapon);
        bool ready = gaugeReady && cooldownReady;

        int stacks = GetResolveRequirementStacks(gearWeapon, out int required);
        gearUI.SetStageWeaponCooldown(
            gearWeapon.weaponSprite,
            GetWeaponCooldownRemaining(gearWeapon),
            GetWeaponCooldown(gearWeapon),
            ready,
            stacks, required);
    }
    #endregion
    private event System.Action grabCleanup;
    public void RegisterGrabCleanup(System.Action cleanup) => grabCleanup += cleanup;
    public void UnregisterGrabCleanup(System.Action cleanup) => grabCleanup -= cleanup;
    public void CancelForGrab()
    {
        Interrupt();
        controller.PlayerCounterState.EndCounter();
        controller.PlayerCounterState.EndInvulnerability();
        foreach (var box in hammerHitBoxes) if (box != null) box.DisableHitbox();
        foreach (var box in hammerSkillBoxes) if (box != null) box.DisableHitbox();
        if (swordHitBoxes != null) foreach (var box in swordHitBoxes) if (box != null) box.DisableHitbox();
        grabCleanup?.Invoke(); grabCleanup = null;
        StopAllCoroutines();
        normalWeaponBuffer.Reset(); secondaryWeaponBuffer.Reset();
        restartAfterHit = false;
    }
    // 점프로 공격을 끊는다(JumpAbility.TryJump, CanJumpCancel일 때). 대시처럼 공격을 정리한 뒤 Animator를 대기 자세로 바로 돌려 놓는다.
    // 회수 → 대기 전이(0.1초)가 섞이는 동안에는 점프 Trigger 전이를 보지 않아 점프 자세가 늦게 나오기 때문이다(대시는 Any State 전이라 필요 없다).
    public void CancelForJump()
    {
        Interrupt();
        if (!controller.animator.isActiveAndEnabled) return;
        controller.animator.Play("Idle", 0, 0f);
        controller.animator.Update(0f);
    }
    public override void Interrupt()
    {
        rifleShotQueued = false;
        SwordFinisherCinematic.End(controller);
        FinishModelReturn();
        storedSkillBuffer.Reset();
        primaryPressPending = false;
        gearPressQueued = false;
        if (abilityState == AbilityState.None)
        {
            CancelGear();
            return;
        }

        // 판정 전에 끊긴 첫 타격인지 먼저 기록한다. 피격으로 끊긴 경우에만 경직 뒤 다시 시작한다(HoldAttackThroughHit).
        bool wasStoring = isStoringSkill;
        restartAfterHit = !isStoringSkill && IsUnresolvedFirstStrike();
        // 아직 효과가 없던 첫 타격을 경직 뒤 다시 시작한다면, 시작할 때 남긴 쿨다운 기록도 지운다(쿨다운이 있는 기본 무기 - 레일건 - 가 다시 시작을 막지 않게).
        // 시작할 때 쿨다운을 통과했으므로 그 전 기록은 이미 지난 값이라 지워도 쿨다운 판정은 같다.
        if (restartAfterHit) weaponLastUseTimes.Remove(selectedWeapon);
        CancelSpecialAttack();
        ClearNormalMeleePresentation();

        controller.PlayerCounterState.EndCounter();

        comboQueuedThisStep = false;
        meleeComboIndex = 0; // 혹은 유지 정책

        normalWeaponBuffer.Reset();
        secondaryWeaponBuffer.Reset();

        CancelGear();

        StopAllCoroutines();
        controller.GravityOn = true;
        // 돌진하는 기술(SwordDashWeaponSO)은 나아가는 동안 외부 힘을 무시하고 속도를 준다. 그 코루틴이 위에서 멈췄으므로 여기서 되돌린다
        // (속도를 남겨 두면 끊긴 뒤에도 물리 틱 한 번만큼 더 나아간다. 대시도 끊길 때 속도를 지운다).
        // 대시·잡기가 켜는 값과는 겹치지 않는다. 둘 다 이 Interrupt가 끝난 뒤에 자기 값을 켜고, 대시 중에는 공격이 진행 중이 아니라 맨 위에서 반환한다.
        if (controller.IgnoreExternalForces)
        {
            controller.IgnoreExternalForces = false;
            controller.Velocity = Vector2.zero;
        }
        abilityState = AbilityState.None;

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 0);
        RestoreExecutionModel();
        if (wasStoring) RestoreStorageAnimation();
        RefreshNormalWeapon();
    }
    private void StartWindup()
    {
        if (selectedWeapon is SwordFinisherWeaponSO) Traits.ChangeResolve(-PlayerWeaponTraits.ResolveCapacity);
        FinishModelReturn();
        attackRun++;
        rifleShotQueued = false;
        bool machineGun = selectedWeapon == normalWeapon && selectedWeapon is MachineGunWeaponSO;
        bool rifle = selectedWeapon == normalWeapon && IsRifle(selectedWeapon);
        // 기관총은 사격을 시작할 때 대각선 입력의 좌우 방향까지 함께 잡는다.
        float horizontal = controller.MoveInput.x;
        int inputDir = machineGun && Mathf.Abs(horizontal) > .01f ? (horizontal > 0f ? 1 : -1) : (int)horizontal;
        if (inputDir != 0)
        {
            controller.FacingUpdate(inputDir);
        }

        if (selectedWeapon == normalWeapon)
        {
            // 위를 누르고 쏘면 위 30도. 아래 사격은 없다(아래 + 공격은 기술 저장·사용 입력이다).
            PrimaryFireDirection = ReadPrimaryAim(controller.Facing);
        }
        abilityState = AbilityState.Windup;
        comboQueuedThisStep = comboQueuedByHold = false;
        currentHitboxOpened = false;

        // Gear·보조 무기는 기술용 모형을 움직이는 Clip으로 연출한다. 대기 모형과 다르면(칼 세트의 해머 기술, 해머 세트의 칼 기술) 실행 동안 바꿔 든다.
        if (executionSetIndex < 0 && selectedWeapon != normalWeapon && selectedWeapon != secondaryWeapon)
        {
            GameObject skillModel = GetSkillModel(selectedWeapon, currentSetIndex);
            if (GetSetModel(currentSetIndex) != skillModel)
            {
                ShowModel(skillModel);
                skillModelShown = true;
            }
        }

        if (selectedWeapon is MeleeWeaponSO melee)
        {
            var step = melee.comboSteps[meleeComboIndex];

            controller.ApplyWeaponAnimation(step.animationSet, selectedWeapon.weaponAnim);

            timer = step.windupTime;
            comboQueueWindow = step.comboQueueWindow;
        }
        else
        {
            controller.ApplyWeaponAnimation(selectedWeapon.animationSet, selectedWeapon.weaponAnim);
            timer = selectedWeapon.windupTime; 
            selectedWeapon.OnWindupStarted(controller);
        }

        if (selectedWeapon is SwordFinisherWeaponSO) StartCoroutine(PlayFinisherPreparation(attackRun, selectedWeapon.windupFeedback));
        else feedbackPlayer.Play(selectedWeapon.windupFeedback, transform);
        if (machineGun)
        {
            // 기관총 장착은 이동 속도 감소·방향 고정이 없다(2026-11-21 사용자 지시 "제한 완전 해제"). 몸은 모터가 이동 방향으로 돌린다.
            // 다음 물리 틱까지 기다리면 쏘기 시작한 순간 총이 1~2프레임 뒤집혀 보이므로 모형 좌우만 지금 맞춘다. 쏘면서 떨어지고 뛸 수 있게 중력은 켠다.
            SnapFacingModel();
            controller.GravityOn = true;
        }
        else if (rifle)
        {
            // 라이플 단발 연사(2026-11-21 사용자 결정): 좌우 이동은 막고(공격 상태) 중력은 그대로 받아 공중에서는 떨어지며 쏜다(세로 속도 유지).
            SnapFacingModel();
            controller.GravityOn = true;
            controller.Velocity.x = 0f;
        }
        else
        {
            controller.GravityOn = false;
            controller.Velocity = Vector2.zero;
        }

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 1);
        if (IsNormalMelee)
        {
            // Execute·Recover에서 다음 Windup으로 바로 이어도 새 Clip의 첫 프레임부터 평가한다.
            controller.animator.Play("WeaponWindup", 0, 0f);
            controller.animator.Update(0f);
        }
        // 준비 시간이 0인 기관총(2026-11-21 사용자 지시 "기관총 공격 시작 선딜레이 삭제")은 다음 프레임을 기다리지 않고 누른 프레임에 첫 발을 쏜다.
        // 준비 Clip(Player_MachineGun_Windup)은 실행 Clip 첫 자세와 같은 정지 자세라 건너뛰어도 모습이 같다.
        // 준비 값(1)을 Animator가 보지 못하므로 Any State 전이 대신 실행 상태를 바로 재생한다(TryContinueMachineGunBurst와 같다).
        if (machineGun && timer <= 0f)
        {
            StartExecute();
            if (!manager.DashAbility.IsActive) controller.animator.Play("WeaponExecute", 0, 0f);
        }
    }
    private Vector2 ReadPrimaryAim(int horizontalFacing)
        => controller.MoveInput.y > .5f ? new Vector2(horizontalFacing * .8660254f, .5f) : new Vector2(horizontalFacing, 0f);

    // 방금 바꾼 보는 방향에 모형 좌우를 바로 맞춘다. 모터는 다음 FixedUpdate에서야 맞추므로(CharacterController2D.HandleCollisionResult)
    // 그사이 총구·탄이 반대쪽에서 나가거나 총이 뒤집혀 보이지 않게 한다. 다른 곳이 방향을 잠근 동안에는 건드리지 않는다.
    private void SnapFacingModel()
    {
        if (controller.LockFacing || controller.facingModel == null) return;
        var facing = controller.facingModel.transform;
        Vector3 scale = facing.localScale;
        scale.x = Mathf.Abs(scale.x) * controller.Facing;
        facing.localScale = scale;
    }

    private System.Collections.IEnumerator PlayFinisherPreparation(int run, FeedbackDataSO feedback)
    {
        float elapsed = 0f;
        while (elapsed < .1f)
        {
            yield return null;
            if (run != attackRun || !IsActive) yield break;
            elapsed += SwordFinisherCinematic.DeltaFor(controller);
        }
        feedbackPlayer.Play(feedback, transform, controller);
    }
    private void StartExecute()
    {
        abilityState = AbilityState.Execute;

        if (selectedWeapon is MeleeWeaponSO melee)
        {
            var step = melee.comboSteps[meleeComboIndex];
            timer = step.executeTime;
            // 한 단계에서 판정이 여러 번 열리는 무기(이도류 칼)는 그 단계의 판정을 모두 준비한다. 여닫는 시각은 실행 Clip이 정한다.
            var boxes = GetHitBoxes(melee);
            int hits = Mathf.Max(1, melee.hitsPerStep);
            int popupGroup = melee.weaponName == swordWeaponName && hits > 1 ? WeaponBaseSO.NewPopupGroup() : 0;
            for (int h = 0; h < hits; h++)
            {
                // 묶음이 단계 × hitsPerStep보다 모자라면(데이터가 어긋난 경우) 없는 판정은 건너뛴다.
                int index = meleeComboIndex * hits + h;
                if (boxes != null && index < boxes.Length && boxes[index] != null)
                    melee.BindMeleeHitboxes(boxes[index], controller, meleeComboIndex, popupGroup);
            }
        }
        else
        {
            timer = selectedWeapon.executeTime;
            int run = attackRun;
            StartCoroutine(selectedWeapon.WeaponUseSequence(controller, () => { if (run == attackRun) ForceRecoverFromWeapon(); }));
        }

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 2);

        // 공격 피드백. 근접 무기는 콤보 단계에 따로 정한 것이 있으면 그것을 쓴다(마무리 타의 휘두름 소리 등).
        FeedbackDataSO executeFeedback = selectedWeapon.executeFeedback;
        if (selectedWeapon is MeleeWeaponSO comboWeapon && comboWeapon.comboSteps[meleeComboIndex].executeFeedback != null)
            executeFeedback = comboWeapon.comboSteps[meleeComboIndex].executeFeedback;
        if (executeFeedback == null)
            return;

        Vector3 offset = executeFeedback.offset;
        Quaternion feedbackRotation = Quaternion.identity;

        if (controller.Facing < 0)
        {
            offset.x *= -1f;
        }
        feedbackPlayer.PlayExact(executeFeedback, transform.position + offset, feedbackRotation, controller);
    }

    private void StartRecover()
    {
        abilityState = AbilityState.Recover;

        if (selectedWeapon is MeleeWeaponSO melee)
        {
            var step = melee.comboSteps[meleeComboIndex];
            timer = step.recoverTime;
        }
        else
        {
            if (!manager.DashAbility.IsActive) controller.GravityOn = true;
            timer = selectedWeapon.recoverTime;
        }

        feedbackPlayer.Play(selectedWeapon.recoverFeedback, transform);
        controller.animator.SetInteger(selectedWeapon.weaponAnim, 3);
    }
    public void ForceRecoverFromWeapon()
    {
        if (abilityState != AbilityState.Execute)
            return;

        StartRecover();
    }
    private void TryStoreGearSkill()
    {
        var weapon = weaponData.weaponSets[currentSetIndex].GetGearWeapon(gearStage);
        if (RejectAttack(weapon == null || weaponData.skillStoreAnimation == null ? RejectionReason.Unavailable
            : skillGauge < weapon.skillGauge ? RejectionReason.Gauge : RejectionReason.None)) return;
        FinishModelReturn();
        pendingSkill = new StoredGearSkill(weapon, currentSetIndex, gearStage,
            gearUI != null ? gearUI.GetStageColor(gearStage) : Color.cyan);
        skillGauge -= weapon.skillGauge;
        PublishSkillGauge();
        selectedWeapon = weapon;
        CancelGear(PlayerGearVisual.EndReason.Fired);
        normalWeaponBuffer.Reset(); secondaryWeaponBuffer.Reset();
        isStoringSkill = true;
        abilityState = AbilityState.Windup;
        timer = Mathf.Max(.01f, weaponData.skillStoreDuration);
        controller.ApplyWeaponAnimation(weaponData.skillStoreAnimation, weapon.weaponAnim);
        controller.GravityOn = false; controller.Velocity = Vector2.zero;
        controller.animator.SetInteger(weapon.weaponAnim, 1);
        controller.animator.Play("WeaponWindup", 0, 0f); controller.animator.Update(0f);
        SkillStorageStarted?.Invoke(pendingSkill);
    }
    private void TickSkillStorage()
    {
        controller.Velocity = Vector2.zero;
        timer -= Time.deltaTime;
        if (timer > .0001f) return;
        if (storedSkills.Count >= StoredSkillCapacity) storedSkills.RemoveAt(0);
        storedSkills.Add(pendingSkill);
        isStoringSkill = false;
        StoredSkillsChanged?.Invoke();
        SkillStorageFinished?.Invoke(true);
        controller.animator.WriteDefaultValues();
        EndAttack();
        // 저장은 Execute/Recover를 거치지 않으므로 Windup의 종료 전이에 의존하지 않는다.
        RestoreStorageAnimation();
    }
    private void RestoreStorageAnimation()
    {
        restoreStorageIdle = !controller.animator.isActiveAndEnabled;
        if (restoreStorageIdle) return;
        controller.animator.Play("Idle", 0, 0f);
        controller.animator.Update(0f);
    }
    private void OnEnable()
    {
        if (restoreStorageIdle && controller != null) RestoreStorageAnimation();
    }
    private void TryUseStoredSkill()
    {
        while (storedSkills.Count > 0 && storedSkills[0].Weapon == null) storedSkills.RemoveAt(0);   // 무기가 사라진 항목이 맨 앞을 막지 않게
        if (storedSkills.Count == 0) { StoredSkillsChanged?.Invoke(); return; }
        var skill = storedSkills[0];
        if (RejectAttack(GetWeaponReadinessBlock(skill.Weapon))) return;
        if (currentSetIndex != skill.SetIndex) ApplyWeaponSet(skill.SetIndex, true);
        MarkWeaponUsed(skill.Weapon);
        storedSkills.RemoveAt(0);
        StoredSkillsChanged?.Invoke();
        selectedWeapon = skill.Weapon;
        executionSetIndex = skill.SetIndex;
        ShowModel(GetSkillModel(selectedWeapon, executionSetIndex));
        BindWeaponEvent(selectedWeapon);
        meleeComboIndex = 0; lastMeleeAttackTime = Time.time;
        CancelGear(selectedWeapon is HandCannonWeaponSO
            ? PlayerGearVisual.EndReason.Silent : PlayerGearVisual.EndReason.Fired);
        StartWindup();
        StoredSkillUsed?.Invoke();
    }
    // 동작이 끝나거나 끊긴 뒤 이 세트의 대기 모형으로 되돌린다.
    // hammerLeaving: 공격용 해머가 보이는 채 끝까지 재생된 경우(해머 1·2타에서 멈춤, 어느 세트의 것이든 HammerSkill·SpinningHammer).
    // Animator가 회수 → 대기를 섞는 동안 그 해머가 대기 자세로 줄어들며 돌아가므로(대기 Clip에 그 자세가 기록돼 있다),
    // 섞임이 끝나는 시각까지 대기 모형을 켜지 않는다. 바로 켜면 두 자루가 겹쳐 보인다.
    private void RestoreExecutionModel(bool hammerLeaving = false)
    {
        GameObject target = GetSetModel(currentSetIndex);
        executionSetIndex = -1;
        skillModelShown = false;
        if (hammerLeaving)
        {
            ShowModel(null);
            modelReturnTime = Time.time + SkillModelReturnBlend;
            return;
        }
        ShowModel(target);
    }
    private void CancelSpecialAttack()
    {
        SwordFinisherCinematic.End(controller);
        if (isStoringSkill)
        {
            isStoringSkill = false;
            SkillStorageFinished?.Invoke(false); // 시작 때 지불한 게이지는 반환하지 않는다.
        }
        if (controller.animator != null) controller.animator.WriteDefaultValues();
        controller.GravityOn = true;
    }
    // 기본 근접 공격(해머·칼)이 끊기거나 끝날 때 자세·판정·궤적을 기본 상태로 되돌린다.
    private void ClearNormalMeleePresentation()
    {
        if (!IsNormalMelee) return;
        var melee = (MeleeWeaponSO)selectedWeapon;
        controller.animator.WriteDefaultValues();
        var boxes = GetHitBoxes(melee);
        if (boxes != null)
        {
            foreach (var box in boxes)
            {
                if (box == null) continue;
                box.DisableHitbox();
                box.gameObject.SetActive(false);
            }
        }
        if (melee.weaponName == swordWeaponName)
        {
            if (swordArc != null) swordArc.enabled = false;
            if (swordArcOff != null) swordArcOff.enabled = false;
        }
        else if (hammerArc != null) hammerArc.enabled = false;
    }
    private void EndAttack()
    {
        rifleShotQueued = false;
        SwordFinisherCinematic.End(controller);
        // 끝난 동작이 남긴 모습을 먼저 본다(아래 정리가 기본값을 다시 쓰기 전에).
        // endsAtIdle: 회수 Clip이 대기 자세에서 끝난 단계(해머 3타, 칼, 칼로 연출하는 기술). hammerShown: 공격용 해머가 아직 보인다.
        bool endsAtIdle = IsNormalMelee
            ? ((MeleeWeaponSO)selectedWeapon).comboSteps[meleeComboIndex].recoverEndsAtIdle
            : selectedWeapon.RecoverEndsAtIdle;
        bool hammerShown = IsAttackHammerShown();
        ClearNormalMeleePresentation();
        RestoreExecutionModel(!endsAtIdle && hammerShown);
        meleeComboIndex = 0;
        controller.PlayerCounterState.EndCounter();

        comboQueuedThisStep = false;
        if (!manager.DashAbility.IsActive) controller.GravityOn = true;
        abilityState = AbilityState.None;
        controller.animator.SetInteger(selectedWeapon.weaponAnim, 0);
        // 대기 자세에서 끝난 단계는 섞지 않고 바로 대기로 넘긴다. 섞으면 0.1초 동안 WeaponModel이 대기 각도로 돌아가면서
        // 이미 제자리에 온 무기가 다시 움직였다가 돌아온다(해머는 일어섰다 눕고, 칼은 사라진 주검이 다시 나타난다).
        if (endsAtIdle && controller.animator.isActiveAndEnabled)
        {
            controller.animator.Play("Idle", 0, 0f);
            controller.animator.Update(0f);
        }
        normalWeaponBuffer.Reset();
        secondaryWeaponBuffer.Reset();
        RefreshNormalWeapon();
    }

    #region Armament
    // 무장(PlayerArmamentSO) = 기본 무기 + Gear 1~3단계 무기 묶음. 자리(weaponSets[n], 0 = 근접, 1 = 원거리)마다 같은 분류의 무장을 하나씩 장착한다.
    // 보조 무기와 교체 색은 자리에 남는다. 장착 상태는 세이브(DataManager.equippedArmaments)에 남고 Initialize에서 다시 적용한다.
    // 업그레이드 메뉴는 아래 읽기 전용 값과 ArmamentChanged를 보고 그리고, 교체는 TryEquipArmament로 요청한다.
    // 해금(2026-11-20 훈련 개편): 훈련 필증으로 해금하지 않은 무장은 장착할 수 없고, 잠긴 Gear 기술·보조 무기 칸은 실행용 사본에서 비운다(ArmamentUnlocks).
    // 비운 칸에는 잠금 표시(WeaponSet.lockedSlots)를 남겨 HUD가 자물쇠를 띄운다. 입력·저장 기술·HUD·Gear 게이지는 기존의 빈 칸 처리를 그대로 쓴다.

    public IReadOnlyList<WeaponBaseSO> SecondaryWeapons => weaponData.secondaryWeapons ?? System.Array.Empty<WeaponBaseSO>();
    public bool TryEquipSecondaryWeapon(int setIndex, WeaponBaseSO weapon)
    {
        var set = GetWeaponSet(setIndex);
        if (set == null || weapon == null || IsActive || isStoringSkill || !ArmamentUnlocks.IsWeaponUnlocked(weapon)) return false;
        bool available = false;
        foreach (var candidate in SecondaryWeapons) if (candidate == weapon) available = true;
        if (!available) return false;
        set.secondary = weapon;
        set.SetSlotLocked(WeaponSet.SecondarySlot, false);
        BindWeaponEvent(weapon);
        if (DataManager.Instance != null) DataManager.Instance.SetEquippedSecondaryWeapon(setIndex, weapon.weaponName);
        if (setIndex == currentSetIndex) ApplyWeaponSet(setIndex, true);
        ArmamentChanged?.Invoke(setIndex);
        return true;
    }
    public int CurrentSetIndex => currentSetIndex;
    /// <summary>지금 실행 중인 기술의 자리(저장해 둔 기술이면 저장한 자리, 아니면 지금 든 자리).</summary>
    public int ExecutingSetIndex => executionSetIndex >= 0 ? executionSetIndex : currentSetIndex;
    public int WeaponSetCount => weaponData != null && weaponData.weaponSets != null ? weaponData.weaponSets.Length : 0;
    public WeaponSet GetWeaponSet(int setIndex) => setIndex >= 0 && setIndex < WeaponSetCount ? weaponData.weaponSets[setIndex] : null;
    public IReadOnlyList<PlayerArmamentSO> Armaments => weaponData != null && weaponData.armaments != null
        ? weaponData.armaments : System.Array.Empty<PlayerArmamentSO>();
    public PlayerArmamentSO GetEquippedArmament(int setIndex) =>
        equippedArmaments != null && setIndex >= 0 && setIndex < equippedArmaments.Length ? equippedArmaments[setIndex] : null;
    /// <summary>자리의 무장이 바뀌었다(자리 번호).</summary>
    public event System.Action<int> ArmamentChanged;

    /// <summary>그 자리에 이 무장을 장착할 수 있는가. 분류가 같아야 하고, 훈련 필증으로 해금한 무장이어야 하며, 공격·기술 저장 중에는 바꾸지 않는다.</summary>
    public bool CanEquipArmament(int setIndex, PlayerArmamentSO armament)
    {
        WeaponSet set = GetWeaponSet(setIndex);
        return set != null && armament != null && armament.category == set.armamentCategory && armament.normal != null
            && !IsActive && !isStoringSkill && ArmamentUnlocks.IsArmamentUnlocked(armament);
    }

    /// <summary>그 자리에 무장을 장착하고 세이브에 남긴다. 지금 든 자리면 모형·HUD를 바로 맞춘다(교체 연출 없이).</summary>
    public bool TryEquipArmament(int setIndex, PlayerArmamentSO armament)
    {
        if (!CanEquipArmament(setIndex, armament)) return false;
        if (GetEquippedArmament(setIndex) == armament) return true;
        ApplyEquippedArmament(setIndex, armament, true);
        return true;
    }

    private void ApplyEquippedArmament(int setIndex, PlayerArmamentSO armament, bool persist)
    {
        EndPrimaryOverride(setIndex);   // 그 자리의 머신건 장착은 풀린다
        WriteArmament(weaponData.weaponSets[setIndex], armament);
        FilterLockedGear(setIndex);
        equippedArmaments[setIndex] = armament;
        if (persist && DataManager.Instance != null) DataManager.Instance.SetEquippedArmament(setIndex, armament.armamentId);
        if (setIndex == currentSetIndex) ApplyWeaponSet(setIndex, true);
        ArmamentChanged?.Invoke(setIndex);
    }

    internal System.Action BeginTutorialWeaponSwap(PlayerArmamentSO practiceArmament)
    {
        if (practiceArmament == null || practiceArmament.normal == null) return null;
        int slot = 0;
        while (slot < WeaponSetCount && GetWeaponSet(slot).armamentCategory != practiceArmament.category) slot++;
        if (slot >= WeaponSetCount) return null;
        var original = GetEquippedArmament(slot);
        if (IsActive) Interrupt();
        if (original != practiceArmament) ApplyEquippedArmament(slot, practiceArmament, false);
        if (CurrentSetIndex == slot && WeaponSetCount > 1) ApplyWeaponSet(slot == 0 ? 1 : 0, true);
        if (original == null || original == practiceArmament) return null;

        // 재수강 중 다른 원거리 무장이 저장돼 있어도 라이플을 빌려 실습하고 기존 무장으로 복귀한다.
        return () =>
        {
            if (this == null || GetEquippedArmament(slot) != practiceArmament) return;
            if (CurrentSetIndex == slot && IsActive) Interrupt();
            ApplyEquippedArmament(slot, original, false);
        };
    }

    // 에셋(Player_Weapon_Data)을 직접 고치지 않도록 실행용 사본을 만든다. 무장을 바꾸면 이 사본의 자리만 바뀐다.
    // 무기 SO는 사본을 만들지 않고 공유한다(쿨다운·적중 보상·훈련 통계가 무기 SO를 키로 쓴다).
    private PlayerWeaponDataSO CreateRuntimeData(PlayerWeaponDataSO source)
    {
        if (source == null) return null;
        runtimeWeaponData = Instantiate(source);
        runtimeWeaponData.name = source.name;
        runtimeWeaponData.hideFlags = HideFlags.DontSave;
        return runtimeWeaponData;
    }

    // 세이브에 남은 장착 무장을 자리마다 적용한다. 기록이 없거나 분류가 다르거나 아직 해금하지 않은 무장이면 그 자리의 기본 무장을 쓴다.
    // 잠긴 무장을 든 옛 기록은 기본 무장으로 바꿔 저장한다(나중에 해금했을 때 저절로 장착되지 않게). 해금하지 않은 Gear 기술은 비운다.
    private void LoadEquippedArmaments()
    {
        int count = WeaponSetCount;
        equippedArmaments = new PlayerArmamentSO[count];
        DataManager save = DataManager.Instance;
        for (int i = 0; i < count; i++)
        {
            WeaponSet set = weaponData.weaponSets[i];
            if (set == null) continue;
            PlayerArmamentSO armament = weaponData.FindArmament(save != null ? save.GetEquippedArmament(i) : null);
            bool lockedRecord = armament != null && !ArmamentUnlocks.IsArmamentUnlocked(armament);
            if (armament == null || armament.category != set.armamentCategory || armament.normal == null || lockedRecord) armament = set.defaultArmament;
            if (lockedRecord && save != null && armament != null) save.SetEquippedArmament(i, armament.armamentId);
            if (armament == null)
            {
                // 무장을 정하지 않은 자리는 무기 칸을 그대로 쓴다(잠긴 Gear 기술만 비운다)
                FilterLockedGear(i);
                continue;
            }
            WriteArmament(set, armament);
            FilterLockedGear(i);
            equippedArmaments[i] = armament;
        }
    }

    private static void WriteArmament(WeaponSet set, PlayerArmamentSO armament)
    {
        set.normal = armament.normal;
        set.gearStage1 = armament.gearStage1;
        set.gearStage2 = armament.gearStage2;
        set.gearStage3 = armament.gearStage3;
    }

    // 해금하지 않은 Gear 기술 칸을 비우고 잠금 표시를 남긴다(HUD가 자물쇠를 띄운다). 기본 공격 칸(normal)은 건드리지 않는다.
    // 원래 비어 있는 단계(Rifle·Sword Gear 2, Railgun Gear 1)는 잠금 표시 없이 빈 칸으로 둔다. 무장 값을 다시 쓴 직후에 부른다.
    // Gear 충전은 빈 단계도 순서대로 지나가므로(Hammer Gear 1이 잠겨도 Gear 2까지 충전된다) 충전 규칙은 바꾸지 않는다.
    private void FilterLockedGear(int setIndex)
    {
        WeaponSet set = GetWeaponSet(setIndex);
        if (set == null) return;
        for (int stage = 1; stage <= 3; stage++)
        {
            WeaponBaseSO weapon = set.GetGearWeapon(stage);
            bool locked = weapon != null && !ArmamentUnlocks.IsWeaponUnlocked(weapon);
            if (locked) set.SetGearWeapon(stage, null);
            set.SetSlotLocked(stage, locked);
        }
    }

    // 무장을 정하지 않은 자리: Gear 칸을 에셋 값으로 되돌린다(잠금으로 비웠던 칸을 다시 거르기 전에 채운다).
    private void CopySourceGear(int setIndex)
    {
        var source = data as PlayerWeaponDataSO;
        WeaponSet set = GetWeaponSet(setIndex);
        WeaponSet from = source != null && source.weaponSets != null && setIndex < source.weaponSets.Length ? source.weaponSets[setIndex] : null;
        if (set == null || from == null) return;
        for (int stage = 1; stage <= 3; stage++) set.SetGearWeapon(stage, from.GetGearWeapon(stage));
    }

    // 에셋의 자리 기본 보조 무기를 기억한다(Initialize에서 실행용 사본을 만든 직후, 아직 바꾸기 전에 부른다).
    private void CaptureDefaultSecondaries()
    {
        int count = WeaponSetCount;
        defaultSecondaries = new WeaponBaseSO[count];
        for (int i = 0; i < count; i++)
            defaultSecondaries[i] = weaponData.weaponSets[i] != null ? weaponData.weaponSets[i].secondary : null;
    }

    // 자리마다 보조 무기를 정한다(PR #156의 세이브 적용 + 해금 검사). 결과는 세이브 기록과 해금 상태만으로 정해지므로 다시 불러와도 같다.
    // 1) 세이브 기록이 후보에 있고 해금됐으면 그것 2) 기록이 후보에 있지만 잠겼으면 기록을 지워 저장하고 3)으로
    // 3) 기록이 없으면(모르는 이름 포함) 에셋 기본값이 해금됐을 때 그것 4) 아니면 비운다.
    // 같은 무기를 두 자리에 쓰는 것(직접 장착과 기본값 채움 모두)은 PR #156 규칙대로 허용한다. 다른 자리의 기록 때문에 기본값을 비우지 않는다
    // (비우면 원거리 자리에 피해 재생을 끼운 뒤 상관없는 해금만으로 Hammer 자리의 피해 재생이 사라지고, 세션과 다시 불러온 결과가 달라진다. 2026-11-20 독립 검토).
    // 세이브 슬롯이 없어 장착을 기록할 수 없으면 지금 칸의 해금된 보조 무기를 그대로 둔다. 비운 자리는 잠금 표시(WeaponSet.SecondarySlot)를 켠다.
    private void ResolveSecondaryWeapons()
    {
        int count = WeaponSetCount;
        DataManager save = DataManager.Instance;
        bool canRecord = save != null && save.HasSelectedSlot;
        for (int i = 0; i < count; i++)
        {
            WeaponSet set = weaponData.weaponSets[i];
            if (set == null) continue;
            WeaponBaseSO chosen = null;
            WeaponBaseSO record = FindSecondaryCandidate(save != null ? save.GetEquippedSecondaryWeapon(i) : null);
            if (record != null && ArmamentUnlocks.IsWeaponUnlocked(record)) chosen = record;
            else
            {
                // 잠긴 기록은 지운다(나중에 해금해도 저절로 장착되지 않게)
                if (record != null && save != null) save.SetEquippedSecondaryWeapon(i, "");
                if (!canRecord && set.secondary != null && ArmamentUnlocks.IsWeaponUnlocked(set.secondary)) chosen = set.secondary;
                else
                {
                    WeaponBaseSO fallback = i < defaultSecondaries.Length ? defaultSecondaries[i] : null;
                    if (fallback != null && ArmamentUnlocks.IsWeaponUnlocked(fallback)) chosen = fallback;
                }
            }
            set.secondary = chosen;
            set.SetSlotLocked(WeaponSet.SecondarySlot, chosen == null);
        }
    }

    private WeaponBaseSO FindSecondaryCandidate(string weaponName)
    {
        if (string.IsNullOrEmpty(weaponName)) return null;
        foreach (var candidate in SecondaryWeapons)
            if (candidate != null && candidate.weaponName == weaponName) return candidate;
        return null;
    }

    /// <summary>
    /// 해금 기록이 바뀌었다(구매·테스트 진입점, ArmamentUnlocks.Changed): 실행용 사본의 무장·Gear·보조 무기를 다시 정하고 HUD·메뉴에 알린다.
    /// 장착 무장이 잠기면(테스트 진입점으로 다시 잠근 경우) 그 자리의 기본 무장으로 바꿔 저장하고, 새로 잠긴 칸이 있으면 머신건 장착을 끝내며
    /// 저장 기술 중 잠긴 것을 지운다. 공격·저장 중이면 먼저 끊는다(메뉴 안의 구매에서는 생기지 않는다).
    /// </summary>
    public void RefreshUnlocks()
    {
        if (weaponData == null || weaponData.weaponSets == null) return;
        if (IsActive || isStoringSkill) Interrupt();
        DataManager save = DataManager.Instance;
        int count = WeaponSetCount;
        if (equippedArmaments == null || equippedArmaments.Length != count) System.Array.Resize(ref equippedArmaments, count);
        var lockedBefore = new int[count];
        bool newlyLocked = false;
        for (int i = 0; i < count; i++)
        {
            WeaponSet set = weaponData.weaponSets[i];
            if (set == null) continue;
            lockedBefore[i] = set.lockedSlots;
            PlayerArmamentSO armament = equippedArmaments[i];
            if (armament != null && !ArmamentUnlocks.IsArmamentUnlocked(armament) && set.defaultArmament != null && set.defaultArmament != armament)
            {
                armament = set.defaultArmament;
                equippedArmaments[i] = armament;
                newlyLocked = true;
                if (save != null) save.SetEquippedArmament(i, armament.armamentId);
            }
            if (armament != null) WriteArmament(set, armament);
            else CopySourceGear(i);
            FilterLockedGear(i);
        }
        ResolveSecondaryWeapons();
        for (int i = 0; i < count; i++)
        {
            WeaponSet set = weaponData.weaponSets[i];
            if (set != null && (set.lockedSlots & ~lockedBefore[i]) != 0) newlyLocked = true;
        }
        if (newlyLocked) EndPrimaryOverride();
        PurgeLockedStoredSkills();
        ApplyWeaponSet(currentSetIndex, true);
        PublishSkillGauge(true);
        for (int i = 0; i < count; i++) ArmamentChanged?.Invoke(i);
    }

    // 저장해 둔 기술 중 지금 잠긴 기술을 지운다(테스트 진입점으로 다시 잠근 경우).
    private void PurgeLockedStoredSkills()
    {
        int removed = storedSkills.RemoveAll(skill => skill.Weapon == null || !ArmamentUnlocks.IsWeaponUnlocked(skill.Weapon));
        if (removed > 0) StoredSkillsChanged?.Invoke();
    }

    /// <summary>이 자리의 그 칸이 해금 전이라 비어 있는가(1~3 = Gear 단계, WeaponSet.SecondarySlot = 보조 무기). 검증·메뉴용.</summary>
    public bool IsSlotLocked(int setIndex, int slot) => GetWeaponSet(setIndex)?.IsSlotLocked(slot) == true;

    /// <summary>그 자리에 이 보조 무기를 끼울 수 있는가(후보에 있고, 해금됐고, 공격·저장 중이 아님).</summary>
    public bool CanEquipSecondaryWeapon(int setIndex, WeaponBaseSO weapon)
    {
        if (GetWeaponSet(setIndex) == null || weapon == null || IsActive || isStoringSkill || !ArmamentUnlocks.IsWeaponUnlocked(weapon)) return false;
        foreach (var candidate in SecondaryWeapons) if (candidate == weapon) return true;
        return false;
    }
    #endregion

    #region Primary Override
    // 기본 공격 자리를 잠시 다른 무기로 바꾼다(Rifle Gear 3단계 '머신건 장착'). 그 자리(setIndex)를 든 동안만 바뀌고, 시간이 지나면 원래 무기로 돌아온다.
    // 대기 모형은 원래 무기의 것을 그대로 쓴다(GetSetModel은 자리의 기본 무기를 본다).

    public WeaponBaseSO PrimaryOverrideWeapon => primaryOverride;
    public int PrimaryOverrideSetIndex => primaryOverride != null ? primaryOverrideSet : -1;
    public float PrimaryOverrideDuration => primaryOverride != null ? primaryOverrideDuration : 0f;
    public float PrimaryOverrideRemaining => primaryOverride != null ? Mathf.Max(0f, primaryOverrideEnd - Time.time) : 0f;
    /// <summary>기본 공격 자리 바꾸기가 시작·끝났다(자리 번호).</summary>
    public event System.Action<int> PrimaryOverrideChanged;

    /// <summary>
    /// 기본 공격 자리를 duration초 동안 weapon으로 바꾼다. setIndex를 주지 않으면 지금 실행 중인 기술의 자리(저장해 둔 기술이면 저장한 자리)다.
    /// 기본 공격 중에 바뀌면 그 공격이 끝난 뒤부터 적용한다.
    /// expirationFeedback은 시간이 다해 돌아갈 때만 한 번 재생한다. 수동 해제·무장 교체·사망에서는 재생하지 않는다.
    /// </summary>
    public void BeginPrimaryOverride(WeaponBaseSO weapon, float duration, int setIndex = -1, FeedbackDataSO expirationFeedback = null)
    {
        if (weapon == null || duration <= 0f) return;
        if (setIndex < 0) setIndex = executionSetIndex >= 0 ? executionSetIndex : currentSetIndex;
        if (setIndex < 0 || setIndex >= WeaponSetCount) return;
        primaryOverride = weapon;
        primaryOverrideSet = setIndex;
        primaryOverrideDuration = duration;
        primaryOverrideEnd = Time.time + duration;
        primaryOverrideExpirationFeedback = expirationFeedback;
        BindWeaponEvent(weapon);
        RefreshNormalWeapon();
        PrimaryOverrideChanged?.Invoke(setIndex);
    }

    // 표시는 다른 자리를 들거나 총이 숨을 때도 꺼진다. 종료음은 표시 대신 실제 장착 시간의 만료에서만 낸다.
    private void TickPrimaryOverride()
    {
        if (primaryOverride == null) return;
        bool empty = primaryOverride is IncendiaryRifleWeaponSO && Traits.IncendiaryAmmo == 0;
        bool expired = Time.time >= primaryOverrideEnd;
        if (!empty && !expired) return;
        // 마지막 작열탄을 쏜 공격은 그 공격이 끝날 때까지 장착 종료를 미룬다. 바로 끝내면 Tick의 mountEnded가 공격을 끊어
        // 마지막 발의 반동 Animation이 잘린다(2026-11-21). 장착 시간 만료는 지금처럼 바로 끊는다.
        if (!expired && IsNormalAttackRunning && selectedWeapon == primaryOverride) return;
        FeedbackDataSO expiredFeedback = primaryOverrideExpirationFeedback;
        EndPrimaryOverride();   // 먼저 상태를 지워 다음 Tick과 종료 이벤트에서 중복 재생하지 않게 한다.
        if (expiredFeedback != null && feedbackPlayer != null) feedbackPlayer.Play(expiredFeedback, transform);
    }

    /// <summary>기본 공격 자리 바꾸기를 끝낸다. setIndex를 주면 그 자리의 것일 때만 끝낸다.</summary>
    public void EndPrimaryOverride(int setIndex = -1)
    {
        if (primaryOverride == null || (setIndex >= 0 && setIndex != primaryOverrideSet)) return;
        int set = primaryOverrideSet;
        primaryOverride = null;
        primaryOverrideSet = -1;
        primaryOverrideEnd = -1f;
        primaryOverrideExpirationFeedback = null;
        RefreshNormalWeapon();
        PrimaryOverrideChanged?.Invoke(set);
    }

    private WeaponBaseSO ResolveNormalWeapon(int setIndex)
    {
        if (primaryOverride != null && primaryOverrideSet == setIndex) return primaryOverride;
        return weaponData.weaponSets[setIndex].normal;
    }

    // 지금 자리의 기본 공격 무기를 다시 정한다. 기본 공격 중이면 그 공격이 끝날 때(EndAttack·Interrupt) 바꾼다.
    // 기본 공격 중에 바꾸면 selectedWeapon과 normalWeapon이 어긋나 연사·조준(IsStarShotActive)이 그 자리에서 끊긴다.
    private void RefreshNormalWeapon()
    {
        if (weaponData == null || (abilityState != AbilityState.None && selectedWeapon == normalWeapon)) return;
        WeaponBaseSO target = ResolveNormalWeapon(currentSetIndex);
        if (normalWeapon == target) return;
        if (selectedWeapon == normalWeapon) selectedWeapon = target;
        normalWeapon = target;
        BindWeaponEvent(normalWeapon);
        meleeComboIndex = 0;
    }
    #endregion
}
