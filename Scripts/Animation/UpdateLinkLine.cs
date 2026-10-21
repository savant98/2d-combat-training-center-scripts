using UnityEngine;

[DefaultExecutionOrder(100)]
public class UpdateLinkLine : MonoBehaviour
{
    [Header("Link")]
    [SerializeField] private Transform[] links;

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
    }

    private void LateUpdate()
    {
        if (lineRenderer == null)
            return;

        for (int i = 0; i < links.Length; i++)
        {
            lineRenderer.SetPosition(i, links[i].position);
        }
    }
}
