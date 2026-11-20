using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 무장 개요 페이지의 열 하나(2026-11-18). 무기 자리(Player_Weapon_Data.weaponSets[setIndex], 0 = 근접, 1 = 원거리)에 장착한 무장을
/// 맨 아래 큰 원으로, 그 무장의 Gear 1~3단계 무기를 위로 쌓인 작은 원으로 보여 주고 원 사이를 세로선으로 잇는다.
/// 무장 정보는 PlayerWeaponAbility의 장착 무장(GetEquippedArmament)에서 읽고, 무장이 없으면 그 자리의 무기 칸을 그대로 보여 준다.
/// Gear 원은 그 무기를 해금했으면 단계 색으로 밝게, 해금 전이면 어둡게 보인다(해금 기록 키는 weaponName, 2026-11-18 2차).
/// 2026-11-20 훈련 개편: 해금 판정은 훈련 필증 해금(ArmamentUnlocks)이고, 잠긴 Gear 원에는 잠금 부품과 '잠김 · 필증 n'을 보인다.
/// Gear 원은 실행용 사본이 아니라 무장 SO의 값을 읽으므로 잠긴 기술도 원에 남아 상세 페이지에서 해금할 수 있다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ArmamentTreeColumn : MonoBehaviour
{
    public const int StageCount = 3;

    [Tooltip("이 열이 보여 주는 무기 자리 번호(0 = 근접, 1 = 원거리)")]
    [SerializeField] private int setIndex;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI captionText;

    [Header("Nodes")]
    [SerializeField] private ArmamentTreeNode armamentNode;
    [Tooltip("[0] Gear 1, [1] Gear 2, [2] Gear 3 (아래에서 위로)")]
    [SerializeField] private ArmamentTreeNode[] gearNodes = new ArmamentTreeNode[StageCount];
    [Tooltip("[0] 무장→Gear 1, [1] Gear 1→2, [2] Gear 2→3 연결선")]
    [SerializeField] private Image[] connectors = new Image[StageCount];

    [Header("Colors")]
    [SerializeField] private Color linkColor = new Color(.263f, .718f, 1f, .55f);
    [SerializeField] private Color emptyLinkColor = new Color(.51f, .62f, .71f, .18f);

    private readonly WeaponBaseSO[] gearWeapons = new WeaponBaseSO[StageCount];

    public int SetIndex => setIndex;
    public PlayerArmamentSO Armament { get; private set; }
    public ArmamentCategory Category { get; private set; }

    // 0 = 장착 무장, 1~3 = Gear 단계
    public ArmamentTreeNode GetNode(int stage)
    {
        if (stage == 0) return armamentNode;
        int i = stage - 1;
        return gearNodes != null && i >= 0 && i < gearNodes.Length ? gearNodes[i] : null;
    }

    public WeaponBaseSO GetGearWeapon(int stage) => stage >= 1 && stage <= StageCount ? gearWeapons[stage - 1] : null;

    public void Refresh(PlayerWeaponAbility ability)
    {
        WeaponSet set = ability != null ? ability.GetWeaponSet(setIndex) : null;
        Armament = ability != null ? ability.GetEquippedArmament(setIndex) : null;
        Category = UpgradeMenuPage.CategoryOf(ability, setIndex);

        if (titleText != null) titleText.text = UpgradeMenuPage.CategoryName(Category) + " 무장";
        if (captionText != null) captionText.text = UpgradeMenuPage.CategoryCode(Category) + "  ·  SLOT " + (setIndex + 1);

        bool held = ability != null && ability.CurrentSetIndex == setIndex;
        WeaponBaseSO normal = Armament != null ? Armament.normal : set != null ? set.normal : null;
        string armamentName = Armament != null ? Armament.DisplayName : normal != null ? normal.DisplayName : null;
        Sprite armamentIcon = Armament != null ? Armament.Icon : normal != null ? normal.weaponSprite : null;
        if (armamentNode != null)
        {
            if (string.IsNullOrEmpty(armamentName)) armamentNode.ShowEmpty("장착 무장 없음", "Z  무장 고르기");
            else armamentNode.Show(armamentIcon, armamentName, held ? "지금 사용 중  ·  Z 교체" : "장착 무장  ·  Z 교체", true);
        }

        for (int stage = 1; stage <= StageCount; stage++)
        {
            WeaponBaseSO weapon = Armament != null ? Armament.GetGearWeapon(stage) : set != null ? set.GetGearWeapon(stage) : null;
            gearWeapons[stage - 1] = weapon;
            var node = GetNode(stage);
            if (node != null)
            {
                // 단계 번호는 원 옆의 큰 'GEAR n' 표시가 보여 주므로 상태 줄에는 해금 상태만 둔다(2026-11-20 훈련 개편: 사용 가능 / 잠김 · 필증 n).
                if (weapon == null) node.ShowEmpty("없음", "비어 있음");
                else
                {
                    bool unlocked = UpgradeMenuPage.IsUnlocked(weapon);
                    node.Show(weapon.weaponSprite, weapon.DisplayName, unlocked ? "사용 가능" : UpgradeMenuPage.LockedLabel(weapon), unlocked);
                    node.SetLocked(!unlocked);
                }
            }
            if (connectors != null && stage - 1 < connectors.Length && connectors[stage - 1] != null)
                connectors[stage - 1].color = weapon != null ? linkColor : emptyLinkColor;
        }
    }
}
