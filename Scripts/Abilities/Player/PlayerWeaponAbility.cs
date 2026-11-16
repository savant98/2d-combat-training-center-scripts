using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
public class PlayerWeaponAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Attack;
    public override bool IsActive => abilityState != AbilityState.None;
    public AbilityState AttackPhase => abilityState;
    public Vector2 PrimaryFireDirection { get; private set; } = Vector2.right;
    public bool IsStarShotActive => !isStoringSkill && !isHammerDrop && IsActive && selectedWeapon == normalWeapon
        && normalWeapon is RangedWeaponSO ranged && ranged.projectilePrefab is StarShot;
    private PlayerStarShotAim starAim;
    private bool restoreStorageIdle;
    public bool QueuePrimaryStarShot(RangedWeaponSO weapon, WeaponBaseSO.DamageInfo damage, Vector2 direction)
        => starAim != null && starAim.Queue(weapon, damage, direction);
    public const int StoredSkillCapacity = 3;
    private readonly List<StoredGearSkill> storedSkills = new();
    public IReadOnlyList<StoredGearSkill> StoredSkills => storedSkills;
    public bool IsStoringSkill => isStoringSkill;
    public bool IsHammerDropActive => isHammerDrop;
    public float SkillStorageProgress => isStoringSkill ? 1f - Mathf.Clamp01(timer / weaponData.skillStoreDuration) : 0f;
    public event System.Action StoredSkillsChanged;
    public event System.Action<StoredGearSkill> SkillStorageStarted;
    public event System.Action<bool> SkillStorageFinished;
    private StoredGearSkill pendingSkill;
    private bool isStoringSkill, isHammerDrop, hammerDiving;
    private float dropElapsed;
    private int executionSetIndex = -1;
    private bool suppressHeldPrimaryUntilRelease;
    private readonly Dictionary<WeaponBaseSO, System.Action<CharacterController2D, Vector2>> hitHandlers = new();
    private readonly Collider2D[] dropHits = new Collider2D[32];
    private readonly HashSet<EnemyHealth> dropTargets = new();

    [Header("Melee Hitboxes")]
    [SerializeField] private string hammerWeaponName = "Hammer";
    [SerializeField] private string hammerSkillName = "HammerSkill";
    [SerializeField] private MeleeHitbox[] hammerHitBoxes;
    [SerializeField] private MeleeHitbox[] hammerSkillBoxes;
    [SerializeField] private GameObject[] weaponModels;

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
    private readonly Dictionary<WeaponBaseSO, float> weaponLastUseTimes = new();

    private PlayerWeaponDataSO weaponData;

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
    private bool IsNormalHammer => !isHammerDrop && !isStoringSkill && selectedWeapon == normalWeapon && selectedWeapon is MeleeWeaponSO && selectedWeapon.weaponName == hammerWeaponName;

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

        weaponData = (PlayerWeaponDataSO)data;
        maxSkillGauge = weaponData.maxSkillGauge;
        weaponSwapCoolTime = weaponData.swapCoolTime;

        normalWeaponBuffer.SetBufferTime(data.inputBufferTime);
        secondaryWeaponBuffer.SetBufferTime(data.inputBufferTime);

        controller.OnNormalWeapon += RegisterNormalAttack;
        controller.OnSecondaryWeapon += RegisterSecondaryAttack;
        controller.OnSwapWeaponSet += SwapWeaponSet;

        // 다음 단계에서 PlayerController에 추가할 이벤트
        controller.OnGearPressed += HandleGearPressed;
        controller.OnGearReleased += HandleGearReleased;

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
        controller.OnLanded += HandleHammerDropLanding;
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
        if (isStoringSkill || isHammerDrop || executionSetIndex >= 0) Interrupt();
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
    private void RegisterNormalAttack()
    {
        suppressHeldPrimaryUntilRelease = gearState != GearState.Inactive;
        if (isStoringSkill || isHammerDrop) { normalWeaponBuffer.Reset(); return; }
        if (gearState == GearState.Waiting && controller.MoveInput.y >= -.5f)
        {
            var target = weaponData.weaponSets[currentSetIndex].GetGearWeapon(gearStage);
            if (target == null || !IsWeaponCooldownReady(target)) { normalWeaponBuffer.Reset(); return; }
        }
        // Gear+공격 동시 입력은 모든 입력 이벤트가 끝난 Tick에서 해석한다.
        normalWeaponBuffer.RegisterInput();
    }
    private void RegisterSecondaryAttack()
    {
        if (isStoringSkill || isHammerDrop || secondaryWeapon == null || !IsWeaponCooldownReady(secondaryWeapon))
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
        if (gearState != GearState.Inactive)
        {
            if (normalWeaponBuffer.ConsumeIfValid())
            {
                suppressHeldPrimaryUntilRelease = true;
                if (gearState == GearState.Charging && chargingTargetStage == 1 && gearStage == 0)
                    TryUseStoredSkill();
                else if (gearState == GearState.Waiting && controller.MoveInput.y < -.5f)
                    TryStoreGearSkill();
                else TryStartGearWeapon();
                if (IsActive) { secondaryWeaponBuffer.Reset(); return; }
            }
            if (secondaryWeaponBuffer.ConsumeIfValid()) StartSecondaryWeapon();
            return;
        }
        // 보조무기는 기본 공격 키 유지 중에도 새 입력으로 우선 사용할 수 있다.
        if (secondaryWeaponBuffer.ConsumeIfValid()) { StartSecondaryWeapon(); return; }
        if (normalWeaponBuffer.ConsumeIfValid() || CanRepeatPrimary) StartNormalWeapon();
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
            normalWeaponBuffer.RegisterInput();
        }
        normalWeaponBuffer.Hold();
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
        var boxes = melee.weaponName == hammerWeaponName ? hammerHitBoxes : hammerSkillBoxes;
        if (boxes == null || meleeComboIndex < 0 || meleeComboIndex >= boxes.Length || boxes[meleeComboIndex] == null)
            return false;
        return boxes[meleeComboIndex].gameObject.activeInHierarchy;
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
        if (!freshInput && !(IsNormalHammer && CanRepeatPrimary))
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

        if (skillGauge < normalWeapon.skillGauge)
        {
            // 무기 스킬 게이지 없을때 사용 피드백 필요
            return;
        }

        if (!IsWeaponCooldownReady(normalWeapon))
            return;

        selectedWeapon = normalWeapon;
        if (normalWeapon is MeleeWeaponSO && normalWeapon.weaponName == hammerWeaponName
            && !controller.IsGrounded && controller.MoveInput.y < -.5f && weaponData.hammerDropAnimation != null)
        {
            StartHammerDrop();
            return;
        }

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
        if (isStoringSkill) { TickSkillStorage(); UpdateWeaponCooldownUI(); UpdateGearStageCooldownUI(); return; }
        if (isHammerDrop) { TickHammerDrop(); UpdateWeaponCooldownUI(); UpdateGearStageCooldownUI(); return; }
        RepeatPrimaryInput();
        if (abilityState != AbilityState.None)
        {
            if (abilityState == AbilityState.Execute && !currentHitboxOpened && IsCurrentHitboxOpen())
                currentHitboxOpened = true;
            TryQueueComboInput();
            timer -= Time.deltaTime;

            // 후속 입력이 회수 중 들어와도 남은 회수 시간을 기다리지 않는다.
            if (IsNormalHammer && abilityState == AbilityState.Recover && CancelAttack())
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
                    if (IsNormalHammer && CancelAttack())
                        break;
                    if (selectedWeapon.ShouldEnterRecover())
                        StartRecover();
                    else
                        EndAttack();
                    break;

                case AbilityState.Recover:
                    if (CancelAttack())
                        break;

                    EndAttack();
                    break;
            }

            UpdateWeaponCooldownUI();
            UpdateGearStageCooldownUI();
            return;
        }

        UpdateGear();
        TryAttack();

        UpdateWeaponCooldownUI();
        UpdateGearStageCooldownUI();
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
        normalWeapon = set.normal;
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

        foreach (GameObject weaponModel in weaponModels)
        {
            weaponModel.SetActive(false);
        }

        weaponModels[currentSetIndex].SetActive(true);

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
                    weaponModels[currentSetIndex]);
            }
        }
    }
    private void BindWeaponEvents()
    {
        var set = weaponData.weaponSets[currentSetIndex];

        BindWeaponEvent(set.normal);
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
    }
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
        if (!CanDo())
            return;

        if (abilityState != AbilityState.None)
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
        grabCleanup?.Invoke(); grabCleanup = null;
        StopAllCoroutines();
        normalWeaponBuffer.Reset(); secondaryWeaponBuffer.Reset();
        restartAfterHit = false;
    }
    public override void Interrupt()
    {
        if (abilityState == AbilityState.None)
        {
            CancelGear();
            return;
        }

        // 판정 전에 끊긴 첫 타격인지 먼저 기록한다. 피격으로 끊긴 경우에만 경직 뒤 다시 시작한다(HoldAttackThroughHit).
        bool wasStoring = isStoringSkill;
        restartAfterHit = !isHammerDrop && !isStoringSkill && IsUnresolvedFirstStrike();
        CancelSpecialAttack();
        ClearNormalHammerPresentation();

        controller.PlayerCounterState.EndCounter();

        comboQueuedThisStep = false;
        meleeComboIndex = 0; // 혹은 유지 정책

        normalWeaponBuffer.Reset();
        secondaryWeaponBuffer.Reset();

        CancelGear();

        StopAllCoroutines();
        controller.GravityOn = true;
        abilityState = AbilityState.None;

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 0);
        RestoreExecutionModel();
        if (wasStoring) RestoreStorageAnimation();
    }
    private void StartWindup()
    {
        // 공격 방향 결정
        int inputDir = (int)controller.MoveInput.x;
        if (inputDir != 0)
        {
            controller.FacingUpdate(inputDir);
        }

        if (selectedWeapon == normalWeapon)
        {
            float vertical = Mathf.Abs(controller.MoveInput.y) > .5f ? Mathf.Sign(controller.MoveInput.y) : 0f;
            PrimaryFireDirection = new Vector2(controller.Facing, vertical * .577350269f).normalized;
        }
        abilityState = AbilityState.Windup;
        comboQueuedThisStep = comboQueuedByHold = false;
        currentHitboxOpened = false;

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
        if (IsNormalHammer)
        {
            // Execute에서 다음 Windup으로 바로 이어도 새 Clip의 첫 프레임부터 평가한다.
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
            if (melee.weaponName == hammerWeaponName)
            {
                melee.BindMeleeHitboxes(hammerHitBoxes[meleeComboIndex], controller, meleeComboIndex);
            }
            else
            {
                melee.BindMeleeHitboxes(hammerSkillBoxes[meleeComboIndex], controller, meleeComboIndex);
            }
        }
        else
        {
            timer = selectedWeapon.executeTime;
            StartCoroutine(selectedWeapon.WeaponUseSequence(controller, ForceRecoverFromWeapon));
        }

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 2);

        // 공격 피드백
        if (selectedWeapon.executeFeedback == null)
            return;

        Vector3 offset = selectedWeapon.executeFeedback.offset;
        Quaternion feedbackRotation = Quaternion.identity;

        if (controller.Facing < 0)
        {
            offset.x *= -1f;
        }
        feedbackPlayer.PlayExact(selectedWeapon.executeFeedback, transform.position + offset, feedbackRotation);
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
        if (storedSkills.Count == 0) return;
        var skill = storedSkills[0];
        if (skill.Weapon == null) return;
        storedSkills.RemoveAt(0);
        StoredSkillsChanged?.Invoke();
        selectedWeapon = skill.Weapon;
        executionSetIndex = skill.SetIndex;
        for (int i = 0; i < weaponModels.Length; i++) weaponModels[i].SetActive(i == executionSetIndex);
        BindWeaponEvent(selectedWeapon);
        meleeComboIndex = 0; lastMeleeAttackTime = Time.time;
        CancelGear(PlayerGearVisual.EndReason.Fired);
        StartWindup();
    }
    private void RestoreExecutionModel()
    {
        if (executionSetIndex < 0) return;
        executionSetIndex = -1;
        for (int i = 0; i < weaponModels.Length; i++) weaponModels[i].SetActive(i == currentSetIndex);
    }
    private void StartHammerDrop()
    {
        normalWeaponBuffer.Reset(); secondaryWeaponBuffer.Reset();
        CancelGear(); ClearNormalHammerPresentation();
        MarkWeaponUsed(normalWeapon);
        isHammerDrop = true; hammerDiving = false; dropElapsed = 0f;
        abilityState = AbilityState.Windup; timer = weaponData.hammerDropWindup;
        controller.ApplyWeaponAnimation(weaponData.hammerDropAnimation, selectedWeapon.weaponAnim);
        controller.GravityOn = false; controller.Velocity = Vector2.zero;
        controller.animator.SetInteger(selectedWeapon.weaponAnim, 1);
        controller.animator.Play("WeaponWindup", 0, 0f); controller.animator.Update(0f);
    }
    private void TickHammerDrop()
    {
        timer -= Time.deltaTime;
        if (abilityState == AbilityState.Windup)
        {
            if (controller.IsGrounded) { CancelSpecialAttack(); EndAttack(); return; }
            if (timer > 0f) return;
            abilityState = AbilityState.Execute; hammerDiving = true;
            controller.animator.SetInteger(selectedWeapon.weaponAnim, 2);
            controller.animator.Play("WeaponExecute", 0, 0f); controller.animator.Update(0f);
        }
        if (abilityState == AbilityState.Execute)
        {
            dropElapsed += Time.deltaTime;
            controller.Velocity = Vector2.down * weaponData.hammerDropSpeed;
            if (dropElapsed > 4f) { CancelSpecialAttack(); EndAttack(); }
        }
        else if (abilityState == AbilityState.Recover && timer <= 0f)
        { CancelSpecialAttack(); EndAttack(); }
    }
    private void HandleHammerDropLanding()
    {
        if (!isHammerDrop || !hammerDiving) return;
        // OnLanded에서 IsGrounded는 아직 이전 값이다. 전용 플래그로 한 번만 확정한다.
        hammerDiving = false;
        abilityState = AbilityState.Recover; timer = .2f;
        controller.Velocity = Vector2.zero;
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(1 << LayerMask.NameToLayer("Enemy"));
        Vector2 center = (Vector2)controller.transform.position + Vector2.down * .15f;
        int count = controller.Motor.OverlapAttackBox(center, new Vector2(6f, 2.1f), 0f, dropHits, filter.layerMask);
        dropTargets.Clear();
        var info = weaponData.hammerDropDamage;
        info.sourceWeapon = selectedWeapon; info.knockbackOrigin = controller.transform.position; info.knockbackFacing = controller.Facing;
        for (int i = 0; i < count; i++)
        {
            var health = dropHits[i].GetComponentInParent<EnemyHealth>();
            if (health == null || health.IsDamageBlocked || !dropTargets.Add(health)) continue;
            Vector2 point = dropHits[i].ClosestPoint(center);
            if (health.enemyShieldSystem != null) health.enemyShieldSystem.TakeDamage(info, point);
            else health.TakeDamage(info, point);
            selectedWeapon.OnHitEnemy?.Invoke(controller, point);
        }
        feedbackPlayer.Play(weaponData.hammerDropImpactFeedback, controller.transform);
        controller.animator.SetInteger(selectedWeapon.weaponAnim, 3);
        controller.animator.Play("WeaponRecover", 0, 0f); controller.animator.Update(0f);
    }
    private void CancelSpecialAttack()
    {
        if (isStoringSkill)
        {
            isStoringSkill = false;
            SkillStorageFinished?.Invoke(false); // 시작 때 지불한 게이지는 반환하지 않는다.
        }
        if (isHammerDrop) { isHammerDrop = hammerDiving = false; }
        if (controller.animator != null) controller.animator.WriteDefaultValues();
        controller.GravityOn = true;
    }
    private void ClearNormalHammerPresentation()
    {
        if (!IsNormalHammer) return;
        controller.animator.WriteDefaultValues();
        foreach (var box in hammerHitBoxes)
        {
            if (box == null) continue;
            box.DisableHitbox();
            box.gameObject.SetActive(false);
        }
        var arc = controller.model.transform.Find("HammerArc");
        if (arc != null && arc.TryGetComponent<SpriteRenderer>(out var renderer)) renderer.enabled = false;
    }
    private void EndAttack()
    {
        ClearNormalHammerPresentation();
        RestoreExecutionModel();
        meleeComboIndex = 0;
        controller.PlayerCounterState.EndCounter();

        comboQueuedThisStep = false;
        controller.GravityOn = true;
        abilityState = AbilityState.None;
        controller.animator.SetInteger(selectedWeapon.weaponAnim, 0);
        normalWeaponBuffer.Reset();
        secondaryWeaponBuffer.Reset();
    }
}
