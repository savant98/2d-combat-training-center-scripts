using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
public class PlayerWeaponAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Attack;
    public override bool IsActive => abilityState != AbilityState.None;
    public AbilityState AttackPhase => abilityState;
    public Vector2 PrimaryFireDirection { get; private set; } = Vector2.right;
    public bool IsStarShotActive => !isStoringSkill && IsActive && selectedWeapon == normalWeapon
        && normalWeapon is RangedWeaponSO ranged && ranged.projectilePrefab is StarShot;
    private PlayerStarShotAim starAim;
    private bool restoreStorageIdle;
    public bool QueuePrimaryStarShot(RangedWeaponSO weapon, WeaponBaseSO.DamageInfo damage, Vector2 direction)
        => starAim != null && starAim.Queue(weapon, damage, direction);
    public const int StoredSkillCapacity = 3;
    private readonly List<StoredGearSkill> storedSkills = new();
    public IReadOnlyList<StoredGearSkill> StoredSkills => storedSkills;
    public bool IsStoringSkill => isStoringSkill;
    public float SkillStorageProgress => isStoringSkill ? 1f - Mathf.Clamp01(timer / weaponData.skillStoreDuration) : 0f;
    public event System.Action StoredSkillsChanged;
    public event System.Action<StoredGearSkill> SkillStorageStarted;
    public event System.Action<bool> SkillStorageFinished;
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

    // 기본 공격 자리를 잠시 다른 무기로 바꾼 상태(Rifle Gear 3단계 '머신건 장착', 2026-11-18). 그 자리를 든 동안만 기본 공격이 바뀌고 시간이 지나면 돌아온다.
    private WeaponBaseSO primaryOverride;
    private int primaryOverrideSet = -1;
    private float primaryOverrideEnd = -1f;
    private float primaryOverrideDuration;

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
        hammerArc = FindArc("HammerArc");
        swordArc = FindArc("SwordArc");
        swordArcOff = FindArc("SwordArcOff");
        var attackRig = controller.model.transform.Find("WeaponModel/MeleeWeapon");
        hammerAttackRig = attackRig != null ? attackRig.gameObject : null;
        var attackBlade = attackRig != null ? attackRig.Find("Hammer") : null;
        hammerAttackBlade = attackBlade != null ? attackBlade.GetComponent<SpriteRenderer>() : null;
        LoadEquippedArmaments();
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

    public void ResetSkillGauge()
    {
        if (isStoringSkill || executionSetIndex >= 0) Interrupt();
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

    private void RegisterNormalAttack()
    {
        suppressHeldPrimaryUntilRelease = gearState != GearState.Inactive;
        if (isStoringSkill) { normalWeaponBuffer.Reset(); return; }
        bool down = IsDownHeld();
        primaryPressPending = false;
        if (gearState == GearState.Inactive && storedSkills.Count > 0)
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
            if (target == null || !IsWeaponCooldownReady(target)) { normalWeaponBuffer.Reset(); return; }
        }
        // Gear+공격 동시 입력은 모든 입력 이벤트가 끝난 Tick에서 해석한다.
        normalWeaponBuffer.RegisterInput();
    }
    private void RegisterSecondaryAttack()
    {
        if (isStoringSkill || secondaryWeapon == null || !IsWeaponCooldownReady(secondaryWeapon))
        { secondaryWeaponBuffer.Reset(); return; }
        secondaryWeaponBuffer.RegisterInput();
    }
    private bool CanRepeatPrimary => !suppressHeldPrimaryUntilRelease && gearState == GearState.Inactive && controller.CanReceiveInput
        && InputManager.Held(Button.Attack) && (!IsActive || selectedWeapon == normalWeapon);
    private void RepeatPrimaryInput()
    {
        // 유지 입력은 실제 선입력 버퍼에 남기지 않아 키를 놓으면 다음 자동 타격이 취소된다.
        if (!InputManager.Held(Button.Attack)) suppressHeldPrimaryUntilRelease = false;
        if (comboQueuedByHold && !InputManager.Held(Button.Attack))
        { comboQueuedThisStep = comboQueuedByHold = false; }
    }
    private void TryAttack()
    {
        if (!CanDo()) { HoldAttackThroughHit(); return; }
        restartAfterHit = false;
        // 기술 저장과 사용은 모두 '아래 + 공격'이다(아래를 누른 채 공격을 눌러도 되고, 대각선 아래처럼 다른 방향이 함께 눌려도 된다).
        // Gear 대기 중이면 그 단계의 기술을 저장하고, Gear를 쓰지 않을 때는 저장해 둔 기술을 꺼내 쓴다(누른 순간 아래가 들어 있었으면 storedSkillBuffer).
        bool down = primaryPressDown || IsDownHeld();
        if (gearState != GearState.Inactive)
        {
            if (normalWeaponBuffer.ConsumeIfValid())
            {
                suppressHeldPrimaryUntilRelease = true;
                // Gear를 충전하는 동안에는 아무 일도 하지 않는다(TryStartGearWeapon은 대기 중에만 쏜다).
                // 저장하려다 조금 일찍 눌러, 저장해 둔 기술이 대신 나가 버리지 않게 한다.
                if (down && gearState == GearState.Waiting)
                    TryStoreGearSkill();
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
                if (IsActive) return;
            }
            StartNormalWeapon();
            return;
        }
        if (CanRepeatPrimary) StartNormalWeapon();
    }
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

        if (skillGauge < normalWeapon.skillGauge)
        {
            // 무기 스킬 게이지 없을때 사용 피드백 필요
            return;
        }

        if (!IsWeaponCooldownReady(normalWeapon))
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
        if (gearWeapon == null)
            return;

        // 게이지가 부족하면 아무 일도 하지 않는다.
        // Gear도 취소하지 않고, 기본공격도 나가지 않는다.
        if (skillGauge < gearWeapon.skillGauge)
            return;

        if (!IsWeaponCooldownReady(gearWeapon))
            return;

        selectedWeapon = gearWeapon;

        // 1차 구현에서는 Gear Weapon 콤보를 쓰지 않음
        meleeComboIndex = 0;
        lastMeleeAttackTime = Time.time;

        skillGauge -= selectedWeapon.skillGauge;
        MarkWeaponUsed(gearWeapon);

        PublishSkillGauge();

        CancelGear(PlayerGearVisual.EndReason.Fired);
        StartWindup();
    }

    private void StartSecondaryWeapon()
    {
        if (secondaryWeapon == null || skillGauge < secondaryWeapon.skillGauge)
        {
            // 무기 스킬 게이지 없을때 사용 피드백 필요
            return;
        }

        if (!IsWeaponCooldownReady(secondaryWeapon))
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
        if (primaryOverride != null && Time.time >= primaryOverrideEnd) EndPrimaryOverride();
        if (isStoringSkill) { TickSkillStorage(); UpdateWeaponCooldownUI(); UpdateGearStageCooldownUI(); return; }
        RepeatPrimaryInput();
        if (abilityState != AbilityState.None)
        {
            // 아래 + 공격: 기본 공격 중이면 곧바로 끊고 저장 기술, 다른 기술의 회수 중이면 끝날 때까지 남겨 둔다
            if (TryCancelIntoStoredSkill())
            {
                UpdateWeaponCooldownUI();
                UpdateGearStageCooldownUI();
                return;
            }
            if (abilityState == AbilityState.Recover && !IsNormalAttackRunning && controller.CanReceiveInput) storedSkillBuffer.Hold();
            if (abilityState == AbilityState.Execute && !currentHitboxOpened && IsCurrentHitboxOpen())
                currentHitboxOpened = true;
            TryQueueComboInput();
            timer -= Time.deltaTime;

            // 빠르게 잇는 무기(칼)는 후속 입력이 회수 중 들어와도 남은 회수 시간을 기다리지 않는다.
            if (ChainsWithoutRecover && abilityState == AbilityState.Recover && CancelAttack())
                return;

            if (timer > 0)
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
                        StartRecover();
                    else
                    {
                        EndAttack();
                        TryChainStoredSkill();
                    }
                    break;

                case AbilityState.Recover:
                    if (CancelAttack())
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
        TryAttack();

        UpdateWeaponCooldownUI();
        UpdateGearStageCooldownUI();
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
            else if (IsDownHeld() && InputManager.Held(Button.Attack))
            {
                primaryPressPending = false;
                suppressHeldPrimaryUntilRelease = true;
                storedSkillBuffer.RegisterInput();
            }
        }
        if (!IsNormalAttackRunning || !storedSkillBuffer.IsValid || storedSkills.Count == 0 || !controller.CanReceiveInput)
            return false;
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
        if (controller != null && controller.Health != null) controller.Health.OnPlayerDeath -= HandlePlayerDeath;
    }
    private void HandlePlayerDeath() => EndPrimaryOverride();
    #endregion

    #region OnWeaponHit
    private void OnWeaponHitEnemy(WeaponBaseSO source, CharacterController2D owner, Vector2 hitPoint)
    {
        if (owner != controller)
            return;

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
        return GetWeaponCooldownRemaining(weapon) <= 0f;
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

        gearUI.SetStageWeaponCooldown(
            gearWeapon.weaponSprite,
            GetWeaponCooldownRemaining(gearWeapon),
            GetWeaponCooldown(gearWeapon),
            ready);
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
    public override void Interrupt()
    {
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
        FinishModelReturn();
        attackRun++;
        // 공격 방향 결정
        int inputDir = (int)controller.MoveInput.x;
        if (inputDir != 0)
        {
            controller.FacingUpdate(inputDir);
        }

        if (selectedWeapon == normalWeapon)
        {
            // 위를 누르고 쏘면 위 30도. 아래 사격은 없다(아래 + 공격은 기술 저장·사용 입력이다).
            float vertical = controller.MoveInput.y > .5f ? 1f : 0f;
            PrimaryFireDirection = new Vector2(controller.Facing, vertical * .577350269f).normalized;
        }
        abilityState = AbilityState.Windup;
        comboQueuedThisStep = comboQueuedByHold = false;
        currentHitboxOpened = false;

        // Gear·보조 무기는 기술용 모형을 움직이는 Clip으로 연출한다. 대기 모형과 다르면(칼 세트의 해머 기술, 해머 세트의 칼 기술) 실행 동안 바꿔 든다.
        if (executionSetIndex < 0 && selectedWeapon != normalWeapon)
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

        feedbackPlayer.Play(selectedWeapon.windupFeedback, transform);
        controller.GravityOn = false;
        controller.Velocity = Vector2.zero;

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 1);
        if (IsNormalMelee)
        {
            // Execute·Recover에서 다음 Windup으로 바로 이어도 새 Clip의 첫 프레임부터 평가한다.
            controller.animator.Play("WeaponWindup", 0, 0f);
            controller.animator.Update(0f);
        }
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
            for (int h = 0; h < hits; h++)
            {
                // 묶음이 단계 × hitsPerStep보다 모자라면(데이터가 어긋난 경우) 없는 판정은 건너뛴다.
                int index = meleeComboIndex * hits + h;
                if (boxes != null && index < boxes.Length && boxes[index] != null)
                    melee.BindMeleeHitboxes(boxes[index], controller, meleeComboIndex);
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
        feedbackPlayer.PlayExact(executeFeedback, transform.position + offset, feedbackRotation);
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
            controller.GravityOn = true;
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
        if (weapon == null || storedSkills.Count >= StoredSkillCapacity || skillGauge < weapon.skillGauge
            || weaponData.skillStoreAnimation == null) return;
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
        storedSkills.RemoveAt(0);
        StoredSkillsChanged?.Invoke();
        selectedWeapon = skill.Weapon;
        executionSetIndex = skill.SetIndex;
        ShowModel(GetSkillModel(selectedWeapon, executionSetIndex));
        BindWeaponEvent(selectedWeapon);
        meleeComboIndex = 0; lastMeleeAttackTime = Time.time;
        CancelGear(PlayerGearVisual.EndReason.Fired);
        StartWindup();
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
        controller.GravityOn = true;
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

    /// <summary>그 자리에 이 무장을 장착할 수 있는가. 분류가 같아야 하고, 공격·기술 저장 중에는 바꾸지 않는다.</summary>
    public bool CanEquipArmament(int setIndex, PlayerArmamentSO armament)
    {
        WeaponSet set = GetWeaponSet(setIndex);
        return set != null && armament != null && armament.category == set.armamentCategory && armament.normal != null
            && !IsActive && !isStoringSkill;
    }

    /// <summary>그 자리에 무장을 장착하고 세이브에 남긴다. 지금 든 자리면 모형·HUD를 바로 맞춘다(교체 연출 없이).</summary>
    public bool TryEquipArmament(int setIndex, PlayerArmamentSO armament)
    {
        if (!CanEquipArmament(setIndex, armament)) return false;
        if (GetEquippedArmament(setIndex) == armament) return true;
        EndPrimaryOverride(setIndex);   // 그 자리의 머신건 장착은 풀린다
        WriteArmament(weaponData.weaponSets[setIndex], armament);
        equippedArmaments[setIndex] = armament;
        if (DataManager.Instance != null) DataManager.Instance.SetEquippedArmament(setIndex, armament.armamentId);
        if (setIndex == currentSetIndex) ApplyWeaponSet(setIndex, true);
        ArmamentChanged?.Invoke(setIndex);
        return true;
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

    // 세이브에 남은 장착 무장을 자리마다 적용한다. 기록이 없거나 분류가 다르면 그 자리의 기본 무장을 쓴다.
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
            if (armament == null || armament.category != set.armamentCategory || armament.normal == null) armament = set.defaultArmament;
            if (armament == null) continue;   // 무장을 정하지 않은 자리는 무기 칸을 그대로 쓴다
            WriteArmament(set, armament);
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
    /// </summary>
    public void BeginPrimaryOverride(WeaponBaseSO weapon, float duration, int setIndex = -1)
    {
        if (weapon == null || duration <= 0f) return;
        if (setIndex < 0) setIndex = executionSetIndex >= 0 ? executionSetIndex : currentSetIndex;
        if (setIndex < 0 || setIndex >= WeaponSetCount) return;
        primaryOverride = weapon;
        primaryOverrideSet = setIndex;
        primaryOverrideDuration = duration;
        primaryOverrideEnd = Time.time + duration;
        BindWeaponEvent(weapon);
        RefreshNormalWeapon();
        PrimaryOverrideChanged?.Invoke(setIndex);
    }

    /// <summary>기본 공격 자리 바꾸기를 끝낸다. setIndex를 주면 그 자리의 것일 때만 끝낸다.</summary>
    public void EndPrimaryOverride(int setIndex = -1)
    {
        if (primaryOverride == null || (setIndex >= 0 && setIndex != primaryOverrideSet)) return;
        int set = primaryOverrideSet;
        primaryOverride = null;
        primaryOverrideSet = -1;
        primaryOverrideEnd = -1f;
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
