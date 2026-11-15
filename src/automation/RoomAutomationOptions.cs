namespace CombatSolver;

public enum RoomAutomationFeature { None,Rewards,Shop,Events,Camp }
public readonly record struct RoomAutomationOptions(bool Rewards,bool Shop,bool Events,bool Camp)
{
    public bool Any=>Rewards||Shop||Events||Camp;
    public bool Allows(RoomAutomationFeature feature)=>feature switch
    {RoomAutomationFeature.Rewards=>Rewards,RoomAutomationFeature.Shop=>Shop,RoomAutomationFeature.Events=>Events,RoomAutomationFeature.Camp=>Camp,_=>false};
}
