using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 훈련실의 특수 구간(등반·달리기) 동안 지형 조각들을 옮겨 길을 열고 구간 전체를 담는 카메라로 전환한다.
// 조각마다 시작 시차와 잠금 해제 동작(반대쪽으로 살짝 물러났다가 움직임)을 줄 수 있어, 벽이 기계처럼 열리는 연출을 만든다.
// 목표 지점이 도달 키를 보내면 레이저·길 안내·코스 적(CourseEnemySpawner)을 거두고, 발판을 없앤 뒤
// 플레이어가 바닥에 내려오면(또는 시작 위치로 돌려보내거나, 그 자리에서 다음 구간을 연 뒤) 완료 키로 웨이브를 끝낸다.
// 웨이브를 처음부터 다시 시작하거나 방을 떠나면 옮긴 지형·카메라 범위·장식·위치를 원래대로 되돌린다.
// 지형은 기존 Platform 레이어의 Tilemap이며, 플레이어 이동과 충돌은 기존 RaycastMotor2D 경로를 그대로 쓴다.
[DisallowMultipleComponent]
public sealed class TrainingCourseStage : MonoBehaviour
{
    public enum FinishMode { FallToFloor, ReturnToStart, HandOff }
    public enum FinishStep { None, Success, Vanishing, Falling, ForcedReturn, HandingOff, Completing }

    [Serializable]
    private struct Segment
    {
        [Tooltip("옮길 Tilemap입니다. Kinematic Rigidbody2D와 함께 둡니다.")]
        public Tilemap tilemap;
        [Tooltip("길을 열 때 옮기는 거리(칸)입니다.")]
        public Vector3 openOffset;
        [Min(0f), Tooltip("열기 시작한 뒤 이 조각이 움직이기 전까지의 시차(초)입니다. 닫을 때는 순서를 거꾸로 씁니다.")]
        public float delay;
        [Tooltip("움직이기 시작할 때 조각 소리를 냅니다. 함께 움직이는 조각끼리 소리가 겹치지 않게 한 조각만 켭니다.")]
        public bool playSound;
    }

    [Header("Course")]
    [SerializeField, Tooltip("웨이브 데이터의 사용자 트리거 키입니다. 마무리가 끝나면 이 키로 웨이브를 완료합니다.")]
    private string courseKey = "Training_Climb";
    [SerializeField, Tooltip("목표 지점(TutorialGoal)이 보내는 도달 키입니다. 받으면 마무리 순서를 시작합니다.")]
    private string goalKey = "Training_Climb_Goal";
    [SerializeField, Tooltip("구간의 진행 방향입니다. 발판을 차례로 없앨 때 이 방향으로 먼 발판부터 없앱니다.")]
    private Vector2 courseDirection = Vector2.up;

    [Header("Terrain")]
    [SerializeField] private Segment[] segments = new Segment[0];
    [SerializeField, Min(0f), Tooltip("조각 하나가 열리는 데 걸리는 시간(초)입니다.")]
    private float openDuration = 1.6f;
    [SerializeField, Min(0f), Tooltip("조각 하나가 닫히는 데 걸리는 시간(초)입니다.")]
    private float closeDuration = 1.2f;
    [SerializeField, Min(0f), Tooltip("열기 전에 반대쪽으로 살짝 물러나는 거리(칸)입니다. 잠금이 풀리는 기계 느낌을 냅니다. 0이면 바로 움직입니다.")]
    private float unlockDistance = 0f;
    [SerializeField, Min(0f)] private float unlockDuration = .15f;
    [SerializeField, Tooltip("playSound가 켜진 조각이 움직이기 시작할 때 낼 소리입니다(유압이 빠지는 소리 등).")]
    private SoundOptions segmentSound;
    [SerializeField, Tooltip("열거나 닫기 시작할 때 한 번 내는 구동음입니다(유압 펌프 등).")]
    private SoundOptions driveSound;

    [Header("Camera")]
    [SerializeField, Tooltip("구간 전체를 담는 Confiner를 가진 카메라입니다. 평소에는 꺼 둡니다.")]
    private CinemachineCamera courseCamera;
    [SerializeField, Tooltip("방 카메라(활성 20)보다 높아야 합니다.")]
    private int activePriority = 30;
    [SerializeField, Tooltip("길이 열리는 동안 열리는 자리를 비추는 고정 카메라입니다. 비워 두면 쓰지 않습니다.")]
    private CinemachineCamera revealCamera;
    [SerializeField] private int revealPriority = 40;
    [SerializeField, Min(0f), Tooltip("열리는 자리로 카메라가 옮겨 간 뒤 열기 시작하기까지의 시간(초)입니다.")]
    private float revealLead = 1f;
    [SerializeField, Min(0f), Tooltip("다 열린 뒤 카메라를 플레이어에게 돌려주기까지의 시간(초)입니다.")]
    private float revealHold = .5f;

    [Header("Backdrop")]
    [SerializeField, Tooltip("통창·외곽선·후경 띠를 구간 끝까지 늘리는 대상 묶음입니다.")]
    private TrainingCourseStretch stretch;
    [SerializeField, Tooltip("켜면 지형이 움직이는 만큼 함께 늘립니다. 끄면 열기 시작할 때 한 번에 늘리고 다 닫힌 뒤 되돌립니다.")]
    private bool stretchFollowsTerrain = true;
    [SerializeField, Tooltip("켜면 장식의 끝 쪽 대신 시작 쪽을 옮깁니다. 구간 끝에 원래 방 크기의 공간을 만들 때 씁니다.")]
    private bool stretchStartSide;

    [Header("Room Camera")]
    [SerializeField, Tooltip("이 구간이 열려 있는 동안 범위를 바꿀 방 기본 카메라의 Confiner입니다. 보스 등장 카메라를 그대로 쓰도록 카메라를 바꾸지 않고 범위만 바꿉니다.")]
    private CinemachineConfiner2D roomConfiner;
    [SerializeField] private Collider2D roomBounds;

    [Header("Relocate")]
    [SerializeField, Tooltip("이 구간이 열려 있는 동안 relocateOffset만큼 옮길 오브젝트입니다(클리어 포털 등).")]
    private Transform[] relocateTargets = new Transform[0];
    [SerializeField, Tooltip("옮기는 오브젝트의 자식이지만 월드 위치는 그대로 둘 오브젝트입니다(포털의 도착 지점 등).")]
    private Transform[] keepInPlace = new Transform[0];
    [SerializeField] private Vector3 relocateOffset;

    [Header("Finish")]
    [SerializeField, Tooltip("FallToFloor: 발판이 사라지면 바닥으로 떨어지게 둡니다. ReturnToStart: 발판이 사라진 뒤 시작 위치로 돌려보냅니다. HandOff: 그 자리에서 handoffStage를 연 뒤 웨이브를 끝냅니다.")]
    private FinishMode finishMode = FinishMode.FallToFloor;
    [SerializeField, Tooltip("HandOff에서 이어서 열 구간입니다. 넘겨준 뒤에는 이 구간의 지형·장식을 그대로 두고, 웨이브 초기화·방 이탈 때 함께 되돌립니다.")]
    private TrainingCourseStage handoffStage;
    [SerializeField, Min(0f), Tooltip("목표에 닿은 뒤 발판을 없애기 시작하기까지의 간격(초)입니다.")]
    private float successPause = .6f;
    [SerializeField, Min(0f), Tooltip("발판 하나씩 사라지는 간격(초)입니다. 0이면 한꺼번에 사라집니다.")]
    private float vanishInterval = .3f;
    [SerializeField, Min(0f), Tooltip("FallToFloor에서 바닥 착지를 기다리는 최대 시간(초)입니다. 넘으면 시작 위치로 강제로 옮깁니다.")]
    private float landingTimeout = 4f;
    [SerializeField, Tooltip("착지로 볼 바닥 윗면 높이(월드)입니다.")]
    private float floorHeight = -3f;
    [SerializeField, Min(0f), Tooltip("착지·강제 이동과 다음 단계 사이의 간격(초)입니다.")]
    private float settlePause = .6f;

    [Header("Player Return")]
    [SerializeField, Tooltip("강제로 돌려보내거나 지형이 닫히기 전에 막힌 자리에 남은 플레이어를 옮길 위치입니다.")]
    private Transform playerReturnPoint;
    [SerializeField] private string playerTransitionTriggerName = "TitleTransition";
    [SerializeField, Tooltip("닫을 때 막히는 쪽에 남은 플레이어를 시작 위치로 옮깁니다.")]
    private bool returnPlayerOnClose = true;
    [SerializeField, Tooltip("지형이 움직이는 동안 플레이어를 그 자리에 고정합니다. 벽이 세워지는 틈으로 빠져나가지 못하게 합니다.")]
    private bool holdPlayerWhileOpening;
    [SerializeField, Tooltip("다 열린 뒤 플레이어가 roomBounds 밖에 있으면 playerReturnPoint(공간 안)로 옮깁니다.")]
    private bool keepPlayerInsideBounds;

    private Rigidbody2D[] bodies = new Rigidbody2D[0];
    private Vector3[] restLocal = new Vector3[0];
    private Coroutine[] motions = new Coroutine[0];
    private bool cached;
    private Coroutine openRoutine;
    private Coroutine finishRoutine;
    private WaveManager subscribedWaveManager;
    private Collider2D originalRoomBounds;
    private bool boundsSwapped;
    private Vector3[] keptWorld = new Vector3[0];
    private bool relocated;
    private bool holdingPlayer;

    public string CourseKey => courseKey;
    public bool IsOpen { get; private set; }
    public bool HandedOff { get; private set; }
    public bool IsRelocated => relocated;
    public bool IsHoldingPlayer => holdingPlayer;
    public Collider2D RoomBounds => roomBounds;
    public bool IsMoving => openRoutine != null || Array.Exists(motions, m => m != null);
    public bool IsRevealing => revealCamera != null && revealCamera.enabled;
    public FinishStep CurrentStep { get; private set; }
    public bool IsFinishing => finishRoutine != null;
    public int SegmentCount => segments.Length;
    public Vector3 SegmentOffset(int index) => cached && segments[index].tilemap != null ? segments[index].tilemap.transform.localPosition - restLocal[index] : Vector3.zero;
    public Vector3 SegmentOpenOffset(int index) => segments[index].openOffset;
    public Tilemap SegmentTilemap(int index) => segments[index].tilemap;
    public Vector3 TerrainOffset => segments.Length > 0 ? SegmentOffset(0) : Vector3.zero;
    public float OpenProgress
    {
        get
        {
            if (segments.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < segments.Length; i++)
            {
                var open = segments[i].openOffset;
                sum += open.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(SegmentOffset(i), open) / open.sqrMagnitude) : 0f;
            }
            return sum / segments.Length;
        }
    }
    public CinemachineCamera CourseCamera => courseCamera;
    public CinemachineCamera RevealCamera => revealCamera;
    public Transform PlayerReturnPoint => playerReturnPoint;
    public float FloorHeight => floorHeight;

    private void Awake() => Cache();

    // 방이 켜져 있는 동안 웨이브의 도달 키와 초기화를 계속 듣는다.
    private void OnEnable() => Subscribe();

    private void Cache()
    {
        if (cached) return;
        bodies = new Rigidbody2D[segments.Length];
        restLocal = new Vector3[segments.Length];
        motions = new Coroutine[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            var tilemap = segments[i].tilemap;
            if (tilemap == null) continue;
            restLocal[i] = tilemap.transform.localPosition;
            // 닫힐 때 막히는 경계를 실제 타일 범위로 계산하도록 범위를 맞춘다.
            tilemap.CompressBounds();
            bodies[i] = tilemap.GetComponent<Rigidbody2D>();
        }
        cached = true;
    }

    public void Begin()
    {
        Cache();
        IsOpen = true;
        HandedOff = false;
        CurrentStep = FinishStep.None;
        SwapRoomBounds(true);
        Relocate(true);
        if (courseCamera != null)
        {
            if (GameManager.Instance != null && GameManager.Instance.Player != null)
                courseCamera.Follow = GameManager.Instance.Player.transform;
            courseCamera.Priority = activePriority;
            courseCamera.enabled = true;
        }
        StopOpen();
        if (isActiveAndEnabled) openRoutine = StartCoroutine(Open());
        else SnapAll(true);
    }

    // 열리는 자리를 비춘 뒤 조각을 시차대로 움직이고, 다 열리면 카메라를 플레이어에게 돌려준다.
    private IEnumerator Open()
    {
        if (holdPlayerWhileOpening) HoldPlayer(true);
        if (revealCamera != null)
        {
            revealCamera.Priority = revealPriority;
            revealCamera.enabled = true;
            if (revealLead > 0f) yield return new WaitForSeconds(revealLead);
        }
        if (!stretchFollowsTerrain) ApplyStretch(1f);
        PlaySound(driveSound, CenterOfSegments());
        for (int i = 0; i < segments.Length; i++)
            StartSegment(i, restLocal[i] + segments[i].openOffset, segments[i].delay, openDuration, unlockDistance);
        while (Array.Exists(motions, m => m != null)) yield return null;
        // 다 세운 뒤 공간 밖에 남은 플레이어는 보스가 나오기 전에 공간 안으로 옮긴다.
        if (keepPlayerInsideBounds) KeepPlayerInside();
        HoldPlayer(false);
        if (revealCamera != null)
        {
            if (revealHold > 0f) yield return new WaitForSeconds(revealHold);
            revealCamera.enabled = false;
        }
        openRoutine = null;
    }

    public void End()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Cache();
        StopFinish();
        StopOpen();
        if (revealCamera != null) revealCamera.enabled = false;
        // 다음 구간에 넘겨준 뒤에는 지형·장식을 그대로 두고, 웨이브 초기화·방 이탈 때 함께 되돌린다.
        if (HandedOff)
        {
            if (courseCamera != null) courseCamera.enabled = false;
            return;
        }
        if (returnPlayerOnClose) ReturnPlayerOutsideTerrain();
        if (courseCamera != null) courseCamera.enabled = false;
        if (!isActiveAndEnabled)
        {
            SnapAll(false);
            return;
        }
        // 닫을 때는 연 순서를 거꾸로 따른다.
        PlaySound(driveSound, CenterOfSegments());
        float last = 0f;
        foreach (var segment in segments) last = Mathf.Max(last, segment.delay);
        for (int i = 0; i < segments.Length; i++)
            StartSegment(i, restLocal[i], last - segments[i].delay, closeDuration, 0f);
    }

    private void StopOpen()
    {
        if (openRoutine != null) StopCoroutine(openRoutine);
        openRoutine = null;
        HoldPlayer(false);
    }

    // 입력을 막아 플레이어를 그 자리에 멈춘다(PlayerController는 입력이 막히면 속도를 0으로 둔다).
    private void HoldPlayer(bool on)
    {
        if (holdingPlayer == on || UIManager.Instance == null) return;
        if (on) UIManager.Instance.PushGameplayBlock();
        else UIManager.Instance.PopGameplayBlock();
        holdingPlayer = on;
    }

    private void KeepPlayerInside()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || roomBounds == null || playerReturnPoint == null) return;
        var bounds = roomBounds.bounds;
        float x = player.transform.position.x;
        if (x > bounds.min.x + 1f && x < bounds.max.x - 1f) return;
        ReturnPlayer();
    }

    private void Subscribe()
    {
        Unsubscribe();
        var room = GetComponentInParent<Room>(true);
        subscribedWaveManager = room != null ? room.WaveManager : null;
        if (subscribedWaveManager == null) return;
        subscribedWaveManager.CustomTriggerRaised += HandleCustomTrigger;
        subscribedWaveManager.WavesReset += ResetAll;
    }

    private void Unsubscribe()
    {
        if (subscribedWaveManager != null)
        {
            subscribedWaveManager.CustomTriggerRaised -= HandleCustomTrigger;
            subscribedWaveManager.WavesReset -= ResetAll;
        }
        subscribedWaveManager = null;
    }

    // 웨이브를 처음부터 시작하거나 강제로 정리할 때(재시도·로비 복귀) 화면 전환 중에 바로 원래 상태로 되돌린다.
    public void ResetAll()
    {
        Cache();
        IsOpen = false;
        HandedOff = false;
        StopFinish();
        StopOpen();
        CurrentStep = FinishStep.None;
        if (courseCamera != null) courseCamera.enabled = false;
        if (revealCamera != null) revealCamera.enabled = false;
        SnapAll(false);
        SwapRoomBounds(false);
        Relocate(false);
    }

    private void ApplyStretch(float amount)
    {
        if (stretch == null) return;
        if (stretchStartSide) stretch.ApplyStart(amount);
        else stretch.Apply(amount);
    }

    private void SwapRoomBounds(bool on)
    {
        if (roomConfiner == null || roomBounds == null || boundsSwapped == on) return;
        if (on)
        {
            originalRoomBounds = roomConfiner.BoundingShape2D;
            roomConfiner.BoundingShape2D = roomBounds;
        }
        else roomConfiner.BoundingShape2D = originalRoomBounds;
        roomConfiner.InvalidateBoundingShapeCache();
        boundsSwapped = on;
    }

    private void Relocate(bool on)
    {
        if (relocated == on || relocateTargets == null || relocateTargets.Length == 0) return;
        if (on)
        {
            keptWorld = new Vector3[keepInPlace.Length];
            for (int i = 0; i < keepInPlace.Length; i++) if (keepInPlace[i] != null) keptWorld[i] = keepInPlace[i].position;
        }
        foreach (var target in relocateTargets)
            if (target != null) target.position += on ? relocateOffset : -relocateOffset;
        for (int i = 0; i < keepInPlace.Length && i < keptWorld.Length; i++)
            if (keepInPlace[i] != null) keepInPlace[i].position = keptWorld[i];
        relocated = on;
    }

    private void HandleCustomTrigger(string key)
    {
        if (!IsOpen || finishRoutine != null || key != goalKey || !isActiveAndEnabled) return;
        finishRoutine = StartCoroutine(Finish());
    }

    // 성공 → 발판 사라짐 → 낙하(또는 강제 이동) → 웨이브 완료 순서를 단계마다 간격을 두고 진행한다.
    private IEnumerator Finish()
    {
        CurrentStep = FinishStep.Success;
        foreach (var laser in FindObjectsByType<ChasingLaserWall>(FindObjectsSortMode.None))
            if (laser.gameObject.activeInHierarchy) laser.Halt();
        foreach (var guide in FindObjectsByType<CourseGuideField>(FindObjectsSortMode.None))
            if (guide.gameObject.activeInHierarchy) guide.ForceReturnToPool();
        // 코스에 놓은 적도 목표에 닿으면 모두 거두고 다시 꺼내지 않는다.
        foreach (var spawner in FindObjectsByType<CourseEnemySpawner>(FindObjectsSortMode.None))
            if (spawner.gameObject.activeInHierarchy) spawner.Halt();
        yield return new WaitForSeconds(successPause);

        CurrentStep = FinishStep.Vanishing;
        var platforms = new List<TemporaryPlatform>();
        foreach (var platform in FindObjectsByType<TemporaryPlatform>(FindObjectsSortMode.None))
            if (platform.gameObject.activeInHierarchy) platforms.Add(platform);
        Vector2 axis = courseDirection.normalized;
        platforms.Sort((a, b) => Vector2.Dot(b.transform.position, axis).CompareTo(Vector2.Dot(a.transform.position, axis)));
        foreach (var platform in platforms)
        {
            if (platform != null && platform.gameObject.activeInHierarchy) platform.ForceReturnToPool();
            if (vanishInterval > 0f) yield return new WaitForSeconds(vanishInterval);
        }

        if (finishMode == FinishMode.FallToFloor)
        {
            CurrentStep = FinishStep.Falling;
            float limit = Time.time + landingTimeout;
            while (!IsPlayerOnFloor() && Time.time < limit) yield return null;
            // 어딘가에 걸려 내려오지 못하면 시작 위치로 옮겨 다음 웨이브가 막히지 않게 한다.
            if (!IsPlayerOnFloor())
            {
                CurrentStep = FinishStep.ForcedReturn;
                ReturnPlayer();
            }
        }
        else if (finishMode == FinishMode.HandOff && handoffStage != null)
        {
            // 그 자리에서 다음 구간을 연다. 다음 구간이 문 카메라와 방 카메라 범위를 먼저 잡은 뒤 이 구간 카메라를 끈다.
            yield return new WaitForSeconds(settlePause);
            CurrentStep = FinishStep.HandingOff;
            handoffStage.Begin();
            HandedOff = true;
            if (courseCamera != null)
            {
                // 다음 구간 카메라로 화면이 넘어가는 동안 이 카메라가 플레이어를 따라 벽 너머를 비추지 않게 마지막 위치에 멈춘다.
                courseCamera.Follow = null;
                courseCamera.enabled = false;
            }
            while (handoffStage.IsMoving) yield return null;
        }
        else
        {
            yield return new WaitForSeconds(settlePause);
            CurrentStep = FinishStep.ForcedReturn;
            ReturnPlayer();
        }

        // 사라지는 발판이 소멸 애니메이션을 마치고 풀로 돌아간 뒤 웨이브를 끝내 정리가 겹치지 않게 한다.
        float vanishLimit = Time.time + 2f;
        while (platforms.Exists(p => p != null && p.gameObject.activeInHierarchy) && Time.time < vanishLimit) yield return null;
        yield return new WaitForSeconds(settlePause);

        CurrentStep = FinishStep.Completing;
        finishRoutine = null;
        var waveManager = subscribedWaveManager;
        if (waveManager != null) waveManager.HandleCustomTrigger(courseKey);
    }

    private void StopFinish()
    {
        if (finishRoutine != null) StopCoroutine(finishRoutine);
        finishRoutine = null;
    }

    private bool IsPlayerOnFloor()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return true;
        var body = player.GetComponent<Collider2D>();
        float feet = body != null ? body.bounds.min.y : player.transform.position.y;
        return player.IsGrounded && feet <= floorHeight + .3f;
    }

    private void ReturnPlayer()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || playerReturnPoint == null) return;
        Vector3 delta = playerReturnPoint.position - player.transform.position;
        player.transform.position = playerReturnPoint.position;
        player.Velocity = Vector2.zero;
        // 카메라가 먼 거리를 끌려오지 않고 옮긴 위치로 바로 따라가게 한다.
        CinemachineCore.OnTargetObjectWarped(player.transform, delta);
        if (!string.IsNullOrEmpty(playerTransitionTriggerName) && player.animator != null)
            player.animator.SetTrigger(playerTransitionTriggerName);
    }

    // 지형이 원래 자리로 돌아왔을 때 막히는 쪽(진행 방향으로 지형 경계 너머)에 남은 플레이어를 시작 위치로 옮긴다.
    private void ReturnPlayerOutsideTerrain()
    {
        var player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null || playerReturnPoint == null) return;
        Vector2 axis = courseDirection.normalized;
        float boundary = float.MaxValue;
        for (int s = 0; s < segments.Length; s++)
        {
            var tilemap = segments[s].tilemap;
            if (tilemap == null || tilemap.transform.parent == null) continue;
            var bounds = tilemap.localBounds;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3(i % 2 == 0 ? bounds.min.x : bounds.max.x, i < 2 ? bounds.min.y : bounds.max.y, 0f);
                boundary = Mathf.Min(boundary, Vector2.Dot(tilemap.transform.parent.TransformPoint(restLocal[s] + corner), axis));
            }
        }
        if (boundary == float.MaxValue || Vector2.Dot(player.transform.position, axis) < boundary - .05f) return;
        ReturnPlayer();
    }

    private void StartSegment(int index, Vector3 targetLocal, float delay, float duration, float unlock)
    {
        if (segments[index].tilemap == null) return;
        if (motions[index] != null) StopCoroutine(motions[index]);
        motions[index] = StartCoroutine(MoveSegment(index, targetLocal, delay, duration, unlock));
    }

    private IEnumerator MoveSegment(int index, Vector3 targetLocal, float delay, float duration, float unlock)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        var tilemap = segments[index].tilemap;
        Vector3 start = tilemap.transform.localPosition;
        Vector3 travel = targetLocal - start;
        if (segments[index].playSound && travel.sqrMagnitude > .01f)
            PlaySound(segmentSound, tilemap.transform.TransformPoint(tilemap.localBounds.center));
        // 잠금이 풀리듯 반대쪽으로 살짝 물러났다가 움직인다.
        if (unlock > 0f && travel.sqrMagnitude > .01f)
        {
            Vector3 back = start - travel.normalized * unlock;
            yield return Move(index, start, back, unlockDuration);
            start = back;
        }
        yield return Move(index, start, targetLocal, duration);
        SetSegmentLocal(index, targetLocal);
        motions[index] = null;
        if (!IsOpen && !stretchFollowsTerrain && !Array.Exists(motions, m => m != null)) ApplyStretch(0f);
    }

    private static void PlaySound(SoundOptions sound, Vector3 position)
    {
        if (sound != null && sound.clip != null && SoundManager.Instance != null) SoundManager.Instance.PlaySFX(sound, position);
    }

    private Vector3 CenterOfSegments()
    {
        Vector3 sum = Vector3.zero; int count = 0;
        foreach (var segment in segments)
            if (segment.tilemap != null) { sum += segment.tilemap.transform.TransformPoint(segment.tilemap.localBounds.center); count++; }
        return count > 0 ? sum / count : transform.position;
    }

    private IEnumerator Move(int index, Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;
            SetSegmentLocal(index, Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration)));
        }
    }

    // Kinematic 지형은 물리 단계에 맞춰 옮겨 같은 프레임의 충돌 질의와 어긋나지 않게 한다.
    private void SetSegmentLocal(int index, Vector3 local)
    {
        var tilemap = segments[index].tilemap;
        Vector3 world = tilemap.transform.parent != null ? tilemap.transform.parent.TransformPoint(local) : local;
        var body = bodies[index];
        if (body != null && body.bodyType == RigidbodyType2D.Kinematic && Application.isPlaying)
        {
            body.MovePosition(world);
            tilemap.transform.localPosition = local;
        }
        else tilemap.transform.localPosition = local;
        if (stretchFollowsTerrain) ApplyStretch(OpenProgress);
    }

    // 비활성화 중에도 안전하도록 물리 이동 대신 Transform을 바로 옮긴다.
    private void SnapAll(bool open)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            if (motions.Length > i && motions[i] != null) StopCoroutine(motions[i]);
            if (motions.Length > i) motions[i] = null;
            if (segments[i].tilemap != null && cached) segments[i].tilemap.transform.localPosition = restLocal[i] + (open ? segments[i].openOffset : Vector3.zero);
        }
        ApplyStretch(open ? 1f : 0f);
    }

    // 방을 떠나거나 비활성화되면 진행 중인 연출과 관계없이 원래 지형과 카메라로 돌려놓는다.
    private void OnDisable()
    {
        IsOpen = false;
        HandedOff = false;
        StopFinish();
        openRoutine = null;
        HoldPlayer(false);
        Unsubscribe();
        CurrentStep = FinishStep.None;
        SwapRoomBounds(false);
        Relocate(false);
        if (courseCamera != null) courseCamera.enabled = false;
        if (revealCamera != null) revealCamera.enabled = false;
        for (int i = 0; i < motions.Length; i++) motions[i] = null;
        if (cached)
            for (int i = 0; i < segments.Length; i++)
                if (segments[i].tilemap != null) segments[i].tilemap.transform.localPosition = restLocal[i];
        ApplyStretch(0f);
    }
}
