using System;
using UnityEngine;

/// <summary>자율훈련장 후경의 생산·내구 시험 설비를 게임 판정 없이 구동한다.</summary>
[DisallowMultipleComponent]
public sealed class SelfTrainingBackdropMotion : MonoBehaviour
{
    [Serializable]
    public sealed class DepthLayer
    {
        public Transform Root;
        [Range(0f, 1f)] public float CameraFollow;
        [NonSerialized] public Vector3 Rest;
    }

    [Serializable]
    public sealed class Gear
    {
        public Transform Rotor;
        public float DegreesPerSecond;
        [NonSerialized] public Quaternion Rest;
    }

    [Serializable]
    public sealed class ProductionLine
    {
        public Transform Carrier;
        public Transform BeltStart;
        public Transform BeltEnd;
        public Transform Scanner;
        public Transform ScannerBottom;
        public Transform ScannerTop;
        public SpriteRenderer[] ProductSprites;
        public LineRenderer RevealBeam;
        public Transform[] BeltTicks;
        public float BeltTickSpacing = .48f;
        public float Duration = 12f;
        public float PhaseOffset;
        [NonSerialized] public Vector3[] TickRest;
        [NonSerialized] public Color[] SpriteColors;
    }

    [Serializable]
    public sealed class EnduranceBay
    {
        public Transform Target;
        public LineRenderer Beam;
        public Transform BeamMuzzle;
        public Transform Contact;
        public Transform Shell;
        public Transform ShellMuzzle;
        public Transform Blast;
        public ParticleSystem Sparks;
        public float PhaseOffset;
        [NonSerialized] public int LastCycle = -1;
        [NonSerialized] public Vector3 TargetRest;
    }

    [SerializeField] private DepthLayer[] layers;
    [SerializeField] private Gear[] gears;
    [SerializeField] private ProductionLine[] production;
    [SerializeField] private EnduranceBay[] endurance;
    [SerializeField] private Vector2 cameraReference = new Vector2(0f, 6f);

    private Camera viewCamera;
    private float elapsed;

    public void Configure(DepthLayer[] depthLayers, Gear[] rotors, ProductionLine[] lines, EnduranceBay[] bays)
    {
        layers = depthLayers;
        gears = rotors;
        production = lines;
        endurance = bays;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        viewCamera = Camera.main;
        elapsed = 0f;
        if (layers != null)
            foreach (var layer in layers)
                if (layer.Root != null) layer.Rest = layer.Root.localPosition;
        if (gears != null)
            foreach (var gear in gears)
                if (gear.Rotor != null) gear.Rest = gear.Rotor.localRotation;
        if (production != null)
            foreach (var line in production)
            {
                line.TickRest = new Vector3[line.BeltTicks.Length];
                for (int i = 0; i < line.TickRest.Length; i++)
                    if (line.BeltTicks[i] != null) line.TickRest[i] = line.BeltTicks[i].localPosition;
                line.SpriteColors = new Color[line.ProductSprites.Length];
                for (int i = 0; i < line.SpriteColors.Length; i++)
                    if (line.ProductSprites[i] != null) line.SpriteColors[i] = line.ProductSprites[i].color;
            }
        if (endurance != null)
            foreach (var bay in endurance)
            {
                bay.LastCycle = -1;
                if (bay.Target != null) bay.TargetRest = bay.Target.localPosition;
            }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        elapsed += Time.deltaTime;
        if (gears != null)
            foreach (var gear in gears)
                if (gear.Rotor != null)
                    gear.Rotor.localRotation = gear.Rest * Quaternion.Euler(0f, 0f, elapsed * gear.DegreesPerSecond);
        if (production != null)
            foreach (var line in production) UpdateProduction(line);
        if (endurance != null)
            foreach (var bay in endurance) UpdateEndurance(bay);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || viewCamera == null || layers == null) return;
        Vector3 localCamera = transform.InverseTransformPoint(viewCamera.transform.position);
        var offset = new Vector3(localCamera.x - cameraReference.x, localCamera.y - cameraReference.y, 0f);
        foreach (var layer in layers)
            if (layer.Root != null) layer.Root.localPosition = layer.Rest + offset * layer.CameraFollow;
    }

    private void UpdateProduction(ProductionLine line)
    {
        if (line.Carrier == null || line.BeltStart == null || line.BeltEnd == null) return;
        float duration = Mathf.Max(1f, line.Duration);
        float progress = Mathf.Repeat(elapsed + line.PhaseOffset, duration) / duration;
        bool building = progress < .3f;
        float reveal = Mathf.Clamp01(progress / .3f);
        Vector3 start = line.BeltStart.position;
        Vector3 end = line.BeltEnd.position;
        line.Carrier.position = Vector3.Lerp(start, end, Mathf.Clamp01((progress - .3f) / .62f));
        float alpha = building ? reveal : 1f - Mathf.Clamp01((progress - .92f) / .08f);
        for (int i = 0; i < line.ProductSprites.Length; i++)
            if (line.ProductSprites[i] != null)
            {
                Color color = line.SpriteColors[i];
                color.a *= alpha;
                line.ProductSprites[i].color = color;
            }
        if (line.Scanner != null && line.ScannerBottom != null && line.ScannerTop != null)
            line.Scanner.position = Vector3.Lerp(line.ScannerBottom.position, line.ScannerTop.position, reveal);
        if (line.RevealBeam != null) line.RevealBeam.enabled = building;
        // 0.62주기 동안 생산물이 벨트를 지나는 속도와 눈금·구동 기어의 방향을 맞춘다.
        float beltSpeed = Vector3.Distance(start, end) / (duration * .62f);
        for (int i = 0; i < line.BeltTicks.Length; i++)
            if (line.BeltTicks[i] != null)
            {
                var tick = line.BeltTicks[i];
                Vector3 localVelocity = tick.parent.InverseTransformVector((end - start).normalized * beltSpeed);
                float shift = Mathf.Repeat(elapsed * localVelocity.magnitude, Mathf.Max(.001f, line.BeltTickSpacing));
                tick.localPosition = line.TickRest[i] + localVelocity.normalized * shift;
            }
    }

    private void UpdateEndurance(EnduranceBay bay)
    {
        float time = elapsed + bay.PhaseOffset;
        float phase = Mathf.Repeat(time, 6f);
        if (bay.Beam != null && bay.BeamMuzzle != null && bay.Contact != null)
        {
            bay.Beam.enabled = phase < 3.8f;
            bay.Beam.SetPosition(0, bay.Beam.transform.InverseTransformPoint(bay.BeamMuzzle.position));
            bay.Beam.SetPosition(1, bay.Beam.transform.InverseTransformPoint(bay.Contact.position));
            bay.Beam.widthMultiplier = .035f + .012f * Mathf.Sin(time * 28f);
        }
        if (bay.Shell != null && bay.ShellMuzzle != null && bay.Contact != null)
        {
            bay.Shell.gameObject.SetActive(phase < 1.15f);
            float t = Mathf.Clamp01(phase / 1.15f);
            Vector3 point = Vector3.Lerp(bay.ShellMuzzle.position, bay.Contact.position, t);
            point.y += Mathf.Sin(t * Mathf.PI) * 1.1f;
            bay.Shell.position = point;
            bay.Shell.Rotate(0f, 0f, Time.deltaTime * -180f, Space.Self);
            int cycle = Mathf.FloorToInt(time / 6f);
            if (phase >= 1.15f && bay.LastCycle != cycle)
            {
                bay.LastCycle = cycle;
                if (bay.Sparks != null) bay.Sparks.Play();
            }
        }
        if (bay.Blast != null)
        {
            float blastTime = phase - 1.15f;
            bool visible = blastTime >= 0f && blastTime < .45f;
            bay.Blast.gameObject.SetActive(visible);
            if (visible) bay.Blast.localScale = Vector3.one * (.15f + blastTime * 2.8f);
        }
        if (bay.Target != null)
        {
            float recoil = bay.Shell != null ? Mathf.Max(0f, 1f - Mathf.Abs(phase - 1.2f) * 5f) : 0f;
            bay.Target.localPosition = bay.TargetRest + Vector3.right * recoil * .1f;
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying) return;
        if (layers != null)
            foreach (var layer in layers)
                if (layer.Root != null) layer.Root.localPosition = layer.Rest;
        if (gears != null)
            foreach (var gear in gears)
                if (gear.Rotor != null) gear.Rotor.localRotation = gear.Rest;
        if (production != null)
            foreach (var line in production)
            {
                if (line.Carrier != null && line.BeltStart != null) line.Carrier.position = line.BeltStart.position;
                if (line.RevealBeam != null) line.RevealBeam.enabled = false;
                for (int i = 0; line.TickRest != null && i < line.TickRest.Length; i++)
                    if (line.BeltTicks[i] != null) line.BeltTicks[i].localPosition = line.TickRest[i];
                for (int i = 0; line.SpriteColors != null && i < line.SpriteColors.Length; i++)
                    if (line.ProductSprites[i] != null) line.ProductSprites[i].color = line.SpriteColors[i];
            }
        if (endurance != null)
            foreach (var bay in endurance)
            {
                if (bay.Target != null) bay.Target.localPosition = bay.TargetRest;
                if (bay.Beam != null) bay.Beam.enabled = false;
                if (bay.Blast != null) bay.Blast.gameObject.SetActive(false);
                if (bay.Shell != null) bay.Shell.gameObject.SetActive(false);
                if (bay.Sparks != null) bay.Sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
    }
}
