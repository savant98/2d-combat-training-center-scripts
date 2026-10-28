using System;
using UnityEngine;

/// <summary>후경의 제작, 이송, 집게 인계를 충돌 판정 없이 연출한다.</summary>
public sealed class TrainingFabricationFlow : MonoBehaviour
{
    [Serializable]
    private sealed class Product
    {
        public Transform Carrier;
        public Transform Stage;
        public Transform Gantry;
        public SpriteMask Mask;
        public Transform LeftWelder;
        public Transform RightWelder;
        public LineRenderer RevealLaser;
        public Transform LeftSpark;
        public Transform RightSpark;
        public float MaskStartY;
        public float MaskEndY;
        public float HalfWidth;
        public float HalfHeight;
        public int LoaderIndex;
    }

    [Serializable]
    private sealed class LoaderArm
    {
        public Transform Anchor;
        public Transform Rest;
        public Transform UpperLink;
        public Transform LowerLink;
        public Transform Elbow;
        public Transform Pad;
        public float PadWidth;
        [NonSerialized] public Vector3 CurrentPad;
    }

    [SerializeField] private Transform depthLayer;
    [SerializeField] private Transform conveyor;
    [SerializeField] private Transform crane;
    [SerializeField] private Transform cranePivot;
    [SerializeField] private Transform leftJaw;
    [SerializeField] private Transform rightJaw;
    [SerializeField] private LineRenderer craneCable;
    [SerializeField] private LoaderArm[] loaderArms;
    [SerializeField] private Product[] products;
    [SerializeField, Min(.01f)] private float preparationDuration = 1.5f;
    [SerializeField, Min(.01f)] private float revealDuration = 4f;
    [SerializeField, Min(.01f)] private float conveyorSpeed = .7f;
    [SerializeField, Min(.01f)] private float craneReturnDuration = 2.5f;

    private int productIndex;
    private float elapsed;
    private Product active;
    private Vector3 stage;
    private Vector3 beltStart;
    private Vector3 beltEnd;
    private Vector3 destination;
    private Vector3 craneStartGrip;
    private float leftJawStart;
    private float rightJawStart;
    private float leftWelderStart;
    private float rightWelderStart;
    private float scanStart;
    private float scanEnd;
    private float beltDuration;

    private void OnEnable()
    {
        if (depthLayer == null || conveyor == null || crane == null || cranePivot == null ||
            leftJaw == null || rightJaw == null || products == null || products.Length == 0)
            return;

        foreach (Product product in products)
        {
            if (product.Carrier != null) product.Carrier.gameObject.SetActive(false);
            if (product.RevealLaser != null) product.RevealLaser.gameObject.SetActive(false);
        }

        if (loaderArms != null)
        {
            foreach (LoaderArm arm in loaderArms)
            {
                if (arm.Rest == null) continue;
                arm.CurrentPad = depthLayer.InverseTransformPoint(arm.Rest.position);
                DrawLoader(arm);
            }
        }

        productIndex = 0;
        BeginProduct();
    }

    private void OnDisable()
    {
        if (products == null) return;
        foreach (Product product in products)
        {
            if (product.RevealLaser != null) product.RevealLaser.gameObject.SetActive(false);
            if (product.Carrier != null) product.Carrier.gameObject.SetActive(false);
        }
        active = null;
    }

    private void BeginProduct()
    {
        active = products[productIndex];
        if (active.Carrier == null || active.Stage == null || active.Mask == null ||
            active.RevealLaser == null || active.LeftWelder == null || active.RightWelder == null ||
            active.LeftSpark == null || active.RightSpark == null)
        {
            active = null;
            return;
        }

        elapsed = 0f;
        stage = depthLayer.InverseTransformPoint(active.Stage.position);
        float entryX = Mathf.Clamp(stage.x, conveyor.localPosition.x - 3.8f,
            conveyor.localPosition.x + 3.6f);
        float beltY = conveyor.localPosition.y + .65f + active.HalfHeight;
        beltStart = new Vector3(entryX, beltY, stage.z);
        beltEnd = new Vector3(conveyor.localPosition.x + 4.45f, beltY, stage.z);
        destination = new Vector3(crane.localPosition.x, crane.localPosition.y - 6f, stage.z);
        beltDuration = Mathf.Abs(beltEnd.x - beltStart.x) / Mathf.Max(.01f, conveyorSpeed);
        craneStartGrip = depthLayer.InverseTransformPoint(cranePivot.TransformPoint(new Vector3(0f, -4f, 0f)));
        leftJawStart = leftJaw.localPosition.x;
        rightJawStart = rightJaw.localPosition.x;
        leftWelderStart = active.LeftWelder.localPosition.y;
        rightWelderStart = active.RightWelder.localPosition.y;
        active.Carrier.localScale = Vector3.one;
        active.Carrier.localPosition = stage;
        active.Carrier.gameObject.SetActive(true);
        SetMask(0f);
        scanStart = MaskTopInDepth();
        SetMask(1f);
        scanEnd = MaskTopInDepth();
        SetMask(0f);
        SetLaser(false);
        PositionRevealLaser(scanStart);
    }

    private void LateUpdate()
    {
        if (active == null) return;
        elapsed += Time.deltaTime;
        float prepareEnd = Mathf.Max(.01f, preparationDuration);
        float revealEnd = prepareEnd + Mathf.Max(.01f, revealDuration);
        float loadEnd = revealEnd + 1.2f;
        float beltEndTime = loadEnd + beltDuration;
        float grabEnd = beltEndTime + 1.2f;
        float liftEnd = grabEnd + .8f;
        float traverseEnd = liftEnd + 1.4f;
        float lowerEnd = traverseEnd + 1.4f;
        float releaseEnd = lowerEnd + 1f;
        float returnEnd = releaseEnd + Mathf.Max(.01f, craneReturnDuration);

        if (elapsed >= revealEnd)
        {
            SetLaser(false);
            SetMask(1f);
            float retract = Mathf.Clamp01((elapsed - revealEnd) / 1.5f);
            float welderY = Mathf.Lerp(scanEnd, scanStart, Mathf.SmoothStep(0f, 1f, retract));
            MoveWelder(active.LeftWelder, welderY);
            MoveWelder(active.RightWelder, welderY);
        }

        if (elapsed < prepareEnd)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / prepareEnd));
            active.Carrier.localPosition = stage;
            SetMask(0f);
            PositionRevealLaser(scanStart);
            PrepareWelder(active.LeftWelder, leftWelderStart, scanStart, t);
            PrepareWelder(active.RightWelder, rightWelderStart, scanStart, t);
            Vector3 idle = new Vector3(beltEnd.x, beltEnd.y + 1f, beltEnd.z);
            SetCranePosition(Vector3.Lerp(craneStartGrip, idle, t));
            leftJaw.localPosition = new Vector3(Mathf.Lerp(leftJawStart, -active.HalfWidth - .4f, t), -4f, -.15f);
            rightJaw.localPosition = new Vector3(Mathf.Lerp(rightJawStart, active.HalfWidth + .4f, t), -4f, -.15f);
        }
        else if (elapsed < revealEnd)
        {
            active.Carrier.localPosition = stage;
            SetMask(Mathf.Clamp01((elapsed - prepareEnd) / (revealEnd - prepareEnd)));
            float scanY = MaskTopInDepth();
            PositionRevealLaser(scanY);
            MoveWelder(active.LeftWelder, scanY);
            MoveWelder(active.RightWelder, scanY);
            SetLaser(true);
            SetCrane(new Vector3(beltEnd.x, beltEnd.y + 1f, beltEnd.z), .4f);
        }
        else if (elapsed < loadEnd)
        {
            active.Carrier.localPosition = Vector3.Lerp(stage, beltStart, (elapsed - revealEnd) / 1.2f);
            SetCrane(new Vector3(beltEnd.x, beltEnd.y + 1f, beltEnd.z), .4f);
        }
        else if (elapsed < beltEndTime)
        {
            float t = beltDuration <= 0f ? 1f : (elapsed - loadEnd) / beltDuration;
            active.Carrier.localPosition = Vector3.Lerp(beltStart, beltEnd, t);
            SetCrane(new Vector3(beltEnd.x, beltEnd.y + 1f, beltEnd.z), .4f);
        }
        else if (elapsed < grabEnd)
        {
            float t = (elapsed - beltEndTime) / 1.2f;
            active.Carrier.localPosition = beltEnd;
            SetCrane(Vector3.Lerp(beltEnd + Vector3.up, beltEnd, Mathf.Clamp01(t * 2f)),
                Mathf.Lerp(.4f, .02f, Mathf.Clamp01((t - .5f) * 2f)));
        }
        else if (elapsed < liftEnd)
        {
            Vector3 held = Vector3.Lerp(beltEnd, beltEnd + Vector3.up * 1.3f, (elapsed - grabEnd) / .8f);
            active.Carrier.localPosition = held;
            SetCrane(held, .02f);
        }
        else if (elapsed < traverseEnd)
        {
            Vector3 held = Vector3.Lerp(beltEnd + Vector3.up * 1.3f,
                new Vector3(destination.x, beltEnd.y + 1.3f, destination.z), (elapsed - liftEnd) / 1.4f);
            active.Carrier.localPosition = held;
            SetCrane(held, .02f);
        }
        else if (elapsed < lowerEnd)
        {
            Vector3 held = Vector3.Lerp(new Vector3(destination.x, beltEnd.y + 1.3f, destination.z),
                destination, (elapsed - traverseEnd) / 1.4f);
            active.Carrier.localPosition = held;
            SetCrane(held, .02f);
        }
        else if (elapsed < releaseEnd)
        {
            float t = (elapsed - lowerEnd) / 1f;
            active.Carrier.localPosition = destination;
            SetCrane(destination, Mathf.Lerp(.02f, .4f, t));
        }
        else if (elapsed < returnEnd)
        {
            active.Carrier.gameObject.SetActive(false);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - releaseEnd) / (returnEnd - releaseEnd)));
            SetCrane(Vector3.Lerp(destination, new Vector3(beltEnd.x, beltEnd.y + 1f, beltEnd.z), t), .4f);
        }
        else
        {
            active.Carrier.gameObject.SetActive(false);
            productIndex = (productIndex + 1) % products.Length;
            BeginProduct();
            return;
        }

        UpdateLoaders(revealEnd, loadEnd);
        if (craneCable != null)
        {
            craneCable.SetPosition(0, new Vector3(0f, 5.7f, 0f));
            craneCable.SetPosition(1, cranePivot.localPosition);
        }
    }

    private void SetMask(float amount)
    {
        Vector3 p = active.Mask.transform.localPosition;
        p.y = Mathf.Lerp(active.MaskStartY, active.MaskEndY, amount);
        active.Mask.transform.localPosition = p;
    }

    private float MaskTopInDepth()
    {
        Bounds b = active.Mask.bounds;
        return depthLayer.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)).y;
    }

    private void PositionRevealLaser(float scanY)
    {
        active.RevealLaser.transform.position = depthLayer.TransformPoint(new Vector3(stage.x, scanY, stage.z));
        active.RevealLaser.SetPosition(0, new Vector3(-active.HalfWidth, 0f, 0f));
        active.RevealLaser.SetPosition(1, new Vector3(active.HalfWidth, 0f, 0f));
        active.LeftSpark.localPosition = new Vector3(-active.HalfWidth, 0f, 0f);
        active.RightSpark.localPosition = new Vector3(active.HalfWidth, 0f, 0f);
    }

    private float WelderLocalY(Transform welder, float scanY)
    {
        return welder.parent.InverseTransformPoint(depthLayer.TransformPoint(new Vector3(stage.x, scanY, stage.z))).y;
    }

    private void PrepareWelder(Transform welder, float start, float scanY, float amount)
    {
        Vector3 p = welder.localPosition;
        p.y = Mathf.Lerp(start, WelderLocalY(welder, scanY), amount);
        welder.localPosition = p;
    }

    private void MoveWelder(Transform welder, float scanY)
    {
        Vector3 p = welder.localPosition;
        p.y = WelderLocalY(welder, scanY);
        welder.localPosition = p;
    }

    private void SetLaser(bool visible)
    {
        if (active.RevealLaser.gameObject.activeSelf != visible)
            active.RevealLaser.gameObject.SetActive(visible);
    }

    private void UpdateLoaders(float revealEnd, float loadEnd)
    {
        if (loaderArms == null) return;
        for (int i = 0; i < loaderArms.Length; i++)
        {
            LoaderArm arm = loaderArms[i];
            if (arm.Rest == null) continue;
            Vector3 rest = depthLayer.InverseTransformPoint(arm.Rest.position);
            Vector3 target = rest;
            if (i == active.LoaderIndex)
            {
                Vector3 stagePad = stage + Vector3.down * (active.HalfHeight + .08f);
                Vector3 beltPad = beltStart + Vector3.down * (active.HalfHeight + .08f);
                if (elapsed >= revealEnd - 1.5f && elapsed < revealEnd)
                    target = Vector3.Lerp(rest, stagePad, Mathf.Clamp01((elapsed - revealEnd + 1.5f) / 1.5f));
                else if (elapsed >= revealEnd && elapsed < loadEnd)
                    target = active.Carrier.localPosition + Vector3.down * (active.HalfHeight + .08f);
                else if (elapsed >= loadEnd && elapsed < loadEnd + 1.5f)
                    target = Vector3.Lerp(beltPad, rest, Mathf.Clamp01((elapsed - loadEnd) / 1.5f));
            }
            arm.CurrentPad = Vector3.MoveTowards(arm.CurrentPad, target, 4f * Time.deltaTime);
            DrawLoader(arm);
        }
    }

    private void DrawLoader(LoaderArm arm)
    {
        if (arm.Anchor == null || arm.UpperLink == null || arm.LowerLink == null ||
            arm.Elbow == null || arm.Pad == null) return;
        Vector3 anchor = depthLayer.InverseTransformPoint(arm.Anchor.position);
        Vector3 end = arm.CurrentPad + Vector3.left * (arm.PadWidth * .5f);
        Vector3 elbow = (anchor + end) * .5f + Vector3.left * .38f;
        DrawLink(arm.UpperLink, anchor, elbow);
        DrawLink(arm.LowerLink, elbow, end);
        arm.Elbow.position = depthLayer.TransformPoint(elbow);
        arm.Pad.position = depthLayer.TransformPoint(arm.CurrentPad);
        arm.Anchor.Rotate(0f, 0f, -35f * Time.deltaTime);
        arm.Elbow.Rotate(0f, 0f, 35f * Time.deltaTime);
    }

    private void DrawLink(Transform link, Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        link.position = depthLayer.TransformPoint((start + end) * .5f);
        link.rotation = depthLayer.rotation * Quaternion.Euler(0f, 0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
        link.localScale = new Vector3(.18f, delta.magnitude, 1f);
    }

    private void SetCranePosition(Vector3 gripCenter)
    {
        Vector3 pivot = crane.InverseTransformPoint(depthLayer.TransformPoint(gripCenter));
        pivot.y += 4f;
        cranePivot.localPosition = pivot;
        cranePivot.localRotation = Quaternion.identity;
    }

    private void SetCrane(Vector3 gripCenter, float extraOpening)
    {
        SetCranePosition(gripCenter);
        leftJaw.localPosition = new Vector3(-active.HalfWidth - extraOpening, -4f, -.15f);
        rightJaw.localPosition = new Vector3(active.HalfWidth + extraOpening, -4f, -.15f);
    }
}
