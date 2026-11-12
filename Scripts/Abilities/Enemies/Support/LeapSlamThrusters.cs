using UnityEngine;

// 도약 내려찍기의 로켓 추진기 두 개(표시 전용). 잡기 공격과 같은 추진기 Prefab을 본체 프레임에 붙여 쓴다.
// 본체는 단단한 기계라 모양이 변하지 않는다. 움직임은 추진기가 만든다.
// - 준비: 프레임 아랫변에서 추진기가 아래로 뻗어 나와 본체를 들어 올린다(노즐 끝은 바닥에 닿아 있다).
// - 도약: 분사해 본체를 밀어 올린다.
// - 낙하: 위쪽에 거꾸로 달려 위로 분사하며 본체를 내리꽂는다.
// - 착지: 떨어져 나가 바닥에 튕긴 뒤 사라진다.
// 판정·이동과는 무관하다. 치수는 프레임(ModelBorderMask)의 로컬 단위다.
public sealed class LeapSlamThrusters
{
    private const int Count = 2;
    // 장착부는 프레임 변에서 MountInset만큼 안쪽에 놓인다. 다 접었을 때는 추진기 머리(분사구, 반높이 HeadHalfHeight)의
    // 끝이 프레임 변과 같은 높이에 오고, 그 자리가 HeadStowedY(장착부 기준)다.
    private const float HeadHalfHeight = 0.16f;
    private const float MountInset = 0.025f;
    private const float HeadStowedY = HeadHalfHeight - MountInset;
    // 프레임 반폭에 대한 추진기 자리의 비율(잡기 공격의 장착 자리와 같다)
    private const float SideRatio = 0.55f;
    // 추진기 Sprite의 정렬 순서에 더하는 값. 본체(Player 0 ~ 2)보다 뒤, 피격 범위 예고(-8 ~ -4)와 같거나 앞이다.
    private const int SortingOffset = -3;
    private const float JettisonLifetime = 1.3f;
    private const float JettisonGravity = 32f;

    private readonly Transform frame;
    private readonly SpriteRenderer frameRenderer;
    private readonly Transform[] roots = new Transform[Count], heads = new Transform[Count], pistons = new Transform[Count], flameRoots = new Transform[Count];
    private readonly ParticleSystem[][] flames = new ParticleSystem[Count][];
    private readonly ParticleSystem.MinMaxCurve[][] flameRotations = new ParticleSystem.MinMaxCurve[Count][];
    private readonly SpriteRenderer[][] surfaces = new SpriteRenderer[Count][];
    private readonly Color[][] surfaceColors = new Color[Count][];
    private readonly Vector2[] velocity = new Vector2[Count];
    private readonly float[] spin = new float[Count];
    private readonly float stroke;
    private float jettisonAge, floorY;
    // 떨어져 나가 프레임 밖(씬 루트)에 있다
    private bool detached;

    /// <summary>뻗은 정도(0 접힘 ~ 1 다 뻗음)</summary>
    public float Deployment { get; private set; }
    public bool IsVisible { get; private set; }
    public bool IsFiring { get; private set; }
    public bool IsJettisoned { get; private set; }
    public bool MountedOnTop { get; private set; }
    public float FlameScale { get; private set; }
    public bool IsValid => roots[0] != null && roots[1] != null;

    /// <summary>노즐 끝이 프레임 아랫변 아래로 내려온 길이(월드 칸). 준비 때 본체를 이만큼 들어 올리면 노즐이 바닥에 닿아 있다.</summary>
    public float LiftHeight => frame != null ? (HeadStowedY - HeadY(Deployment)) * Mathf.Abs(frame.lossyScale.y) : 0f;

    /// <summary>본체 가운데에서 추진기까지의 가로 거리(월드 칸)</summary>
    public float SideOffset => frame != null ? FrameBounds().extents.x * SideRatio * Mathf.Abs(frame.lossyScale.x) : 0f;

    public int ParticleCount
    {
        get
        {
            int count = 0;
            foreach (var group in flames)
                if (group != null)
                    foreach (var flame in group)
                        if (flame != null) count += flame.particleCount;
            return count;
        }
    }

    public LeapSlamThrusters(Transform frame, GameObject prefab, float stroke)
    {
        this.frame = frame;
        this.stroke = Mathf.Max(0.05f, stroke);
        frameRenderer = frame != null ? frame.GetComponent<SpriteRenderer>() : null;
        if (frame == null || prefab == null) return;
        for (int i = 0; i < Count; i++)
        {
            roots[i] = Object.Instantiate(prefab, frame).transform;
            roots[i].name = i == 0 ? "LeapThrusterLeft" : "LeapThrusterRight";
            heads[i] = roots[i].Find("Head");
            pistons[i] = roots[i].Find("Piston");
            flameRoots[i] = roots[i].Find("Head/Port/RocketFlame");
            var socket = roots[i].Find("Socket") != null ? roots[i].Find("Socket").GetComponent<SpriteRenderer>() : null;
            if (socket != null)
            {
                // 프레임에 물리는 가로 플랜지와 두 고정턱(잡기 공격의 장착부와 같은 모양)
                Bracket(roots[i], socket, "MountFlange", new Vector2(0f, 0.02f), new Vector2(0.3f, 0.07f));
                Bracket(roots[i], socket, "MountJawLeft", new Vector2(-0.11f, 0.04f), new Vector2(0.045f, 0.18f));
                Bracket(roots[i], socket, "MountJawRight", new Vector2(0.11f, 0.04f), new Vector2(0.045f, 0.18f));
            }
            flames[i] = roots[i].GetComponentsInChildren<ParticleSystem>(true);
            flameRotations[i] = new ParticleSystem.MinMaxCurve[flames[i].Length];
            for (int j = 0; j < flames[i].Length; j++)
            {
                flames[i][j].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                flameRotations[i][j] = flames[i][j].main.startRotation;
            }
            surfaces[i] = roots[i].GetComponentsInChildren<SpriteRenderer>(true);
            surfaceColors[i] = new Color[surfaces[i].Length];
            for (int j = 0; j < surfaces[i].Length; j++)
            {
                surfaceColors[i][j] = surfaces[i][j].color;
                // 본체보다 뒤에 그려, 접혀 있을 때는 본체에 가려 있다가 아래로 빠져나오는 것처럼 보이게 한다.
                surfaces[i][j].sortingOrder += SortingOffset;
            }
            // 본체와 같은 카메라가 그리도록 본체의 Layer를 따른다.
            foreach (var part in roots[i].GetComponentsInChildren<Transform>(true)) part.gameObject.layer = frame.gameObject.layer;
            roots[i].gameObject.SetActive(false);
        }
    }

    private float HeadY(float deployment) => Mathf.Lerp(HeadStowedY, -(0.08f + stroke), Mathf.Clamp01(deployment));

    private Bounds FrameBounds()
        => frameRenderer != null && frameRenderer.sprite != null ? frameRenderer.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);

    /// <summary>추진기를 프레임 아랫변(도약) 또는 윗변(낙하, 거꾸로)에 단다. 떨어져 나간 추진기는 다시 붙인다.</summary>
    public void Mount(bool onTop)
    {
        if (!IsValid) return;
        if (IsJettisoned) Hide();
        Reattach();
        MountedOnTop = onTop;
        TurnFlames(onTop);
        Place();
    }

    private void Reattach()
    {
        if (!detached) return;
        detached = false;
        for (int i = 0; i < Count; i++) roots[i].SetParent(frame, false);
    }

    // 불꽃 입자는 화면을 보고 그려져 Transform을 뒤집어도 그림이 돌지 않는다. 위로 분사할 때는 입자를 반 바퀴 돌려 혀끝이 위를 향하게 한다.
    private void TurnFlames(bool upward)
    {
        for (int i = 0; i < Count; i++)
            for (int j = 0; j < flames[i].Length; j++)
            {
                if (flames[i][j] == null) continue;
                var main = flames[i][j].main;
                main.startRotation = upward ? Turned(flameRotations[i][j]) : flameRotations[i][j];
            }
    }

    private static ParticleSystem.MinMaxCurve Turned(ParticleSystem.MinMaxCurve rotation)
    {
        switch (rotation.mode)
        {
            case ParticleSystemCurveMode.Constant: return new ParticleSystem.MinMaxCurve(rotation.constant + Mathf.PI);
            case ParticleSystemCurveMode.TwoConstants: return new ParticleSystem.MinMaxCurve(rotation.constantMin + Mathf.PI, rotation.constantMax + Mathf.PI);
            default: return rotation;
        }
    }

    /// <summary>분사구 끝의 월드 좌표. 아래에 달렸을 때는 아래 끝, 위에 달렸을 때는 위 끝이다.</summary>
    public Vector2 NozzleTip(int index)
        => IsValid && heads[index] != null ? (Vector2)heads[index].TransformPoint(new Vector3(0f, 0.5f, 0f)) : Vector2.zero;

    public void SetDeployment(float deployment)
    {
        if (!IsValid || IsJettisoned) return;
        Deployment = Mathf.Clamp01(deployment);
        IsVisible = Deployment > 0f;
        Place();
    }

    private void Place()
    {
        Bounds bounds = FrameBounds();
        float headY = HeadY(Deployment);
        for (int i = 0; i < Count; i++)
        {
            float side = i == 0 ? -1f : 1f;
            roots[i].localPosition = new Vector3(bounds.center.x + side * bounds.extents.x * SideRatio,
                MountedOnTop ? bounds.max.y - MountInset : bounds.min.y + MountInset, 0f);
            roots[i].localRotation = MountedOnTop ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
            roots[i].localScale = Vector3.one;
            roots[i].gameObject.SetActive(IsVisible);
            if (heads[i] != null) heads[i].localPosition = new Vector3(0f, headY, 0f);
            if (pistons[i] != null)
            {
                // 장착부에서 머리까지 잇는 기둥. 접혀 있을 때는 머리 뒤에 숨는다.
                float length = Mathf.Max(0f, -headY);
                pistons[i].localPosition = new Vector3(0f, -length * 0.5f, 0f);
                pistons[i].localScale = new Vector3(0.17f, 0.04f + length, 1f);
            }
        }
    }

    /// <summary>분사 불꽃의 크기를 정한다. 0 이하면 끈다.</summary>
    public void SetFlame(float scale)
    {
        if (!IsValid) return;
        FlameScale = Mathf.Max(0f, scale);
        IsFiring = FlameScale > 0f && IsVisible && !IsJettisoned;
        for (int i = 0; i < Count; i++)
        {
            if (flameRoots[i] != null && IsFiring) flameRoots[i].localScale = Vector3.one * FlameScale;
            foreach (var flame in flames[i])
            {
                if (flame == null) continue;
                if (IsFiring) { if (!flame.isEmitting) flame.Play(false); }
                else flame.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    /// <summary>지금 자세 그대로 떼어 내 좌우로 튕겨 보낸다. floorY는 튕길 바닥 높이다.</summary>
    public void Jettison(float floorY)
    {
        if (!IsValid || !IsVisible || IsJettisoned) return;
        SetFlame(0f);
        IsJettisoned = true;
        detached = true;
        jettisonAge = 0f;
        this.floorY = floorY + 0.3f;
        for (int i = 0; i < Count; i++)
        {
            // 본체가 좌우로 뒤집혀 있어도 화면에서 바깥쪽으로 튕긴다.
            float side = roots[i].position.x < frame.position.x ? -1f : 1f;
            roots[i].SetParent(null, true);
            velocity[i] = new Vector2(side * Random.Range(3.5f, 5.5f), Random.Range(6f, 9f));
            spin[i] = -side * Random.Range(220f, 340f);
        }
    }

    /// <summary>떨어져 나간 추진기를 움직인다. 매 프레임 부른다.</summary>
    public void Tick(float deltaTime)
    {
        if (!IsJettisoned || !IsValid) return;
        jettisonAge += Mathf.Max(0f, deltaTime);
        if (jettisonAge >= JettisonLifetime)
        {
            Hide();
            return;
        }
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, JettisonLifetime, jettisonAge));
        for (int i = 0; i < Count; i++)
        {
            velocity[i].y -= JettisonGravity * deltaTime;
            Vector3 position = roots[i].position + (Vector3)(velocity[i] * deltaTime);
            if (position.y < floorY && velocity[i].y < 0f)
            {
                // 바닥에 튕길 때마다 힘을 잃는다.
                position.y = floorY;
                velocity[i] = new Vector2(velocity[i].x * 0.55f, Mathf.Abs(velocity[i].y) > 2.5f ? -velocity[i].y * 0.32f : 0f);
                spin[i] *= 0.4f;
            }
            roots[i].position = position;
            roots[i].rotation *= Quaternion.Euler(0f, 0f, spin[i] * deltaTime);
            for (int j = 0; j < surfaces[i].Length; j++)
            {
                if (surfaces[i][j] == null) continue;
                Color color = surfaceColors[i][j];
                color.a *= fade;
                surfaces[i][j].color = color;
            }
        }
    }

    /// <summary>바로 거두고 프레임에 접힌 채로 되돌린다(패턴 중단·종료).</summary>
    public void Hide()
    {
        if (!IsValid || frame == null) return;
        for (int i = 0; i < Count; i++)
        {
            foreach (var flame in flames[i])
                if (flame != null) flame.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int j = 0; j < surfaces[i].Length; j++)
                if (surfaces[i][j] != null) surfaces[i][j].color = surfaceColors[i][j];
        }
        IsJettisoned = IsFiring = IsVisible = false;
        MountedOnTop = false;
        Deployment = FlameScale = 0f;
        TurnFlames(false);
        if (detached && !frame.gameObject.activeInHierarchy)
        {
            // 본체가 꺼지는 도중에는 그 아래로 옮길 수 없다. 꺼 두기만 하고, 다음에 달 때(Mount) 다시 붙인다.
            for (int i = 0; i < Count; i++) roots[i].gameObject.SetActive(false);
            return;
        }
        Reattach();
        Place();
    }

    public void Destroy()
    {
        for (int i = 0; i < Count; i++)
            if (roots[i] != null) Object.Destroy(roots[i].gameObject);
    }

    private static void Bracket(Transform parent, SpriteRenderer source, string name, Vector2 position, Vector2 size)
    {
        if (source.sprite == null) return;
        var part = new GameObject(name).AddComponent<SpriteRenderer>();
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        Vector2 original = source.sprite.bounds.size;
        part.transform.localScale = new Vector3(size.x / Mathf.Max(0.001f, original.x), size.y / Mathf.Max(0.001f, original.y), 1f);
        part.sprite = source.sprite;
        part.sharedMaterial = source.sharedMaterial;
        part.color = source.color;
        part.sortingLayerID = source.sortingLayerID;
        part.sortingOrder = source.sortingOrder + 1;
    }
}
