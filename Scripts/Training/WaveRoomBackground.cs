using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Tilemaps;

// 배경 선택은 카메라·통창·지형·진행 오브젝트와 별도로 적용한다. null은 원본 유지다.
public sealed class WaveRoomBackground : MonoBehaviour
{
    private readonly Dictionary<Renderer, bool> originalRenderers = new();
    private WaveDataSO data;
    private GameObject selectedPrefab;
    private GameObject instance;
    public GameObject Instance => instance;

    public static bool IsBackground(GameObject prefab)
        => prefab != null && prefab.GetComponentsInChildren<Renderer>(true).Length > 0
            && prefab.GetComponentsInChildren<Camera>(true).Length == 0
            && prefab.GetComponentsInChildren<CinemachineVirtualCameraBase>(true).Length == 0
            && prefab.GetComponentsInChildren<Collider2D>(true).Length == 0
            && prefab.GetComponentsInChildren<Room>(true).Length == 0
            && prefab.GetComponentsInChildren<WaveManager>(true).Length == 0;

    public void Configure(WaveDataSO source)
    {
        data = source;
        foreach (var map in GetComponentsInChildren<Tilemap>(true))
            if (map.GetComponent<TilemapCollider2D>() == null)
            {
                var renderer = map.GetComponent<TilemapRenderer>();
                if (renderer != null) originalRenderers[renderer] = renderer.enabled;
            }
        foreach (Transform child in transform)
            if (child.name.Contains("Backdrop"))
                foreach (var renderer in child.GetComponentsInChildren<Renderer>(true)) originalRenderers[renderer] = renderer.enabled;
        Apply(data.backgroundPrefab);
    }
    public void Apply(GameObject prefab)
    {
        if (prefab == selectedPrefab && (prefab == null || instance != null)) return;
        if (prefab != null && !IsBackground(prefab))
        {
            Debug.LogError("배경 Prefab에는 카메라·Collider·Room·WaveManager를 포함할 수 없습니다.", prefab);
            return;
        }
        if (instance != null) { instance.SetActive(false); Destroy(instance); }
        selectedPrefab = prefab;
        foreach (var row in originalRenderers) if (row.Key != null) row.Key.enabled = prefab == null && row.Value;
        if (prefab != null)
        {
            instance = Instantiate(prefab, transform, false);
            instance.name = "WaveBackground_" + prefab.name;
        }
    }
}
