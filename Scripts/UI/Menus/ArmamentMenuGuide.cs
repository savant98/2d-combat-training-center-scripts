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
        "Hammer" => "해머 피해로 방어막 충전\n활성 중 받는 피해의 50% 흡수",
        "Rifle" => $"{PlayerWeaponTraits.MagazineCapacity}발 탄창 · {PlayerWeaponTraits.ReloadDuration:0}초 장전\n화상 최대 {EnemyBurn.Capacity}중첩 · 기관총 피해 증가",
        "Sword" => $"돌진 베기 적중으로 필살 최대 {PlayerWeaponTraits.ResolveCapacity}스택\n검기 강화와 일격필살에 사용",
        "Railgun" => $"사격으로 과열 · 사격 중지 시 냉각\n폭발 후 {PlayerWeaponTraits.OverheatDuration:0}초 동안 무장 복구",
        _ => armament != null ? armament.description : string.Empty
    };

    public static (string summary, string synergy) Gear(WeaponBaseSO weapon) => weapon switch
    {
        CounterWeaponSO => ("방어 자세에서 타격을 받아내고 충격파로 반격합니다.",
            "반격으로 피해를 주면 해머 방어막이 충전됩니다."),
        RangedWeaponSO ranged when ranged.projectilePrefab is SpinningHammerShot
            => ("회전하는 해머를 던져 진행 경로의 적을 공격합니다.",
                "멀리 있는 적에게 피해를 줘도 해머 방어막이 충전됩니다."),
        MeleeWeaponSO => ("해머를 내려쳐 지면을 강하게 타격합니다.",
            "타격 피해로 해머 방어막을 충전합니다. 방어막 유지 중에는 충전이 멈춥니다."),
        HandCannonWeaponSO => ("정면의 좁은 부채꼴 범위를 한 번에 공격합니다.",
            $"피해를 준 적에게 화상을 부여합니다. 최대 {EnemyBurn.Capacity}중첩이며 0.25초마다 피해를 줍니다."),
        IncendiaryMountWeaponSO => ($"기본 공격을 최대 {PlayerWeaponTraits.IncendiaryCapacity}발의 관통 작열탄으로 바꿉니다. 공격 입력마다 한 발씩 발사합니다.",
            "기본 피해에 작열 피해를 더하고 화상을 부여합니다. 탄을 소진하거나 장착 시간이 끝나면 라이플로 돌아옵니다."),
        MachineGunMountWeaponSO mount => ($"{mount.mountDuration:0.#}초 동안 기본 공격을 무한탄창 기관총으로 바꿉니다.",
            "화상이 쌓인 적을 쏘면 중첩 수에 비례해 피해가 증가합니다."),
        SwordDashWeaponSO => ("정면으로 돌진하며 지나온 경로의 적을 여러 번 벱니다.",
            "한 번의 돌진 베기가 한 명 이상에게 적중하면 필살 1스택을 얻습니다."),
        SwordWaveWeaponSO => ("공중으로 뛰어올라 대각선 아래로 검기를 발사합니다. 한 번 맞힌 대상에게 3타를 줍니다.",
            "필살 2스택이 있으면 소비해 6타로 강화합니다. 강화 검기가 적중하면 2스택을 돌려받습니다."),
        SwordFinisherWeaponSO => ("화면을 가르는 참격으로 범위 안의 적을 공격합니다.",
            $"필살 {PlayerWeaponTraits.ResolveCapacity}스택이 필요하며 발동할 때 모두 소비합니다. HP 피해를 받을 때마다 필살이 1스택 감소합니다."),
        RailgunChargeWeaponSO => ("과열을 30%p 올리고 5초 동안 레일건 계열 공격을 강화합니다.",
            "기본 사격과 별창 직접 적중은 추격 2타를 얻습니다. 파괴 광선에는 반복 X자 연출과 추가 2타가 붙습니다."),
        StarSpearWeaponSO => ("별창을 소환합니다. 별창은 주변의 적을 찾아 날아가 폭발합니다.",
            "레일건 충전 중 소환하면 별창을 직접 맞은 적에게 추격 2타가 들어갑니다."),
        LaserWeaponSO => ("정면으로 지속 광선을 발사해 적에게 반복 피해를 줍니다.",
            "레일건 충전 상태에서 발사하면 X자 광선이 겹치고, 적중한 대상마다 추가 2타가 들어갑니다."),
        _ => (string.Empty, string.Empty)
    };
}
