using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CombatSolver.Routing;

internal static class AutoRoute
{
    private static long _next;
    private static string? _pending;
    private static string Key(MapPoint point)=>$"{point.coord.col},{point.coord.row}";
    private static IEnumerable<T> Descendants<T>(Node node) where T:Node
    {
        foreach(var child in node.GetChildren())
        {
            if(child is T value)yield return value;
            foreach(var value2 in Descendants<T>(child))yield return value2;
        }
    }
    internal static void Tick()
    {
        if(!Entry.Enabled||!SolverSettings.Current.AutoRouteEnabled||StandaloneSmokeTest.PauseRouting||RoomAutomation.IsBusy||SolverOverlay.SettingsVisibleForAutomation
            ||SolverController.IsMultiplayerSession||CombatManager.Instance.IsInProgress
            ||System.Environment.TickCount64<_next)return;
        _next=System.Environment.TickCount64+700;
        try
        {
            var manager=RunManager.Instance;
            if(!manager.IsInProgress||manager.State is not {} run||run.Players.Count!=1)return;
            var map=NMapScreen.Instance;
            if(map==null)return;
            if(MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance?.GetCurrentScreen()
                is not (NMapScreen or MegaCrit.Sts2.Core.Nodes.Rooms.NMapRoom))return;
            if(!map.IsOpen)
            {
                if(run.CurrentRoom is MapRoom)map.Open();
                return;
            }
            if(!map.IsTravelEnabled||map.IsTraveling||map.IsDebugTravelEnabled)return;
            var legal=Descendants<NMapPoint>(map).Where(p=>p.IsEnabled&&p.State.ToString()=="Travelable"
                && AccessTools.Method(p.GetType(),"IsInputAllowed").Invoke(p,null) is true).ToArray();
            if(legal.Length==0){_pending=null;return;}
            string signature=$"{run.CurrentActIndex}:{run.CurrentMapCoord}:"+string.Join('|',legal.Select(p=>Key(p.Point)));
            if(_pending==signature)return;
            var points=run.Map.GetAllMapPoints().Append(run.Map.StartingMapPoint).Append(run.Map.BossMapPoint)
                .Concat(run.Map.SecondBossMapPoint is {} second?[second]:Array.Empty<MapPoint>()).DistinctBy(Key)
                .Select(p=>new RoutePoint(Key(p),p.PointType.ToString(),p.Children.Select(Key).ToArray())).ToArray();
            var player=LocalContext.GetMe(run);
            if(player==null)return;
            double basics=player.Deck.Cards.Count(c=>c.IsBasicStrikeOrDefend)/(double)Math.Max(1,player.Deck.Cards.Count);
            double readiness=Math.Clamp(.25+(1-basics)*.65+player.Deck.Cards.Count(c=>c.CurrentUpgradeLevel>0)*.025,0,1);
            string? chosen=RoutePlanner.Choose(new(player.Creature.CurrentHp,player.Creature.MaxHp,player.Gold,run.CurrentActIndex+1,readiness),points,legal.Select(p=>Key(p.Point)).ToArray());
            var target=legal.FirstOrDefault(p=>Key(p.Point)==chosen);
            if(target==null)return;
            _pending=signature;
            Entry.Logger.Info($"[route] choose={chosen} kind={target.Point.PointType} hp={player.Creature.CurrentHp} gold={player.Gold}");
            map.OnMapPointSelectedLocally(target);
        }
        catch(Exception ex){_pending=null;_next=System.Environment.TickCount64+5000;Entry.Logger.Warn("[route] native_control=true "+ex.Message);}
    }
}
