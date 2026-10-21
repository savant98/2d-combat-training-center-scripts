using UnityEngine;
using System;
using DG.Tweening;
using System.Collections;

public class EnemyTeleporter : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private GameObject[] targetObjects;

    [Header("Optional")]
    [SerializeField] private EnemyController controller;

    [Header("Teleport Visual")]
    [SerializeField] private Color teleportEmissionColor = Color.white;
    [SerializeField] private float fadeToTeleportColorDuration = 0.08f;
    [SerializeField] private float fadeToOriginalColorDuration = 0.08f;

    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private Material[][] cachedMaterials;
    private Color[][] originalEmissionColors;

    private Sequence teleportSequence;
    private Tween emissionRestoreTween;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponent<EnemyController>();

        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);

        CacheMaterials();
    }

    private void OnDestroy()
    {
        teleportSequence?.Kill();
        RestoreOriginalEmission();
    }

    private void CacheMaterials()
    {
        cachedMaterials = new Material[targetRenderers.Length][];
        originalEmissionColors = new Color[targetRenderers.Length][];

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer renderer = targetRenderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.materials;
            cachedMaterials[i] = mats;
            originalEmissionColors[i] = new Color[mats.Length];

            for (int j = 0; j < mats.Length; j++)
            {
                Material mat = mats[j];

                if (mat != null && mat.HasProperty(EmissionColorID))
                    originalEmissionColors[i][j] = mat.GetColor(EmissionColorID);
                else
                    originalEmissionColors[i][j] = Color.black;
            }
        }
    }

    public void Cancel()
    {
        teleportSequence?.Kill();
        teleportSequence = null;

        emissionRestoreTween?.Kill();
        emissionRestoreTween = null;

        RestoreOriginalEmission();
    }

    /// <summary>
    /// startFx -> emission을 텔포 색으로 변경 -> disappearDelay -> 위치 이동 -> appearDelay -> endFx -> 원래 emission으로 복구
    /// </summary>
    public void TeleportSequence(
    Vector2 targetPos,
    FeedbackPlayer feedbackPlayer,
    FeedbackData startFx,
    FeedbackData endFx,
    float disappearDelay,
    float appearDelay,
    Action onDone = null)
    {
        // 기존 텔레포트 시퀀스가 실행 중이면 중단
        teleportSequence?.Kill();
        teleportSequence = null;

        // 혹시 남아있을 수 있는 emission 상태를 원래 색으로 복구
        RestoreOriginalEmission();

        // 텔레포트 시작 시 이동 관성을 제거
        if (controller != null)
            controller.Velocity = Vector2.zero;

        // DOTween 시퀀스 생성 (이 아래부터 순서대로 실행됨)
        teleportSequence = DOTween.Sequence().SetUpdate(UpdateType.Fixed);

        // -------------------------------------------------------
        // 1. 텔레포트 시작 피드백 실행 (사라지기 전 연출)
        // -------------------------------------------------------
        teleportSequence.AppendCallback(() =>
        {
            controller.CanTouchDamageActive(false);

            if (feedbackPlayer != null && startFx != null)
                feedbackPlayer.PlayExact(startFx, transform.position, Quaternion.identity);
        });

        // -------------------------------------------------------
        // 2. 적의 EmissionColor를 텔레포트 색으로 변화
        //    (예: 하얗게 번쩍이는 효과)
        // -------------------------------------------------------
        teleportSequence.Append(
            TweenAllEmissionTo(teleportEmissionColor, fadeToTeleportColorDuration)
        );

        // -------------------------------------------------------
        // 3. 실제로 사라짐
        // -------------------------------------------------------
        teleportSequence.AppendCallback(() =>
        {
            controller.Health.IsInvincible = true;
            SetRenderersVisible(false);
        });

        // -------------------------------------------------------
        // 4. 사라진 상태에서 대기
        //    (연출 타이밍을 맞추기 위한 시간)
        // -------------------------------------------------------
        if (disappearDelay > 0f)
            teleportSequence.AppendInterval(disappearDelay);


        // -------------------------------------------------------
        // 5. 다음 프레임에 목표 좌표 도달 Velocity 설정
        // -------------------------------------------------------
        teleportSequence.AppendCallback(() =>
        {
            Vector2 currentPos = controller.transform.position;
            Vector2 delta = targetPos - currentPos;

            controller.Velocity = delta / Time.fixedDeltaTime;
        });

        // -------------------------------------------------------
        // 6. FixedUpdate 시간 대기
        // -------------------------------------------------------
        teleportSequence.AppendInterval(Time.fixedDeltaTime);

        // -------------------------------------------------------
        // 7. 이동 종료 후 속도 제거
        // -------------------------------------------------------
        teleportSequence.AppendCallback(() =>
        {
            controller.Velocity = Vector2.zero;
        });

        // -------------------------------------------------------
        // 8. 등장 전 딜레이
        //    (새 위치에서 나타나기 전 잠깐의 텀)
        // -------------------------------------------------------
        if (appearDelay > 0f)
            teleportSequence.AppendInterval(appearDelay);


        // -------------------------------------------------------
        // 9. 다시 보이게
        // -------------------------------------------------------
        teleportSequence.AppendCallback(() =>
        {
            controller.Health.IsInvincible = false;
            SetRenderersVisible(true);
        });

        // -------------------------------------------------------
        // 10. 텔레포트 완료 피드백 실행 (등장 연출)
        // -------------------------------------------------------
        teleportSequence.AppendCallback(() =>
        {
            if (feedbackPlayer != null && endFx != null)
                feedbackPlayer.PlayExact(endFx, transform.position, Quaternion.identity);
        });

        // -------------------------------------------------------
        // 11. 텔레포트 시퀀스 종료 처리
        // -------------------------------------------------------
        teleportSequence.OnComplete(() =>
        {
            if (!controller.Brain.isPaused)
            {
                controller.CanTouchDamageActive(true);
            }

            // 시퀀스 참조 정리
            teleportSequence = null;

            PlayRestoreEmission();

            // 외부에서 넘겨준 완료 콜백 실행
            onDone?.Invoke();
        });
    }
    private Tween TweenAllEmissionTo(Color targetColor, float duration)
    {
        Sequence seq = DOTween.Sequence();

        for (int i = 0; i < cachedMaterials.Length; i++)
        {
            Material[] mats = cachedMaterials[i];
            if (mats == null)
                continue;

            for (int j = 0; j < mats.Length; j++)
            {
                Material mat = mats[j];
                if (mat == null || !mat.HasProperty(EmissionColorID))
                    continue;

                seq.Join(DOTween.To(
                    () => mat.GetColor(EmissionColorID),
                    value => mat.SetColor(EmissionColorID, value),
                    targetColor,
                    duration
                ));
            }
        }

        return seq;
    }

    private void PlayRestoreEmission()
    {
        emissionRestoreTween?.Kill();
        emissionRestoreTween = TweenRestoreEmission(fadeToOriginalColorDuration);

        emissionRestoreTween.OnComplete(() =>
        {
            RestoreOriginalEmission();
            emissionRestoreTween = null;
        });
    }
    private Tween TweenRestoreEmission(float duration)
    {
        Sequence seq = DOTween.Sequence();

        for (int i = 0; i < cachedMaterials.Length; i++)
        {
            Material[] mats = cachedMaterials[i];
            if (mats == null)
                continue;

            for (int j = 0; j < mats.Length; j++)
            {
                Material mat = mats[j];
                if (mat == null || !mat.HasProperty(EmissionColorID))
                    continue;

                Color originalColor = originalEmissionColors[i][j];

                seq.Join(DOTween.To(
                    () => mat.GetColor(EmissionColorID),
                    value => mat.SetColor(EmissionColorID, value),
                    originalColor,
                    duration
                ));
            }
        }

        return seq;
    }

    private void RestoreOriginalEmission()
    {
        if (cachedMaterials == null || originalEmissionColors == null)
            return;

        for (int i = 0; i < cachedMaterials.Length; i++)
        {
            Material[] mats = cachedMaterials[i];
            if (mats == null)
                continue;

            for (int j = 0; j < mats.Length; j++)
            {
                Material mat = mats[j];
                if (mat == null || !mat.HasProperty(EmissionColorID))
                    continue;

                mat.SetColor(EmissionColorID, originalEmissionColors[i][j]);
            }
        }
    }

    private void SetRenderersVisible(bool visible)
    {
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] != null)
                targetRenderers[i].enabled = visible;
        }

        for (int j = 0; j < targetObjects.Length; j++)
        {
            if (targetObjects[j] != null)
                targetObjects[j].SetActive(visible);
        }
    }
}