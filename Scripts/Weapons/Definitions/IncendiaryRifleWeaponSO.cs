using UnityEngine;

// 작열탄 발사(Rifle Gear 2 '작열탄 발사' 장착 중의 기본 공격). 탄(IncendiaryShot)이 맞기 전 화상 1스택 이상이던 적을 맞히면 그 자리에서 범위 폭발이 난다.
// 2026-11-21 사용자 지시 4차 "작열탄의 추가 폭발이 크기가 별창폭발의 폭발범위만큼 커지고, 추가 폭발을 맞으면 그 대상도 화상스택이 올라가도록":
// - blastRadius: 폭발 반경(칸). 별창(StarSpear.asset)의 explosionRadius와 같게 생성 도구(tools/Unity/WeaponTraitIncendiarySetup.cs)가 맞춘다.
//   폭발 연출(BurnExplosionLarge)도 같은 도구가 이 반경으로 만들므로, Inspector에서 이 값만 바꾸면 판정과 연출 크기가 어긋난다(도구를 다시 실행할 것).
// - blastDamageScale: 폭발 피해 = 탄 피해 × 이 배율(기본 1 = 기존 '기본 피해만큼의 폭발 피해'). 반경 안의 적마다 한 번씩 준다.
public sealed class IncendiaryRifleWeaponSO : RangedWeaponSO
{
    public const float DefaultBlastRadius = 3.75f;   // 별창 폭발 반경(Player 충돌체 지름 1.5칸 × 2.5)

    [Header("Incendiary Blast")]
    [Min(0f)] public float blastRadius = DefaultBlastRadius;
    [Min(0f)] public float blastDamageScale = 1f;
}
