using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 기존 Room의 셸·카메라 배치 정보를 보존한다. 생성 발판은 WaveCellTerrain의 한 칸 데이터로 처리한다.
[DisallowMultipleComponent]
public sealed class TrainingRoomLayoutController : MonoBehaviour
{
    [Serializable]
    public struct Layout
    {
        public float halfWidth;
        public float height;
        public bool platforms;
        public bool centerPlayer;
    }

    private sealed class Panel
    {
        public Tilemap map;
        public int width = -1;
        public int height = -1;
        public float assemblyOffset;
        public readonly Dictionary<Vector3Int, float> assembling = new();
    }

    [SerializeField] private WaveManager waves;
    [SerializeField] private Transform leftWall;
    [SerializeField] private Transform rightWall;
    [SerializeField] private Transform floor;
    [SerializeField] private Transform ceiling;
    [SerializeField] private Transform[] platforms;
    [SerializeField] private TileBase terrainTile;
    [SerializeField] private Transform backdrop;
    [SerializeField] private TrainingRoomPresentation presentation;
    [SerializeField] private Transform exitPortal;
    [SerializeField] private Transform playerReturnPoint;
    [SerializeField] private BoxCollider2D cameraBounds;
    [SerializeField] private CinemachineConfiner2D confiner;
    [SerializeField] private Layout[] layouts;
    [SerializeField] private SoundOptions driveSound;
    [SerializeField] private SoundOptions segmentSound;
    [SerializeField] private float duration = 1.2f;

    private Panel[] panels;
    private float assemblingHalfWidth;
    private int currentLayout = -1;
    public bool IsMoving { get; private set; }
    public int CurrentLayout => currentLayout;
    public Layout[] Layouts => (Layout[])layouts.Clone();

    private void OnEnable()
    {
        Cache();
        waves.WavesReset += ResetLayout;
        waves.EnemySpawned += FaceCenter;
    }

    private void Cache()
    {
        if (panels != null && panels.Length == 4) return;
        panels = new[] { new Panel { map = floor.GetComponent<Tilemap>(), assemblyOffset = -3f }, new Panel { map = ceiling.GetComponent<Tilemap>(), assemblyOffset = 3f },
            new Panel { map = leftWall.GetComponent<Tilemap>() }, new Panel { map = rightWall.GetComponent<Tilemap>() } };
    }

    public IEnumerator PrepareWave(int index)
    {
        Cache();
        Layout next = Resolve(index);
        Layout previous = currentLayout < 0 ? Resolve(0) : Resolve(currentLayout);
        bool terrainChanges = previous.halfWidth != next.halfWidth || previous.height != next.height;
        currentLayout = index;
        if (!terrainChanges && !next.centerPlayer)
        {
            ApplyShell(next.halfWidth, next.height);
            yield break;
        }

        IsMoving = true;
        // 이동 잠금과 준비 완료 뒤 Spawn은 WaveManager의 공통 시작 단계에서 처리한다.

        if (next.halfWidth > previous.halfWidth)
        {
            // 벽 밖의 바닥을 먼저 조립한 뒤 벽과 카메라 영역을 연다. 이동 중인 플레이어의 추락을 막는다.
            assemblingHalfWidth = next.halfWidth;
            ApplyShell(previous.halfWidth, previous.height);
            while (panels[0].assembling.Count > 0 || panels[1].assembling.Count > 0)
            {
                yield return new WaitForFixedUpdate();
                ApplyShell(previous.halfWidth, previous.height);
            }
        }
        if (terrainChanges)
        {
            Play(driveSound);
            float time = 0f;
            while (time < duration)
            {
                yield return new WaitForFixedUpdate();
                time += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(time / duration);
                float progress = t < .12f ? -.015f * Mathf.Sin(t / .12f * Mathf.PI) : Mathf.SmoothStep(0f, 1f, (t - .12f) / .88f);
                ApplyShell(Mathf.LerpUnclamped(previous.halfWidth, next.halfWidth, progress), Mathf.LerpUnclamped(previous.height, next.height, progress));
                if (time >= .15f && time - Time.fixedDeltaTime < .15f) Play(segmentSound);
            }
        }
        assemblingHalfWidth = 0f;
        ApplyShell(next.halfWidth, next.height);
        while (panels[0].assembling.Count > 0 || panels[1].assembling.Count > 0)
        {
            yield return new WaitForFixedUpdate();
            ApplyShell(next.halfWidth, next.height);
        }
        IsMoving = false;
    }

    private Layout Resolve(int index)
    {
        var source = waves.Data;
        if (source != null && index >= 0 && index < source.waves.Length)
        {
            var room = WaveRoomState.LayoutAt(source, index);
            return new Layout { halfWidth = room.bounds.width * .5f, height = room.bounds.height,
                platforms = !room.paintPlatforms && room.platformCells.Length > 0, centerPlayer = room.centerPlayer };
        }
        return layouts[Mathf.Clamp(index, 0, layouts.Length - 1)];
    }

    private static void WarpPlayer(PlayerController player, Vector3 position, Vector2 velocity)
    {
        Vector3 from = player.transform.position;
        player.ResetPlayerPosition(position);
        player.Velocity = velocity;
        CinemachineCore.OnTargetObjectWarped(player.transform, player.transform.position - from);
    }

    private void KeepPlayerInside(float halfWidth, float height)
    {
        if (!IsMoving || GameManager.Instance == null || GameManager.Instance.Player == null) return;
        var player = GameManager.Instance.Player;
        var body = player.GetComponent<Collider2D>().bounds;
        Vector3 center = transform.InverseTransformPoint(body.center);
        const float margin = .02f;
        const float tolerance = .01f;
        Vector3 safe = center;
        // 모터의 정상 접지 오차에는 개입하지 않는다. 실제 경계를 넘었을 때만 안쪽으로 옮긴다.
        if (center.x - body.extents.x < -halfWidth - tolerance || center.x + body.extents.x > halfWidth + tolerance)
            safe.x = Mathf.Clamp(center.x, -halfWidth + body.extents.x + margin, halfWidth - body.extents.x - margin);
        if (center.y - body.extents.y < -tolerance || center.y + body.extents.y > height + tolerance)
            safe.y = Mathf.Clamp(center.y, body.extents.y + margin, height - body.extents.y - margin);
        Vector3 correction = transform.TransformVector(safe - center);
        if (correction.sqrMagnitude < .000001f) return;
        // 실제 패널이 몸체를 침범한 축만 보정하고 나머지 이동은 이어 간다.
        Vector2 velocity = player.Velocity;
        if (Mathf.Abs(correction.x) > .0001f) { velocity.x = 0; player.externalForce.x = 0; }
        if (Mathf.Abs(correction.y) > .0001f) { velocity.y = 0; player.externalForce.y = 0; }
        WarpPlayer(player, player.transform.position + correction, velocity);
    }

    private void ApplyShell(float halfWidth, float height)
    {
        Cache();
        // 패널 폭·높이는 타일 수로 바꾼다. 벽이 움직이는 동안에도 셀 한 칸의 크기는 유지한다.
        int width = 2 * Mathf.CeilToInt(Mathf.Max(halfWidth, assemblingHalfWidth) + 3f);
        int wallHeight = Mathf.CeilToInt(height);
        Fill(panels[0], width, 3);
        Fill(panels[1], width, 3);
        Fill(panels[2], 3, wallHeight);
        Fill(panels[3], 3, wallHeight);
        AnimateAssembly(panels[0]);
        AnimateAssembly(panels[1]);
        floor.localScale = ceiling.localScale = leftWall.localScale = rightWall.localScale = Vector3.one;
        MovePanel(leftWall, new Vector3(-halfWidth - 1.5f, height * .5f, 0));
        MovePanel(rightWall, new Vector3(halfWidth + 1.5f, height * .5f, 0));
        MovePanel(ceiling, new Vector3(0, height + 1.5f, 0));
        backdrop.localScale = Vector3.one;
        if (presentation != null) presentation.Apply(halfWidth, height);
        cameraBounds.size = new Vector2(halfWidth * 2f + 2f, height + 3f);
        cameraBounds.transform.localPosition = new Vector3(0, (height - 3f) * .5f, 0);
        var destination = exitPortal.GetComponent<RoomTrigger>();
        Vector3 returnPosition = destination.TeleportPoint.position;
        exitPortal.localPosition = new Vector3(halfWidth - 3f, 1.5f, 0);
        destination.TeleportPoint.position = returnPosition;
        confiner.InvalidateBoundingShapeCache();
        KeepPlayerInside(halfWidth, height);
    }

    private void Fill(Panel panel, int width, int height)
    {
        if (panel.width == width && panel.height == height) return;
        var before = panel.map.cellBounds;
        var after = new BoundsInt(-width / 2, -height / 2, 0, width, height, 1);
        // 중앙 셀은 그대로 두고 바깥으로 늘어난 열만 빈 영역에서 조립한다.
        foreach (var cell in before.allPositionsWithin)
            if (!after.Contains(cell)) { panel.map.SetTile(cell, null); panel.assembling.Remove(cell); }
        var centered = new Vector3(width % 2 == 0 ? 0 : -.5f, height % 2 == 0 ? 0 : -.5f, 0);
        foreach (var cell in after.allPositionsWithin)
        {
            bool added = !panel.map.HasTile(cell);
            if (added)
            {
                panel.map.SetTile(cell, terrainTile);
                panel.map.SetTileFlags(cell, TileFlags.None);
                if (Application.isPlaying && IsMoving && panel.width >= 0 && panel.assemblyOffset != 0f)
                {
                    panel.assembling[cell] = Time.time;
                    panel.map.SetTransformMatrix(cell, Matrix4x4.Translate(centered + Vector3.up * panel.assemblyOffset));
                }
                else panel.map.SetTransformMatrix(cell, Matrix4x4.Translate(centered));
            }
            // 세로 벽은 움직이는 패널 중심에 맞춘다. 가로 판의 중앙 타일은 재작성하지 않는다.
            else if (panel.assemblyOffset == 0f)
                panel.map.SetTransformMatrix(cell, Matrix4x4.Translate(centered));
        }
        panel.map.CompressBounds();
        panel.width = width; panel.height = height;
    }

    private void AnimateAssembly(Panel panel)
    {
        if (panel.assembling.Count == 0) return;
        var finished = new List<Vector3Int>();
        foreach (var item in panel.assembling)
        {
            float t = IsMoving ? Mathf.Clamp01((Time.time - item.Value) / .45f) : 1f;
            float remaining = t < .2f ? 1f + .08f * Mathf.Sin(t / .2f * Mathf.PI)
                : 1f - Mathf.SmoothStep(0f, 1f, (t - .2f) / .8f);
            float offset = panel.assemblyOffset * remaining;
            panel.map.SetTransformMatrix(item.Key, Matrix4x4.Translate(new Vector3(0, -.5f + offset, 0)));
            if (t >= 1f) finished.Add(item.Key);
        }
        foreach (var cell in finished) panel.assembling.Remove(cell);
    }

    private static void MovePanel(Transform panel, Vector3 local)
    {
        var body = panel.GetComponent<Rigidbody2D>();
        if (Application.isPlaying && body != null) body.MovePosition(panel.parent.TransformPoint(local));
        panel.localPosition = local;
    }

    private void FaceCenter(EnemyController enemy)
    {
        if (currentLayout >= 0 && waves.Data != null && waves.Data.waves[currentLayout].room.faceCenter)
            enemy.FacingUpdate(enemy.transform.position.x < playerReturnPoint.position.x ? 1 : -1);
    }
    private void Play(SoundOptions sound)
    {
        if (sound != null && sound.clip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(sound, transform.position);
    }
    private void ResetLayout()
    {
        StopAllCoroutines();
        IsMoving = false;
        assemblingHalfWidth = 0f;
        currentLayout = -1;
        var initial = Resolve(0);
        ApplyShell(initial.halfWidth, initial.height);
    }
    private void OnDisable()
    {
        waves.WavesReset -= ResetLayout;
        waves.EnemySpawned -= FaceCenter;
        ResetLayout();
    }
}
