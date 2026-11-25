/// <summary>
/// Wave 진행 연출. 기본값(0)은 지금까지의 방식이라 기존 WaveData는 직렬화 기본값 그대로 같은 동작을 한다.
/// 새 연출을 더할 때는 값을 뒤에 추가하고, 그 연출을 맡는 구성요소가 WaveManager 이벤트를 구독해 진행을 넘겨받는다(WaveManager는 고치지 않는다).
/// </summary>
public enum WaveFlowMode
{
    /// <summary>처치하면 다음 Wave로 자동 진행한다. Wave 사이에 지형 패널이 바뀌고 크레인이 Player를 옮긴다.</summary>
    Sequential = 0,
    /// <summary>Wave 하나가 던전 하나다. 방을 돌며 적을 처치하고, 관을 통해 방을 옮긴다. 던전을 클리어하면 크레인이 다음 던전으로 옮긴다.</summary>
    Dungeon = 1,
}
