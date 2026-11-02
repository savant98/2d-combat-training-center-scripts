using UnityEngine;

// 배치용 벽 루트는 보존하고 내부 면만 뜯겨 추락한 뒤 디지털 잔상처럼 복구한다.
[DisallowMultipleComponent]
public sealed class NightmareWallPeelLoop : MonoBehaviour
{
    [SerializeField] private Transform[] panels;
    [SerializeField] private Renderer[] faces;
    [SerializeField, Min(6f)] private float cycleDuration = 18f;
    [SerializeField] private float floorY = -4.982f;
    [SerializeField] private Transform[] rotors;
    [SerializeField] private float[] rotationSpeeds;
    [SerializeField] private Transform[] beltTicks;
    [SerializeField] private Renderer[] lightLeaks;
    [SerializeField] private int[] lightLeakPanels;
    private Quaternion[] rotorHome;
    private Vector3[] beltHome;
    private Vector3[] homePositions;
    private Quaternion[] homeRotations;
    private Renderer[][] panelFaces;
    private int[][] homeOrders;
    private float[] panelRadii;
    private MaterialPropertyBlock properties;
    private float elapsed;
    public float Elapsed => elapsed;
    public int PanelCount => panels == null ? 0 : panels.Length;

    private void OnEnable()
    {
        if (!Application.isPlaying || panels == null) return;
        homePositions = new Vector3[panels.Length]; homeRotations = new Quaternion[panels.Length];
        for (int i = 0; i < panels.Length; i++) { homePositions[i] = panels[i].localPosition; homeRotations[i] = panels[i].localRotation; }
        panelFaces = new Renderer[panels.Length][]; homeOrders = new int[panels.Length][]; panelRadii = new float[panels.Length];
        for (int i = 0; i < panels.Length; i++)
        {
            panelFaces[i] = panels[i].GetComponentsInChildren<Renderer>(true);
            homeOrders[i] = new int[panelFaces[i].Length];
            for (int j = 0; j < panelFaces[i].Length; j++)
            { homeOrders[i][j] = panelFaces[i][j].sortingOrder; panelRadii[i] = Mathf.Max(panelRadii[i], Vector3.Distance(panels[i].position, panelFaces[i][j].bounds.center) + panelFaces[i][j].bounds.extents.magnitude); }
        }
        if (properties == null) properties = new MaterialPropertyBlock();
        if(rotors!=null){rotorHome=new Quaternion[rotors.Length];for(int i=0;i<rotors.Length;i++)rotorHome[i]=rotors[i].localRotation;}
        if(beltTicks!=null){beltHome=new Vector3[beltTicks.Length];for(int i=0;i<beltTicks.Length;i++)beltHome[i]=beltTicks[i].localPosition;}
        elapsed = 0; Apply();
    }

    private void Update()
    {
        if (!Application.isPlaying || homePositions == null) return;
        elapsed += Time.deltaTime;
        Apply();
        if(rotorHome!=null)for(int i=0;i<rotors.Length;i++)rotors[i].localRotation=rotorHome[i]*Quaternion.Euler(0,0,elapsed*rotationSpeeds[i]);
        if(beltHome!=null)for(int i=0;i<beltTicks.Length;i++)beltTicks[i].localPosition=beltHome[i]+Vector3.right*Mathf.Repeat(elapsed*.3f,.46f);
    }

    private void Apply()
    {
        for (int i = 0; i < panels.Length; i++)
        {
            float phase = Mathf.Repeat(elapsed / (cycleDuration + i % 3 * 1.3f) + i * .618034f, 1f);
            float direction = i % 2 == 0 ? 1 : -1;
            Vector3 offset = Vector3.zero; Quaternion rotation = homeRotations[i]; float alpha = 1;
            bool detached = phase >= .74f && phase < .94f;
            if (detached)
            {
                float peel = Mathf.SmoothStep(0, 1, Mathf.Clamp01((phase - .74f) / .07f));
                float fall = Mathf.Clamp01((phase - .79f) / .13f);
                // 회전한 조각의 윗모서리까지 실제 바닥 아래에 들어간 뒤 사라지게 한다.
                float distance = Mathf.Max(2, panels[i].parent.TransformPoint(homePositions[i]).y - floorY + panelRadii[i] + .5f);
                var worldOffset = new Vector3(direction * (.12f * peel + .8f * fall * fall), -distance * fall * fall, -.45f * peel);
                offset = panels[i].parent.InverseTransformVector(worldOffset);
                rotation *= Quaternion.Euler(0, direction * 32f * peel, direction * (6f * peel + 58f * fall * fall));
                alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((fall - .94f) / .06f));
            }
            else if (phase >= .94f && phase < .97f) alpha = 0;
            else if (phase >= .97f) alpha = Mathf.SmoothStep(0, 1, (phase - .97f) / .03f);
            panels[i].localPosition = homePositions[i] + offset;
            panels[i].localRotation = rotation;
            properties.Clear(); properties.SetFloat("_Visibility", alpha);
            properties.SetFloat("_EdgeGlow", detached ? 2.4f : 1f);
            for (int j = 0; j < panelFaces[i].Length; j++)
            {
                // 같은 후경 RT 안에서만 하부 벽보다 앞에 그려 낙하가 가려지지 않게 한다.
                panelFaces[i][j].sortingOrder = detached ? Mathf.Max(-2, homeOrders[i][j]) : homeOrders[i][j];
                panelFaces[i][j].SetPropertyBlock(properties);
            }
        }
        if (lightLeaks != null && lightLeakPanels != null)
            for (int i = 0; i < Mathf.Min(lightLeaks.Length, lightLeakPanels.Length); i++)
            {
                int panel = lightLeakPanels[i];
                if (lightLeaks[i] == null || panel < 0 || panel >= panels.Length) continue;
                float drop = Mathf.Abs(panels[panel].parent.TransformVector(panels[panel].localPosition - homePositions[panel]).y);
                faces[panel].GetPropertyBlock(properties);
                float opening = Mathf.Max(1 - properties.GetFloat("_Visibility"), Mathf.Clamp01((drop - .55f) / 1f));
                properties.Clear(); properties.SetFloat("_Visibility", opening * .95f);
                lightLeaks[i].SetPropertyBlock(properties);
            }
    }

    private void OnDisable()
    {
        if(rotorHome!=null&&rotors!=null&&rotorHome.Length==rotors.Length)for(int i=0;i<rotors.Length;i++)if(rotors[i]!=null)rotors[i].localRotation=rotorHome[i];
        if(beltHome!=null&&beltTicks!=null&&beltHome.Length==beltTicks.Length)for(int i=0;i<beltTicks.Length;i++)if(beltTicks[i]!=null)beltTicks[i].localPosition=beltHome[i];
        if (homePositions == null || panels == null || homePositions.Length != panels.Length) return;
        if (lightLeaks != null) foreach (var light in lightLeaks) if (light != null) light.SetPropertyBlock(null);
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] == null) continue;
            panels[i].localPosition = homePositions[i]; panels[i].localRotation = homeRotations[i];
            if (faces[i] != null) faces[i].SetPropertyBlock(null);
            if (panelFaces != null && i < panelFaces.Length)
                for (int j = 0; j < panelFaces[i].Length; j++)
                    if (panelFaces[i][j] != null) { panelFaces[i][j].sortingOrder = homeOrders[i][j]; panelFaces[i][j].SetPropertyBlock(null); }
        }
    }
}
