using System;
using System.Collections;
using UnityEngine;

public class SkillGaugeOrb : WorldEntityBase
{
    [Header("SkillGaugeOrb Arrive Feedback")]
    [SerializeField] private FeedbackData arriveFeedback;

    private Coroutine routine;
    private Action<float> onArrive;
    private float gainAmount;
    private bool arrived;

    public override void OnSpawnedFromPool()
    {
        base.OnSpawnedFromPool();

        routine = null;
        onArrive = null;
        gainAmount = 0f;
        arrived = false;

        transform.localScale = Vector3.one;
    }

    public void Play(
        Vector2 startPos,
        Transform target,
        float gainAmount,
        SkillGaugeOrbSettings settings,
        Action<float> onArrive)
    {
        this.gainAmount = gainAmount;
        this.onArrive = onArrive;
        arrived = false;

        transform.position = startPos;
        transform.localScale = Vector3.one;

        routine = StartCoroutine(CoPlay(startPos, target, settings));
    }

    private IEnumerator CoPlay(
        Vector2 startPos,
        Transform target,
        SkillGaugeOrbSettings settings)
    {
        Vector2 randomDir = new Vector2(
            UnityEngine.Random.Range(settings.randomXRange.x, settings.randomXRange.y),
            UnityEngine.Random.Range(settings.randomYRange.x, settings.randomYRange.y)
        );

        if (randomDir.sqrMagnitude <= 0.0001f)
            randomDir = Vector2.up;

        randomDir.Normalize();

        Vector2 popTarget = startPos + randomDir * settings.popDistance;

        yield return MoveTo(
            startPos,
            popTarget,
            settings.popDuration,
            EaseOutCubic);

        if (settings.waitDuration > 0f)
            yield return new WaitForSeconds(settings.waitDuration);

        Vector2 homingStart = transform.position;
        float t = 0f;

        while (t < settings.homingDuration)
        {
            if (target == null)
                break;

            t += Time.deltaTime;

            float k = Mathf.Clamp01(t / settings.homingDuration);
            float eased = EaseInCubic(k);

            Vector2 targetPos = target.position;
            transform.position = Vector2.Lerp(homingStart, targetPos, eased);

            if (Vector2.Distance(transform.position, targetPos) <= settings.arriveDistance)
                break;

            yield return null;
        }

        Arrive();

        yield return ScaleOut(settings.arriveScaleDuration);

        ReturnToPool();
    }

    private void Arrive()
    {
        if (arrived)
            return;

        arrived = true;
        onArrive?.Invoke(gainAmount);
        onArrive = null;

        if (arriveFeedback != null)
            FeedbackPlayer.Instance.Play(arriveFeedback, transform.position);
    }

    private IEnumerator MoveTo(
        Vector2 from,
        Vector2 to,
        float duration,
        Func<float, float> ease)
    {
        if (duration <= 0f)
        {
            transform.position = to;
            yield break;
        }

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;

            float k = Mathf.Clamp01(t / duration);
            transform.position = Vector2.Lerp(from, to, ease(k));

            yield return null;
        }

        transform.position = to;
    }

    private IEnumerator ScaleOut(float duration)
    {
        if (duration <= 0f)
            yield break;

        Vector3 startScale = transform.localScale;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;

            float k = Mathf.Clamp01(t / duration);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, EaseInCubic(k));

            yield return null;
        }

        transform.localScale = Vector3.zero;
    }

    public override void ForceReturnToPool()
    {
        onArrive = null;
        arrived = true;
        base.ForceReturnToPool();
    }

    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    private float EaseInCubic(float t)
    {
        return t * t * t;
    }
}