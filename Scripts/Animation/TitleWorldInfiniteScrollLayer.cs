using System;
using UnityEngine;

[DisallowMultipleComponent]
public class TitleWorldInfiniteScrollLayer : MonoBehaviour
{
    [Header("Scroll Targets")]
    [Tooltip("스크롤될 오브젝트들입니다. 비워두고 Auto Collect를 켜면 직속 자식들을 자동 등록합니다.")]
    [SerializeField] private Transform[] targets;

    [Tooltip("Awake 시 이 오브젝트의 직속 자식들을 자동으로 Targets에 등록합니다.")]
    [SerializeField] private bool autoCollectChildren = true;

    [Header("Scroll Settings")]
    [Tooltip("왼쪽으로 이동하는 속도입니다.")]
    [Min(0f)]
    [SerializeField] private float scrollSpeed = 3f;

    [Tooltip("Time.timeScale과 무관하게 타이틀 배경을 움직입니다.")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Loop Settings")]
    [Tooltip("이 로컬 X 위치보다 왼쪽으로 나간 오브젝트를 오른쪽으로 되돌립니다.")]
    [SerializeField] private float recycleLeftX = -20f;

    [Tooltip("재배치할 때 오른쪽으로 이동시킬 거리입니다. 반복 구간 전체 너비와 같아야 합니다.")]
    [Min(0.01f)]
    [SerializeField] private float loopWidth = 40f;


    private void Awake()
    {
        if (autoCollectChildren)
            CollectDirectChildren();

        RemoveNullTargets();

        if (targets == null || targets.Length == 0)
        {
            Debug.LogWarning(
                $"[{nameof(TitleWorldInfiniteScrollLayer)}] {name}에 스크롤 대상이 없습니다.",
                this);

            enabled = false;
        }
    }
    private void OnEnable()
    {
        TitleMenu.OnTitleExitStarted += StopScroll;
    }

    private void OnDisable()
    {
        TitleMenu.OnTitleExitStarted -= StopScroll;
    }
    private void Update()
    {
        float deltaTime = useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        float moveDistance = scrollSpeed * deltaTime;

        for (int i = 0; i < targets.Length; i++)
        {
            Transform target = targets[i];

            if (target == null)
                continue;

            Vector3 localPos = target.localPosition;
            localPos.x -= moveDistance;

            while (localPos.x < recycleLeftX)
            {
                localPos.x += loopWidth;
            }

            target.localPosition = localPos;
        }
    }

    [ContextMenu("Collect Direct Children")]
    private void CollectDirectChildren()
    {
        targets = new Transform[transform.childCount];

        for (int i = 0; i < transform.childCount; i++)
        {
            targets[i] = transform.GetChild(i);
        }
    }

    private void RemoveNullTargets()
    {
        if (targets == null)
            return;

        targets = Array.FindAll(targets, target => target != null);
    }

    public void SetScrollSpeed(float speed)
    {
        scrollSpeed = Mathf.Max(0f, speed);
    }

    public void StopScroll()
    {
        enabled = false;
    }

    public void ResumeScroll()
    {
        enabled = true;
    }
}