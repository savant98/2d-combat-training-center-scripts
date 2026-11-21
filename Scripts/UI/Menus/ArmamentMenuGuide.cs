public static class ArmamentMenuGuide
{
    public static string TraitName(PlayerArmamentSO armament) => armament?.armamentId switch
    {
        "Hammer" => "충전 방어막",
        "Rifle" => "탄창 · 화상",
        "Sword" => "필살 스택",
        "Railgun" => "과열 · 충전",
        _ => null
    };

    public static string CardDescription(PlayerArmamentSO armament) => armament?.armamentId switch
    {
        "Hammer" => "해머 피해로 방어막 충전 · 반격 성공 시 100%\n받는 피해를 방어막 양만큼 먼저 막음",
        "Rifle" => $"{PlayerWeaponTraits.MagazineCapacity}발 탄창 · 단발 연사 · 다 쓰거나 {PlayerWeaponTraits.IdleReloadDelay:0.#}초 쉬면 장전\n화상 최대 {EnemyBurn.Capacity}중첩 · 기관총 피해 증가",
        "Sword" => $"돌진 베기 적중으로 필살 최대 {PlayerWeaponTraits.ResolveCapacity}스택\n검기 강화와 일격필살에 사용",
        "Railgun" => $"연속 {PlayerWeaponTraits.RailShotsToOverheat}발에 과열 · 사격 중지 시 냉각\n폭발 후 {PlayerWeaponTraits.OverheatDuration:0}초 동안 무장 복구",
        _ => armament != null ? armament.description : string.Empty
    };

    public static (string summary, string synergy) Gear(WeaponBaseSO weapon) => weapon switch
    {
        CounterWeaponSO => ("방어 자세에서 타격을 받아내고 충격파로 반격합니다.",
            "반격에 성공하면 남은 양과 관계없이 해머 방어막이 100%로 채워집니다."),
        RangedWeaponSO ranged when ranged.projectilePrefab is SpinningHammerShot
            => ("회전하는 해머를 던져 진행 경로의 적을 공격합니다.",
                "멀리 있는 적에게 피해를 줘도 해머 방어막이 충전됩니다."),
        MeleeWeaponSO => ("해머를 내려쳐 지면을 강하게 타격합니다.",
            "타격 피해로 해머 방어막을 충전합니다. 방어막 유지 중에는 충전이 멈춥니다."),
        HandCannonWeaponSO => ("정면의 좁은 부채꼴 범위를 한 번에 공격합니다.",
            "화상에 걸린 적을 맞히면 화상 중첩 수만큼 연속 폭발해 추가 피해를 주고, 그 화상은 모두 꺼집니다."),
        IncendiaryMountWeaponSO => ($"기본 공격을 최대 {PlayerWeaponTraits.IncendiaryCapacity}발의 관통 작열탄으로 바꿉니다. 공격 키를 누르고 있으면 연사하며, 탄을 소진하거나 장착 시간이 끝나면 라이플로 돌아옵니다.",
            $"적중한 적에게 화상을 부여하고, 이미 화상에 걸린 적에게는 폭발 피해를 한 번 더 줍니다. 화상은 최대 {EnemyBurn.Capacity}중첩이며 {EnemyBurn.TickInterval:0.#}초마다 피해를 주고, 마지막 부여 {EnemyBurn.Duration:0}초 뒤부터 {EnemyBurn.Duration:0}초마다 1중첩씩 꺼집니다."),
        MachineGunMountWeaponSO mount => ($"{mount.mountDuration:0.#}초 동안 기본 공격을 무한탄창 기관총으로 바꿉니다.",
            "기관총 탄도 화상을 1중첩씩 쌓고, 화상이 쌓인 적을 쏘면 중첩 수에 비례해 피해가 증가합니다."),
        SwordDashWeaponSO => ("정면으로 돌진하며(돌진 중 무적) 지나온 경로의 적을 여러 번 벱니다.",
            "한 번의 돌진 베기가 한 명 이상에게 적중하면 필살 1스택을 얻습니다."),
        SwordWaveWeaponSO => ("빠르게 높이 뛰어올라(도약 중 무적) 큰 검기를 대각선 아래로 내려칩니다. 검기는 바닥을 따라 미끄러지며 지나는 적에게 3타를 줍니다.",
            "필살 2스택이 있으면 소비해 6타로 강화합니다. 강화 검기가 적중하면 2스택을 돌려받습니다."),
        SwordFinisherWeaponSO => ("화면을 가르는 참격으로 범위 안의 적을 공격합니다.",
            $"필살 {PlayerWeaponTraits.ResolveCapacity}스택이 필요하며 발동할 때 모두 소비합니다. HP 피해를 받을 때마다 필살이 1스택 감소합니다."),
        RailgunChargeWeaponSO => ($"총열을 바로 식혀 과열을 0%로 되돌리고 {PlayerWeaponTraits.ChargeDuration:0}초 동안 레일건 계열 공격을 강화합니다.",
            "기본 사격 뒤 0.2초 간격으로 추가 레일 사격 2발이 이어지고, 별창에는 미니 별창이, 파괴 광선에는 미니 레이저 2발이 뒤따릅니다."),
        StarSpearWeaponSO => ("별창을 소환합니다. 별창은 주변의 적을 찾아 날아가 폭발합니다.",
            "레일건 충전 중 소환하면 별창마다 미니 별창 2발이 0.2초 간격으로 뒤따라 날아갑니다."),
        LaserWeaponSO => ("정면으로 지속 광선을 발사해 적에게 반복 피해를 줍니다.",
            "레일건 충전 상태에서 발사하면 미니 레이저 2발이 0.2초 간격으로 추가 발사됩니다."),
        _ => (string.Empty, string.Empty)
    };
}
