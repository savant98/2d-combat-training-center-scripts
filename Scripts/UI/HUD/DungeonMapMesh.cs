using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 던전 배치도(미니맵·격자 Tool 미리보기)를 VertexHelper에 쓰는 정적 Mesh 빌더. 상태를 갖지 않는 순수 함수만 둔다.
/// - 단위 칸 하나 = 작은 사각형, 칸 사이 간격 = 칸 × gapRatio. 같은 방의 이웃 단위 칸은 간격을 메워 한 덩어리(L·ㄱ 모양이 보이게).
/// - 방 상태: 현재 방 = 밝은 청록 채움 + 밝은 외곽, 방문 = 중간 채움 + 청록 외곽(미클리어면 주황 점), 알게 된 미방문 = 흐린 외곽선만,
///   모름 = 안 그림, 보스 방 = 빨간 마름모, 봉인 = 빨간 점선 외곽, 시작 방 = 작은 흰 점. 출입구 연결 = 경계 간격을 건너는 짧은 밝은 다리.
/// - 2차(2026-11-24): 작은 방(sizeClass Small) = 같은 단위 칸을 안쪽 여백을 크게 두고 더 작게 그림(다리는 줄어든 만큼 더 뻗음),
///   알게 된 텔레포트 연결 = 두 방 사이 파란 점선 + 양 끝 작은 파란 마름모, 휴식 방 = 작은 초록 십자, 방 클리어 순간 녹색 펄스 층(<see cref="WriteClearPulse"/>).
/// - 모든 정점은 그릴 영역(area) 안으로 잘린다(축 정렬 사각형은 잘라 내고, 마름모는 영역에 다 들어올 때만 그린다).
/// - 판(배경): 반투명 어두운 판, 왼쪽 위·오른쪽 아래 45° 깎인 모서리, 얇은 청록 외곽선, 오른쪽 위·왼쪽 아래 밝은 꺾쇠, 머리말 띠·선.
///   색·두께는 상황판(TrainingBoardSetup/TrainingBoardStyle)·PlayerHUD(PlayerHudSetup)와 같은 판 언어다.
/// UnityEngine(+ UI의 VertexHelper)과 던전 계약 타입만 쓴다 — 편집기(Wave 격자 Tool)도 <see cref="BuildMesh"/>로 같은 그림을 만든다.
/// - 3차(2026-11-24, DESIGN_V3 '평소 HUD'·'현장 단말기'): 실제 방 모양. 방 셀(<see cref="DungeonRoomDetail"/>)에서 바깥 빈칸을 flood fill로 걸러
///   실내 공기·공기와 맞닿은 고체 테두리·OneWay 선을 행 런으로 만든다(<see cref="BuildSilhouette(DungeonRoomDetail)"/>, 방마다 한 번).
///   지금 방 자세히(<see cref="WriteSilhouette"/> + <see cref="WriteRoomMarks"/>: 출입구 상태·텔레포트·수리대·목표·시설)와
///   큰 지도(<see cref="BuildFloorModel"/> → <see cref="FitFloor"/> → <see cref="WriteFloorMap"/>: 알게 된 방 실루엣을 공통 배율로 단위 칸 블록 안 가운데에 두고
///   출입구끼리 꺾인 선으로 잇는다)를 쓴다. 단위 칸 배치도(<see cref="WriteMap"/>)는 상세 정보가 없을 때의 대체 그림으로 그대로 둔다.
/// - 4차(2026-11-25, DESIGN_V4 §2.6): 시작 방 출구 문 표식(<see cref="DungeonDetailMarkerKind.Exit"/>) — 잠김 = 흐린 앰버 문틀 + 가로 볼트 2줄,
///   열림 = 밝은 앰버 문틀 + 옆으로 밀린 문짝 + 가운데 빛. 열림 여부는 states가 <see cref="IDungeonExitSource"/>도 구현하면(DungeonManager) 그 값을 읽는다(선택 인터페이스).
///   출구 안내 층(<see cref="WriteExitGuide"/>): 출구 해금 뒤 지금 방이 출구 방이면 밝은 출구 표식 + 둘레 테두리, 아니면 출구 쪽 연결 출입구 둘레 테두리 + 방 안쪽에서
///   그 출입구를 가리키는 겹 화살. 미니맵이 이 층을 CanvasRenderer 알파로 맥동시킨다.
/// - 5차(2026-11-25, DESIGN_V5 §1 S9): 표식을 셀 크기와 무관한 고정 크기 '배지형 픽토그램'(미니맵 11~16px·큰 지도 12~20px — <see cref="BadgeSide"/>)으로 바꿨다.
///   어두운 바탕 → 역할 색 테두리 → 밝은 안쪽 그림: 출입구 관(플랜지·열림 겹 꺾쇠·잠김 셔터·봉인 줄무늬 + 이웃 방 정보 점)·텔레포트 링·레버(배전반 손잡이)·
///   출구(문틀 + 램프 = 레버 수)·수리대(흰 배지 + 초록 십자)·경로 크레인(훅 + 점선 경로 + 양 끝 도크)·고정 크레인(고리 + 사슬)·점프대(받침 + 겹 꺾쇠).
///   크레인·점프대는 기능부 파랑(WAVE_ENVIRONMENT_DESIGN 중립 기능). 함정·적은 그리지 않는다. Player 표식은 빛 고리 + 바라보는 쪽 꼬리(<see cref="WritePlayerMarker"/>),
///   핑 고리(<see cref="WritePing"/>), 큰 지도 지금 방 맥동 테두리·꺾쇠·표지 삼각형·화면 밖 화살(<see cref="WriteCurrentFrame"/>), 범례 9칸.
///   원·고리는 정적 단위 원 표로 그린다(할당 없음). Mesh는 지금처럼 SetVerticesDirty 때만 만든다.
///   출구 안내 층도 같은 기하를 따른다 — 출구는 배지 안 문틀만 밝히고 배지 + 램프 줄 자리(<see cref="ExitMarkBounds"/>)를, 출입구는 관 + 플랜지 + 꺾쇠 + 이웃 방 정보 점 자리
///   (<see cref="DoorMarkBounds"/>)를 여백을 두고 둘러싼다(표식 위에 테두리·옛 크기 그림을 덮어 그리지 않음).
/// </summary>
public static class DungeonMapMesh
{
    /// <summary>색·두께. 1080p 기준 UI 단위(= px).</summary>
    public sealed class Style
    {
        // 판(상황판과 같은 값)
        public Color32 plateFill = new Color32(8, 17, 29, 219);          // #08111d α.86
        public Color32 headerBand = new Color32(5, 11, 20, 179);         // Deep #050b14 α.7
        public Color32 line = new Color32(43, 200, 226, 255);            // #2bc8e2
        public Color32 lineDim = new Color32(23, 75, 92, 255);           // #174b5c
        public Color32 lineBright = new Color32(143, 244, 255, 255);     // #8ff4ff
        public float plateLine = 1.2f, plateInnerLine = .9f, plateChamferLine = 1.8f, bracketLine = 2f;
        public float plateCut = 16f, plateInnerInset = 5f, bracketLength = 14f;

        // 방
        public Color32 currentFill = new Color32(64, 214, 236, 170);     // 밝은 청록 채움
        public Color32 currentLine = new Color32(143, 244, 255, 255);    // #8ff4ff
        public Color32 visitedFill = new Color32(26, 92, 110, 205);      // 중간 채움
        public Color32 visitedLine = new Color32(43, 200, 226, 255);     // #2bc8e2
        public Color32 knownLine = new Color32(23, 75, 92, 255);         // #174b5c(흐린 외곽선만)
        public Color32 unclearedDot = new Color32(255, 179, 71, 255);    // #ffb347
        public Color32 boss = new Color32(255, 90, 60, 255);             // #ff5a3c
        public Color32 startDot = new Color32(236, 250, 255, 255);
        public Color32 bridge = new Color32(143, 244, 255, 255);         // 둘 다 들어가 본 방 사이
        public Color32 bridgeDim = new Color32(43, 200, 226, 170);       // 한쪽만 들어가 본 방 사이
        public Color32 highlightFill = new Color32(143, 244, 255, 70);   // 현재 방 맥동 층(채움)
        public Color32 highlightLine = new Color32(143, 244, 255, 255);  // 현재 방 맥동 층(굵은 외곽)
        public float roomLine = 1.25f, currentLineWidth = 1.5f, bridgeWidth = 1.5f, highlightLineWidth = 3f;
        /// <summary>다리가 방 안쪽으로 들어가는 길이(칸 대비).</summary>
        public float bridgeReach = .14f;
        /// <summary>점·표식 크기(칸 대비): 미클리어 점 한 변, 시작 점 한 변, 보스 마름모 반대각선.</summary>
        public float dotSize = .26f, startDotSize = .17f, bossSize = .25f;
        /// <summary>봉인 점선의 선분·틈 길이(칸 대비, 최소 px).</summary>
        public float dashLength = .2f, dashGap = .14f, dashMin = 2.5f;

        // 2차(2026-11-24)
        /// <summary>작은 방의 안쪽 여백(칸 대비, 한 변). 작은 방은 같은 단위 칸을 이만큼 줄여 그린다.</summary>
        public float smallInset = .17f;
        /// <summary>텔레포트 연결(중립 기능 = 파랑): 두 방 모두 들어가 봤으면 밝게, 아니면 흐리게.</summary>
        public Color32 teleport = new Color32(77, 141, 255, 255);        // #4d8dff
        public Color32 teleportDim = new Color32(77, 141, 255, 150);
        public float teleportWidth = 1.5f;
        /// <summary>텔레포트 점선의 선분·틈(칸 대비, 최소 px)과 끝 마름모 반대각선(칸 대비), 끝점 자리(방 표식 칸 가운데에서 칸 대비 왼쪽 아래로).</summary>
        public float teleportDash = .22f, teleportGap = .16f, teleportEndSize = .13f, teleportEndOffset = .3f;
        /// <summary>휴식 방(수리대) 초록 십자: 팔 길이(칸 대비, 가운데에서 끝까지)·굵기(칸 대비, 최소 1.5px).</summary>
        public Color32 rest = new Color32(51, 242, 89, 255);             // #33f259(회복 초록)
        public float restArm = .17f, restThickness = .075f;
        /// <summary>방 클리어 펄스 층(채움 + 굵은 외곽). DungeonMinimap이 CanvasRenderer 알파로 한 번 번쩍인다.</summary>
        public Color32 clearFill = new Color32(51, 242, 89, 90);
        public Color32 clearLine = new Color32(170, 255, 190, 255);
        public float clearLineWidth = 3f;

        // 3차(2026-11-24): 실제 방 모양(지금 방 자세히·현장 단말기 큰 지도)
        /// <summary>지금 방 자세히(미니맵): 실내 공기 채움·벽 테두리·OneWay 선.</summary>
        public Color32 roomAir = new Color32(20, 70, 86, 205);
        public Color32 roomWall = new Color32(43, 200, 226, 255);         // #2bc8e2
        public Color32 oneWayLine = new Color32(200, 250, 255, 255);
        /// <summary>큰 지도: 현재 방 공기(밝은 청록, 반투명). 방문 방은 visitedFill, 벽은 currentLine/visitedLine/knownLine.</summary>
        public Color32 currentAir = new Color32(64, 214, 236, 150);
        /// <summary>출입구 상태: 열림(밝은 청록 + 바깥 화살)·잠김(빨강 채움)·봉인(빨강 테두리).</summary>
        public Color32 doorOpen = new Color32(143, 244, 255, 235);
        public Color32 doorLocked = new Color32(255, 90, 60, 255);       // #ff5a3c(위험 빨강)
        public Color32 doorSealed = new Color32(255, 90, 60, 200);
        /// <summary>목표 앰버(#FFB020 계열 = 1, .69, .13). 완료 = 채움, 미완료 = 테두리 + 손잡이.</summary>
        public Color32 objective = new Color32(255, 176, 33, 255);
        public Color32 objectiveDim = new Color32(255, 176, 33, 150);
        /// <summary>4차 출구 문: 열린 문 안쪽 빛(밝은 앰버)·안내 화살과 테두리.</summary>
        public Color32 exitGlow = new Color32(255, 231, 179, 230);
        public Color32 exitGuide = new Color32(255, 196, 72, 255);
        /// <summary>그 밖의 시설(크레인·컨베이어 등 — 4차에 승강기 삭제) 작은 점.</summary>
        public Color32 facility = new Color32(170, 240, 255, 220);
        /// <summary>Player 표식: 흰 마름모 + 청록 테두리.</summary>
        public Color32 player = new Color32(255, 255, 255, 255);
        public Color32 playerRing = new Color32(43, 200, 226, 255);
        /// <summary>OneWay 선 두께(셀 대비, 최소 1.2px)·표식 크기(셀 대비 최소 px는 함수 안).</summary>
        public float oneWayThickness = .32f;
        /// <summary>큰 지도 출입구 연결선 두께(px).</summary>
        public float connectorWidth = 2f;

        // 5차(2026-11-25, DESIGN_V5 S9): 배지형 픽토그램·크레인·점프대·이웃 방 정보 점·큰 지도 지금 방·Player 강조
        /// <summary>배지 바탕(어두운 판)과 수리대 배지의 바깥 어두운 테두리.</summary>
        public Color32 badgeBack = new Color32(6, 14, 24, 235);
        public Color32 badgeEdge = new Color32(2, 6, 12, 255);
        /// <summary>크레인 표식(기능부 파랑 — WAVE_ENVIRONMENT_DESIGN '중립 기능')·경로 점선·도크.</summary>
        public Color32 crane = new Color32(92, 168, 255, 255);
        public Color32 cranePath = new Color32(120, 186, 255, 200);
        /// <summary>점프대 표식(중립 기능 파랑, 크레인보다 밝은 하늘색).</summary>
        public Color32 jumpPad = new Color32(120, 204, 255, 255);
        /// <summary>수리대 배지 판(흰 계열).</summary>
        public Color32 repairPlate = new Color32(226, 255, 234, 255);
        /// <summary>레버 완료 표시등(녹색 — 실물 '녹색 완료').</summary>
        public Color32 leverLamp = new Color32(51, 242, 89, 255);
        /// <summary>출구 램프 꺼짐(어두운 앰버). 켜짐은 objective.</summary>
        public Color32 lampOff = new Color32(84, 60, 18, 255);
        /// <summary>잠긴 출입구 셔터 줄·볼트(어두운 색).</summary>
        public Color32 doorShutter = new Color32(60, 14, 10, 255);
        /// <summary>이웃 방 정보 점: 미방문(흐림). 보스(미클리어) = boss 마름모, 방문·미클리어 = unclearedDot, 클리어 = rest.</summary>
        public Color32 neighborUnknown = new Color32(120, 160, 172, 170);
        /// <summary>큰 지도: 지금 방이 아닌 방문 방 채움(지금 방이 더 돋보이게 α205 → 170).</summary>
        public Color32 floorVisitedFill = new Color32(26, 92, 110, 170);
        /// <summary>큰 지도 지금 방 맥동 테두리·꺾쇠·표지 삼각형·화면 밖 화살.</summary>
        public Color32 currentFrame = new Color32(143, 244, 255, 255);
        public float currentFrameWidth = 3f, currentBracket = 14f, currentBracketWidth = 3f;
        /// <summary>Player 빛 고리(반투명 원판 + 밝은 테두리)·바라보는 쪽 꼬리·핑 고리.</summary>
        public Color32 playerHalo = new Color32(43, 200, 226, 80);
        public Color32 playerHaloRing = new Color32(143, 244, 255, 200);
        public Color32 playerTail = new Color32(143, 244, 255, 255);
        /// <summary>배지 한 변(px) = clamp(셀 px × 배율, 최소, 최대). 미니맵 11~16, 큰 지도 12~20.</summary>
        public float miniBadgeScale = 1.8f, miniBadgeMin = 11f, miniBadgeMax = 16f;
        public float floorBadgeScale = 4f, floorBadgeMin = 12f, floorBadgeMax = 20f;
        /// <summary>크레인 훅·경로를 매달린 발보다 올려 그리는 칸 수(매달린 몸 가운데쯤). 도크는 발 높이.</summary>
        public float craneLift = 2f;

        public static readonly Style Default = new Style();
    }

    /// <summary>배치도를 영역에 놓는 방법(단위 공간 → 화면).</summary>
    [Serializable]
    public struct View
    {
        /// <summary>영역 가운데에 오는 단위 공간 좌표. 단위 칸 u의 몸체는 단위 공간 [u, u + k](k = 1/(1 + gapRatio)).</summary>
        public Vector2 center;
        /// <summary>단위 칸 한 변(px).</summary>
        public float cell;
        /// <summary>칸 사이 간격 / 칸.</summary>
        public float gapRatio;

        public float Pitch => cell * (1f + gapRatio);
        public float Gap => cell * gapRatio;
        /// <summary>단위 공간에서 칸 몸체의 길이(나머지는 간격).</summary>
        public float Body => 1f / (1f + gapRatio);

        public static View Lerp(View a, View b, float t) => new View
        {
            center = Vector2.LerpUnclamped(a.center, b.center, t),
            cell = Mathf.LerpUnclamped(a.cell, b.cell, t),
            gapRatio = Mathf.LerpUnclamped(a.gapRatio, b.gapRatio, t),
        };

        public bool Approximately(View other, float eps = .001f) =>
            (center - other.center).sqrMagnitude < eps * eps && Mathf.Abs(cell - other.cell) < eps && Mathf.Abs(gapRatio - other.gapRatio) < eps;
    }

    /// <summary>칸 크기 자동 맞춤 설정.</summary>
    [Serializable]
    public struct FitOptions
    {
        public float maxCell;
        public float minCell;
        public float gapRatio;
        /// <summary>영역 가장자리 여백(현재 방의 굵은 외곽이 잘리지 않게).</summary>
        public float padding;

        public static FitOptions Default => new FitOptions { maxCell = 26f, minCell = 12f, gapRatio = .18f, padding = 4f };
    }

    /// <summary>한 번 그린 결과(검증·디버그용).</summary>
    public struct MapStats
    {
        public int vertices;
        public int roomsDrawn;
        public int filledRooms;
        public int outlineOnlyRooms;
        public int currentDrawn;
        public int unclearedDots;
        public int bossMarks;
        public int sealedRooms;
        public int startDots;
        public int bridges;
        /// <summary>작게 그린 방(sizeClass Small) 수.</summary>
        public int smallRooms;
        /// <summary>그린 텔레포트 연결(한 쌍에 하나) 수.</summary>
        public int teleportLinks;
        /// <summary>휴식 방 초록 십자 수.</summary>
        public int restMarks;
        /// <summary>영역 밖이라 잘려 나가거나 생략된 조각 수.</summary>
        public int clipped;
    }

    // ===== 맞춤 =====

    /// <summary>그릴 방인지(revealAll이면 전부 — 편집기 미리보기).</summary>
    public static bool IsDrawable(DungeonMapRoom room, bool revealAll) => room != null && room.units != null && room.units.Length > 0 && (revealAll || room.known || room.visited);

    /// <summary>
    /// 그릴 방들의 범위가 영역에 맞도록 칸 크기를 정하고(최대 maxCell), 최소 칸으로도 넘치면 현재 방을 가운데로 두되 배치도 끝이 영역 안쪽으로 들어오지 않게 화면을 민다.
    /// </summary>
    public static View Fit(IReadOnlyList<DungeonMapRoom> rooms, int currentRoom, Rect area, FitOptions options, bool revealAll = false)
    {
        float gapRatio = Mathf.Max(0f, options.gapRatio);
        float k = 1f / (1f + gapRatio);
        float maxCell = Mathf.Max(1f, options.maxCell);
        float minCell = Mathf.Clamp(options.minCell, 1f, maxCell);
        if (!TryBounds(rooms, revealAll, out var min, out var max))
            return new View { center = new Vector2(k * .5f, k * .5f), cell = maxCell, gapRatio = gapRatio };

        float pad = Mathf.Max(0f, options.padding);
        float availW = Mathf.Max(1f, area.width - 2f * pad), availH = Mathf.Max(1f, area.height - 2f * pad);
        float spanX = max.x - min.x + k, spanY = max.y - min.y + k;     // 단위 공간
        float fit = Mathf.Min(availW / (spanX * (1f + gapRatio)), availH / (spanY * (1f + gapRatio)));
        float cell = Mathf.Clamp(fit, minCell, maxCell);
        float pitch = cell * (1f + gapRatio);

        Vector2 focus = new Vector2((min.x + max.x + k) * .5f, (min.y + max.y + k) * .5f);
        var current = Find(rooms, currentRoom);
        if (current != null && IsDrawable(current, revealAll)) focus = RoomCenter(current, k);

        var center = new Vector2(
            Axis(min.x, max.x + k, focus.x, availW * .5f / pitch),
            Axis(min.y, max.y + k, focus.y, availH * .5f / pitch));
        return new View { center = center, cell = cell, gapRatio = gapRatio };
    }

    // 배치도가 영역에 들어오면 배치도 가운데, 넘치면 초점을 따라가되 끝이 영역 안쪽으로 들어오지 않게 막는다.
    private static float Axis(float lo, float hi, float focus, float halfView)
    {
        if (hi - lo <= 2f * halfView) return (lo + hi) * .5f;
        return Mathf.Clamp(focus, lo + halfView, hi - halfView);
    }

    private static bool TryBounds(IReadOnlyList<DungeonMapRoom> rooms, bool revealAll, out Vector2Int min, out Vector2Int max)
    {
        min = new Vector2Int(int.MaxValue, int.MaxValue);
        max = new Vector2Int(int.MinValue, int.MinValue);
        bool any = false;
        if (rooms == null) return false;
        for (int r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            if (!IsDrawable(room, revealAll)) continue;
            foreach (var u in room.units)
            {
                min = Vector2Int.Min(min, u);
                max = Vector2Int.Max(max, u);
                any = true;
            }
        }
        return any;
    }

    /// <summary>방 단위 칸 가운데들의 평균(단위 공간).</summary>
    public static Vector2 RoomCenter(DungeonMapRoom room, float body)
    {
        if (room == null || room.units == null || room.units.Length == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        foreach (var u in room.units) sum += new Vector2(u.x + body * .5f, u.y + body * .5f);
        return sum / room.units.Length;
    }

    /// <summary>단위 칸 몸체의 화면 사각형(영역 좌표).</summary>
    public static Rect UnitRect(Rect area, View view, Vector2Int unit)
    {
        float p = view.Pitch;
        var min = area.center + (new Vector2(unit.x, unit.y) - view.center) * p;
        return new Rect(min, new Vector2(view.cell, view.cell));
    }

    /// <summary>방을 그릴 때 단위 칸 몸체를 안쪽으로 줄이는 양(px). 작은 방만 0보다 크다.</summary>
    public static float RoomInset(DungeonMapRoom room, View view, Style style = null)
    {
        style ??= Style.Default;
        if (room == null || room.sizeClass != DungeonRoomSize.Small) return 0f;
        return Mathf.Clamp(view.cell * style.smallInset, 0f, view.cell * .4f);
    }

    /// <summary>방의 단위 칸 몸체가 실제로 그려지는 화면 사각형(작은 방은 안쪽 여백만큼 줄어든다).</summary>
    public static Rect RoomUnitRect(Rect area, View view, DungeonMapRoom room, Vector2Int unit, Style style = null)
    {
        var r = UnitRect(area, view, unit);
        float inset = RoomInset(room, view, style);
        return inset > 0f ? Inset(r, inset) : r;
    }

    /// <summary>텔레포트 연결 끝점(방 표식 칸 가운데에서 왼쪽 아래로 조금 — 보스 마름모·미클리어 점과 겹치지 않게).</summary>
    public static Vector2 TeleportEndPoint(Rect area, View view, DungeonMapRoom room, Style style = null)
    {
        style ??= Style.Default;
        var anchor = RoomUnitRect(area, view, room, AnchorUnit(room), style);
        float off = anchor.width * style.teleportEndOffset;
        return anchor.center - new Vector2(off, off);
    }

    /// <summary>방 표식(점·마름모)을 놓는 단위 칸: 단위 칸 가운데 평균에 가장 가까운 칸.</summary>
    public static Vector2Int AnchorUnit(DungeonMapRoom room)
    {
        if (room == null || room.units == null || room.units.Length == 0) return Vector2Int.zero;
        Vector2 mean = Vector2.zero;
        foreach (var u in room.units) mean += (Vector2)u;
        mean /= room.units.Length;
        var best = room.units[0];
        float bestD = float.MaxValue;
        foreach (var u in room.units)
        {
            float d = ((Vector2)u - mean).sqrMagnitude;
            if (d < bestD - 1e-5f) { bestD = d; best = u; }
        }
        return best;
    }

    // ===== 배치도 =====

    /// <summary>방 배치도를 쓴다(채움 → 외곽 → 다리 → 표식 순서). vh는 비우지 않는다.</summary>
    public static MapStats WriteMap(VertexHelper vh, IReadOnlyList<DungeonMapRoom> rooms, int currentRoom, Rect area, View view, Style style = null, bool revealAll = false)
    {
        style ??= Style.Default;
        var stats = new MapStats();
        int startVerts = vh.currentVertCount;
        if (rooms == null || rooms.Count == 0 || view.cell <= 0f) return stats;
        var ctx = new Ctx(vh, area, view);

        // 1. 채움
        for (int r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            if (!IsDrawable(room, revealAll)) continue;
            stats.roomsDrawn++;
            bool current = room.id == currentRoom;
            if (current) stats.currentDrawn++;
            float inset = RoomInset(room, view, style);
            if (inset > 0f) stats.smallRooms++;
            if (current || room.visited || revealAll)
            {
                stats.filledRooms++;
                FillRoom(ref ctx, room, current ? style.currentFill : style.visitedFill, ref stats, inset);
            }
            else stats.outlineOnlyRooms++;
        }

        // 2. 외곽(봉인 방은 빨간 점선)
        for (int r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            if (!IsDrawable(room, revealAll)) continue;
            bool current = room.id == currentRoom;
            float inset = RoomInset(room, view, style);
            if (room.sealedRoom)
            {
                stats.sealedRooms++;
                OutlineRoom(ref ctx, room, style.boss, style.roomLine, true, style, ref stats, inset);
                continue;
            }
            Color32 tint = current ? style.currentLine : (room.visited || revealAll) ? style.visitedLine : style.knownLine;
            OutlineRoom(ref ctx, room, tint, current ? style.currentLineWidth : style.roomLine, false, style, ref stats, inset);
        }

        // 3. 출입구 다리(한 쌍에 한 번)
        var byId = new Dictionary<int, DungeonMapRoom>();
        for (int r = 0; r < rooms.Count; r++) if (rooms[r] != null) byId[rooms[r].id] = rooms[r];
        for (int r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            if (!IsDrawable(room, revealAll) || room.links == null) continue;
            foreach (var link in room.links)
            {
                if (!byId.TryGetValue(link.toRoom, out var other) || !IsDrawable(other, revealAll)) continue;
                // 같은 연결을 두 번 그리지 않는다: 건너편 방도 이 경계를 가리키는 연결을 가지면 id가 작은 쪽만 그린다.
                if (other.id < room.id && HasLink(other, link.unit + DungeonShapes.Outward(link.side), DungeonShapes.Opposite(link.side), room.id)) continue;
                bool bright = (room.visited && other.visited) || revealAll;
                // 작은 방은 몸체가 줄어든 만큼 다리를 더 뻗는다(오른쪽 연결이면 이 방이 왼쪽).
                float insetRoom = RoomInset(room, view, style), insetOther = RoomInset(other, view, style);
                bool right = link.side == DungeonDoorSide.Right;
                Bridge(ref ctx, link.unit, link.side, bright ? style.bridge : style.bridgeDim, style, ref stats,
                       right ? insetRoom : insetOther, right ? insetOther : insetRoom);
                stats.bridges++;
            }
        }

        // 3-2. 텔레포트 연결(한 쌍에 한 번, 두 방 모두 알게 됐을 때): 파란 점선 + 양 끝 작은 마름모
        for (int r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            if (!IsDrawable(room, revealAll) || room.teleportTo < 0 || room.teleportTo == room.id) continue;
            if (!byId.TryGetValue(room.teleportTo, out var other) || !IsDrawable(other, revealAll)) continue;
            if (other.teleportTo == room.id && other.id < room.id) continue;
            bool bright = (room.visited && other.visited) || revealAll;
            var tint = bright ? style.teleport : style.teleportDim;
            var a = TeleportEndPoint(area, view, room, style);
            var b = TeleportEndPoint(area, view, other, style);
            float end = Mathf.Max(2f, view.cell * style.teleportEndSize);
            // 선은 끝 마름모 바깥에서 시작한다.
            Vector2 dir = b - a;
            float len = dir.magnitude;
            if (len > 2f * end + 1f)
            {
                dir /= len;
                float dash = Mathf.Max(style.dashMin, view.cell * style.teleportDash);
                float gap = Mathf.Max(style.dashMin * .7f, view.cell * style.teleportGap);
                DashedLine(ref ctx, a + dir * (end + 1f), b - dir * (end + 1f), style.teleportWidth, dash, gap, tint);
            }
            if (!Diamond(ref ctx, a, end, tint)) ctx.clipped++;
            if (!Diamond(ref ctx, b, end, tint)) ctx.clipped++;
            stats.teleportLinks++;
        }

        // 4. 표식
        for (int r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            if (!IsDrawable(room, revealAll)) continue;
            var anchor = RoomUnitRect(area, view, room, AnchorUnit(room), style);
            bool isBoss = room.kind == DungeonRoomKind.Boss;
            if (isBoss)
            {
                if (Diamond(ref ctx, anchor.center, view.cell * style.bossSize, style.boss)) stats.bossMarks++;
                else stats.clipped++;
            }
            if (room.rest)
            {
                // 휴식 방(수리대): 가운데 작은 초록 십자
                float arm = Mathf.Max(2.5f, view.cell * style.restArm), thick = Mathf.Max(1.5f, view.cell * style.restThickness);
                var c = anchor.center;
                bool h = AddClipped(ref ctx, Rect.MinMaxRect(c.x - arm, c.y - thick * .5f, c.x + arm, c.y + thick * .5f), style.rest);
                bool v = AddClipped(ref ctx, Rect.MinMaxRect(c.x - thick * .5f, c.y - arm, c.x + thick * .5f, c.y + arm), style.rest);
                if (h || v) stats.restMarks++;
            }
            if (room.visited && !room.cleared && !revealAll)
            {
                float s = Mathf.Max(2f, view.cell * style.dotSize);
                // 보스 표식·휴식 십자와 겹치지 않게 그 방에서는 오른쪽 위 구석으로 옮긴다.
                Vector2 c = isBoss || room.rest ? new Vector2(anchor.xMax - s * .9f, anchor.yMax - s * .9f) : anchor.center;
                if (AddClipped(ref ctx, CenteredRect(c, s), style.unclearedDot)) stats.unclearedDots++;
                else stats.clipped++;
            }
            if (room.kind == DungeonRoomKind.Start)
            {
                float s = Mathf.Max(2f, view.cell * style.startDotSize);
                if (AddClipped(ref ctx, CenteredRect(anchor.center, s), style.startDot)) stats.startDots++;
                else stats.clipped++;
            }
        }
        stats.clipped += ctx.clipped;
        stats.vertices = vh.currentVertCount - startVerts;
        return stats;
    }

    /// <summary>현재 방 강조 층(맥동용): 옅은 채움 + 굵은 밝은 외곽. 그린 정점 수를 돌려준다.</summary>
    public static int WriteHighlight(VertexHelper vh, IReadOnlyList<DungeonMapRoom> rooms, int currentRoom, Rect area, View view, Style style = null)
    {
        style ??= Style.Default;
        return WriteRoomOverlay(vh, rooms, currentRoom, area, view, style.highlightFill, style.highlightLine, style.highlightLineWidth, style);
    }

    /// <summary>방 클리어 펄스 층: 그 방에 옅은 초록 채움 + 굵은 밝은 초록 외곽. 그린 정점 수를 돌려준다(방을 못 찾으면 0).</summary>
    public static int WriteClearPulse(VertexHelper vh, IReadOnlyList<DungeonMapRoom> rooms, int roomId, Rect area, View view, Style style = null)
    {
        style ??= Style.Default;
        return WriteRoomOverlay(vh, rooms, roomId, area, view, style.clearFill, style.clearLine, style.clearLineWidth, style);
    }

    // 한 방 위에 덮는 층(채움 + 외곽). 작은 방은 배치도와 같은 안쪽 여백을 쓴다.
    private static int WriteRoomOverlay(VertexHelper vh, IReadOnlyList<DungeonMapRoom> rooms, int roomId, Rect area, View view, Color32 fill, Color32 line, float lineWidth, Style style)
    {
        var room = Find(rooms, roomId);
        if (room == null || room.units == null || room.units.Length == 0 || view.cell <= 0f) return 0;
        int start = vh.currentVertCount;
        var ctx = new Ctx(vh, area, view);
        var stats = new MapStats();
        float inset = RoomInset(room, view, style);
        FillRoom(ref ctx, room, fill, ref stats, inset);
        OutlineRoom(ref ctx, room, line, lineWidth, false, style, ref stats, inset);
        return vh.currentVertCount - start;
    }

    /// <summary>편집기 미리보기 등: 배치도를 Mesh 하나로 만든다(현재 방 강조 층 포함).</summary>
    public static MapStats BuildMesh(Mesh output, IReadOnlyList<DungeonMapRoom> rooms, int currentRoom, Rect area, View view, Style style = null, bool revealAll = false, bool includeHighlight = false)
    {
        using (var vh = new VertexHelper())
        {
            var stats = WriteMap(vh, rooms, currentRoom, area, view, style, revealAll);
            if (includeHighlight) WriteHighlight(vh, rooms, currentRoom, area, view, style);
            vh.FillMesh(output);
            return stats;
        }
    }

    /// <summary>
    /// 생성기 결과(DungeonLayout)를 미니맵 방 목록으로 옮긴다(편집기 미리보기용). revealAll이면 모든 방을 '알게 된·방문한' 상태로 둔다.
    /// 실행 중 상태는 DungeonManager가 따로 채운다.
    /// </summary>
    public static List<DungeonMapRoom> FromLayout(DungeonLayout layout, bool revealAll = true)
    {
        var list = new List<DungeonMapRoom>();
        if (layout == null) return list;
        // 텔레포트 짝(방 id → 건너편 방 id). 한 방에 짝이 둘이면 처음 것만 표시한다.
        var teleportTo = new Dictionary<int, int>();
        foreach (var pair in layout.teleports)
        {
            if (!teleportTo.ContainsKey(pair.roomA)) teleportTo[pair.roomA] = pair.roomB;
            if (!teleportTo.ContainsKey(pair.roomB)) teleportTo[pair.roomB] = pair.roomA;
        }
        foreach (var node in layout.rooms)
        {
            if (node == null) continue;
            var links = new List<(Vector2Int, DungeonDoorSide, int)>();
            foreach (var link in node.links)
            {
                if (link.socket < 0 || link.socket >= node.sockets.Count) continue;
                var socket = node.sockets[link.socket];
                links.Add((node.gridOrigin + socket.unit, socket.side, link.toRoom));
            }
            var units = new List<Vector2Int>(node.Units);
            list.Add(new DungeonMapRoom
            {
                id = node.id,
                kind = node.kind,
                units = units.ToArray(),
                links = links.ToArray(),
                known = revealAll || node.id == layout.startRoom,
                visited = revealAll || node.id == layout.startRoom,
                cleared = revealAll || node.id == layout.startRoom,
                sizeClass = node.template != null ? node.template.sizeClass : DungeonRoomSize.Large,
                rest = node.template != null && node.template.HasTag(DungeonRoomTags.Rest),
                teleportTo = teleportTo.TryGetValue(node.id, out int to) ? to : -1,
            });
        }
        return list;
    }

    // ===== 판 =====

    /// <summary>
    /// 미니맵 판: 반투명 어두운 판(왼쪽 위·오른쪽 아래 45° 깎임), 청록 외곽선, 안쪽 옅은 선, 깎인 모서리 밝은 선, 오른쪽 위·왼쪽 아래 밝은 꺾쇠,
    /// 머리말 띠(headerHeight > 0이면)·옅은 가로선·밝은 강조선. 모든 정점은 rect 안에 있다.
    /// </summary>
    public static void WritePlate(VertexHelper vh, Rect rect, Style style = null, float headerHeight = 36f)
    {
        style ??= Style.Default;
        float edge = Mathf.Max(style.plateLine, style.plateChamferLine) * .5f + .6f;   // 선이 판 밖으로 나가지 않게 안쪽으로 들인다
        var outer = Inset(rect, edge);
        float cut = Mathf.Min(style.plateCut, Mathf.Min(outer.width, outer.height) * .3f);
        var poly = Chamfered(outer, cut);

        // 채움(볼록 다각형 부채꼴)
        int center = vh.currentVertCount;
        vh.AddVert(outer.center, style.plateFill, Vector2.zero);
        foreach (var p in poly) vh.AddVert(p, style.plateFill, Vector2.zero);
        for (int i = 0; i < poly.Length; i++) vh.AddTriangle(center, center + 1 + i, center + 1 + (i + 1) % poly.Length);

        // 머리말 띠·선
        if (headerHeight > 0f)
        {
            AddQuad(vh, new Rect(rect.xMin + 14f, rect.yMax - 31f, rect.width - 21f, 25f), style.headerBand);
            AddQuad(vh, new Rect(rect.xMin + 7f, rect.yMax - 32.5f, rect.width - 14f, 1f), style.lineDim);
            AddQuad(vh, new Rect(rect.xMin + 7f, rect.yMax - 33f, Mathf.Min(70f, rect.width - 14f), 2f), style.lineBright);
        }

        // 안쪽 옅은 선
        float inset = style.plateInnerInset;
        var inner = Inset(outer, inset);
        var innerPoly = Chamfered(inner, Mathf.Max(0f, cut - inset * (2f - Mathf.Sqrt(2f))));
        StrokeLoop(vh, innerPoly, style.plateInnerLine, style.lineDim);

        // 외곽선(깎인 모서리 = 0번·3번 변은 밝게 굵게)
        for (int i = 0; i < poly.Length; i++)
        {
            bool chamfer = i == 0 || i == 3;
            StrokeSegment(vh, poly[i], poly[(i + 1) % poly.Length], chamfer ? style.plateChamferLine : style.plateLine, chamfer ? style.lineBright : style.line);
        }

        // 꺾쇠(오른쪽 위·왼쪽 아래)
        float b = Mathf.Min(style.bracketLength, Mathf.Min(outer.width, outer.height) * .25f), w = style.bracketLine, o = 2.5f;
        float right = outer.xMax - o, top = outer.yMax - o, left = outer.xMin + o, bottom = outer.yMin + o;
        AddQuad(vh, new Rect(right - b, top - w, b, w), style.lineBright);
        AddQuad(vh, new Rect(right - w, top - b, w, b - w), style.lineBright);
        AddQuad(vh, new Rect(left, bottom, b, w), style.lineBright);
        AddQuad(vh, new Rect(left, bottom + w, w, b - w), style.lineBright);
    }

    // 왼쪽 위·오른쪽 아래를 깎은 육각형(시계 방향). 변 i = 점 i → 점 i+1:
    // 0 왼쪽 위 깎인 변, 1 위 변, 2 오른쪽 변, 3 오른쪽 아래 깎인 변, 4 아래 변, 5 왼쪽 변.
    private static Vector2[] Chamfered(Rect r, float cut) => new[]
    {
        new Vector2(r.xMin, r.yMax - cut),
        new Vector2(r.xMin + cut, r.yMax),
        new Vector2(r.xMax, r.yMax),
        new Vector2(r.xMax, r.yMin + cut),
        new Vector2(r.xMax - cut, r.yMin),
        new Vector2(r.xMin, r.yMin),
    };

    private static Rect Inset(Rect r, float d) => new Rect(r.xMin + d, r.yMin + d, Mathf.Max(0f, r.width - 2f * d), Mathf.Max(0f, r.height - 2f * d));

    private static void StrokeLoop(VertexHelper vh, Vector2[] poly, float width, Color32 tint)
    {
        for (int i = 0; i < poly.Length; i++) StrokeSegment(vh, poly[i], poly[(i + 1) % poly.Length], width, tint);
    }

    // 끝을 반폭만큼 늘인 선분(이웃 변과 모서리에서 이어진다).
    private static void StrokeSegment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 tint)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f) return;
        d /= len;
        float h = width * .5f;
        a -= d * h;
        b += d * h;
        Vector2 n = new Vector2(-d.y, d.x) * h;
        int i = vh.currentVertCount;
        vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
        vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    private static void AddQuad(VertexHelper vh, Rect r, Color32 tint)
    {
        if (r.width <= 0f || r.height <= 0f) return;
        int i = vh.currentVertCount;
        vh.AddVert(new Vector2(r.xMin, r.yMin), tint, Vector2.zero);
        vh.AddVert(new Vector2(r.xMax, r.yMin), tint, Vector2.zero);
        vh.AddVert(new Vector2(r.xMax, r.yMax), tint, Vector2.zero);
        vh.AddVert(new Vector2(r.xMin, r.yMax), tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    // ===== 방 도형(세분 격자) =====
    // 단위 칸 u를 세분 격자 2u(몸체)와 2u+1(다음 칸과의 간격)로 나눈다. 간격 칸은 양쪽(또는 네 칸 모두)이 같은 방일 때만 메운다.
    // 세분 칸 i의 단위 공간 범위: 짝수 [u, u + k], 홀수 [u + k, u + 1] (u = i >> 1, 음수도 내림).

    private struct Ctx
    {
        public VertexHelper vh;
        public Rect area;
        public View view;
        public float body;
        public float pitch;
        public int clipped;

        public Ctx(VertexHelper vh, Rect area, View view)
        {
            this.vh = vh;
            this.area = area;
            this.view = view;
            body = view.Body;
            pitch = view.Pitch;
            clipped = 0;
        }

        public float X(float unitSpace) => area.center.x + (unitSpace - view.center.x) * pitch;
        public float Y(float unitSpace) => area.center.y + (unitSpace - view.center.y) * pitch;
        public float FineStart(int i) => (i >> 1) + ((i & 1) != 0 ? body : 0f);
    }

    private static bool Filled(DungeonMapRoom room, int i, int j)
    {
        int x = i >> 1, y = j >> 1;
        bool oddI = (i & 1) != 0, oddJ = (j & 1) != 0;
        if (!oddI && !oddJ) return Has(room, x, y);
        if (oddI && !oddJ) return Has(room, x, y) && Has(room, x + 1, y);
        if (!oddI) return Has(room, x, y) && Has(room, x, y + 1);
        return Has(room, x, y) && Has(room, x + 1, y) && Has(room, x, y + 1) && Has(room, x + 1, y + 1);
    }

    private static bool Has(DungeonMapRoom room, int x, int y)
    {
        var units = room.units;
        for (int n = 0; n < units.Length; n++) if (units[n].x == x && units[n].y == y) return true;
        return false;
    }

    private static void FineRange(DungeonMapRoom room, out int i0, out int i1, out int j0, out int j1)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var u in room.units)
        {
            minX = Math.Min(minX, u.x); maxX = Math.Max(maxX, u.x);
            minY = Math.Min(minY, u.y); maxY = Math.Max(maxY, u.y);
        }
        i0 = minX * 2; i1 = maxX * 2; j0 = minY * 2; j1 = maxY * 2;
    }

    private static void FillRoom(ref Ctx ctx, DungeonMapRoom room, Color32 tint, ref MapStats stats, float inset = 0f)
    {
        FineRange(room, out int i0, out int i1, out int j0, out int j1);
        if (inset > 0f)
        {
            FillRoomInset(ref ctx, room, tint, inset, i0, i1, j0, j1);
            return;
        }
        for (int j = j0; j <= j1; j++)
        {
            // 한 줄에서 이어진 칸은 사각형 하나로 합친다(정점 수 절약, 겹침 없음).
            int run = int.MinValue;
            for (int i = i0; i <= i1 + 1; i++)
            {
                bool on = i <= i1 && Filled(room, i, j);
                if (on && run == int.MinValue) run = i;
                if (!on && run != int.MinValue)
                {
                    var r = Rect.MinMaxRect(ctx.X(ctx.FineStart(run)), ctx.Y(ctx.FineStart(j)), ctx.X(ctx.FineStart(i)), ctx.Y(ctx.FineStart(j + 1)));
                    AddClipped(ref ctx, r, tint);
                    run = int.MinValue;
                }
            }
        }
    }

    // 작은 방 채움: 세분 칸마다 바깥(빈 칸)과 맞닿은 변을 inset만큼 안으로 들이고, 오목한 모서리(두 이웃은 차 있고 대각선 칸이 빔)는
    // 그 모서리의 inset×inset 사각형을 비운다. 칸 하나를 아래·가운데·위 세 띠로 나눠 쓴다(겹침 없음).
    private static void FillRoomInset(ref Ctx ctx, DungeonMapRoom room, Color32 tint, float inset, int i0, int i1, int j0, int j1)
    {
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                if (!Filled(room, i, j)) continue;
                bool left = Filled(room, i - 1, j), right = Filled(room, i + 1, j), down = Filled(room, i, j - 1), up = Filled(room, i, j + 1);
                float x0 = ctx.X(ctx.FineStart(i)) + (left ? 0f : inset);
                float x1 = ctx.X(ctx.FineStart(i + 1)) - (right ? 0f : inset);
                float y0 = ctx.Y(ctx.FineStart(j)) + (down ? 0f : inset);
                float y1 = ctx.Y(ctx.FineStart(j + 1)) - (up ? 0f : inset);
                if (x1 - x0 < .01f || y1 - y0 < .01f) continue;
                bool cutBL = left && down && !Filled(room, i - 1, j - 1);
                bool cutBR = right && down && !Filled(room, i + 1, j - 1);
                bool cutTL = left && up && !Filled(room, i - 1, j + 1);
                bool cutTR = right && up && !Filled(room, i + 1, j + 1);
                float s = Mathf.Min(inset, Mathf.Min(x1 - x0, y1 - y0) * .5f);
                float yb = (cutBL || cutBR) ? y0 + s : y0;
                float yt = (cutTL || cutTR) ? y1 - s : y1;
                if (yb > y0) AddClipped(ref ctx, Rect.MinMaxRect(x0 + (cutBL ? s : 0f), y0, x1 - (cutBR ? s : 0f), yb), tint);
                if (yt > yb) AddClipped(ref ctx, Rect.MinMaxRect(x0, yb, x1, yt), tint);
                if (y1 > yt) AddClipped(ref ctx, Rect.MinMaxRect(x0 + (cutTL ? s : 0f), yt, x1 - (cutTR ? s : 0f), y1), tint);
            }
    }

    // 경계 선분을 같은 선 위에서 이어 붙인 뒤 그린다(반투명 선이 이음매에서 겹쳐 진해지지 않게).
    private static readonly List<(int line, int normal, int index)> HorizontalEdges = new List<(int, int, int)>();
    private static readonly List<(int line, int normal, int index)> VerticalEdges = new List<(int, int, int)>();

    private static void OutlineRoom(ref Ctx ctx, DungeonMapRoom room, Color32 tint, float width, bool dashed, Style style, ref MapStats stats, float inset = 0f)
    {
        FineRange(room, out int i0, out int i1, out int j0, out int j1);
        HorizontalEdges.Clear();
        VerticalEdges.Clear();
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                if (!Filled(room, i, j)) continue;
                if (!Filled(room, i, j + 1)) HorizontalEdges.Add((j + 1, 1, i));
                if (!Filled(room, i, j - 1)) HorizontalEdges.Add((j, -1, i));
                if (!Filled(room, i + 1, j)) VerticalEdges.Add((i + 1, 1, j));
                if (!Filled(room, i - 1, j)) VerticalEdges.Add((i, -1, j));
            }
        EmitEdges(ref ctx, HorizontalEdges, true, tint, width, dashed, style, room, inset);
        EmitEdges(ref ctx, VerticalEdges, false, tint, width, dashed, style, room, inset);
    }

    // inset > 0(작은 방)이면 변을 안쪽(채운 쪽)으로 inset만큼 옮기고, 끝은 볼록한 모서리면 inset만큼 줄이고 오목한 모서리면 inset만큼 늘인다.
    private static void EmitEdges(ref Ctx ctx, List<(int line, int normal, int index)> edges, bool horizontal, Color32 tint, float width, bool dashed, Style style,
                                  DungeonMapRoom room = null, float inset = 0f)
    {
        edges.Sort((a, b) => a.line != b.line ? a.line.CompareTo(b.line) : a.normal != b.normal ? a.normal.CompareTo(b.normal) : a.index.CompareTo(b.index));
        int n = 0;
        while (n < edges.Count)
        {
            var first = edges[n];
            int last = first.index;
            int m = n + 1;
            while (m < edges.Count && edges[m].line == first.line && edges[m].normal == first.normal && edges[m].index == last + 1) { last = edges[m].index; m++; }
            float along0 = ctx.FineStart(first.index), along1 = ctx.FineStart(last + 1), across = ctx.FineStart(first.line);
            if (inset > 0f && room != null)
            {
                int outer = first.normal > 0 ? first.line : first.line - 1;     // 변 바깥쪽 세분 줄
                bool startConcave = horizontal ? Filled(room, first.index - 1, outer) : Filled(room, outer, first.index - 1);
                bool endConcave = horizontal ? Filled(room, last + 1, outer) : Filled(room, outer, last + 1);
                Segment(ref ctx, horizontal, along0, along1, across, tint, width, dashed, style,
                        startConcave ? -inset : inset, endConcave ? inset : -inset, -first.normal * inset);
            }
            else Segment(ref ctx, horizontal, along0, along1, across, tint, width, dashed, style);
            n = m;
        }
    }

    // 단위 공간의 선분(가로면 along = x, across = y)을 화면 폭 width로 그린다. 양 끝은 반폭만큼 늘여 모서리를 닫는다.
    // shift0·shift1·shiftAcross는 화면 px 보정(작은 방 안쪽 여백): 시작·끝·가로지르는 좌표에 더한다.
    private static void Segment(ref Ctx ctx, bool horizontal, float along0, float along1, float across, Color32 tint, float width, bool dashed, Style style,
                                float shift0 = 0f, float shift1 = 0f, float shiftAcross = 0f)
    {
        float h = width * .5f;
        float a0 = horizontal ? ctx.X(along0) : ctx.Y(along0);
        float a1 = horizontal ? ctx.X(along1) : ctx.Y(along1);
        float c = horizontal ? ctx.Y(across) : ctx.X(across);
        a0 += shift0; a1 += shift1; c += shiftAcross;
        a0 -= h; a1 += h;
        if (!dashed)
        {
            AddClipped(ref ctx, horizontal ? Rect.MinMaxRect(a0, c - h, a1, c + h) : Rect.MinMaxRect(c - h, a0, c + h, a1), tint);
            return;
        }
        float dash = Mathf.Max(style.dashMin, ctx.view.cell * style.dashLength);
        float gap = Mathf.Max(style.dashMin * .7f, ctx.view.cell * style.dashGap);
        // 양 끝이 선분으로 끝나도록 틈을 고르게 나눈다.
        float length = a1 - a0;
        int count = Mathf.Max(1, Mathf.FloorToInt((length + gap) / (dash + gap)));
        float spare = count > 1 ? (length - count * dash) / (count - 1) : 0f;
        if (count == 1) dash = length;
        for (int k = 0; k < count; k++)
        {
            float s = a0 + k * (dash + spare);
            float e = Mathf.Min(a1, s + dash);
            AddClipped(ref ctx, horizontal ? Rect.MinMaxRect(s, c - h, e, c + h) : Rect.MinMaxRect(c - h, s, c + h, e), tint);
        }
    }

    // insetLeft·insetRight: 다리 왼쪽·오른쪽 방이 작은 방이면 그 몸체가 줄어든 양(px) — 그만큼 다리를 더 뻗는다.
    private static void Bridge(ref Ctx ctx, Vector2Int unit, DungeonDoorSide side, Color32 tint, Style style, ref MapStats stats, float insetLeft = 0f, float insetRight = 0f)
    {
        float k = ctx.body;
        float reach = Mathf.Max(1.5f, ctx.view.cell * style.bridgeReach) / ctx.pitch;   // 단위 공간
        float from, to;
        if (side == DungeonDoorSide.Right) { from = unit.x + k - reach; to = unit.x + 1f + reach; }
        else { from = unit.x - (1f - k) - reach; to = unit.x + reach; }
        float y = ctx.Y(unit.y + k * .5f), h = style.bridgeWidth * .5f;
        AddClipped(ref ctx, Rect.MinMaxRect(ctx.X(from) - insetLeft, y - h, ctx.X(to) + insetRight, y + h), tint);
        // 다리 양 끝의 짧은 세로 눈금(문 자리)
        float tick = Mathf.Max(2f, ctx.view.cell * .12f);
        float tickLeft = ctx.X(from + reach * .5f) - insetLeft, tickRight = ctx.X(to - reach * .5f) + insetRight;
        AddClipped(ref ctx, Rect.MinMaxRect(tickLeft - h * .8f, y - tick, tickLeft + h * .8f, y + tick), tint);
        AddClipped(ref ctx, Rect.MinMaxRect(tickRight - h * .8f, y - tick, tickRight + h * .8f, y + tick), tint);
    }

    // 임의 방향 점선(텔레포트 연결). 선분은 회전한 사각형이라 영역으로 자를 수 없으므로, 네 꼭짓점이 모두 영역 안인 선분만 그린다.
    private static void DashedLine(ref Ctx ctx, Vector2 a, Vector2 b, float width, float dash, float gap, Color32 tint)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 1e-3f) return;
        d /= length;
        Vector2 n = new Vector2(-d.y, d.x) * (width * .5f);
        int count = Mathf.Max(1, Mathf.FloorToInt((length + gap) / (dash + gap)));
        float spare = count > 1 ? (length - count * dash) / (count - 1) : 0f;
        if (count == 1) dash = length;
        var area = ctx.area;
        for (int k = 0; k < count; k++)
        {
            float s = k * (dash + spare), e = Mathf.Min(length, s + dash);
            Vector2 p0 = a + d * s, p1 = a + d * e;
            Vector2 c0 = p0 - n, c1 = p1 - n, c2 = p1 + n, c3 = p0 + n;
            if (!area.Contains(c0) || !area.Contains(c1) || !area.Contains(c2) || !area.Contains(c3)) { ctx.clipped++; continue; }
            var vh = ctx.vh;
            int i = vh.currentVertCount;
            vh.AddVert(c0, tint, Vector2.zero); vh.AddVert(c1, tint, Vector2.zero);
            vh.AddVert(c2, tint, Vector2.zero); vh.AddVert(c3, tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }

    private static bool HasLink(DungeonMapRoom room, Vector2Int unit, DungeonDoorSide side, int toRoom)
    {
        if (room.links == null) return false;
        foreach (var l in room.links) if (l.unit == unit && l.side == side && l.toRoom == toRoom) return true;
        return false;
    }

    private static DungeonMapRoom Find(IReadOnlyList<DungeonMapRoom> rooms, int id)
    {
        if (rooms == null || id < 0) return null;
        for (int r = 0; r < rooms.Count; r++) if (rooms[r] != null && rooms[r].id == id) return rooms[r];
        return null;
    }

    private static Rect CenteredRect(Vector2 c, float size) => new Rect(c.x - size * .5f, c.y - size * .5f, size, size);

    // 영역으로 잘라 사각형을 쓴다. 다 잘리면 false.
    private static bool AddClipped(ref Ctx ctx, Rect r, Color32 tint)
    {
        var a = ctx.area;
        float x0 = Mathf.Max(r.xMin, a.xMin), x1 = Mathf.Min(r.xMax, a.xMax);
        float y0 = Mathf.Max(r.yMin, a.yMin), y1 = Mathf.Min(r.yMax, a.yMax);
        if (x1 - x0 < .01f || y1 - y0 < .01f) { ctx.clipped++; return false; }
        if (x0 > r.xMin || x1 < r.xMax || y0 > r.yMin || y1 < r.yMax) ctx.clipped++;
        AddQuad(ctx.vh, Rect.MinMaxRect(x0, y0, x1, y1), tint);
        return true;
    }

    // 마름모는 영역에 다 들어올 때만 그린다(자르면 모양이 무너진다).
    private static bool Diamond(ref Ctx ctx, Vector2 c, float half, Color32 tint)
    {
        half = Mathf.Max(2f, half);
        var a = ctx.area;
        if (c.x - half < a.xMin || c.x + half > a.xMax || c.y - half < a.yMin || c.y + half > a.yMax) return false;
        var vh = ctx.vh;
        int i = vh.currentVertCount;
        vh.AddVert(new Vector2(c.x, c.y - half), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x + half, c.y), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x, c.y + half), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x - half, c.y), tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        return true;
    }

    // ===== 3차(2026-11-24): 실제 방 모양 =====

    /// <summary>
    /// 방 하나의 실루엣(셀 좌표 행 런). 바깥 빈칸(외접 + 1칸 테두리에서 flood fill)이 아닌 빈칸 = 실내 공기, 공기와 8방향으로 맞닿은 Platform = 벽 테두리,
    /// 공기 안의 OneWayPlatform = 얇은 선. 벽 3칸 전체를 칠하지 않아 지도가 깔끔하다. 방마다 한 번 만들고 고치지 않는다(UI가 방 id로 캐시).
    /// </summary>
    public sealed class RoomSilhouette
    {
        public int roomId = -1;
        /// <summary>만든 원본(같은 원본이면 다시 만들지 않는다).</summary>
        public DungeonRoomDetail source;
        /// <summary>그릴 범위(공기 ∪ 테두리, 셀 좌표). 비어 있으면 width 0.</summary>
        public RectInt bounds;
        /// <summary>행 런: RectInt(x, y, 길이, 1).</summary>
        public readonly List<RectInt> air = new List<RectInt>();
        public readonly List<RectInt> border = new List<RectInt>();
        public readonly List<RectInt> oneWay = new List<RectInt>();
        public int airCells, borderCells, oneWayCells;
        /// <summary>막힌 실내를 찾지 못해 실내 외접 범위(interior)로 대신했는지.</summary>
        public bool fallback;
        public bool IsEmpty => airCells == 0 && borderCells == 0;
        /// <summary>셀 좌표 (x, y)가 그 런 목록에 들어 있는지(검증용).</summary>
        public static bool Contains(List<RectInt> runs, int x, int y)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                if (y == r.y && x >= r.x && x < r.x + r.width) return true;
            }
            return false;
        }
    }

    /// <summary>실루엣이 너무 크면(잘못된 셀) 실내 범위로 대신한다(칸 수 상한).</summary>
    public const int MaxSilhouetteCells = 400000;

    public static RoomSilhouette BuildSilhouette(DungeonRoomDetail detail)
    {
        if (detail == null) return new RoomSilhouette();
        var s = BuildSilhouette(detail.cells, detail.interior, detail.id);
        s.source = detail;
        return s;
    }

    /// <summary>방 셀(방 로컬 셀 좌표)로 실루엣을 만든다. 셀 (x, y)는 방 로컬 [x, x+1]×[y, y+1]을 차지한다.</summary>
    public static RoomSilhouette BuildSilhouette(WaveMapCell[] cells, Rect interior, int roomId = -1)
    {
        var s = new RoomSilhouette { roomId = roomId };
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        if (cells != null)
            foreach (var c in cells)
            {
                xMin = Math.Min(xMin, c.position.x); yMin = Math.Min(yMin, c.position.y);
                xMax = Math.Max(xMax, c.position.x); yMax = Math.Max(yMax, c.position.y);
            }
        if (xMin == int.MaxValue)
        {
            InteriorFallback(s, interior);
            return s;
        }
        // 실내 범위도 격자 안에 들어오게 넓힌다(대체 그림용).
        if (interior.width > 0f && interior.height > 0f)
        {
            xMin = Math.Min(xMin, Mathf.FloorToInt(interior.xMin)); yMin = Math.Min(yMin, Mathf.FloorToInt(interior.yMin));
            xMax = Math.Max(xMax, Mathf.CeilToInt(interior.xMax) - 1); yMax = Math.Max(yMax, Mathf.CeilToInt(interior.yMax) - 1);
        }
        int ox = xMin - 1, oy = yMin - 1, w = xMax - xMin + 3, h = yMax - yMin + 3;
        if ((long)w * h > MaxSilhouetteCells)
        {
            InteriorFallback(s, interior);
            return s;
        }
        // 0 빈칸, 1 고체(Platform), 2 OneWay
        var kind = new byte[w * h];
        foreach (var c in cells)
        {
            int i = (c.position.x - ox) + (c.position.y - oy) * w;
            if (c.kind == WaveCellKind.Platform) kind[i] = 1;
            else if (kind[i] == 0) kind[i] = 2;
        }
        // 바깥 빈칸: 테두리 1칸(늘 빈칸)에서 4방향 flood fill. OneWay는 지나갈 수 있는 칸으로 본다.
        var outside = new bool[w * h];
        var queue = new int[w * h];
        int head = 0, tail = 0;
        outside[0] = true;
        queue[tail++] = 0;
        while (head < tail)
        {
            int i = queue[head++];
            int x = i % w, y = i / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int n = nx + ny * w;
                if (outside[n] || kind[n] == 1) continue;
                outside[n] = true;
                queue[tail++] = n;
            }
        }
        var air = new bool[w * h];
        int airCount = 0;
        for (int i = 0; i < air.Length; i++)
            if (!outside[i] && kind[i] != 1) { air[i] = true; airCount++; }
        if (airCount == 0 && interior.width > 0f && interior.height > 0f)
        {
            // 방이 바깥으로 열려 있다(막힌 실내 없음): 실내 범위 안의 빈칸을 공기로 본다.
            s.fallback = true;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    float cx = x + ox + .5f, cy = y + oy + .5f;
                    if (kind[i] != 1 && interior.Contains(new Vector2(cx, cy))) { air[i] = true; airCount++; }
                }
        }
        // 행 런
        for (int y = 0; y < h; y++)
        {
            int airRun = -1, borderRun = -1, oneWayRun = -1;
            for (int x = 0; x <= w; x++)
            {
                int i = x + y * w;
                bool isAir = x < w && air[i];
                bool isOneWay = isAir && kind[i] == 2;
                bool isBorder = x < w && kind[i] == 1 && TouchesAir(air, w, h, x, y);
                Run(ref airRun, isAir, x, y, ox, oy, s.air, ref s.airCells);
                Run(ref oneWayRun, isOneWay, x, y, ox, oy, s.oneWay, ref s.oneWayCells);
                Run(ref borderRun, isBorder, x, y, ox, oy, s.border, ref s.borderCells);
            }
        }
        if (s.IsEmpty)
        {
            InteriorFallback(s, interior);
            return s;
        }
        s.bounds = RunBounds(s);
        return s;
    }

    private static bool TouchesAir(bool[] air, int w, int h, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                if (air[nx + ny * w]) return true;
            }
        return false;
    }

    private static void Run(ref int start, bool on, int x, int y, int ox, int oy, List<RectInt> runs, ref int count)
    {
        if (on && start < 0) start = x;
        if (!on && start >= 0)
        {
            runs.Add(new RectInt(start + ox, y + oy, x - start, 1));
            count += x - start;
            start = -1;
        }
    }

    private static RectInt RunBounds(RoomSilhouette s)
    {
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        void Add(List<RectInt> runs)
        {
            foreach (var r in runs)
            {
                xMin = Math.Min(xMin, r.x); yMin = Math.Min(yMin, r.y);
                xMax = Math.Max(xMax, r.x + r.width); yMax = Math.Max(yMax, r.y + 1);
            }
        }
        Add(s.air);
        Add(s.border);
        return xMin == int.MaxValue ? default : new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    // 셀이 없거나 너무 클 때: 실내 범위를 공기, 그 둘레 1칸을 테두리로 본다.
    private static void InteriorFallback(RoomSilhouette s, Rect interior)
    {
        s.fallback = true;
        s.air.Clear(); s.border.Clear(); s.oneWay.Clear();
        s.airCells = s.borderCells = s.oneWayCells = 0;
        if (interior.width <= 0f || interior.height <= 0f) { s.bounds = default; return; }
        int x0 = Mathf.FloorToInt(interior.xMin), y0 = Mathf.FloorToInt(interior.yMin);
        int x1 = Mathf.Max(x0 + 1, Mathf.CeilToInt(interior.xMax)), y1 = Mathf.Max(y0 + 1, Mathf.CeilToInt(interior.yMax));
        int width = x1 - x0;
        for (int y = y0; y < y1; y++)
        {
            s.air.Add(new RectInt(x0, y, width, 1));
            s.airCells += width;
            s.border.Add(new RectInt(x0 - 1, y, 1, 1));
            s.border.Add(new RectInt(x1, y, 1, 1));
            s.borderCells += 2;
        }
        s.border.Add(new RectInt(x0 - 1, y0 - 1, width + 2, 1));
        s.border.Add(new RectInt(x0 - 1, y1, width + 2, 1));
        s.borderCells += 2 * (width + 2);
        s.bounds = new RectInt(x0 - 1, y0 - 1, width + 2, y1 - y0 + 2);
    }

    /// <summary>방 로컬 좌표 → 화면(그릴 영역 좌표): origin + local × cell.</summary>
    [Serializable]
    public struct RoomView
    {
        public Vector2 origin;
        /// <summary>셀 한 칸의 화면 길이(px).</summary>
        public float cell;
        public Vector2 ToScreen(Vector2 local) => origin + local * cell;
        public Rect CellRect(RectInt cells) => new Rect(origin.x + cells.x * cell, origin.y + cells.y * cell, cells.width * cell, cells.height * cell);
    }

    /// <summary>실루엣을 영역 가운데에 맞춘다(셀 px 상한 maxCell).</summary>
    public static RoomView FitRoom(RoomSilhouette s, Rect area, float padding, float maxCell)
    {
        if (s == null || s.bounds.width <= 0 || s.bounds.height <= 0) return new RoomView { origin = area.center, cell = Mathf.Max(1f, maxCell) };
        float availW = Mathf.Max(1f, area.width - 2f * padding), availH = Mathf.Max(1f, area.height - 2f * padding);
        float cell = Mathf.Min(availW / s.bounds.width, availH / s.bounds.height);
        cell = Mathf.Min(cell, Mathf.Max(.5f, maxCell));
        Vector2 center = s.bounds.center;
        return new RoomView { origin = area.center - center * cell, cell = cell };
    }

    /// <summary>방 실루엣을 쓴다(공기 채움 → 벽 테두리 → OneWay 선). 그린 정점 수를 돌려준다. 모든 사각형은 영역으로 잘린다.</summary>
    public static int WriteSilhouette(VertexHelper vh, RoomSilhouette s, Rect area, RoomView view, Color32 air, Color32 wall, Color32 oneWay, Style style, ref int clipped)
    {
        style ??= Style.Default;
        if (s == null || view.cell <= 0f) return 0;
        int start = vh.currentVertCount;
        var ctx = new Ctx(vh, area, default);
        if (air.a > 0) foreach (var r in s.air) AddClipped(ref ctx, view.CellRect(r), air);
        if (wall.a > 0) foreach (var r in s.border) AddClipped(ref ctx, view.CellRect(r), wall);
        if (oneWay.a > 0)
        {
            float t = Mathf.Max(1.2f, view.cell * style.oneWayThickness);
            foreach (var r in s.oneWay)
            {
                var rect = view.CellRect(r);
                AddClipped(ref ctx, Rect.MinMaxRect(rect.xMin, rect.yMax - t, rect.xMax, rect.yMax), oneWay);
            }
        }
        clipped += ctx.clipped;
        return vh.currentVertCount - start;
    }

    /// <summary>지금 방 자세히 표식 결과(검증용).</summary>
    public struct RoomMarkStats
    {
        public int vertices;
        public int doors, openDoors, lockedDoors, sealedDoors;
        public int teleports, repairs, objectives, objectivesDone, facilities;
        /// <summary>4차: 출구 문 표식 수·그중 열린 것.</summary>
        public int exits, exitsOpen;
        /// <summary>5차: 경로 크레인·고정 크레인·점프대 배지 수, 그린 도크 수(경로 하나에 양 끝 2).</summary>
        public int cranePaths, craneAnchors, jumpPads, docks;
        /// <summary>5차: 출입구 이웃 방 정보 점(전체·보스·방문 미클리어·클리어·미방문).</summary>
        public int neighborDots, neighborBoss, neighborOpen, neighborClear, neighborUnknown;
        /// <summary>5차: 출구 램프(전체·켜진 것).</summary>
        public int exitLamps, exitLampsLit;
        /// <summary>5차: 그린 배지 중 가장 작은 한 변(px, 없으면 0).</summary>
        public float minBadge;
        public int clipped;
    }

    /// <summary>목표 진행 번호 index가 완료됐는지(표식 목록에서 찾는다. 없으면 false).</summary>
    public static bool ObjectiveDone(IReadOnlyList<DungeonObjectiveMarker> markers, int room, int index)
    {
        if (markers == null) return false;
        for (int i = 0; i < markers.Count; i++)
            if (markers[i].index == index && (markers[i].room == room || room < 0)) return markers[i].done;
        return false;
    }

    /// <summary>
    /// 지금 방 표식(5차 S9 — 셀 크기와 무관한 고정 크기 배지형 픽토그램, 셀 2.3~9px 방에서도 11~16px):
    /// 연결된 출입구(실제 크기 2×3칸·최소 6×10px, 관 입구 플랜지 + 열림 = 밝은 청록 관 + 바깥 겹 꺾쇠, 잠김 = 빨강 몸통 + 셔터 줄 + 볼트, 봉인 = 빨강 테두리 + 경고 줄무늬;
    /// rooms가 있으면 출입구 위 이웃 방 정보 점), 텔레포트 링(짝 없으면 흐림 + 사선), 수리대(흰 배지 + 초록 십자), 레버(배전반 손잡이 — 완료 = 앰버 채움 + 녹색 표시등,
    /// rooms로 본 이 방이 미클리어면 흐림), 출구 문(문틀·볼트 + 위쪽 램프 = 레버 수·켜진 램프 = 완료 수, <see cref="ExitOpen"/>), 경로 크레인(훅 배지 + 점선 경로 + 양 끝 도크 —
    /// <see cref="DungeonRoomDetail.cranePaths"/>), 고정 크레인(고리 + 사슬), 점프대(받침 + 겹 꺾쇠), 그 밖의 시설(작은 점).
    /// 배지는 영역 안으로 붙잡는다(잘리지 않게). states가 없으면 출입구는 열림으로 본다.
    /// </summary>
    public static RoomMarkStats WriteRoomMarks(VertexHelper vh, DungeonRoomDetail detail, IDungeonDetailSource states, IReadOnlyList<DungeonObjectiveMarker> objectives,
                                               Rect area, RoomView view, Style style = null, bool drawDoors = true, IReadOnlyList<DungeonMapRoom> rooms = null)
    {
        style ??= Style.Default;
        var stats = new RoomMarkStats();
        if (detail == null || view.cell <= 0f) return stats;
        int start = vh.currentVertCount;
        var ctx = new Ctx(vh, area, default);
        float cell = view.cell;
        float side = BadgeSide(cell, false, style), half = side * .5f;
        var lift = new Vector2(0f, style.craneLift);
        if (drawDoors && detail.doors != null)
            foreach (var door in detail.doors)
            {
                if (door.toRoom < 0) continue;
                var state = states != null ? states.DoorState(detail.id, door.socket) : DungeonDoor.DoorState.Open;
                var c = view.ToScreen(door.local);
                DoorGlyph(ref ctx, c, door.side, state, cell, style);
                stats.doors++;
                if (state == DungeonDoor.DoorState.Open) stats.openDoors++;
                else if (state == DungeonDoor.DoorState.Locked) stats.lockedDoors++;
                else stats.sealedDoors++;
                if (rooms != null) NeighborDot(ref ctx, DoorRect(c, cell), Find(rooms, door.toRoom), style, ref stats);
            }
        var self = rooms != null ? Find(rooms, detail.id) : null;
        bool roomReady = self == null || self.cleared;
        ObjectiveCounts(states, objectives, out int objectiveDone, out int objectiveTotal);
        if (detail.points != null)
            foreach (var point in detail.points)
            {
                var c = view.ToScreen(point.local);
                switch (point.kind)
                {
                    case DungeonDetailMarkerKind.Teleport:
                        if (TeleportBadge(ref ctx, ClampBadge(area, c, half), side, point.toRoom >= 0, style)) { stats.teleports++; NoteBadge(ref stats.minBadge, side); }
                        break;
                    case DungeonDetailMarkerKind.Repair:
                        if (RepairBadge(ref ctx, ClampBadge(area, c, half), side, style)) { stats.repairs++; NoteBadge(ref stats.minBadge, side); }
                        break;
                    case DungeonDetailMarkerKind.Objective:
                        bool done = ObjectiveDone(objectives, detail.id, point.index);
                        if (LeverBadge(ref ctx, ClampBadge(area, c, half), side, done, roomReady, style))
                        {
                            stats.objectives++;
                            if (done) stats.objectivesDone++;
                            NoteBadge(ref stats.minBadge, side);
                        }
                        break;
                    case DungeonDetailMarkerKind.Exit:
                        bool open = ExitOpen(states);
                        if (ExitBadge(ref ctx, ClampBadge(area, c, half), side, open, objectiveDone, objectiveTotal, style, ref stats.exitLamps, ref stats.exitLampsLit))
                        {
                            stats.exits++;
                            if (open) stats.exitsOpen++;
                            NoteBadge(ref stats.minBadge, side);
                        }
                        break;
                    case DungeonDetailMarkerKind.CranePath:
                        stats.docks += CranePathTrack(ref ctx, detail, point.index, view, side, style);
                        if (CraneHookBadge(ref ctx, ClampBadge(area, view.ToScreen(point.local + lift), half), side, style)) { stats.cranePaths++; NoteBadge(ref stats.minBadge, side); }
                        break;
                    case DungeonDetailMarkerKind.CraneAnchor:
                        if (CraneAnchorBadge(ref ctx, ClampBadge(area, view.ToScreen(point.local + lift), half), side, style)) { stats.craneAnchors++; NoteBadge(ref stats.minBadge, side); }
                        break;
                    case DungeonDetailMarkerKind.JumpPad:
                        if (JumpPadBadge(ref ctx, ClampBadge(area, c, half), side, style)) { stats.jumpPads++; NoteBadge(ref stats.minBadge, side); }
                        break;
                    default:
                        float f = Mathf.Max(2.5f, cell * .9f);
                        if (AddClipped(ref ctx, CenteredRect(c, f), style.facility)) stats.facilities++;
                        break;
                }
            }
        stats.clipped = ctx.clipped;
        stats.vertices = vh.currentVertCount - start;
        return stats;
    }

    // 출입구 관 한 개(점유 2×3칸 — 최소 6×10px, 가운데 c). 위·아래 플랜지(몸통보다 양옆으로 조금 나온 테)는 모든 상태에 그린다.
    // 열림 = 밝은 청록 몸통 + 바깥 겹 꺾쇠(»), 잠김 = 빨강 몸통 + 어두운 셔터 줄 2개 + 가운데 볼트, 봉인 = 빨강 테두리 + 대각 경고 줄무늬 2개.
    private static void DoorGlyph(ref Ctx ctx, Vector2 c, DungeonDoorSide side, DungeonDoor.DoorState state, float cell, Style style)
    {
        var rect = DoorRect(c, cell);
        float hw = rect.width * .5f;
        float flange = Mathf.Max(1.2f, cell * .35f), lip = DoorLip(rect);
        Color32 main = state == DungeonDoor.DoorState.Open ? style.doorOpen : state == DungeonDoor.DoorState.Locked ? style.doorLocked : style.doorSealed;
        AddClipped(ref ctx, Rect.MinMaxRect(rect.xMin - lip, rect.yMax - flange, rect.xMax + lip, rect.yMax), main);
        AddClipped(ref ctx, Rect.MinMaxRect(rect.xMin - lip, rect.yMin, rect.xMax + lip, rect.yMin + flange), main);
        if (state == DungeonDoor.DoorState.Sealed)
        {
            float t = Mathf.Max(1.2f, cell * .45f);
            AddClipped(ref ctx, Rect.MinMaxRect(rect.xMin, rect.yMin + flange, rect.xMin + t, rect.yMax - flange), style.doorSealed);
            AddClipped(ref ctx, Rect.MinMaxRect(rect.xMax - t, rect.yMin + flange, rect.xMax, rect.yMax - flange), style.doorSealed);
            float span = rect.width - 2f * t;
            for (int k = 0; k < 2; k++)
            {
                float y0 = Mathf.Lerp(rect.yMin + flange, rect.yMax - flange - span, k == 0 ? .15f : .75f);
                Line(ref ctx, new Vector2(rect.xMin + t, y0), new Vector2(rect.xMax - t, y0 + span), Mathf.Max(1f, cell * .3f), style.doorSealed);
            }
            return;
        }
        AddClipped(ref ctx, rect, main);
        if (state == DungeonDoor.DoorState.Locked)
        {
            float line = Mathf.Max(1f, cell * .25f);
            for (int k = 1; k <= 2; k++)
            {
                float y = Mathf.Lerp(rect.yMin, rect.yMax, k / 3f);
                AddClipped(ref ctx, Rect.MinMaxRect(rect.xMin + 1f, y - line * .5f, rect.xMax - 1f, y + line * .5f), style.doorShutter);
            }
            AddClipped(ref ctx, CenteredRect(c, Mathf.Max(1.5f, hw * .7f)), style.doorShutter);
            return;
        }
        float dir = side == DungeonDoorSide.Right ? 1f : -1f;
        float baseX = dir > 0f ? rect.xMax + lip + 1f : rect.xMin - lip - 1f;
        DoorChevron(cell, out float length, out float spread, out float step);
        for (int k = 0; k < 2; k++)
        {
            float x = baseX + dir * k * step;
            Triangle(ref ctx, new Vector2(x, c.y - spread), new Vector2(x, c.y + spread), new Vector2(x + dir * length, c.y), style.doorOpen);
        }
    }

    // 목표 표식: 완료 = 채운 네모 + 밝은 테두리, 미완료 = 테두리 + 가운데 손잡이(레버) 막대.
    private static bool ObjectiveGlyph(ref Ctx ctx, Vector2 c, float size, bool done, Style style)
    {
        var r = CenteredRect(c, size);
        var a = ctx.area;
        if (r.xMin < a.xMin || r.xMax > a.xMax || r.yMin < a.yMin || r.yMax > a.yMax) { ctx.clipped++; return false; }
        float t = Mathf.Max(1.2f, size * .16f);
        if (done)
        {
            AddQuad(ctx.vh, r, style.objective);
            return true;
        }
        var tint = style.objective;
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + t), tint);
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMin, r.yMax - t, r.xMax, r.yMax), tint);
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMin, r.yMin + t, r.xMin + t, r.yMax - t), tint);
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMax - t, r.yMin + t, r.xMax, r.yMax - t), tint);
        float bar = Mathf.Max(1.2f, size * .18f);
        AddQuad(ctx.vh, Rect.MinMaxRect(c.x - bar * .5f, r.yMin + t * 1.5f, c.x + bar * .5f, r.yMax - t * 1.5f), style.objectiveDim);
        return true;
    }

    /// <summary>출구가 열렸는지(states가 IDungeonExitSource를 구현하고 출구가 있고 해금됨). 아니면 잠김으로 그린다.</summary>
    public static bool ExitOpen(IDungeonDetailSource states)
    {
        if (!(states is IDungeonExitSource exits)) return false;
        if (exits is UnityEngine.Object unityObject && unityObject == null) return false;
        return exits.HasExit && exits.ExitUnlocked;
    }

    // 출구 문 표식(문 5×6칸 비율 — 세로로 조금 긴 문틀). 잠김 = 흐린 앰버 문틀 + 가로 볼트 2줄, 열림 = 밝은 앰버 문틀 + 옆으로 밀린 문짝 + 가운데 빛.
    // 영역 밖으로 나가면 그리지 않는다(문 모양이 잘리면 다른 표식처럼 보인다).
    private static bool ExitGlyph(ref Ctx ctx, Vector2 c, float size, bool open, Style style)
    {
        float w = size * .84f, h = size;
        var r = new Rect(c.x - w * .5f, c.y - h * .5f, w, h);
        var a = ctx.area;
        if (r.xMin < a.xMin || r.xMax > a.xMax || r.yMin < a.yMin || r.yMax > a.yMax) { ctx.clipped++; return false; }
        float t = Mathf.Max(1.2f, size * .13f);
        var frame = open ? style.objective : style.objectiveDim;
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + t), frame);
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMin, r.yMax - t, r.xMax, r.yMax), frame);
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMin, r.yMin + t, r.xMin + t, r.yMax - t), frame);
        AddQuad(ctx.vh, Rect.MinMaxRect(r.xMax - t, r.yMin + t, r.xMax, r.yMax - t), frame);
        var inner = Rect.MinMaxRect(r.xMin + t, r.yMin + t, r.xMax - t, r.yMax - t);
        if (inner.width <= .5f || inner.height <= .5f) return true;
        if (open)
        {
            // 문짝 두 장이 좌우로 밀려 문틀 안쪽에 붙고, 가운데가 밝다.
            float leaf = inner.width * .22f;
            AddQuad(ctx.vh, Rect.MinMaxRect(inner.xMin, inner.yMin, inner.xMin + leaf, inner.yMax), style.objective);
            AddQuad(ctx.vh, Rect.MinMaxRect(inner.xMax - leaf, inner.yMin, inner.xMax, inner.yMax), style.objective);
            AddQuad(ctx.vh, Rect.MinMaxRect(inner.xMin + leaf, inner.yMin, inner.xMax - leaf, inner.yMax), style.exitGlow);
            return true;
        }
        // 잠김: 문짝을 가로지르는 볼트 2줄(출구 문의 잠금 볼트와 같은 말).
        float bolt = Mathf.Max(1f, size * .1f);
        for (int i = 0; i < 2; i++)
        {
            float y = Mathf.Lerp(inner.yMin, inner.yMax, i == 0 ? .33f : .67f);
            AddQuad(ctx.vh, Rect.MinMaxRect(r.xMin, y - bolt * .5f, r.xMax, y + bolt * .5f), style.objectiveDim);
        }
        return true;
    }

    /// <summary>출구 안내 층 결과(검증용).</summary>
    public struct ExitGuideStats
    {
        public int vertices;
        /// <summary>밝게 그린 출구 표식(지금 방이 출구 방일 때).</summary>
        public int exits;
        /// <summary>안내한 출입구(지금 방에서 출구 쪽으로 가는 연결 출입구).</summary>
        public int guides;
        public int clipped;
    }

    /// <summary>
    /// 출구 안내 층(4차): exitHere면 그 방의 출구 표식을 밝게(열림) + 둘레 테두리로, guideSocket(0 이상)이 있으면 그 연결 출입구 둘레에 앰버 테두리와
    /// 방 안쪽에서 출입구를 가리키는 겹 화살을 쓴다. 정점은 area 안에만 쓴다. 모양은 고정이고 맥동은 부르는 쪽이 CanvasRenderer 알파로 한다.
    /// 5차 보정(c5): 표식 층(<see cref="WriteRoomMarks"/>)의 5차 배지·출입구 관과 같은 기하를 쓴다 — 출구는 ClampBadge한 배지 가운데에 배지 안 문틀
    /// (<see cref="ExitGlyphSize"/>)만 같은 자리·크기로 밝히고, 테두리는 배지 + 램프 줄 자리(<see cref="ExitMarkBounds"/>)를 여백을 두고 둘러싼다.
    /// 출입구 테두리는 관 + 플랜지 + 바깥 겹 꺾쇠 + 이웃 방 정보 점 자리(<see cref="DoorMarkBounds"/>)를 둘러싼다. 그래서 안내 층(표식 층보다 뒤 형제)이
    /// 새 배지·램프·관 위에 옛 크기 그림이나 테두리 선을 덮어 그리지 않는다.
    /// </summary>
    public static ExitGuideStats WriteExitGuide(VertexHelper vh, DungeonRoomDetail detail, int guideSocket, bool exitHere, Rect area, RoomView view, Style style = null)
    {
        style ??= Style.Default;
        var stats = new ExitGuideStats();
        if (detail == null || view.cell <= 0f) return stats;
        int start = vh.currentVertCount;
        var ctx = new Ctx(vh, area, default);
        float cell = view.cell;
        if (exitHere && detail.points != null)
        {
            // 배지와 같은 기하: 한 변 BadgeSide(고정 11~16px)·가운데 ClampBadge. 여백·두께도 배지 크기를 따른다(셀 크기와 무관).
            float side = BadgeSide(cell, false, style), half = side * .5f;
            float pad = Mathf.Max(1.5f, side * .12f), t = Mathf.Max(1.2f, side * .1f);
            foreach (var point in detail.points)
            {
                if (point.kind != DungeonDetailMarkerKind.Exit) continue;
                var bc = ClampBadge(area, view.ToScreen(point.local), half);
                Outline(ref ctx, Grow(ExitMarkBounds(area, bc, side), pad + t), t, style.exitGuide);
                if (ExitGlyph(ref ctx, bc, ExitGlyphSize(side), true, style)) stats.exits++;
            }
        }
        if (guideSocket >= 0 && detail.doors != null)
            foreach (var door in detail.doors)
            {
                if (door.socket != guideSocket || door.toRoom < 0) continue;
                var c = view.ToScreen(door.local);
                float pad = Mathf.Max(1.5f, cell * .45f), t = Mathf.Max(1.2f, cell * .3f);
                var ring = Grow(DoorMarkBounds(area, c, door.side, cell), pad + t);
                Outline(ref ctx, ring, t, style.exitGuide);
                // 방 안쪽(출입구의 반대편)에서 출입구 쪽을 가리키는 겹 화살. 실내는 판 안에 있으므로 잘리지 않는다.
                float dir = door.side == DungeonDoorSide.Right ? 1f : -1f;
                float tipX = dir > 0f ? ring.xMin - 1.5f : ring.xMax + 1.5f;
                float length = Mathf.Max(4f, cell * 1.6f), half = Mathf.Max(3f, cell * 1.3f), gap = Mathf.Max(2.5f, cell * .9f);
                bool drew = false;
                for (int k = 0; k < 2; k++)
                {
                    float tip = tipX - dir * k * (length * .55f + gap);
                    float back = tip - dir * length;
                    drew |= Triangle(ref ctx, new Vector2(back, c.y - half), new Vector2(back, c.y + half), new Vector2(tip, c.y), style.exitGuide);
                }
                if (drew) stats.guides++;
            }
        stats.clipped = ctx.clipped;
        stats.vertices = vh.currentVertCount - start;
        return stats;
    }

    private static bool Cross(ref Ctx ctx, Vector2 c, float arm, float thick, Color32 tint)
    {
        bool h = AddClipped(ref ctx, Rect.MinMaxRect(c.x - arm, c.y - thick * .5f, c.x + arm, c.y + thick * .5f), tint);
        bool v = AddClipped(ref ctx, Rect.MinMaxRect(c.x - thick * .5f, c.y - arm, c.x + thick * .5f, c.y + arm), tint);
        return h || v;
    }

    // 세 꼭짓점이 모두 영역 안일 때만 그린다.
    private static bool Triangle(ref Ctx ctx, Vector2 a, Vector2 b, Vector2 c, Color32 tint)
    {
        var area = ctx.area;
        if (!area.Contains(a) || !area.Contains(b) || !area.Contains(c)) { ctx.clipped++; return false; }
        var vh = ctx.vh;
        int i = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        return true;
    }

    private static void AddDiamond(VertexHelper vh, Vector2 c, float half, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(new Vector2(c.x, c.y - half), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x + half, c.y), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x, c.y + half), tint, Vector2.zero);
        vh.AddVert(new Vector2(c.x - half, c.y), tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    // ===== 5차(2026-11-25, DESIGN_V5 §1 S9): 배지형 픽토그램 =====
    // 셀 크기와 무관한 고정 크기 배지(어두운 바탕 → 역할 색 테두리 → 밝은 안쪽 그림). 원·고리는 정적 단위 원 표(8각·12각)로 그린다(할당 없음).
    // 배지 함수는 바탕이 영역 안에 다 들어올 때만 그리고 true를 돌려준다(미니맵은 부르는 쪽이 ClampBadge로 영역 안으로 붙잡는다).

    private static readonly Vector2[] Oct = UnitCircle(8, 22.5f);
    private static readonly Vector2[] Circle12 = UnitCircle(12, 0f);

    private static Vector2[] UnitCircle(int n, float startDegrees)
    {
        var points = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = (startDegrees + i * 360f / n) * Mathf.Deg2Rad;
            points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        return points;
    }

    /// <summary>배지 한 변(px): clamp(셀 px × 배율, 최소, 최대). 미니맵(floorMap = false) 11~16, 큰 지도 12~20.</summary>
    public static float BadgeSide(float cell, bool floorMap, Style style = null)
    {
        style ??= Style.Default;
        return floorMap ? Mathf.Clamp(cell * style.floorBadgeScale, style.floorBadgeMin, style.floorBadgeMax)
                        : Mathf.Clamp(cell * style.miniBadgeScale, style.miniBadgeMin, style.miniBadgeMax);
    }

    /// <summary>배지 안쪽 그림 반경(배지 한 변의 0.33배).</summary>
    public static float BadgeInner(float side) => side * .33f;

    /// <summary>출구 배지 안 문틀 높이(px) — 문틀 폭은 0.84배(<see cref="ExitGlyph"/>).</summary>
    public static float ExitGlyphSize(float side) => BadgeInner(side) * 1.7f;

    /// <summary>출입구 표식 사각형(실제 크기 2×3칸, 최소 6×10px) — 가운데 c.</summary>
    public static Rect DoorRect(Vector2 c, float cell)
    {
        float hw = Mathf.Max(3f, cell * DungeonDoor.Depth * .5f), hh = Mathf.Max(5f, cell * DungeonDoor.Height * .5f);
        return Rect.MinMaxRect(c.x - hw, c.y - hh, c.x + hw, c.y + hh);
    }

    /// <summary>출구 배지 램프 줄의 최대 칸 수(레버 수를 이 값으로 자른다).</summary>
    public const int MaxExitLamps = 6;

    // 출구 배지 램프 줄 자리(<see cref="ExitBadge"/>·<see cref="ExitMarkBounds"/>가 함께 쓴다): 램프 한 칸 ls·간격 gap, 줄 가운데 높이 y(배지 위,
    // 자리가 없으면 아래), 왼쪽 끝 x0(영역 안으로 붙잡음). 줄 폭 = n × ls + (n − 1) × gap.
    private static void ExitLampRow(Rect area, Vector2 c, float side, int n, out float ls, out float gap, out float x0, out float y)
    {
        ls = Mathf.Max(2f, side * .18f);
        gap = Mathf.Max(1f, ls * .5f);
        float row = n * ls + (n - 1) * gap;
        y = c.y + side * .5f + 1.5f + ls * .5f;
        if (y + ls * .5f > area.yMax) y = c.y - side * .5f - 1.5f - ls * .5f;
        x0 = Mathf.Clamp(c.x - row * .5f, area.xMin, area.xMax - row);
    }

    /// <summary>
    /// 출구 배지가 차지하는 범위(배지 한 변 side + 램프 줄 <see cref="MaxExitLamps"/>칸 자리 — <see cref="ExitBadge"/>와 같은 기하). bc = ClampBadge한 배지 가운데.
    /// 출구 안내 층이 이 범위를 비켜 테두리를 두른다. 안내 층은 레버 수를 모르므로 최대 칸으로 잡는다(칸이 적은 줄은 같은 규칙으로 이 안에 든다).
    /// </summary>
    public static Rect ExitMarkBounds(Rect area, Vector2 bc, float side)
    {
        float half = side * .5f;
        ExitLampRow(area, bc, side, MaxExitLamps, out float ls, out float gap, out float x0, out float y);
        float row = MaxExitLamps * ls + (MaxExitLamps - 1) * gap;
        return Rect.MinMaxRect(Mathf.Min(bc.x - half, x0), Mathf.Min(bc.y - half, y - ls * .5f),
                               Mathf.Max(bc.x + half, x0 + row), Mathf.Max(bc.y + half, y + ls * .5f));
    }

    // 출입구 관 위·아래 플랜지가 관 옆으로 나온 길이(<see cref="DoorGlyph"/>).
    private static float DoorLip(Rect door) => Mathf.Max(1f, door.width * .5f * .35f);

    // 열린 출입구 바깥 겹 꺾쇠(»): 한 개 길이·반높이·두 개 사이 간격(<see cref="DoorGlyph"/>).
    private static void DoorChevron(float cell, out float length, out float spread, out float step)
    {
        length = Mathf.Max(3f, cell * 1.2f);
        spread = Mathf.Max(2.5f, cell * 1f);
        step = length * .75f;
    }

    // 출입구 위(자리가 없으면 아래)의 이웃 방 정보 점 반경과 가운데(<see cref="NeighborDot"/>). 보스 마름모는 반대각선 반경 × 1.3까지 뻗는다.
    private const float NeighborDotRadius = 2.5f;
    private static float NeighborDotReach => Mathf.Max(2f, NeighborDotRadius * 1.3f);

    private static Vector2 NeighborDotCenter(Rect area, Rect door)
    {
        float r = NeighborDotRadius;
        var c = new Vector2(door.center.x, door.yMax + 2f + r);
        if (c.y + r > area.yMax) c.y = door.yMin - 2f - r;
        return c;
    }

    /// <summary>
    /// 출입구 표식이 차지하는 범위(관 + 위·아래 플랜지 + 바깥 겹 꺾쇠 + 이웃 방 정보 점 자리 — <see cref="DoorGlyph"/>·<see cref="NeighborDot"/>와 같은 기하).
    /// 출구 안내 층이 이 범위를 비켜 테두리를 두른다. 안내 층은 출입구 상태·이웃 방을 모르므로 꺾쇠(열림일 때만 그림)·점 자리를 늘 잡는다.
    /// </summary>
    public static Rect DoorMarkBounds(Rect area, Vector2 c, DungeonDoorSide side, float cell)
    {
        var rect = DoorRect(c, cell);
        float lip = DoorLip(rect);
        DoorChevron(cell, out float length, out float spread, out float step);
        float reach = lip + 1f + step + length;   // 관 옆면에서 바깥 꺾쇠 두 번째 끝까지
        float xMin = rect.xMin - lip, xMax = rect.xMax + lip;
        if (side == DungeonDoorSide.Right) xMax = rect.xMax + reach;
        else xMin = rect.xMin - reach;
        var dot = NeighborDotCenter(area, rect);
        float r = NeighborDotReach;
        return Rect.MinMaxRect(Mathf.Min(xMin, dot.x - r), Mathf.Min(Mathf.Min(rect.yMin, c.y - spread), dot.y - r),
                               Mathf.Max(xMax, dot.x + r), Mathf.Max(Mathf.Max(rect.yMax, c.y + spread), dot.y + r));
    }

    private static Rect Grow(Rect r, float d) => Rect.MinMaxRect(r.xMin - d, r.yMin - d, r.xMax + d, r.yMax + d);

    /// <summary>배지 가운데를 영역 안으로 붙잡는다(배지가 잘리지 않게). 영역이 배지보다 작으면 그대로.</summary>
    public static Vector2 ClampBadge(Rect area, Vector2 c, float half)
    {
        if (area.width < half * 2f || area.height < half * 2f) return c;
        return new Vector2(Mathf.Clamp(c.x, area.xMin + half, area.xMax - half), Mathf.Clamp(c.y, area.yMin + half, area.yMax - half));
    }

    private static bool InArea(Rect a, Vector2 p) => p.x >= a.xMin - .001f && p.x <= a.xMax + .001f && p.y >= a.yMin - .001f && p.y <= a.yMax + .001f;

    private static void PolyRaw(VertexHelper vh, Vector2 c, float r, Vector2[] unit, Color32 tint)
    {
        int i0 = vh.currentVertCount;
        vh.AddVert(c, tint, Vector2.zero);
        for (int k = 0; k < unit.Length; k++) vh.AddVert(c + unit[k] * r, tint, Vector2.zero);
        for (int k = 0; k < unit.Length; k++) vh.AddTriangle(i0, i0 + 1 + k, i0 + 1 + (k + 1) % unit.Length);
    }

    private static void RingRaw(VertexHelper vh, Vector2 c, float r, float t, Vector2[] unit, Color32 tint)
    {
        float ri = Mathf.Max(0f, r - t);
        int i0 = vh.currentVertCount, n = unit.Length;
        for (int k = 0; k < n; k++)
        {
            vh.AddVert(c + unit[k] * r, tint, Vector2.zero);
            vh.AddVert(c + unit[k] * ri, tint, Vector2.zero);
        }
        for (int k = 0; k < n; k++)
        {
            int a = i0 + 2 * k, b = i0 + 2 * ((k + 1) % n);
            vh.AddTriangle(a, b, b + 1);
            vh.AddTriangle(a, b + 1, a + 1);
        }
    }

    // 정다각형 채움(외접 사각형이 영역 안일 때만).
    private static bool FillPoly(ref Ctx ctx, Vector2 c, float r, Vector2[] unit, Color32 tint)
    {
        if (r <= .2f) return false;
        if (!InArea(ctx.area, c - new Vector2(r, r)) || !InArea(ctx.area, c + new Vector2(r, r))) { ctx.clipped++; return false; }
        PolyRaw(ctx.vh, c, r, unit, tint);
        return true;
    }

    // 정다각형 고리(바깥 반경 r, 두께 t — 외접 사각형이 영역 안일 때만).
    private static bool RingPoly(ref Ctx ctx, Vector2 c, float r, float t, Vector2[] unit, Color32 tint)
    {
        if (r <= .2f || t <= 0f) return false;
        if (!InArea(ctx.area, c - new Vector2(r, r)) || !InArea(ctx.area, c + new Vector2(r, r))) { ctx.clipped++; return false; }
        RingRaw(ctx.vh, c, r, t, unit, tint);
        return true;
    }

    // 굵은 선분(회전한 사각형 — 네 꼭짓점이 모두 영역 안일 때만).
    private static bool Line(ref Ctx ctx, Vector2 a, Vector2 b, float width, Color32 tint)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 1e-3f) return false;
        d /= length;
        Vector2 n = new Vector2(-d.y, d.x) * (width * .5f);
        Vector2 c0 = a - n, c1 = b - n, c2 = b + n, c3 = a + n;
        var area = ctx.area;
        if (!InArea(area, c0) || !InArea(area, c1) || !InArea(area, c2) || !InArea(area, c3)) { ctx.clipped++; return false; }
        var vh = ctx.vh;
        int i = vh.currentVertCount;
        vh.AddVert(c0, tint, Vector2.zero); vh.AddVert(c1, tint, Vector2.zero);
        vh.AddVert(c2, tint, Vector2.zero); vh.AddVert(c3, tint, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        return true;
    }

    // 배지 바탕: 역할 색 8각 테두리 + 어두운 안쪽. 영역 밖이면 false(안쪽 그림도 그리지 않는다).
    private static bool BadgeBase(ref Ctx ctx, Vector2 c, float side, Color32 ring, Style style)
    {
        float h = side * .5f, t = Mathf.Max(1.2f, side * .1f);
        if (!FillPoly(ref ctx, c, h, Oct, ring)) return false;
        FillPoly(ref ctx, c, h - t, Oct, style.badgeBack);
        return true;
    }

    // 텔레포트 링: 12각 고리 + 가운데 마름모 코어. 짝 없음 = 흐린 색 + 사선.
    private static bool TeleportBadge(ref Ctx ctx, Vector2 c, float side, bool paired, Style style)
    {
        var tint = paired ? style.teleport : style.teleportDim;
        if (!BadgeBase(ref ctx, c, side, tint, style)) return false;
        float i = BadgeInner(side);
        RingPoly(ref ctx, c, i * .95f, Mathf.Max(1f, i * .28f), Circle12, tint);
        Diamond(ref ctx, c, i * .42f, tint);
        if (!paired) Line(ref ctx, c + new Vector2(-i, -i) * .8f, c + new Vector2(i, i) * .8f, Mathf.Max(1f, i * .22f), style.teleportDim);
        return true;
    }

    // 수리대: 어두운 테두리 + 흰 8각 배지 + 초록 십자.
    private static bool RepairBadge(ref Ctx ctx, Vector2 c, float side, Style style)
    {
        float h = side * .5f;
        if (!FillPoly(ref ctx, c, h, Oct, style.badgeEdge)) return false;
        FillPoly(ref ctx, c, h - Mathf.Max(1f, side * .08f), Oct, style.repairPlate);
        float i = BadgeInner(side);
        Cross(ref ctx, c, i * 1.05f, Mathf.Max(1.5f, i * .55f), style.rest);
        return true;
    }

    // 레버(배전반 손잡이 — DungeonObjectiveMarkerGraphic의 레버 모양과 같은 말): 미완료 = 받침 + 기울어진 손잡이 + 머리 마름모,
    // 완료 = 앰버 채움 + 아래로 내린 손잡이 + 오른쪽 위 녹색 표시등. ready가 거짓(방 미클리어 — 아직 쓸 수 없음)이면 흐린 앰버.
    private static bool LeverBadge(ref Ctx ctx, Vector2 c, float side, bool done, bool ready, Style style)
    {
        var tint = done || ready ? style.objective : style.objectiveDim;
        if (!BadgeBase(ref ctx, c, side, tint, style)) return false;
        float i = BadgeInner(side);
        if (done)
        {
            FillPoly(ref ctx, c, i * 1.05f, Oct, style.objective);
            Line(ref ctx, c + new Vector2(0f, i * .1f), c + new Vector2(-i * .45f, -i * .6f), Mathf.Max(1f, i * .3f), style.badgeBack);
            AddClipped(ref ctx, CenteredRect(c + new Vector2(i * .45f, i * .45f), Mathf.Max(1.5f, i * .5f)), style.leverLamp);
            return true;
        }
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - i * .7f, c.y - i * .8f, c.x + i * .7f, c.y - i * .45f), tint);
        Line(ref ctx, new Vector2(c.x, c.y - i * .5f), new Vector2(c.x - i * .4f, c.y + i * .4f), Mathf.Max(1f, i * .26f), tint);
        Diamond(ref ctx, new Vector2(c.x - i * .4f, c.y + i * .45f), i * .32f, tint);
        return true;
    }

    // 출구 문: 배지 + 문틀(잠김 = 흐린 문틀·볼트, 열림 = 밝은 문틀·문짝·빛) + 배지 위(자리가 없으면 아래) 램프 줄(수 = 레버 수, 켜진 수 = 완료 수).
    private static bool ExitBadge(ref Ctx ctx, Vector2 c, float side, bool open, int done, int total, Style style, ref int lamps, ref int lit)
    {
        if (!BadgeBase(ref ctx, c, side, open ? style.objective : style.objectiveDim, style)) return false;
        ExitGlyph(ref ctx, c, ExitGlyphSize(side), open, style);
        int n = Mathf.Clamp(total, 0, MaxExitLamps);
        if (n <= 0) return true;
        ExitLampRow(ctx.area, c, side, n, out float ls, out float gap, out float x0, out float y);
        for (int k = 0; k < n; k++)
        {
            bool on = k < done;
            if (AddClipped(ref ctx, CenteredRect(new Vector2(x0 + ls * .5f + k * (ls + gap), y), ls), on ? style.objective : style.lampOff))
            {
                lamps++;
                if (on) lit++;
            }
        }
        return true;
    }

    // 경로 크레인 훅: 트롤리 막대(위) + 줄 + J자 훅(기능부 파랑).
    private static bool CraneHookBadge(ref Ctx ctx, Vector2 c, float side, Style style)
    {
        if (!BadgeBase(ref ctx, c, side, style.crane, style)) return false;
        float i = BadgeInner(side), t = Mathf.Max(1f, i * .26f);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - i * .8f, c.y + i * .55f, c.x + i * .8f, c.y + i * .55f + t), style.crane);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - t * .5f, c.y - i * .55f, c.x + t * .5f, c.y + i * .55f), style.crane);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - i * .6f, c.y - i * .55f - t, c.x + t * .5f, c.y - i * .55f), style.crane);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - i * .6f, c.y - i * .55f - t, c.x - i * .6f + t, c.y - i * .15f), style.crane);
        return true;
    }

    // 고정 크레인: 천장 틱 + 사슬 2마디 + 아래 큰 고리(케이지 없음 — S3 '막대 끝 O').
    private static bool CraneAnchorBadge(ref Ctx ctx, Vector2 c, float side, Style style)
    {
        if (!BadgeBase(ref ctx, c, side, style.crane, style)) return false;
        float i = BadgeInner(side), t = Mathf.Max(1f, i * .24f);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - i * .7f, c.y + i * .75f, c.x + i * .7f, c.y + i * .75f + t), style.crane);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - t * .5f, c.y + i * .42f, c.x + t * .5f, c.y + i * .72f), style.crane);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - t * .5f, c.y + i * .08f, c.x + t * .5f, c.y + i * .36f), style.crane);
        RingPoly(ref ctx, c + new Vector2(0f, -i * .4f), i * .5f, Mathf.Max(1f, i * .22f), Oct, style.crane);
        return true;
    }

    // 점프대: 받침 막대 + 위로 겹 꺾쇠(^^).
    private static bool JumpPadBadge(ref Ctx ctx, Vector2 c, float side, Style style)
    {
        if (!BadgeBase(ref ctx, c, side, style.jumpPad, style)) return false;
        float i = BadgeInner(side), t = Mathf.Max(1f, i * .24f);
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - i * .75f, c.y - i * .8f, c.x + i * .75f, c.y - i * .8f + t), style.jumpPad);
        for (int k = 0; k < 2; k++)
        {
            float y = c.y - i * .35f + k * i * .5f;
            var tip = new Vector2(c.x, y + i * .38f);
            Line(ref ctx, new Vector2(c.x - i * .55f, y), tip, t, style.jumpPad);
            Line(ref ctx, tip, new Vector2(c.x + i * .55f, y), t, style.jumpPad);
        }
        return true;
    }

    // 경로 크레인 경로: 매달린 발 + craneLift 높이의 점선(경로점 사이) + 양 끝 도크(발 높이의 작은 판). 그린 도크 수.
    private static int CranePathTrack(ref Ctx ctx, DungeonRoomDetail detail, int index, RoomView view, float side, Style style)
    {
        var paths = detail != null ? detail.cranePaths : null;
        if (paths == null || index < 0 || index >= paths.Length) return 0;
        var path = paths[index];
        if (path == null || path.Length < 2) return 0;
        var lift = new Vector2(0f, style.craneLift);
        float dash = Mathf.Max(2.5f, view.cell * .9f), gap = Mathf.Max(1.5f, view.cell * .6f);
        for (int k = 0; k + 1 < path.Length; k++)
            DashedLine(ref ctx, view.ToScreen(path[k] + lift), view.ToScreen(path[k + 1] + lift), 1.4f, dash, gap, style.cranePath);
        return Dock(ref ctx, view.ToScreen(path[0]), side, style) + Dock(ref ctx, view.ToScreen(path[path.Length - 1]), side, style);
    }

    // 도크(승강장): 발 높이의 가로 판 + 가운데 기둥 표시.
    private static int Dock(ref Ctx ctx, Vector2 c, float side, Style style)
    {
        float w = Mathf.Max(3f, side * .32f), t = Mathf.Max(1.2f, side * .12f);
        if (!AddClipped(ref ctx, Rect.MinMaxRect(c.x - w, c.y - t, c.x + w, c.y), style.crane)) return 0;
        AddClipped(ref ctx, Rect.MinMaxRect(c.x - t * .5f, c.y, c.x + t * .5f, c.y + w * .6f), style.cranePath);
        return 1;
    }

    // 출입구 위(자리가 없으면 아래)의 이웃 방 정보 점: 클리어 = 초록, 보스(미클리어) = 빨간 마름모, 방문·미클리어 = 주황, 미방문 = 흐림.
    private static void NeighborDot(ref Ctx ctx, Rect door, DungeonMapRoom neighbor, Style style, ref RoomMarkStats stats)
    {
        if (neighbor == null) return;
        float r = NeighborDotRadius;
        var c = NeighborDotCenter(ctx.area, door);
        bool drawn;
        if (neighbor.cleared) { drawn = AddClipped(ref ctx, CenteredRect(c, r * 2f), style.rest); if (drawn) stats.neighborClear++; }
        else if (neighbor.kind == DungeonRoomKind.Boss) { drawn = Diamond(ref ctx, c, r * 1.3f, style.boss); if (drawn) stats.neighborBoss++; }
        else if (neighbor.visited) { drawn = AddClipped(ref ctx, CenteredRect(c, r * 2f), style.unclearedDot); if (drawn) stats.neighborOpen++; }
        else { drawn = AddClipped(ref ctx, CenteredRect(c, r * 2f), style.neighborUnknown); if (drawn) stats.neighborUnknown++; }
        if (drawn) stats.neighborDots++;
    }

    // 출구 램프 수: 원본이 IDungeonObjectiveSource면(DungeonManager) 그 진행, 아니면 표식 목록에서 센다.
    private static void ObjectiveCounts(IDungeonDetailSource states, IReadOnlyList<DungeonObjectiveMarker> objectives, out int done, out int total)
    {
        done = total = 0;
        if (states is IDungeonObjectiveSource source && !(source is UnityEngine.Object unityObject && unityObject == null) && source.HasObjective)
        {
            done = Mathf.Max(0, source.ObjectiveDone);
            total = Mathf.Max(0, source.ObjectiveTotal);
            return;
        }
        if (objectives == null) return;
        total = objectives.Count;
        for (int i = 0; i < objectives.Count; i++) if (objectives[i].done) done++;
    }

    private static void NoteBadge(ref float smallest, float side) => smallest = smallest <= 0f ? side : Mathf.Min(smallest, side);

    /// <summary>Player 표식이 가운데에서 뻗는 최대 거리(px) — 꼬리·빛 고리 포함(heading).</summary>
    public static float PlayerExtent(float half, bool heading) => heading ? half * 1.75f : half;

    /// <summary>
    /// Player 표식. heading이면(5차) 반투명 빛 원판 + 밝은 고리 + 오른쪽(+x)으로 뻗은 꼬리 삼각형을 먼저 그린다 — 왼쪽을 볼 때는 부르는 쪽이 localScale.x를 −1로 뒤집는다(Mesh 재생성 없음).
    /// 그 위에 흰 마름모 + 청록 테두리. 그린 정점 수.
    /// </summary>
    public static int WritePlayerMarker(VertexHelper vh, Vector2 c, float half, Style style = null, bool heading = false)
    {
        style ??= Style.Default;
        int start = vh.currentVertCount;
        if (heading)
        {
            float r = half * 1.45f;
            PolyRaw(vh, c, r, Circle12, style.playerHalo);
            RingRaw(vh, c, r, Mathf.Max(1f, half * .14f), Circle12, style.playerHaloRing);
            int i = vh.currentVertCount;
            vh.AddVert(new Vector2(c.x + half * .45f, c.y - half * .55f), style.playerTail, Vector2.zero);
            vh.AddVert(new Vector2(c.x + half * 1.75f, c.y), style.playerTail, Vector2.zero);
            vh.AddVert(new Vector2(c.x + half * .45f, c.y + half * .55f), style.playerTail, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }
        AddDiamond(vh, c, half, style.playerRing);
        AddDiamond(vh, c, Mathf.Max(1f, half * .58f), style.player);
        return vh.currentVertCount - start;
    }

    /// <summary>Player 핑 고리(1.6초마다 퍼짐 — 부르는 쪽이 localScale·알파로 움직인다). 반경 r, 그린 정점 수.</summary>
    public static int WritePing(VertexHelper vh, Vector2 c, float r, Style style = null)
    {
        style ??= Style.Default;
        if (r <= .5f) return 0;
        int start = vh.currentVertCount;
        RingRaw(vh, c, r, Mathf.Max(1.2f, r * .14f), Circle12, style.playerHaloRing);
        return vh.currentVertCount - start;
    }

    /// <summary>큰 지도 지금 방 층 결과(검증용).</summary>
    public struct CurrentFrameStats
    {
        public int vertices;
        /// <summary>테두리·꺾쇠를 그렸는지(지금 방이 지도 영역과 겹침).</summary>
        public bool framed;
        /// <summary>위 표지 삼각형(아래를 가리킴)을 그렸는지.</summary>
        public bool marker;
        /// <summary>지금 방이 영역 밖이라 가장자리 화살을 그렸는지.</summary>
        public bool offscreen;
        /// <summary>테두리 바깥 사각형(화면).</summary>
        public Rect ring;
        public int clipped;
    }

    /// <summary>큰 지도 지금 방 테두리 여백(px)과 표지 삼각형 높이(px).</summary>
    public const float CurrentPad = 5f, CurrentMarkerHeight = 9f;

    /// <summary>
    /// 큰 지도 지금 방 층(5차): box(지금 방 실루엣 범위, 화면)가 영역과 겹치면 굵은 밝은 테두리 + HUD 판 언어의 꺾쇠 4개 + 위 표지 삼각형(아래를 가리킴),
    /// 영역 밖이면(방향키로 지도를 옮겼을 때) 영역 가장자리에 지금 방 쪽을 가리키는 화살. 모양은 고정이고 맥동은 부르는 쪽이 CanvasRenderer 알파로 한다.
    /// </summary>
    public static CurrentFrameStats WriteCurrentFrame(VertexHelper vh, Rect area, Rect box, Style style = null)
    {
        style ??= Style.Default;
        var stats = new CurrentFrameStats();
        int start = vh.currentVertCount;
        var ctx = new Ctx(vh, area, default);
        var tint = style.currentFrame;
        if (box.width > 0f && box.height > 0f && box.Overlaps(area))
        {
            float w = style.currentFrameWidth;
            var ring = Rect.MinMaxRect(box.xMin - CurrentPad, box.yMin - CurrentPad, box.xMax + CurrentPad, box.yMax + CurrentPad);
            stats.ring = ring;
            Outline(ref ctx, ring, w, tint);
            // 꺾쇠: 테두리 바깥 3px, 길이 currentBracket(방이 작으면 줄임)
            float b = Mathf.Min(style.currentBracket, Mathf.Min(ring.width, ring.height) * .45f), t = style.currentBracketWidth, o = 3f;
            float l = ring.xMin - o, r = ring.xMax + o, bo = ring.yMin - o, to = ring.yMax + o;
            AddClipped(ref ctx, Rect.MinMaxRect(l - t, to, l + b, to + t), tint);
            AddClipped(ref ctx, Rect.MinMaxRect(l - t, to - b, l, to), tint);
            AddClipped(ref ctx, Rect.MinMaxRect(r - b, to, r + t, to + t), tint);
            AddClipped(ref ctx, Rect.MinMaxRect(r, to - b, r + t, to), tint);
            AddClipped(ref ctx, Rect.MinMaxRect(l - t, bo - t, l + b, bo), tint);
            AddClipped(ref ctx, Rect.MinMaxRect(l - t, bo, l, bo + b), tint);
            AddClipped(ref ctx, Rect.MinMaxRect(r - b, bo - t, r + t, bo), tint);
            AddClipped(ref ctx, Rect.MinMaxRect(r, bo, r + t, bo + b), tint);
            stats.framed = true;
            // 위 표지 삼각형(아래를 가리킴). 영역 위로 나가면 그리지 않는다(글자 표지는 부르는 쪽이 아래로 옮긴다).
            float tip = ring.yMax + o + t + 2f, half = 6f;
            stats.marker = Triangle(ref ctx, new Vector2(box.center.x - half, tip + CurrentMarkerHeight), new Vector2(box.center.x + half, tip + CurrentMarkerHeight), new Vector2(box.center.x, tip), tint);
        }
        else if (box.width > 0f && box.height > 0f)
        {
            // 영역 가장자리(여백 16)에서 지금 방 쪽을 가리키는 화살.
            var inner = Rect.MinMaxRect(area.xMin + 16f, area.yMin + 16f, area.xMax - 16f, area.yMax - 16f);
            Vector2 from = area.center, dir = box.center - from;
            float len = dir.magnitude;
            if (len > 1e-3f && inner.width > 0f && inner.height > 0f)
            {
                dir /= len;
                float tx = Mathf.Abs(dir.x) > 1e-4f ? (dir.x > 0f ? inner.xMax - from.x : inner.xMin - from.x) / dir.x : float.MaxValue;
                float ty = Mathf.Abs(dir.y) > 1e-4f ? (dir.y > 0f ? inner.yMax - from.y : inner.yMin - from.y) / dir.y : float.MaxValue;
                var tipPoint = from + dir * Mathf.Min(tx, ty);
                var n = new Vector2(-dir.y, dir.x);
                var back = tipPoint - dir * 14f;
                stats.offscreen = Triangle(ref ctx, back + n * 8f, back - n * 8f, tipPoint, tint);
            }
        }
        stats.clipped = ctx.clipped;
        stats.vertices = vh.currentVertCount - start;
        return stats;
    }

    /// <summary>
    /// 큰 지도 '현재 방' 표지 글자 가운데(화면): 표지 삼각형 위. 영역 위로 나가면 지금 방 아래로 내리고 above = false.
    /// </summary>
    public static Vector2 CurrentLabelPoint(Rect area, Rect box, Style style, out bool above)
    {
        style ??= Style.Default;
        float top = box.yMax + CurrentPad + 3f + style.currentBracketWidth + 2f + CurrentMarkerHeight + 11f;
        above = top + 10f <= area.yMax;
        float y = above ? top : box.yMin - CurrentPad - 3f - style.currentBracketWidth - 12f;
        return new Vector2(Mathf.Clamp(box.center.x, area.xMin + 40f, area.xMax - 40f), y);
    }

    // ===== 3차: 큰 지도(현장 단말기) =====

    /// <summary>큰 지도에 놓을 방 하나: 실루엣과 단위 칸 블록 배치(공통 배율).</summary>
    public sealed class FloorRoom
    {
        public DungeonMapRoom room;
        public DungeonRoomDetail detail;
        public RoomSilhouette silhouette;
        /// <summary>단위 칸 블록 가운데(단위 공간).</summary>
        public Vector2 blockCenter;
        /// <summary>블록 가운데에 맞출 방 로컬 점(실내 외접 범위 가운데).</summary>
        public Vector2 localCenter;
        /// <summary>단위 공간에 놓인 실루엣 범위.</summary>
        public Rect unitBounds;
        public Vector2 ToUnit(Vector2 local, float cellToUnit) => blockCenter + (local - localCenter) * cellToUnit;
    }

    /// <summary>큰 지도의 방 배치(던전 시작 뒤 한 번, 방 상세가 바뀌지 않는 동안 그대로).</summary>
    public sealed class FloorModel
    {
        public readonly List<FloorRoom> rooms = new List<FloorRoom>();
        /// <summary>셀 1칸 = 단위 공간 몇 칸(모든 방 공통 — 실제 비율 유지).</summary>
        public float cellToUnit;
        public FloorRoom Find(int id)
        {
            for (int i = 0; i < rooms.Count; i++) if (rooms[i].room != null && rooms[i].room.id == id) return rooms[i];
            return null;
        }
    }

    /// <summary>
    /// 방 목록(단위 칸·상태)과 방 상세로 큰 지도 배치를 만든다. 공통 배율 = 모든 방 중 '블록 크기 × fill / 실루엣 크기'의 최솟값이라
    /// 작은 방은 자동으로 작게 보이고, 어느 방도 자기 블록을 넘지 않는다(fill 0.78 → 블록 사이 연결선 자리). silhouettes는 방 id 캐시(없으면 만들어 넣는다).
    /// </summary>
    public static FloorModel BuildFloorModel(IReadOnlyList<DungeonMapRoom> rooms, IDungeonDetailSource details, Dictionary<int, RoomSilhouette> silhouettes, float fill = .78f)
    {
        var model = new FloorModel();
        if (rooms == null) return model;
        float k = float.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null || room.units == null || room.units.Length == 0) continue;
            var detail = details != null ? details.RoomDetail(room.id) : null;
            if (detail == null) continue;
            RoomSilhouette sil = null;
            if (silhouettes != null && silhouettes.TryGetValue(room.id, out var cached) && ReferenceEquals(cached.source, detail)) sil = cached;
            if (sil == null)
            {
                sil = BuildSilhouette(detail);
                if (silhouettes != null) silhouettes[room.id] = sil;
            }
            if (sil.IsEmpty) continue;
            int uxMin = int.MaxValue, uyMin = int.MaxValue, uxMax = int.MinValue, uyMax = int.MinValue;
            foreach (var u in room.units)
            {
                uxMin = Math.Min(uxMin, u.x); uyMin = Math.Min(uyMin, u.y);
                uxMax = Math.Max(uxMax, u.x); uyMax = Math.Max(uyMax, u.y);
            }
            float boxW = uxMax - uxMin + 1, boxH = uyMax - uyMin + 1;
            var entry = new FloorRoom
            {
                room = room,
                detail = detail,
                silhouette = sil,
                blockCenter = new Vector2((uxMin + uxMax + 1) * .5f, (uyMin + uyMax + 1) * .5f),
                localCenter = detail.interior.width > 0f && detail.interior.height > 0f ? detail.interior.center : (Vector2)sil.bounds.center,
            };
            model.rooms.Add(entry);
            k = Mathf.Min(k, fill * Mathf.Min(boxW / Mathf.Max(1, sil.bounds.width), boxH / Mathf.Max(1, sil.bounds.height)));
        }
        model.cellToUnit = k == float.MaxValue ? .05f : k;
        foreach (var e in model.rooms)
        {
            var b = e.silhouette.bounds;
            var min = e.ToUnit(new Vector2(b.xMin, b.yMin), model.cellToUnit);
            var max = e.ToUnit(new Vector2(b.xMax, b.yMax), model.cellToUnit);
            e.unitBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        return model;
    }

    /// <summary>큰 지도 화면 배치: 단위 공간 center가 영역 가운데, 단위 1칸 = pitch px.</summary>
    [Serializable]
    public struct FloorView
    {
        public Vector2 center;
        public float pitch;
        public float cellToUnit;
        public Vector2 UnitToScreen(Rect area, Vector2 unit) => area.center + (unit - center) * pitch;
        /// <summary>방 로컬 점 → 화면.</summary>
        public Vector2 LocalToScreen(Rect area, FloorRoom room, Vector2 local) => UnitToScreen(area, room.ToUnit(local, cellToUnit));
        /// <summary>그 방의 RoomView(방 로컬 → 화면) — 실루엣·표식 함수에 그대로 넘긴다.</summary>
        public RoomView RoomViewFor(Rect area, FloorRoom room)
        {
            float cell = cellToUnit * pitch;
            return new RoomView { origin = UnitToScreen(area, room.blockCenter) - room.localCenter * cell, cell = cell };
        }
    }

    /// <summary>그릴 방(알게 됐거나 들어가 본 방, revealAll이면 전부)의 범위를 영역에 맞춘다. 넘치면 현재 방을 따라가되 끝이 안쪽으로 들어오지 않게. pan은 단위 공간 이동량.</summary>
    public static FloorView FitFloor(FloorModel model, int currentRoom, Rect area, float padding, float minPitch, float maxPitch, Vector2 pan = default, bool revealAll = false)
    {
        var view = new FloorView { center = Vector2.zero, pitch = Mathf.Max(1f, maxPitch), cellToUnit = model != null ? model.cellToUnit : .05f };
        if (model == null) return view;
        bool any = false;
        Rect span = default;
        FloorRoom current = null;
        foreach (var e in model.rooms)
        {
            if (!IsDrawable(e.room, revealAll)) continue;
            span = any ? Rect.MinMaxRect(Mathf.Min(span.xMin, e.unitBounds.xMin), Mathf.Min(span.yMin, e.unitBounds.yMin),
                                         Mathf.Max(span.xMax, e.unitBounds.xMax), Mathf.Max(span.yMax, e.unitBounds.yMax)) : e.unitBounds;
            any = true;
            if (e.room.id == currentRoom) current = e;
        }
        if (!any) return view;
        float availW = Mathf.Max(1f, area.width - 2f * padding), availH = Mathf.Max(1f, area.height - 2f * padding);
        float pitch = Mathf.Min(availW / Mathf.Max(.01f, span.width), availH / Mathf.Max(.01f, span.height));
        pitch = Mathf.Clamp(pitch, Mathf.Min(minPitch, maxPitch), maxPitch);
        Vector2 focus = current != null ? current.unitBounds.center : span.center;
        float halfW = availW * .5f / pitch, halfH = availH * .5f / pitch;
        var center = new Vector2(Axis(span.xMin, span.xMax, focus.x, halfW), Axis(span.yMin, span.yMax, focus.y, halfH));
        // 방향키 이동: 범위 끝이 화면 가운데를 넘지 않을 만큼만.
        center += pan;
        center.x = Mathf.Clamp(center.x, span.xMin, span.xMax);
        center.y = Mathf.Clamp(center.y, span.yMin, span.yMax);
        view.center = center;
        view.pitch = pitch;
        return view;
    }

    /// <summary>큰 지도 결과(검증용).</summary>
    public struct FloorMapStats
    {
        public int vertices;
        public int roomsDrawn, filledRooms, outlineOnlyRooms, currentDrawn;
        public int connectors, lockedConnectors, teleportLinks;
        public int objectives, objectivesDone, repairs, teleports;
        /// <summary>4차: 출구 문 표식 수·그중 열린 것.</summary>
        public int exits, exitsOpen;
        public int bossMarks, startDots, unclearedDots, restMarks;
        /// <summary>5차: 경로 크레인·고정 크레인·점프대 배지 수와 출구 램프 수.</summary>
        public int cranePaths, craneAnchors, jumpPads, exitLamps;
        public int clipped;
    }

    /// <summary>
    /// 큰 지도를 쓴다: 방 실루엣(현재 = 밝은 청록 채움 + 밝은 벽, 방문 = 중간 채움 + 청록 벽, 알게 된 미방문 = 흐린 벽만, 봉인 = 빨간 벽, 모름 = 안 그림)
    /// → 출입구끼리 꺾인 연결선(둘 다 방문 = 밝게, 잠김·봉인 = 빨강) → 텔레포트 연결(파란 점선) → 표식(텔레포트·수리대·목표·출구 문(4차)·보스·시작·미클리어).
    /// 5차: 지금 방이 아닌 방문 방 채움은 floorVisitedFill(α170 — 지금 방이 더 돋보이게), 표식은 배지형 픽토그램(12~20px)에 경로 크레인(점선 경로·도크)·고정 크레인·점프대를 더한다.
    /// 배지가 영역 밖으로 나가면 그리지 않는다(지도 이동 중 가장자리에 쌓이지 않게).
    /// </summary>
    public static FloorMapStats WriteFloorMap(VertexHelper vh, FloorModel model, int currentRoom, Rect area, FloorView view, IDungeonDetailSource states,
                                              IReadOnlyList<DungeonObjectiveMarker> objectives, Style style = null, bool revealAll = false)
    {
        style ??= Style.Default;
        var stats = new FloorMapStats();
        if (model == null || model.rooms.Count == 0 || view.pitch <= 0f) return stats;
        int start = vh.currentVertCount;
        int clipped = 0;

        // 1. 방 실루엣
        foreach (var e in model.rooms)
        {
            if (!IsDrawable(e.room, revealAll)) continue;
            stats.roomsDrawn++;
            bool current = e.room.id == currentRoom;
            if (current) stats.currentDrawn++;
            bool filled = current || e.room.visited || revealAll;
            if (filled) stats.filledRooms++; else stats.outlineOnlyRooms++;
            Color32 air = current ? style.currentAir : filled ? style.floorVisitedFill : new Color32(0, 0, 0, 0);
            Color32 wall = e.room.sealedRoom ? style.boss : current ? style.currentLine : filled ? style.visitedLine : style.knownLine;
            Color32 oneWay = filled ? style.oneWayLine : new Color32(0, 0, 0, 0);
            WriteSilhouette(vh, e.silhouette, area, view.RoomViewFor(area, e), air, wall, oneWay, style, ref clipped);
        }

        var ctx = new Ctx(vh, area, default);
        float w = Mathf.Max(1f, style.connectorWidth);
        float scale = view.cellToUnit * view.pitch;   // 셀 1칸 px

        // 2. 출입구 연결선(한 쌍에 한 번: 건너편 출입구와 짝지어 꺾인 선)
        foreach (var a in model.rooms)
        {
            if (!IsDrawable(a.room, revealAll) || a.detail.doors == null) continue;
            foreach (var door in a.detail.doors)
            {
                if (door.toRoom < 0) continue;
                var b = model.Find(door.toRoom);
                if (b == null || !IsDrawable(b.room, revealAll) || b.detail.doors == null) continue;
                if (b.room.id < a.room.id && HasDoorTo(b.detail, a.room.id, DungeonShapes.Opposite(door.side))) continue;
                if (!TryMatchDoor(b.detail, a.room.id, DungeonShapes.Opposite(door.side), door.local.y - a.localCenter.y + b.localCenter.y, out var other)) continue;
                var pa = view.LocalToScreen(area, a, door.local);
                var pb = view.LocalToScreen(area, b, other.local);
                var sa = states != null ? states.DoorState(a.room.id, door.socket) : DungeonDoor.DoorState.Open;
                var sb = states != null ? states.DoorState(b.room.id, other.socket) : DungeonDoor.DoorState.Open;
                bool locked = sa != DungeonDoor.DoorState.Open || sb != DungeonDoor.DoorState.Open;
                bool bright = (a.room.visited && b.room.visited) || revealAll;
                Color32 tint = locked ? style.doorLocked : bright ? style.bridge : style.bridgeDim;
                Elbow(ref ctx, pa, pb, w, tint);
                float end = Mathf.Max(2.5f, scale * 1.5f);
                AddClipped(ref ctx, CenteredRect(pa, end), sa == DungeonDoor.DoorState.Open ? tint : style.doorLocked);
                AddClipped(ref ctx, CenteredRect(pb, end), sb == DungeonDoor.DoorState.Open ? tint : style.doorLocked);
                stats.connectors++;
                if (locked) stats.lockedConnectors++;
            }
        }

        // 3. 텔레포트 연결(두 방 모두 그릴 때, 한 쌍에 한 번): 파란 점선
        foreach (var a in model.rooms)
        {
            if (!IsDrawable(a.room, revealAll) || a.detail.points == null) continue;
            foreach (var p in a.detail.points)
            {
                if (p.kind != DungeonDetailMarkerKind.Teleport || p.toRoom < 0) continue;
                var b = model.Find(p.toRoom);
                if (b == null || !IsDrawable(b.room, revealAll) || b.detail.points == null) continue;
                if (b.room.id < a.room.id) continue;
                if (!TryFindTeleport(b.detail, a.room.id, out var q)) continue;
                var pa = view.LocalToScreen(area, a, p.local);
                var pb = view.LocalToScreen(area, b, q.local);
                bool bright = (a.room.visited && b.room.visited) || revealAll;
                Vector2 dir = pb - pa;
                float len = dir.magnitude;
                if (len > 8f)
                {
                    dir /= len;
                    DashedLine(ref ctx, pa + dir * 4f, pb - dir * 4f, style.teleportWidth, 6f, 4f, bright ? style.teleport : style.teleportDim);
                }
                stats.teleportLinks++;
            }
        }

        // 4. 표식(5차: 배지형 픽토그램)
        ObjectiveCounts(states, objectives, out int objectiveDone, out int objectiveTotal);
        float side = BadgeSide(scale, true, style);
        var lift = new Vector2(0f, style.craneLift);
        int lamps = 0, lit = 0;
        foreach (var e in model.rooms)
        {
            if (!IsDrawable(e.room, revealAll)) continue;
            var roomView = view.RoomViewFor(area, e);
            if (e.detail.points != null)
                foreach (var p in e.detail.points)
                {
                    var c = roomView.ToScreen(p.local);
                    switch (p.kind)
                    {
                        case DungeonDetailMarkerKind.Teleport:
                            if (TeleportBadge(ref ctx, c, side, p.toRoom >= 0, style)) stats.teleports++;
                            break;
                        case DungeonDetailMarkerKind.Repair:
                            if (RepairBadge(ref ctx, c, side, style)) stats.repairs++;
                            break;
                        case DungeonDetailMarkerKind.Objective:
                            bool done = ObjectiveDone(objectives, e.room.id, p.index);
                            if (LeverBadge(ref ctx, c, side, done, e.room.cleared || revealAll, style)) { stats.objectives++; if (done) stats.objectivesDone++; }
                            break;
                        case DungeonDetailMarkerKind.Exit:
                            bool open = ExitOpen(states);
                            if (ExitBadge(ref ctx, c, side, open, objectiveDone, objectiveTotal, style, ref lamps, ref lit)) { stats.exits++; if (open) stats.exitsOpen++; }
                            break;
                        case DungeonDetailMarkerKind.CranePath:
                            CranePathTrack(ref ctx, e.detail, p.index, roomView, side, style);
                            if (CraneHookBadge(ref ctx, roomView.ToScreen(p.local + lift), side, style)) stats.cranePaths++;
                            break;
                        case DungeonDetailMarkerKind.CraneAnchor:
                            if (CraneAnchorBadge(ref ctx, roomView.ToScreen(p.local + lift), side, style)) stats.craneAnchors++;
                            break;
                        case DungeonDetailMarkerKind.JumpPad:
                            if (JumpPadBadge(ref ctx, c, side, style)) stats.jumpPads++;
                            break;
                    }
                }
            var box = roomView.CellRect(e.silhouette.bounds);
            float mark = Mathf.Max(3f, Mathf.Min(box.width, box.height) * .12f);
            if (e.room.kind == DungeonRoomKind.Boss)
            {
                if (Diamond(ref ctx, box.center, mark * 1.4f, style.boss)) stats.bossMarks++;
            }
            if (e.room.kind == DungeonRoomKind.Start)
            {
                if (AddClipped(ref ctx, CenteredRect(box.center, mark * .9f), style.startDot)) stats.startDots++;
            }
            if (e.room.rest && !HasPoint(e.detail, DungeonDetailMarkerKind.Repair))
            {
                if (Cross(ref ctx, box.center, mark, Mathf.Max(1.5f, mark * .4f), style.rest)) stats.restMarks++;
            }
            if (e.room.visited && !e.room.cleared && !revealAll)
            {
                float s = mark * .9f;
                if (AddClipped(ref ctx, CenteredRect(new Vector2(box.xMax - s * 1.2f, box.yMax - s * 1.2f), s), style.unclearedDot)) stats.unclearedDots++;
            }
        }
        stats.exitLamps = lamps;
        stats.clipped = clipped + ctx.clipped;
        stats.vertices = vh.currentVertCount - start;
        return stats;
    }

    private static bool HasPoint(DungeonRoomDetail detail, DungeonDetailMarkerKind kind)
    {
        if (detail?.points == null) return false;
        foreach (var p in detail.points) if (p.kind == kind) return true;
        return false;
    }

    private static bool HasDoorTo(DungeonRoomDetail detail, int toRoom, DungeonDoorSide side)
    {
        if (detail?.doors == null) return false;
        foreach (var d in detail.doors) if (d.toRoom == toRoom && d.side == side) return true;
        return false;
    }

    // 건너편 방에서 이 방을 가리키는 반대쪽 출입구 중 높이가 가장 가까운 것(같은 두 방 사이 출입구가 여럿이어도 짝이 맞게).
    private static bool TryMatchDoor(DungeonRoomDetail detail, int toRoom, DungeonDoorSide side, float localY, out DungeonRoomDetailDoor match)
    {
        match = default;
        bool found = false;
        float best = float.MaxValue;
        foreach (var d in detail.doors)
        {
            if (d.toRoom != toRoom || d.side != side) continue;
            float diff = Mathf.Abs(d.local.y - localY);
            if (diff < best) { best = diff; match = d; found = true; }
        }
        return found;
    }

    private static bool TryFindTeleport(DungeonRoomDetail detail, int toRoom, out DungeonRoomDetailPoint match)
    {
        match = default;
        foreach (var p in detail.points)
            if (p.kind == DungeonDetailMarkerKind.Teleport && p.toRoom == toRoom) { match = p; return true; }
        return false;
    }

    // a → b 꺾인 선: 가로(a.y) → 세로(가운데 x) → 가로(b.y). 축 정렬 사각형이라 영역으로 자를 수 있다.
    private static void Elbow(ref Ctx ctx, Vector2 a, Vector2 b, float width, Color32 tint)
    {
        float h = width * .5f, mx = (a.x + b.x) * .5f;
        AddClipped(ref ctx, Rect.MinMaxRect(Mathf.Min(a.x, mx) - h, a.y - h, Mathf.Max(a.x, mx) + h, a.y + h), tint);
        if (Mathf.Abs(a.y - b.y) > width)
            AddClipped(ref ctx, Rect.MinMaxRect(mx - h, Mathf.Min(a.y, b.y) + h, mx + h, Mathf.Max(a.y, b.y) - h), tint);
        AddClipped(ref ctx, Rect.MinMaxRect(Mathf.Min(b.x, mx) - h, b.y - h, Mathf.Max(b.x, mx) + h, b.y + h), tint);
    }

    /// <summary>범례 칩 수(5차: 현재 방·잠긴 출입구·텔레포트·수리대·레버·출구·크레인 경로·크레인 고정·현재 위치).</summary>
    public const int LegendChips = 9;

    /// <summary>
    /// 큰 지도 범례 칩(5차 9칸 — 현재 방·잠긴 출입구·텔레포트·수리대·레버·출구·크레인 경로·크레인 고정·현재 위치)을 왼쪽부터 spacing 간격으로 쓴다.
    /// 칩 가운데 = 왼쪽 + 10 + i × spacing(글자는 생성 도구가 같은 간격에 둔 TMP). 오른쪽 끝을 넘는 칩은 그리지 않는다. 그린 칩 수.
    /// </summary>
    public static int WriteLegend(VertexHelper vh, Rect rect, float spacing, Style style = null)
    {
        style ??= Style.Default;
        var ctx = new Ctx(vh, rect, default);
        float y = rect.center.y, s = Mathf.Min(14f, rect.height - 4f);
        int count = 0, lamps = 0, lit = 0;
        for (int i = 0; i < LegendChips; i++)
        {
            var c = new Vector2(rect.xMin + 10f + i * spacing, y);
            if (c.x + s * .5f > rect.xMax) break;
            bool drew;
            switch (i)
            {
                case 0:
                    drew = AddClipped(ref ctx, CenteredRect(c, s), style.currentAir);
                    Outline(ref ctx, CenteredRect(c, s), 1.5f, style.currentFrame);
                    break;
                case 1:
                    drew = AddClipped(ref ctx, new Rect(c.x - s * .3f, c.y - s * .5f, s * .6f, s), style.doorLocked);
                    AddClipped(ref ctx, new Rect(c.x - s * .3f + 1f, c.y - .75f, s * .6f - 2f, 1.5f), style.doorShutter);
                    break;
                case 2: drew = TeleportBadge(ref ctx, c, s, true, style); break;
                case 3: drew = RepairBadge(ref ctx, c, s, style); break;
                case 4: drew = LeverBadge(ref ctx, c, s, false, true, style); break;
                case 5: drew = ExitBadge(ref ctx, c, s, false, 0, 0, style, ref lamps, ref lit); break;
                case 6: drew = CraneHookBadge(ref ctx, c, s, style); break;
                case 7: drew = CraneAnchorBadge(ref ctx, c, s, style); break;
                default:
                    WritePlayerMarker(vh, c, s * .5f / 1.75f * 1.2f, style, true);
                    drew = true;
                    break;
            }
            if (drew) count++;
        }
        return count;
    }

    private static void Outline(ref Ctx ctx, Rect r, float t, Color32 tint)
    {
        AddClipped(ref ctx, Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + t), tint);
        AddClipped(ref ctx, Rect.MinMaxRect(r.xMin, r.yMax - t, r.xMax, r.yMax), tint);
        AddClipped(ref ctx, Rect.MinMaxRect(r.xMin, r.yMin + t, r.xMin + t, r.yMax - t), tint);
        AddClipped(ref ctx, Rect.MinMaxRect(r.xMax - t, r.yMin + t, r.xMax, r.yMax - t), tint);
    }
}
