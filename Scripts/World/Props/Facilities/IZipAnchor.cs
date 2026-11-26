using UnityEngine;

/// <summary>
/// 붙기(FacilityZip) 대상(DESIGN_V4 §2.2 · 5차 DESIGN_V5 §1 크레인) — 경로 크레인(PropCraneHook)·고정 크레인(PropCraneAnchor)이 구현한다.
/// 5차: 경로 크레인의 빈 도크(호출 지점)도 이 계약으로 목록에 오른다(IZipCallTarget을 함께 구현 — 6차 T5: 고르면 크레인이 그 도크에 즉시 생기고 같은 프레임에 붙는다).
/// 살아 있는 대상은 FacilityZip.Register/Unregister로 목록에 올린다(물리 질의 없이 고른다 — FacilityInteractPoint.Live 선례).
/// 좌표는 모두 월드. '매달린 발' = 매달린 Player 몸통 바닥 가운데(배치 기준 (x + 1, y)).
/// </summary>
public interface IZipAnchor
{
    /// <summary>지금 붙기 대상이 될 수 있는지(살아 있음·쉬는 자리(경로 크레인은 쉬는 도크)에서 대기·움직이는 중 아님·다른 Player를 태우고 있지 않음·사용 시점 허락).</summary>
    bool ZipAvailable { get; }
    /// <summary>매달린 Player 몸통 바닥 가운데(월드). 붙기는 몸 바닥을 이 점으로 옮긴다.</summary>
    Vector2 ZipFeet { get; }
    /// <summary>붙기 반경(칸, 배치 설정 zipRadius). 기준 = Player 몸 중심 ↔ 매달림 몸 중심.</summary>
    float ZipRadius { get; }
    /// <summary>와이어가 나가는 훅 자리(월드, 표시용).</summary>
    Vector2 ZipHookPoint { get; }
    /// <summary>이 대상에 player가 매달려 있는지(탑승 중).</summary>
    bool HasRider(PlayerController player);
    /// <summary>붙기 대상으로 '선택'됐는지 알린다(키 안내·직선 표시·빠른 비컨·강조). 값이 바뀔 때만 부른다.</summary>
    void SetZipFocus(bool focused);
    /// <summary>붙기 이동을 시작했다(훅에서 Player로 와이어를 뻗고 출발 연출을 낸다).</summary>
    void BeginZipIn(PlayerController player);
    /// <summary>붙기 이동이 끝났다(arrived면 도착 효과·타격감, 아니면 와이어만 거둔다).</summary>
    void EndZipIn(bool arrived);
    /// <summary>도착 인계: 이미 구속된 Player를 이 시설의 탑승으로 넘겨받는다(FacilityRide.BeginHandoff). 실패하면 false(아무것도 남기지 않음).</summary>
    bool AcceptZip(PlayerController player);
    /// <summary>연쇄 붙기: 이 대상의 탑승을 끝낸다(구속은 붙기 보유자가 유지 — 몸을 옮기거나 떨어뜨리지 않는다).</summary>
    void ReleaseForZip(PlayerController player);
}

/// <summary>
/// 경로 크레인의 빈 도크(호출 지점). 6차 T5(DESIGN_V6 — 5차 호출 B안 대체): FacilityZip이 확정한 대상이 이 계약이면 Call로 크레인을 그 도크에
/// 즉시 생성하고(옛 자리 파괴·새 자리 생성 연출은 크레인이 낸다) 같은 프레임에 Owner로 붙기를 시작한다. 붙기가 막히면 크레인은 그 도크에서 기다린다.
/// 6차 T3: Owner는 '방금 내린 크레인의 가족'(그 크레인 + 그 도크 호출 지점) 판정에도 쓴다.
/// </summary>
public interface IZipCallTarget
{
    /// <summary>크레인을 이 도크에 즉시 생성한다(같은 호출 안에서 크레인이 그 도크에서 쉬는 상태가 된다). 고를 수 없는 도크면 false(아무것도 하지 않는다).</summary>
    bool Call(PlayerController player);
    /// <summary>이 호출 지점을 가진 크레인(생성 뒤 붙는 대상·가족 판정).</summary>
    IZipAnchor Owner { get; }
}
