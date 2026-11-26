// 던전 방의 함정이 방 클리어 시점에 멈추는 계약(6차 T9).
// - DungeonRoomStage가 방에 세운 Prop·Wave 장치에서 이 계약을 모은다.
// - halted = 그 방이 클리어됨: 같은 호출 안에서 피해 판정을 즉시 끄고, 외형은 정지 자세(불 꺼짐·공격 모양 아님·안 움직임)로 간다.
// - instant = 방에 들어올 때 이미 클리어 상태라 정지 자세로 바로 들어간다(재입장 순찰 중에도 정지 유지).
// - 신호를 한 번도 받지 않으면(던전 밖 일반 Wave) 늘 작동한다.
public interface IDungeonRoomHazard
{
    void SetHazardHalted(bool halted, bool instant);
}
