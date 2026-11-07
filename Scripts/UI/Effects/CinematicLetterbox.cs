using System;
using System.Collections.Generic;
using UnityEngine;

// 여러 연출이 자기 소유권만 반환하는 공통 레터박스다.
public sealed class CinematicLetterbox : MonoBehaviour
{
    [SerializeField] private RectTransform top;
    [SerializeField] private RectTransform bottom;
    [SerializeField] private float heightRatio = .115f;
    [SerializeField] private float blendTime = .2f;
    private static CinematicLetterbox instance;
    private readonly Dictionary<int, UnityEngine.Object> owners = new Dictionary<int, UnityEngine.Object>();
    private int nextId;
    private float amount, from, target, changedAt, activeBlendTime = -.1f;
    public static float VisibleHeightRatio => 1 - 2 * (instance != null ? instance.heightRatio : .115f);
    public float Amount => amount;
    public int RequestCount => owners.Count;
    public sealed class Lease : IDisposable
    {
        private CinematicLetterbox box; private readonly int id;
        internal Lease(CinematicLetterbox box, int id) { this.box = box; this.id = id; }
        public void Dispose() => Release(-1);
        public void Release(float duration) { if(box!=null) box.Remove(id,duration); box=null; }
    }
    public static Lease Acquire(CinematicLetterbox prefab, UnityEngine.Object owner)
    {
        if (instance == null) instance = Instantiate(prefab);
        int id = ++instance.nextId; instance.owners[id] = owner; instance.activeBlendTime=instance.blendTime; instance.SetTarget(1);
        return new Lease(instance, id);
    }
    private void Remove(int id,float duration=-1) { owners.Remove(id); if(owners.Count==0) { activeBlendTime=duration>0 ? duration : blendTime; SetTarget(0); } }
    private void SetTarget(float value)
    { if (target == value) return; from = amount; target = value; changedAt = Time.unscaledTime; }
    private void Update()
    {
        var expired = new List<int>();
        foreach (var pair in owners)
            if (pair.Value == null || (pair.Value is GameObject go && !go.activeInHierarchy)
                || (pair.Value is Component c && !c.gameObject.activeInHierarchy)
                || (pair.Value is Behaviour b && !b.isActiveAndEnabled)) expired.Add(pair.Key);
        foreach (int id in expired) Remove(id);
        float t = Mathf.Clamp01((Time.unscaledTime - changedAt) / Mathf.Max(.001f, activeBlendTime>0 ? activeBlendTime : blendTime));
        amount = Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, t));
        float height = ((RectTransform)transform).rect.height * heightRatio * amount;
        top.sizeDelta = bottom.sizeDelta = new Vector2(0, height);
        if (owners.Count == 0 && amount < .001f) { if (instance == this) instance = null; Destroy(gameObject); }
    }
    private void OnDestroy() { if (instance == this) instance = null; }
}
