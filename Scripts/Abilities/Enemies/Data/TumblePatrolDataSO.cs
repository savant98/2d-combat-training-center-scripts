using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Tumble Patrol")]
public class TumblePatrolDataSO : EnemyAbilityDataSO
{
    [Header("Tumble")]
    [Tooltip("넘어가는 정사각형 몸체의 한 변(칸)입니다. 한 번 넘어갈 때마다 이만큼 이동하므로 외형·충돌 상자와 같은 크기로 둡니다.")]
    [Min(0.1f)] public float tumbleSize = 1.7f;
    [Tooltip("바닥에 놓인 상태에서 앞쪽 모서리를 축으로 몸체를 들어 올려 꼭대기(45도)에 닿는 시간(초)입니다.")]
    [Min(0.05f)] public float liftDuration = 0.36f;
    [Tooltip("꼭대기(45도)를 넘을 때의 회전 속도(도/초)입니다. 작을수록 모서리 위에서 오래 버티다 넘어갑니다.")]
    [Min(1f)] public float topAngularSpeed = 90f;
    [Tooltip("꼭대기를 넘은 뒤 떨어질 때 쓰는 중력 가속도(칸/초²)입니다. 모서리 축 각가속도는 3g / (2√2 × 한 변) × sin(각도 - 45도)입니다.")]
    [Min(0.1f)] public float tumbleGravity = 30f;

    [Header("Landing")]
    [Tooltip("새 면이 바닥에 닿은 직후 같은 모서리를 축으로 뒤로 젖혀졌다 돌아오는 각도(도)입니다. 0이면 반동이 없습니다.")]
    [Range(0f, 10f)] public float landingBounceAngle = 2.5f;
    [Tooltip("착지 반동이 끝나는 시간(초)입니다.")]
    [Min(0.01f)] public float landingBounceDuration = 0.14f;
    [Tooltip("착지한 뒤 다음으로 넘어가기 전까지 쉬는 시간(초)입니다. 다른 패턴으로 넘어가는 판단도 이때만 받습니다.")]
    [Min(0f)] public float restDuration = 0.2f;

    [Header("Wall")]
    [Tooltip("진행 방향의 벽까지 한 칸이 남지 않으면 넘어가지 않고 벽 쪽으로 기울었다 돌아온 뒤 방향을 바꿉니다. 이때 기우는 최대 각도(도)입니다. 모서리가 벽에 먼저 닿으면 그 각도까지만 기웁니다.")]
    [Range(0f, 44f)] public float maxBlockedTiltAngle = 30f;
    [Tooltip("벽 쪽으로 기우는 시간(초)입니다. 기우는 각도가 작으면 비례해 짧아집니다.")]
    [Min(0.02f)] public float blockedTiltDuration = 0.22f;

    [Header("Direction")]
    [Tooltip("플레이어를 발견해 다른 패턴(Jumper는 점프)으로 넘어갔다가 순찰로 돌아올 때 플레이어 쪽으로 출발합니다. 끄면 마지막으로 바라본 방향으로 이어 갑니다.")]
    public bool facePlayerAfterOtherPattern = true;
    [Tooltip("새 면으로 이동한 직후 플레이어 방향을 한 번 기록하고 다음 면 이동에 적용합니다.")]
    public bool facePlayerAfterEachStep = false;
    [Tooltip("순찰을 시작한 뒤 다른 패턴에 자리를 넘기기 전에 끝까지 넘어가야 하는 최소 횟수입니다. 점프 직후 다시 점프하지 않고 플레이어 쪽으로 한 번 이상 굴러간 뒤 공격합니다.")]
    [Min(0)] public int minStepsBeforeYield = 1;
}
