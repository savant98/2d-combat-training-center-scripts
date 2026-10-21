using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
public class PlayerWeaponAbility : PlayerAbility
{
    public override PlayerActionType ActionType => PlayerActionType.Attack;
    public override bool IsActive => abilityState != AbilityState.None;
    public AbilityState AttackPhase => abilityState;

    [Header("Melee Hitboxes")]
    [SerializeField] private string hammerWeaponName = "Hammer";
    [SerializeField] private string hammerSkillName = "HammerSkill";
    [SerializeField] private MeleeHitbox[] hammerHitBoxes;
    [SerializeField] private MeleeHitbox[] hammerSkillBoxes;
    [SerializeField] private GameObject[] weaponModels;

    [Header("Skill Gauge Orb")]
    [SerializeField] private Transform skillGaugeOrbTarget;

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

    public override void Initialize(AbilityManager manager)
    {
        base.Initialize(manager);

        weaponData = (PlayerWeaponDataSO)data;
        maxSkillGauge = weaponData.maxSkillGauge;
        weaponSwapCoolTime = weaponData.swapCoolTime;

        normalWeaponBuffer.SetBufferTime(data.inputBufferTime);
        secondaryWeaponBuffer.SetBufferTime(data.inputBufferTime);

        controller.OnNormalWeapon += () => normalWeaponBuffer.RegisterInput();
        controller.OnSecondaryWeapon += () => secondaryWeaponBuffer.RegisterInput();
        controller.OnSwapWeaponSet += SwapWeaponSet;

        // 다음 단계에서 PlayerController에 추가할 이벤트
        controller.OnGearPressed += HandleGearPressed;
        controller.OnGearReleased += HandleGearReleased;

        ApplyWeaponSet(0, true);

        selectedWeapon = normalWeapon;
        skillGauge = 0;

        CancelGear();

        if (gearUI != null)
        {
            gearUI.SetGearUI(false, 0, 0f, false);
        }
    }

    public void ResetSkillGauge()
    {
        skillGauge = 0;
        UIManager.Instance.UpdateSkillGauge(0f, maxSkillGauge, weaponData.weaponSets[currentSetIndex]);
    }
    private void TryAttack()
    {
        if (!CanDo())
            return;

        // Gear 상태에서는 Normal 입력을 기본공격 버퍼로 남기면 안 된다.
        if (gearState != GearState.Inactive)
        {
            if (normalWeaponBuffer.Use())
            {
                normalWeaponBuffer.Clear();
                TryStartGearWeapon();
            }

            if (secondaryWeaponBuffer.Use())
            {
                secondaryWeaponBuffer.Clear();
                StartSecondaryWeapon();
            }

            return;
        }

        if (normalWeaponBuffer.Use())
        {
            StartNormalWeapon();
            return;
        }

        if (secondaryWeaponBuffer.Use())
        {
            StartSecondaryWeapon();
        }
    }
    private void TryQueueComboInput()
    {
        if (comboQueuedThisStep) // 이미 예약됐으면 금지
            return;

        if (selectedWeapon is not MeleeWeaponSO melee)
            return;

        // normal 입력만 콤보로 처리
        if (!normalWeaponBuffer.Use())
            return;

        // 콤보 입력 허용 구간
        if (!IsInComboQueueWindow(melee))
            return;

        comboQueuedThisStep = true;
    }
    private bool IsInComboQueueWindow(MeleeWeaponSO melee)
    {
        // Recover에서는 항상 예약 허용
        if (abilityState == AbilityState.Recover)
            return true;

        // Execute에서는 끝나기 직전 comboQueueWindow에만 예약 허용
        if (abilityState == AbilityState.Execute)
        {
            normalWeaponBuffer.Clear();
            return timer <= comboQueueWindow;
        }

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
            normalWeaponBuffer.Clear();
            StartWindup();
            return true;
        }

        return false;
    }
    private void StartNormalWeapon()
    {
        normalWeaponBuffer.Clear();

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

        UIManager.Instance.UpdateSkillGauge(skillGauge, maxSkillGauge, weaponData.weaponSets[currentSetIndex]);

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

        UIManager.Instance.UpdateSkillGauge(
            skillGauge,
            maxSkillGauge,
            weaponData.weaponSets[currentSetIndex]);

        CancelGear();
        StartWindup();
    }

    private void StartSecondaryWeapon()
    {
        selectedWeapon = secondaryWeapon;

        if (skillGauge < secondaryWeapon.skillGauge)
        {
            // 무기 스킬 게이지 없을때 사용 피드백 필요
            return;
        }

        if (!IsWeaponCooldownReady(secondaryWeapon))
            return;

        skillGauge -= secondaryWeapon.skillGauge;
        MarkWeaponUsed(secondaryWeapon);

        UIManager.Instance.UpdateSkillGauge(skillGauge, maxSkillGauge, weaponData.weaponSets[currentSetIndex]);

        CancelGear();
        StartWindup();
    }
    public override void Tick()
    {
        if (abilityState != AbilityState.None)
        {
            TryQueueComboInput();
            timer -= Time.deltaTime;

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
        if (!firstInit)
        {
            UnBindWeaponEvents();
        }

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

        normalWeaponBuffer.Clear();
        secondaryWeaponBuffer.Clear();

        CancelGear();

        foreach (GameObject weaponModel in weaponModels)
        {
            weaponModel.SetActive(false);
        }

        weaponModels[currentSetIndex].SetActive(true);

        UIManager.Instance.StartWeaponSwap(set, skillGauge);

        if (!firstInit)
            FeedbackPlayer.Instance.Play(weaponData.swapFeedback, transform);
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

    private void UnBindWeaponEvents()
    {
        var set = weaponData.weaponSets[currentSetIndex];

        UnBindWeaponEvent(set.normal);
        UnBindWeaponEvent(set.gearStage1);
        UnBindWeaponEvent(set.gearStage2);
        UnBindWeaponEvent(set.gearStage3);
        UnBindWeaponEvent(set.secondary);
    }

    private void BindWeaponEvent(WeaponBaseSO weapon)
    {
        if (weapon == null)
            return;

        weapon.OnHitEnemy -= OnWeaponHitEnemy;
        weapon.OnHitEnemy += OnWeaponHitEnemy;
    }

    private void UnBindWeaponEvent(WeaponBaseSO weapon)
    {
        if (weapon == null)
            return;

        weapon.OnHitEnemy -= OnWeaponHitEnemy;
    }
    #endregion

    #region OnWeaponHit
    private void OnWeaponHitEnemy(CharacterController2D owner, Vector2 hitPoint)
    {
        if (owner != controller)
            return;

        if (selectedWeapon.skillGainPerHit <= 0)
            return;

        SpawnSkillGaugeOrb(hitPoint, selectedWeapon.skillGainPerHit);
    }
    private void SpawnSkillGaugeOrb(Vector2 hitPoint, float gainAmount)
    {
        SkillGaugeOrbSettings settings = weaponData.skillGaugeOrbSettings;

        if (settings == null || settings.orbPrefab == null)
        {
            AddSkillGauge(gainAmount);
            return;
        }

        Transform target = skillGaugeOrbTarget != null
            ? skillGaugeOrbTarget
            : controller.transform;

        float gaugePerOrb = Mathf.Max(0.0001f, settings.gaugePerOrb);
        int orbCount = Mathf.Max(1, Mathf.CeilToInt(gainAmount / gaugePerOrb));

        float remainingGain = gainAmount;

        for (int i = 0; i < orbCount; i++)
        {
            float orbGain = Mathf.Min(gaugePerOrb, remainingGain);
            remainingGain -= orbGain;

            SkillGaugeOrb orb = WorldEntityPool.Instance.Spawn(
                settings.orbPrefab,
                hitPoint,
                Quaternion.identity);

            orb.Play(hitPoint, target, orbGain, settings, AddSkillGauge);
        }
    }
    private void AddSkillGauge(float amount)
    {
        skillGauge += amount;
        skillGauge = Mathf.Min(skillGauge, maxSkillGauge);

        UIManager.Instance.UpdateSkillGauge(
            skillGauge,
            maxSkillGauge,
            weaponData.weaponSets[currentSetIndex]);
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
            CancelGear();
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
            CancelGear();
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
            CancelGear();
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
        CancelGear();
    }

    private void CancelGear()
    {
        gearState = GearState.Inactive;
        gearStage = 0;
        chargingTargetStage = 0;
        gearTimer = 0f;
        isGearHeld = false;

        // Gear 상태에서 눌렀던 Normal 입력이
        // Gear 해제 후 기본공격으로 발동되는 것을 방지
        normalWeaponBuffer.Clear();

        UpdateGearUI();
    }

    private void UpdateGearUI()
    {
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
    public override void Interrupt()
    {
        if (abilityState == AbilityState.None)
        {
            CancelGear();
            return;
        }

        controller.PlayerCounterState.EndCounter();

        comboQueuedThisStep = false;
        meleeComboIndex = 0; // 혹은 유지 정책

        normalWeaponBuffer.Clear();
        secondaryWeaponBuffer.Clear();

        CancelGear();

        //StopAllCoroutines();
        controller.GravityOn = true;
        abilityState = AbilityState.None;

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 0);
    }
    private void StartWindup()
    {
        // 공격 방향 결정
        int inputDir = (int)controller.MoveInput.x;
        if (inputDir != 0)
        {
            controller.FacingUpdate(inputDir);
        }

        abilityState = AbilityState.Windup;
        comboQueuedThisStep = false;

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
        }

        feedbackPlayer.Play(selectedWeapon.windupFeedback, transform);
        controller.GravityOn = false;
        controller.Velocity = Vector2.zero;

        controller.animator.SetInteger(selectedWeapon.weaponAnim, 1);
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
    private void EndAttack()
    {
        controller.PlayerCounterState.EndCounter();

        comboQueuedThisStep = false;
        controller.GravityOn = true;
        abilityState = AbilityState.None;
        controller.animator.SetInteger(selectedWeapon.weaponAnim, 0);
    }
}
