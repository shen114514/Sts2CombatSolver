using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireStrategist.Core;
using MegaCrit.Sts2.Core.Models.Potions;

namespace SpireStrategist.Mod;

internal sealed partial class PartialAutopilot
{
    private int shopFoulUses;
    private bool TickFoulMerchant(NMerchantRoom room)
    {
        // Only called for a real MerchantRoom. FakeMerchant can start combat and
        // must keep its separate risk assessment. Use the native action queue.
        var potion=service.Player.Potions.OfType<FoulPotion>().FirstOrDefault();
        if(potion==null)return false;
        if(potion.IsQueued){Wait("接管中 · 等待污浊药水兑换完成");return true;}
        if(room.Inventory.IsOpen)
        {
            Wait("接管中 · 关闭商品界面，准备向普通商人投掷污浊药水");
            if(Claim($"foul-close:{room.GetInstanceId()}:{shopFoulUses}",GameActions.Field(room.Inventory,"_isInputBlocked") is not true))
                GameActions.Invoke(room.Inventory,"Close");
            return true;
        }
        Wait("接管中 · 向普通商人投掷污浊药水，随后重新评估商店");
        if(Claim($"foul-use:{room.GetInstanceId()}:{shopFoulUses}",potion.PassesCustomUsabilityCheck&&room.MerchantButton.IsEnabled))
        {
            service.RecordAuto("shop_foul_potion",new{state=SnapshotReader.Read(service.State,service.Player),sequence=shopFoulUses,source="native potion action"});
            potion.EnqueueManualUse(null);
            shopFoulUses++;
            leavingShop=null;
        }
        return true;
    }

    private bool TickPriorityShop(NMerchantRoom room,NMerchantSlot[] slots,RunSnapshot state)
    {
        var relics=slots.Where(s=>s.Entry is MerchantRelicEntry {Model:not null})
            .Select(s=>{var e=(MerchantRelicEntry)s.Entry;return new ShopRelic(s.GetInstanceId().ToString(),e.Model!.Id.Entry,e.Model.Title.GetFormattedText(),e.Cost);}).ToArray();
        var cards=slots.Where(s=>s.Entry is MerchantCardEntry {CreationResult:not null})
            .Select(s=>new ShopCard(s.GetInstanceId().ToString(),SnapshotReader.ReadCard(((MerchantCardEntry)s.Entry).CreationResult!.Card),s.Entry.Cost)).ToArray();
        bool finalShop=service.State.CurrentMapPoint is {} point&&
            ShopBudget.Reserve(state,RouteAdapter.Nodes(service.State),RouteAdapter.Key(point),100)==0;
        var plan=PriorityShopPolicy.Evaluate(state,relics,cards,shopBoughtCards,
            slots.FirstOrDefault(s=>s.Entry is MerchantCardRemovalEntry)?.Entry.Cost,finalShop);
        if(plan==null)return false;
        var key=plan.Card?.Key??plan.Relic.Key;
        var slot=slots.Single(s=>s.GetInstanceId().ToString()==key);
        var choice=new ShopChoice(key,plan.Card?.Card.Name??plan.Relic.Name,plan.Card!=null?"card":"relic",
            slot.Entry.Cost,100,[plan.Reason,$"本次计划总价 {plan.TotalPrice}，现款 {state.Gold}"]);
        service.ShowShop(state,new(choice,[choice],0,[plan.Reason,"遗物与买牌、删牌先比较预算；成交后重新评估。"]));
        Wait($"接管中 · 优先购买 {choice.Name}（{choice.Price} 金币）");
        if(Claim($"priority-shop:{room.GetInstanceId()}:{key}:{state.Gold}:{choice.Price}",slot.Entry.EnoughGold,8000))
        {
            service.RecordAuto("shop_priority",new{state,plan,choice});
            Track(()=>BuyShopSlot(slot,plan.Card!=null));
        }
        return true;
    }
    private ulong? leavingShop;
    private string? shopSlotFingerprint;
    private void TickClimbShop(NMerchantRoom room,NMerchantInventory inventory,NMerchantSlot[] slots,RunSnapshot state)
    {
        var nodes=RouteAdapter.Nodes(service.State);
        var reserve=service.State.CurrentMapPoint is {} point?
            ShopBudget.Reserve(state,nodes,RouteAdapter.Key(point),100):100;
        var relics=slots.Where(s=>s.Entry is MerchantRelicEntry {Model:not null}&&s.Entry.Cost<=state.Gold&&s.Entry.EnoughGold)
            .Select(s=>(Slot:s,Net:PriorityShopPolicy.RelicNetValue(state,((MerchantRelicEntry)s.Entry).Model!.Id.Entry,s.Entry.Cost,reserve==0)))
            .Where(x=>x.Net>0).OrderByDescending(x=>x.Net).ThenBy(x=>x.Slot.Entry.Cost).ToArray();
        // 第三幕清仓已经按 200 的底线收手时，不要再让预留金那一步把金币继续花到 200 以下。
        if(relics.Length>0)
        {
            var slot=relics[0].Slot;
            var fingerprint=string.Join("|",relics.Select(x=>((MerchantRelicEntry)x.Slot.Entry).Model!.Id+":"+x.Slot.Entry.Cost));
            var reason="按遗物的当前构筑净收益排序；未知效果不参与随机清仓。";
            var choice=new ShopChoice(slot.GetInstanceId().ToString(),((MerchantRelicEntry)slot.Entry).Model!.Title.GetFormattedText(),"relic",slot.Entry.Cost,relics[0].Net!.Value,[reason]);
            service.ShowShop(state,new(choice,[choice],reserve,[reason]));
            Wait("接管中 · 购买适配当前构筑的遗物");
            if(Claim("climb-relic:"+room.GetInstanceId()+":"+state.Gold+":"+fingerprint,delay:8000))
            {
                service.RecordAuto("climb_modeled_relic",new{state,reserve,looping=false,score=relics[0].Net,
                    candidates=relics.Select(x=>new{id=((MerchantRelicEntry)x.Slot.Entry).Model!.Id.ToString(),cost=x.Slot.Entry.Cost,net=x.Net})});
                Track(()=>BuyShopSlot(slot,false));
            }
            return;
        }
        var potionPlan=slots.Where(s=>s.Entry is MerchantPotionEntry {Model:not null})
            .Select(s=>(Slot:s,Choice:ShopAdvice.Potion(state,s,reserve))).Where(x=>x.Choice.Eligible&&x.Choice.Score>0)
            .OrderByDescending(x=>x.Choice.Score).ThenBy(x=>x.Choice.Price).FirstOrDefault();
        var potion=potionPlan.Slot;
        if(potion!=null)
        {
            var choice=potionPlan.Choice;
            service.ShowShop(state,new(choice,[choice],reserve,[]));
            Wait("接管中 · 最后补充空药水栏（可停止接管）");
            if(Claim("climb-potion:"+room.GetInstanceId()+":"+potion.GetInstanceId()+":"+state.Gold,delay:8000))
            {service.RecordAuto("climb_buy_potion",new{state,reserve,price=potion.Entry.Cost});Track(()=>BuyShopSlot(potion,false));}
            return;
        }
        Wait("接管中 · 商店处理完成，离开");
        // 遗物和药水都买不到时别空手走人：金币充足就按"清仓"预算再看一次买牌/删牌，
        // 否则 600+ 金币的店会被直接跳过（打到最后钱没花完）。
        var cards = slots.Where(s => s.Entry is MerchantCardEntry { CreationResult: not null })
            .Select(s => new ShopCard(s.GetInstanceId().ToString(),
                SnapshotReader.ReadCard(((MerchantCardEntry)s.Entry).CreationResult!.Card), s.Entry.Cost))
            .ToArray();
        var removalCost = slots.FirstOrDefault(s => s.Entry is MerchantCardRemovalEntry)?.Entry.Cost;
        var spend = ShopPolicy.Evaluate(state, cards, removalCost, state.Gold, shopBoughtCards, 0);
        if (spend.Recommendation is { } pick)
        {
            var target = pick.Kind == "remove"
                ? slots.FirstOrDefault(s => s.Entry is MerchantCardRemovalEntry)
                : slots.FirstOrDefault(s => s.GetInstanceId().ToString() == pick.Key);
            if (target != null)
            {
                var choice = new ShopChoice(pick.Key, pick.Name, pick.Kind, pick.Price, 0,
                    ["遗物与药水都不可用，剩余金币按清仓预算投入", ..spend.Notes]);
                service.ShowShop(state, new(choice, [choice], 0, choice.Reasons));
                Wait($"接管中 · 清仓 {pick.Name}（{pick.Price} 金币）");
                if (Claim($"climb-spend:{room.GetInstanceId()}:{target.GetInstanceId()}:{state.Gold}:{shopBoughtCards}",
                        target.Entry.EnoughGold, 8000))
                {
                    if (pick.Kind == "remove")
                        removalTarget = service.Player.Deck.Cards[Math.Clamp(pick.DeckIndex, 0, service.Player.Deck.Cards.Count - 1)];
                    service.RecordAuto("climb_spend", new { state, plan = spend, choice });
                    Track(() => BuyShopSlot(target, pick.Kind == "card"));
                }
                return;
            }
        }
        if (Claim("climb-shop-close:"+room.GetInstanceId()))
        {
            leavingShop=room.GetInstanceId();
            GameActions.Invoke(inventory,"Close");
        }
    }
}
