using UnityEngine;

// 웨이브 데이터에 넣으면 생성될 때 현재 방에서 같은 키의 훈련 구간을 열고, 웨이브가 끝나 회수될 때 닫는다.
// 웨이브가 비필수 월드 오브젝트를 정리하는 기존 수명(ForceReturnToPool)에 구간의 시작과 끝을 맞춘다.
public sealed class TrainingCourseActivator : WorldEntityBase
{
    [SerializeField, Tooltip("열 구간의 키입니다. 현재 방에서 같은 CourseKey를 가진 TrainingCourseStage를 찾습니다.")]
    private string courseKey = "Training_Climb";
    [SerializeField, Tooltip("코스를 열기 전에 플레이어를 시작 지점으로 옮깁니다.")]
    private bool resetPlayerToStart;
    [SerializeField, Tooltip("코스 완료 시 낙하를 기다리지 않고 지형을 닫기 전에 시작 지점으로 돌려보냅니다.")]
    private bool returnPlayerOnFinish;

    private TrainingCourseStage stage;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();
        var room = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
        stage = null;
        if (room != null)
            foreach (var candidate in room.GetComponentsInChildren<TrainingCourseStage>(true))
                if (candidate.CourseKey == courseKey) { stage = candidate; break; }
        if (stage == null)
        {
            Debug.LogWarning($"현재 방에 {courseKey} 구간(TrainingCourseStage)이 없어 열지 않습니다.", this);
            return;
        }
        stage.Begin(resetPlayerToStart && (room.WaveManager == null || !room.WaveManager.IsPreparingWave), returnPlayerOnFinish);
    }

    public override void ForceReturnToPool()
    {
        CloseStage();
        base.ForceReturnToPool();
    }

    private void OnDisable() => CloseStage();

    private void CloseStage()
    {
        if (stage != null) stage.End();
        stage = null;
    }
}
