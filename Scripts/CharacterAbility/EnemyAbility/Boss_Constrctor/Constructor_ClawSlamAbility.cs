using UnityEngine;

namespace GameProject.AI.Abilities.Constructor
{
    public class Constructor_ClawSlamAbility : AiAbility
    {
        private enum ExecutePhase
        {
            Track,      // 플레이어를 다이내믹하게 추적
            Telegraph,  // 공중에서 멈칫하며 아래로 경고선 표시
            Slam        // 바닥으로 내리찍기
        }

        private Constructor_ClawSlamData slamData;
        private Transform playerTr;

        private ExecutePhase currentPhase;
        private float currentVelocityX; // 가속도 물리 연산을 위한 현재 X 속도

        private LaserPreviewLine activePreview; // 경고선
        private float slamTime;

        private void Awake()
        {
            slamData = (Constructor_ClawSlamData)data;
            slamTime = data.executeTime - (slamData.telegraphDelay + slamData.trackDuration);
        }

        protected override void OnWindupStart()
        {
            playerTr = GameManager.Instance.Player.transform;
            controller.Velocity = Vector2.zero;
        }

        protected override void OnWindup(float t)
        {
            controller.Velocity = new Vector2(0f, slamData.windupRiseSpeed);
        }

        protected override void OnExecuteStart()
        {
            // Execute 시작 시 추적 페이즈로 초기화
            currentPhase = ExecutePhase.Track;
            currentVelocityX = 0f;
            controller.Velocity = Vector2.zero;
        }

        protected override void OnExecute(float t)
        {
            // ==========================================
            // 상태 전이(Transition) 체크 로직
            // ==========================================
            if (currentPhase == ExecutePhase.Track && t >= slamData.trackDuration)
            {
                currentPhase = ExecutePhase.Telegraph;
                controller.Velocity = Vector2.zero;
                SpawnPreview(); // Phase 2 진입 시 수직 방향 경고선 생성
            }
            else if (currentPhase == ExecutePhase.Telegraph && t >= slamData.trackDuration + slamData.telegraphDelay)
            {
                currentPhase = ExecutePhase.Slam;
                ReturnPreview(); // 내리찍기 직전에 경고선 회수
            }

            // ==========================================
            // 현재 페이즈(Phase)별 행동 수행
            // ==========================================
            switch (currentPhase)
            {
                case ExecutePhase.Track:
                    float targetX = playerTr.position.x;
                    float deltaX = targetX - controller.transform.position.x;

                    // [다이내믹 추적 공식] 
                    // 거리가 멀수록 더 강한 힘(가속도)을 받고, 마찰력(Drag)에 의해 감속됩니다.
                    // 이로 인해 플레이어가 갑자기 멈춰도 보스가 관성 때문에 앞으로 밀려나며 지나치게(Overshoot) 됩니다.
                    float acceleration = deltaX * slamData.trackAcceleration;
                    currentVelocityX += acceleration * Time.deltaTime;
                    currentVelocityX *= (1f - slamData.trackDrag * Time.deltaTime);

                    controller.Velocity = new Vector2(currentVelocityX, 0f);

                    // 플레이어 방향으로 바라보기
                    if (Mathf.Abs(deltaX) > 0.1f)
                        controller.FacingUpdate(deltaX > 0 ? 1 : -1);
                    break;

                case ExecutePhase.Telegraph:
                    controller.Velocity = Vector2.zero;
                    UpdatePreview(data.executeTime - slamTime - t, slamData.telegraphDelay); // 공중에 멈춰서 경고선 지속 업데이트
                    break;

                case ExecutePhase.Slam:
                    controller.Velocity = new Vector2(0f, -slamData.slamSpeed);
                    break;
            }
        }

        protected override void OnRecoverStart()
        {
            controller.Velocity = Vector2.zero;
            ReturnPreview(); // 안전 장치: 바닥에 닿으면 무조건 경고선 회수

            if (slamData.meleeHitbox != null && recoverPoint != null)
            {
                MeleeHitbox hitbox = WorldEntityPool.Instance.Spawn(
                    slamData.meleeHitbox,
                    recoverPoint.position,
                    Quaternion.identity
                );
                hitbox.Initialize(controller, slamData.damageInfo);
                hitbox.SetHitFeedback(null);
            }
        }

        protected override void OnRecover(float t)
        {
            controller.Velocity = Vector2.zero;
        }

        protected override void OnStop()
        {
            controller.Velocity = Vector2.zero;
            ReturnPreview();
        }

        // ========================================================================
        // 경고선(Preview Line) 관리 메서드
        // ========================================================================
        private void SpawnPreview()
        {
            if (slamData.previewLinePrefab == null || activePreview != null)
                return;

            activePreview = WorldEntityPool.Instance.Spawn(
                slamData.previewLinePrefab,
                executePoint.position,
                Quaternion.identity
            );

            // 멈칫하는 시간(telegraphDelay)을 수명으로 주어 페이드아웃 되게 설정합니다.
            activePreview.Show(
                executePoint.position,
                Vector2.down, // 무조건 수직 아래 방향
                slamData.previewLineLength,
                slamData.previewLineWidth,
                slamData.previewBlockMask,
                slamData.previewLineColor,
                slamData.telegraphDelay
            );
        }

        private void UpdatePreview(float remaining, float totalTime)
        {
            if (activePreview == null) return;

            activePreview.SetOrigin(executePoint.position);
            activePreview.SetDirection(Vector2.down);
            activePreview.UpdatePreviewVisual(remaining, totalTime);
        }

        private void ReturnPreview()
        {
            if (activePreview == null) return;

            activePreview.ForceReturnToPool();
            activePreview = null;
        }
    }
}