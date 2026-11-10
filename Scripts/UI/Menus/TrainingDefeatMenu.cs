using System;
using UnityEngine;

// 패배 결과 화면의 선택 메뉴(재도전 / 포기). 옛 DeathMenu(2026-11-11 삭제)와 같은 SelectableMenuUI 기반이라 입력 계층(System)·마우스·효과음이 같다.
// 좌우로 항목을 고르고, 상하는 결과 보드의 쪽 넘김으로 넘긴다(TrainingCompleteBoardUI가 받는다).
public class TrainingDefeatMenu : SelectableMenuUI
{
    public event Action<int> PageRequested;     // +1 다음 쪽, -1 이전 쪽

    protected override void ProcessMove(Vector2 dir)
    {
        if (Mathf.Abs(dir.y) > 0.5f)
        {
            PageRequested?.Invoke(dir.y < 0f ? 1 : -1);
            return;
        }
        if (dir.x > 0.5f) ChangeIndex(currentIndex + 1);
        else if (dir.x < -0.5f) ChangeIndex(currentIndex - 1);
    }

    // 클릭한 항목을 실행한다. 기본 구현은 지금 강조된 항목을 실행하는데, 포인터가 메뉴가 열리기 전부터 버튼 위에 있었으면
    // 진입 이벤트가 오지 않아 강조가 다른 항목에 남아 있을 수 있다('포기'를 눌렀는데 '재도전'이 실행됨).
    protected override void OnPointerClick(int index)
    {
        if (!IsActive) return;
        if (UIManager.Instance != null && UIManager.Instance.IsUIInputBlocked) return;
        ChangeIndex(index);
        OnConfirm();
    }
}
