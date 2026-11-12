// 로딩 표시(TrainingRoomLoadingPanel)가 진행 상황을 읽어 가는 대상.
// TrainingRoom 입장 로딩(TrainingRoomHost)과 씬 전환 로딩(SceneLoadingSequence)이 구현한다.
public interface ILoadingSource
{
    /// <summary>로딩이 진행 중인지. 끝나면 표시가 스스로 사라진다.</summary>
    bool IsLoading { get; }
    /// <summary>진행률(0~1).</summary>
    float LoadingProgress { get; }
    /// <summary>지금 하고 있는 단계의 이름(TERRAIN·BACKDROP·UNITS·SCENE). 표시가 화면에 적을 문구로 바꾼다.</summary>
    string LoadingStep { get; }
}
