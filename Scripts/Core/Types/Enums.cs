public enum Button { Jump, Dash, Attack, Gear, Interact, SecondaryWeapon, SwapWeapon, Escape }
public enum ButtonState { None, Pressed, Held, Released }
public enum PlayerState
{
    Idle,
    Jump,
    Dash,
    Move,
    Attack,
    Hit,
    Dead,
    Interact
}

public enum PlayerActionType
{
    None,
    Move,
    Jump,
    Attack,
    Dash,
    Hit,
    Interact
}

public enum Faction
{
    Player,
    Enemy
}

public enum AbilityState
{
    None,
    Windup,
    Execute,
    Recover
}

public enum DamageType
{
    Normal,
    Skill,
}
public enum RoomType
{
    Lobby,
    Training,
    Boss,
    Result,
    SelfTraining
}
public enum WeaponSlot
{
    Primary,
    Secondary
}
public enum Grade
{
    Poor,
    Pass,
    Excellent,
    Flawless
}
public enum UIInputLayer
{
    Gameplay,
    Pause,
    System
}

[System.Serializable]
public struct RunResult
{
    public string trainingId;
    public int hitCount;
    public float timeSeconds;
    public Grade grade;
    public bool cleared;
    public string endedAtIso;
    public int earnedGold;
}