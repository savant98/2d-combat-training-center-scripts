using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TutorialWallPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI heading;
    [SerializeField] private TextMeshProUGUI instruction;
    [SerializeField] private TextMeshProUGUI controls;

    private void OnEnable()
    {
        InputManager.BindingsChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => InputManager.BindingsChanged -= Refresh;

    private void Refresh()
    {
        var manager = WaveManager.ActiveInstance;
        if (manager == null || manager.Data == null || manager.currentWaveIndex < 0
            || manager.currentWaveIndex >= manager.Data.waves.Length) return;

        var lesson = manager.Data.waves[manager.currentWaveIndex].tutorial;
        if (lesson == null || !lesson.wallInstructions) return;
        heading.text = lesson.title;
        instruction.text = lesson.description;
        controls.text = lesson.action switch
        {
            TutorialAction.ShieldBreakGear => Key("Attack") + " 기본 공격    ·    " + Key("Gear") + " 유지 + " + Key("Attack") + " 기어 발동",
            TutorialAction.SkillStorage => Key("Gear") + " 기어 준비    ·    " + Key("StoreSkill") + " 저장 / 사용",
            _ => Key(TutorialWaveGuide.ActionName(lesson.action))
        };
    }

    private static string Key(string action) => "[ " + InputBindingDisplay.Get(action) + " ]";
}
