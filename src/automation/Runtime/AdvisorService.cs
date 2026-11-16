using System.Text.Json;
using CombatSolver;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal sealed record DecisionRecord(string Id,string Kind,RunSnapshot State,DecisionOption[] Options,Evaluation Evaluation);
internal static class Journal
{
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
}

// The existing offline evaluators are shared by the four optional native room
// adapters. No advice window, remote bridge, persona or telemetry is started.
internal sealed class AdvisorService
{
    internal static AdvisorService? Current {get;private set;}
    internal RunState State {get;}
    internal Player Player {get;}
    internal PartialAutopilot Autopilot {get;}
    internal DecisionRecord? Decision {get;private set;}
    internal NCardRewardSelectionScreen? CardScreen {get;private set;}
    internal NRestSiteRoom? RestRoom {get;private set;}
    internal IReadOnlyList<CardCreationResult> CardOptions {get;private set;}=[];
    internal bool RestBusy=>RestRoom!=null&&RestRoom.ProceedButton.IsEnabled
        && RestRoom.Options.All(o=>RestRoom.GetButtonForOption(o)?.IsEnabled!=true);
    private readonly Policy policy=new();
    private string fingerprint="";
    private string shopFingerprint="";
    internal ShopEvaluation? LastShopPlan {get;private set;}
    internal MegaCrit.Sts2.Core.Rooms.AbstractRoom? FaultedRoom {get;private set;}
    internal void ClearFault()=>FaultedRoom=null;
    internal AdvisorService(RunState state,Player player){State=state;Player=player;Autopilot=new(this);Current=this;}
    internal static void Clear(){Current?.Autopilot.Pause("房间操作已停止");Current=null;}
    internal void Guard(Action action){try{action();}catch(Exception ex){Autopilot.Pause(ex.GetBaseException().Message);RecordAuto("error",new{error=ex.ToString()});}}
    internal RunSnapshot WithForesight(RunSnapshot state)=>state;
    internal void RecordAuto(string type,object payload)
    {
        if(type=="error")FaultedRoom=State.CurrentRoom;
        if(type=="submit")RoomAutomation.ObserveSubmittedAction();
        try{Entry.Logger.Info("[automation] "+type+" "+JsonSerializer.Serialize(payload,Journal.Json));}
        catch(Exception ex){Entry.Logger.Warn("[automation] diagnostic_unavailable "+ex.Message);}
    }
    internal void ShowShop(RunSnapshot state,ShopEvaluation plan)
    {
        LastShopPlan=plan;
        string next=JsonSerializer.Serialize(new{state,plan},Journal.Json);
        if(next==shopFingerprint)return;
        shopFingerprint=next;RecordAuto("shop_preview",new{state,plan});
    }
    internal void PresentEvent(EventPlan plan)
    {
        Decision=new(Guid.NewGuid().ToString("N"),"event",plan.State,
            plan.Choices.Select(c=>new DecisionOption(c.Key,c.Name,"event",c.Enabled)).ToArray(),plan.Evaluation);
    }
    internal void Poll()
    {
        var screen=ActiveScreenContext.Instance?.GetCurrentScreen();
        if(State.CurrentRoom is MegaCrit.Sts2.Core.Rooms.RestSiteRoom&&NRestSiteRoom.Instance is {} rest)RestRoom=rest;
        if(screen is not NCardRewardSelectionScreen && screen is not NRestSiteRoom)return;
        var snapshot=SnapshotReader.Read(State,Player);
        DecisionOption[] options;
        string kind;
        if(screen is NCardRewardSelectionScreen cards)
        {
            CardScreen=cards;
            CardOptions=GameActions.Field(cards,"_options") as IReadOnlyList<CardCreationResult>??[];
            var alternatives=GameActions.Field(cards,"_extraOptions") as IReadOnlyList<CardRewardAlternative>??[];
            snapshot=snapshot with{HpAfterPendingAncient=SnapshotReader.PendingAncientHp(State,Player)};
            options=CardOptions.Select((c,i)=>new DecisionOption("card:"+i,c.Card.Title,"card",Card:SnapshotReader.ReadCard(c.Card)))
                .Concat(alternatives.Select((o,i)=>new DecisionOption("alternative:"+i,o.Title.GetFormattedText(),
                    o.OptionId.Equals("Skip",StringComparison.OrdinalIgnoreCase)?"skip":
                    o.OptionId=="REROLL"?"reroll":o.OptionId=="SACRIFICE"&&Player.GetRelic<PaelsWing>()!=null?"effect":"unknown",
                    EffectValue:o.OptionId=="SACRIFICE"?4:null))).ToArray();
            kind="cards";
        }
        else
        {
            RestRoom=(NRestSiteRoom)screen;
            if(RestBusy)return;
            options=RestRoom.Options.Select(o=>new DecisionOption(o.OptionId,o.Title.GetFormattedText(),
                o is HealRestSiteOption?"heal":o is SmithRestSiteOption?"smith":CampMechanics.Effect(this,o,snapshot)!=null?"effect":"unknown",o.IsEnabled,
                HealAmount:o is HealRestSiteOption?(double)HealRestSiteOption.GetHealAmount(Player):0,
                SmithCount:o is SmithRestSiteOption smith?ApiCompatHelpers.SmithCount(smith):1,
                EffectValue:CampMechanics.Effect(this,o,snapshot))).ToArray();
            kind="rest";
            if(State.CurrentMapPoint is {} point&&snapshot.Map?.Error==null)
            {
                try{snapshot=snapshot with{CampRouteRisk=CampRoutePolicy.Risks(snapshot,options,RouteAdapter.Nodes(State),RouteAdapter.Key(point),RouteAdapter.Economy(this))};}
                catch(Exception ex){RecordAuto("camp_route_unavailable",new{error=ex.Message});}
            }
        }
        string next=JsonSerializer.Serialize(new{screen=((Node)screen).GetInstanceId(),snapshot,options},Journal.Json);
        if(next==fingerprint)return;
        fingerprint=next;
        var evaluation=policy.Evaluate(snapshot,options);
        Decision=new(Guid.NewGuid().ToString("N"),kind,snapshot,options,evaluation);
        RecordAuto("decision",Decision);
    }
    internal static double? RestEffect(RestSiteOption option,RunSnapshot snapshot)
    {
        var value=new StrategicValue(snapshot);
        return option switch
        {
            CookRestSiteOption=>value.RemoveBest(2)+value.MaxHp(5),
            CloneRestSiteOption=>snapshot.Deck.Where(c=>c.Enchantment=="ENCHANTMENT.CLONE").Sum(value.Add),
            HatchRestSiteOption=>10*value.Horizon,
            KindleRestSiteOption=>5+Math.Min(4,value.Attack),
            LiftRestSiteOption=>snapshot.Character=="Silent"?SilentMechanics.LiftValue(snapshot):4+value.Multi*2,
            DigRestSiteOption=>8,_=>null
        };
    }
}
