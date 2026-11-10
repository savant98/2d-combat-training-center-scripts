using System;
using UnityEngine;

// Scene에는 이 Host와 Room 하나만 남기고, WaveData가 선택한 모듈을 교체한다.
[DisallowMultipleComponent]
public sealed class TrainingRoomHost : MonoBehaviour
{
    [Serializable]
    public sealed class Binding
    {
        public TrainingRoomContent template;
        public string path;
        public string componentType;
        public int componentIndex;
        public string property;
        public UnityEngine.Object target;
    }
    public WaveDataSO defaultData;
    public TrainingRoomContent defaultTemplate;
    public Binding[] bindings = Array.Empty<Binding>();
    public Transform entryPoint;
    public TrainingRoomContent Content { get; private set; }
    public WaveDataSO Data { get; private set; }

    public void Load(WaveDataSO data)
    {
        if (data == null) data = defaultData;
        if (Content != null && Data == data)
        {
            // Tool에서 첫 Wave 좌표를 바꾼 뒤 같은 데이터로 다시 입장하는 경우도 갱신한다.
            entryPoint.localPosition = WaveRoomState.EntryPoint(data);
            return;
        }
        var owner = GetComponent<Room>();
        owner.ReleaseContent();
        if (Content != null)
        {
            Content.gameObject.SetActive(false);
            Destroy(Content.gameObject);
        }
        Data = data;
        var template = data.roomTemplate != null ? data.roomTemplate : defaultTemplate;
        // 초기화 전에 참조와 WaveData를 복구하기 위해 비활성 부모에서 만든다.
        var staging = new GameObject("ContentStaging");
        staging.SetActive(false);
        staging.transform.SetParent(transform, false);
        Content = Instantiate(template, staging.transform);
        Content.name = "TrainingContent";
        Content.transform.localPosition = Vector3.zero;
        foreach (var binding in bindings)
        {
            if (binding.template != template) continue;
            var node = string.IsNullOrEmpty(binding.path) ? Content.transform : Content.transform.Find(binding.path);
            if (node == null) continue;
            var type = Type.GetType(binding.componentType);
            if (type == null) continue;
            var components = node.GetComponents(type);
            if (binding.componentIndex >= components.Length) continue;
            // 직렬화 필드 경로는 Editor 이관 시 생성한 바인딩으로 런타임에 복구한다.
            WaveRoomBinding.Apply(components[binding.componentIndex], binding.property, binding.target);
        }
        Content.waves.Configure(data, Content.spawnRoot, data.training);
        if (!data.customRoom)
        {
            foreach (var stage in Content.GetComponentsInChildren<TrainingCourseStage>(true)) stage.enabled = false;
            foreach (var layout in Content.GetComponentsInChildren<TrainingRoomLayoutController>(true)) layout.enabled = false;
        }
        if (data.cellMap.enabled)
        {
            foreach (var layout in Content.GetComponentsInChildren<TrainingRoomLayoutController>(true)) layout.enabled = false;
            Content.gameObject.AddComponent<WaveCellTerrain>().Configure(data, Content.waves);
        }
        else
        {
            var builder = Content.gameObject.AddComponent<WaveRoomBuilder>();
            builder.Configure(Content, data);
        }
        owner.BindContent(Content);
        entryPoint.localPosition = WaveRoomState.EntryPoint(data);
        Content.transform.SetParent(transform, false);
        Destroy(staging);
    }
}
