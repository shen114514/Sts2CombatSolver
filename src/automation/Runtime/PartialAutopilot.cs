using CombatSolver;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rewards;
using SpireStrategist.Core;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Entities.Merchant;

namespace SpireStrategist.Mod;
internal sealed partial class PartialAutopilot
{
    private readonly AdvisorService service;
    private readonly ActionGate gate=new();
    private Task? operation;
    private object? operationOrigin;
    private readonly Stack<(Task Task,object? Origin)> parentOperations=new();
    private Task? chestOperation;
    private ulong operationStarted;
    private CardModel? smithTarget,removalTarget;
    private NRestSiteRoom? smithRoom;
    private MerchantRoom? shoppingRoom;
    private double nextShopLeaveClick;
    private int shopArrivalGold,shopBoughtCards;
    internal bool FullAutomatic=>true;
    internal bool WaitingForManual {get;private set;}
    internal bool Enabled=>gate.Enabled;
    internal bool IsBusy=>operation is {IsCompleted:false}||chestOperation is {IsCompleted:false}||parentOperations.Any(p=>!p.Task.IsCompleted);
    internal string Status {get;private set;}="自动房间操作已关闭";
    internal PartialAutopilot(AdvisorService owner)=>service=owner;
    internal void Enable(){gate.Enable();WaitingForManual=false;UpdateStatus("自动房间操作待命");}
    internal void ResetPendingForManual()=>gate.ResetPending();
    internal void Pause(string reason)
    {
        gate.Pause();WaitingForManual=false;smithTarget=null;smithRoom=null;removalTarget=null;
        ownedEvent=null;eventSelectionTargets=null;ownedRestSelection=null;ownedPickup=null;ownedPickupRoom=null;ownsDig=false;
        UpdateStatus(reason);
    }
    private void UpdateStatus(string text){if(Status==text)return;Status=text;CombatSolver.RoomAutomation.SetStatus(text);}
    private void Wait(string text){WaitingForManual=false;UpdateStatus(text);}
    private void Manual(string text){gate.ResetPending();WaitingForManual=true;UpdateStatus("等待手动操作 · "+text);}
    private bool Claim(string key,bool ready=true,ulong delay=2000)
    {
        if(!CombatSolver.RoomAutomation.AllowsCurrentScreen(service.State))return false;
        bool claimed=gate.TryClaim(key,Time.GetTicksMsec(),ready,delay);
        if(claimed)service.RecordAuto("submit",new{key});
        return claimed;
    }
    private void Track(Func<Task> start)
    {
        var origin=ActiveScreenContext.Instance.GetCurrentScreen();var task=start();
        if(operation!=null)parentOperations.Push((operation,operationOrigin));
        operation=task;operationOrigin=origin;operationStarted=Time.GetTicksMsec();
    }
    internal void Tick()
    {
        try{TickCore();}catch(Exception ex){Pause("自动操作已暂停："+ex.GetBaseException().Message);service.RecordAuto("error",new{error=ex.ToString()});}
    }
    private void TickCore()
    {
        if(!Enabled||CombatManager.Instance.IsInProgress||service.Player.Creature.CurrentHp<=0||RunOver())return;
        var screen=ActiveScreenContext.Instance.GetCurrentScreen();
        if(!CombatSolver.RoomAutomation.AllowsCurrentScreen(service.State)){gate.ResetPending();return;}
        if(NModalContainer.Instance?.OpenModal!=null){Manual("请先处理当前弹窗。");return;}
        if(chestOperation is {IsCompleted:true}){var task=chestOperation;chestOperation=null;task.GetAwaiter().GetResult();}
        if(service.Player.Creature.CurrentHp<=0) {Pause("本局已结束。");return;}
        while(operation is {IsCompleted:true})
        {
            var finished=operation;
            if(parentOperations.TryPop(out var parent))
            {operation=parent.Task;operationOrigin=parent.Origin;operationStarted=Time.GetTicksMsec();}
            else {operation=null;operationOrigin=null;}
            finished.GetAwaiter().GetResult();
            // A child reward ending does not end the rest/upgrade that is waiting for it.
            if(operation==null){smithTarget=null;smithRoom=null;removalTarget=null;ownedRestSelection=null;ownsDig=false;}
            service.RecordAuto("completed",new{pendingParents=parentOperations.Count,parentPending=operation!=null});
        }
        if(operation!=null && Time.GetTicksMsec()-operationStarted>30000) {Pause("操作等待超过 30 秒，请手动检查。");return;}

        // Native result viewer (e.g. Pandora's Box): cards are already in the deck.
        // Confirm only this exact read-only screen, never a selection or inspection screen.
        if(!CombatManager.Instance.IsInProgress && screen is NSimpleCardsViewScreen results &&
            results.GetType()==typeof(NSimpleCardsViewScreen))
        {
            Wait("接管中 · 确认已完成的卡牌变化结果");
            var confirm=GameActions.Field(results,"_confirmButton") as NClickableControl;
            if(Claim("card-results-confirm:"+results.GetInstanceId(),
                confirm is {IsEnabled:true} && confirm.IsVisibleInTree() && results.IsVisibleInTree()))
            {
                service.RecordAuto("card_results_confirm",new{screen=results.GetType().Name,
                    cards=(GameActions.Field(results,"_cards") as IEnumerable<CardModel>)?.Select(c=>SnapshotReader.ReadCard(c)).ToArray()});
                GameActions.Invoke(results,"OnReturnButtonPressed",confirm);
            }
            return;
        }
        if(screen is NMerchantInventory busyInventory && GameActions.Field(busyInventory,"_isInputBlocked") is true)
        {Wait("接管中 · 等待商店遗物或动画处理完成");return;}
        PrepareParasolRemoval(screen);
        if(TickEvent(screen))return;
        // 事件里的"选一张牌加入牌组/从三张里挑一张"用的是 NChooseACardSelectionScreen，
        // 它不属于 TickEvent 处理的那几种选牌界面，原来会一直停在这里等玩家。
        if(screen is NChooseACardSelectionScreen grid)
        {
            if(TickChooseACardGrid(grid))return;
        }
        var excludedRewards=false;
        if(screen is NDeckCardSelectScreen remove && removalTarget!=null && service.State.CurrentRoom==shoppingRoom)
        {
            var eligible=(IEnumerable<CardModel>)GameActions.Field(remove,"_cards")!;
            var selected=(HashSet<CardModel>)GameActions.Field(remove,"_selectedCards")!;
            if(!eligible.Contains(removalTarget) || !service.Player.Deck.Cards.Contains(removalTarget)) {Pause("删牌目标已变化。");return;}
            if(selected.Count>0 && !selected.SetEquals([removalTarget])) {Pause("检测到你更改了删牌目标。");return;}
            Wait("接管中 · 商店删除 "+removalTarget.Title);
            if(selected.Count==0)
            {if(Claim("remove-select:"+remove.GetInstanceId()))GameActions.Invoke(remove,"OnCardClicked",removalTarget);}
            else
            {
                var confirm=(NClickableControl)GameActions.Field(remove,"_previewConfirmButton")!;
                if(Claim("remove-confirm:"+remove.GetInstanceId(),confirm.IsEnabled && confirm.IsVisibleInTree()))GameActions.Invoke(remove,"ConfirmSelection",confirm);
            }
            return;
        }
        // During an owned smith operation only, finish the game's actual selection and confirmation screen.
        if(screen is NDeckUpgradeSelectScreen upgrade && (smithRoom==service.RestRoom || smithTarget==null&&service.State.CurrentRoom is RestSiteRoom))
        {
            var eligible=(IEnumerable<CardModel>)GameActions.Field(upgrade,"_cards")!;
            // 以前这里直接落到「不认识的界面 → 手动处理」，接管当场 Pause。现在自己挑一张最好的。
            if(smithTarget==null)
            {
                var snap=SnapshotReader.Read(service.State,service.Player);
                smithTarget=eligible.Where(c=>c.IsUpgradable && CardCatalog.Find(SnapshotReader.ReadCard(c))!=null)
                    .OrderByDescending(c=>CardMechanics.Upgrade(snap,SnapshotReader.ReadCard(c))).FirstOrDefault();
                if(smithTarget==null){Wait("接管中 · 没有可升级的牌");return;}
                smithRoom=service.RestRoom;
            }
            if(!smithTarget.IsUpgradable || !service.Player.Deck.Cards.Contains(smithTarget) || !eligible.Contains(smithTarget)) {Pause("升级目标已变化。");return;}
            var selected=(HashSet<CardModel>)GameActions.Field(upgrade,"_selectedCards")!;
            if(selected.Count>0 && !selected.SetEquals([smithTarget]))
            {
                // 以前这里直接 Pause，营火就停在一半、接管从此断开。
                smithTarget=selected.First();
                Wait("接管中 · 接受手动选择的升级目标 "+smithTarget.Title); return;
            }
            if(selected.Count==0)
            {
                if(Claim("upgrade-select:"+upgrade.GetInstanceId())) GameActions.SelectUpgrade(upgrade,smithTarget);
            }
            else if(Claim("upgrade-confirm:"+upgrade.GetInstanceId())) GameActions.ConfirmUpgrade(upgrade);
            Wait("接管中 · 升级 "+smithTarget.Title); return;
        }
        if(screen is NCardRewardSelectionScreen cards && ReferenceEquals(cards,service.CardScreen))
        {
            if(excludedRewards){Manual("商店或先古奖励请手动选择。");return;}
            var d=service.Decision;
            if(d?.Kind!="cards") {gate.ResetPending();return;}
            var chosen=d.Options.FirstOrDefault(o=>o.Key==d.Evaluation.RecommendedKey);
            // 不要为了一个没建模的候选把整屏交还人工：能算出分的照旧选最优，算不出的也要给一个
            // 确定性的结果（跳过 > 稀有度最高的牌），并记录原因供事后核对。
            if(chosen==null||d.Evaluation.Options.Any(o=>o.Enabled&&!o.Supported))
            {
                chosen=FallbackRewardChoice(d,out string rewardFallbackReason);
                service.RecordAuto("reward_fallback",new{reason=rewardFallbackReason,decision=d.Id,
                    chosen=chosen?.Key,options=d.Evaluation.Options.Select(o=>new{o.Key,o.Name,o.Supported,o.Enabled,o.Score})});
                if(chosen==null){Manual("这组奖励没有可自动执行的选项，请手动处理。");return;}
            }
            Wait("接管中 · 即将选择 "+chosen.Name);
            if(!Claim("decision:"+d.Id,GameActions.RewardReady(cards))) return;
            if(chosen.Key.StartsWith("card:")) GameActions.SelectCard(cards,service.CardOptions[int.Parse(chosen.Key[5..])].Card);
            else if(chosen.Kind is "skip" or "effect" or "reroll") GameActions.SelectSkip(cards,int.Parse(chosen.Key[12..]));
            else Manual("当前奖励选项尚未支持自动执行。");
            return;
        }
        // A relic pickup can open an unimplemented card selector. Retain its task while the
        // user completes it, and resume afterwards without repeatedly claiming the reward.
        if(operation!=null && screen is not (NRewardsScreen or NRestSiteRoom or NTreasureRoom or NCombatRoom or NMerchantInventory or NMerchantRoom))
        {Manual("当前动作需要手动完成选择。");operationStarted=Time.GetTicksMsec();return;}
        if(screen is NTreasureRoom treasure)
        {
            // v0.113.1 has no _hasChestBeenOpened field. Use the native finished
            // state after either an automatic or a manual relic pick.
            if(GameActions.Field(treasure,"_proceedButtonState")?.ToString()=="Proceed")
            {Proceed(treasure,treasure.ProceedButton,"OnProceedButtonReleased");return;}
            var collection=GameActions.Descendants<NTreasureRoomRelicCollection>(treasure).FirstOrDefault(c=>c.IsVisibleInTree());
            var holders=collection==null?[]:GameActions.Descendants<NTreasureRoomRelicHolder>(collection).Where(h=>h.IsVisibleInTree() && h.IsEnabled && h.MouseFilter!=Control.MouseFilterEnum.Ignore).ToArray();
            var holder=holders.FirstOrDefault(h=>h.Relic.Model.Rarity!=RelicRarity.Ancient);
            if(holder!=null)
            {
                Wait("接管中 · 领取宝箱遗物 "+holder.Relic.Model.Title.GetFormattedText());
                if(Claim("chest-relic:"+holder.GetInstanceId()))
                {PrepareRelicSelection(holder.Relic.Model);GameActions.Invoke(collection!,"PickRelic",holder);}
                return;
            }
            if(holders.Length>0){Manual("先古遗物请手动选择。");return;}
            if(chestOperation!=null){Wait("接管中 · 等待宝箱处理完成");return;}
            var chest=(NClickableControl)GameActions.Field(treasure,"_chestButton")!;
            if(chest.IsEnabled && chest.IsVisibleInTree() && chest.MouseFilter!=Control.MouseFilterEnum.Ignore)
            {
                Wait("接管中 · 打开宝箱");
                if(Claim("chest:"+treasure.GetInstanceId(),chest.IsEnabled))
                {chest.Disable();chestOperation=(Task)GameActions.Invoke(treasure,"OpenChest")!;}return;
            }
            Wait("接管中 · 等待宝箱遗物处理完成");return;
        }
        // Sleep/dig/relic tasks await their reward overlay. Drive that child overlay while
        // retaining the parent task, but do not click another button on a still-busy source.
        if(operation!=null && (screen is not NRewardsScreen || ReferenceEquals(screen,operationOrigin)))
        {gate.ResetPending();Wait("接管中 · 等待当前动作完成");return;}
        if(screen is NRestSiteRoom rest && ReferenceEquals(rest,service.RestRoom))
        {
            if(service.RestBusy) {Proceed(rest,rest.ProceedButton,"OnProceedButtonReleased");return;}
            var d=service.Decision;
            if(d?.Kind!="rest") {Proceed(rest,rest.ProceedButton,"OnProceedButtonReleased");return;}
            var chosen=d.Options.FirstOrDefault(o=>o.Key==d.Evaluation.RecommendedKey && o.Enabled);
            if(rest.ProceedButton.IsEnabled && (chosen==null&&d.Options.All(o=>!o.Enabled)||
                d.Evaluation.Options.FirstOrDefault(o=>o.Key==chosen?.Key)?.Score<=0))
            {Proceed(rest,rest.ProceedButton,"OnProceedButtonReleased");return;}
            var option=rest.Options.FirstOrDefault(o=>o.OptionId==chosen?.Key);
            if(option is not (HealRestSiteOption or SmithRestSiteOption or RestoreRestSiteOption or CookRestSiteOption or CloneRestSiteOption or HatchRestSiteOption or KindleRestSiteOption or LiftRestSiteOption or DigRestSiteOption)) {Manual("营火含未适配选项，请手动操作。");return;}
            var button=rest.GetButtonForOption(option);
            Wait("接管中 · 即将"+chosen!.Name);
            if(!Claim("decision:"+d.Id,button?.IsEnabled==true && button.IsVisibleInTree()))return;
            if(option is SmithRestSiteOption smith)
            {
                if(ApiCompatHelpers.SmithCount(smith)!=1){ownedRestSelection=rest;eventSelection="upgrade";eventSelectionTargets=null;Track(()=>GameActions.SelectRest(button!,option));return;}
                smithTarget=service.Player.Deck.Cards.Where(c=>c.IsUpgradable && CardCatalog.Find(SnapshotReader.ReadCard(c))!=null)
                    .OrderByDescending(c=>CardMechanics.Upgrade(service.Decision!.State,SnapshotReader.ReadCard(c))).FirstOrDefault();
                if(smithTarget==null){Pause("没有可执行的升级目标。");return;}
                smithRoom=rest;
            }
            if(option is CookRestSiteOption){ownedRestSelection=rest;eventSelection="remove";eventSelectionTargets=null;}
            if(option is DigRestSiteOption){ownedRestSelection=rest;ownsDig=true;}
            Track(()=>GameActions.SelectRest(button!,option));return;
        }
        if(screen is NRewardsScreen rewards)
        {
            if(excludedRewards){Manual("商店或先古奖励请手动处理。");return;}
            var skipped=(IEnumerable<Control>)GameActions.Field(rewards,"_skippedRewardButtons")!;
            var buttons=GameActions.Descendants<NRewardButton>(rewards).Where(b=>b.IsVisibleInTree() && b.IsEnabled && !skipped.Contains(b)).ToArray();
            var potionState=SnapshotReader.Read(service.State,service.Player);
            PotionCandidate DescribePotion(PotionModel p)=>new(p.GetType().Name,p.Rarity.ToString(),p.GetType().Assembly==typeof(PotionModel).Assembly);
            var bestPotion=buttons.Where(b=>b.Reward is PotionReward {Potion:not null})
                .Where(b=>MegaCrit.Sts2.Core.Hooks.Hook.ShouldProcurePotion(service.State,service.Player.Creature.CombatState,((PotionReward)b.Reward!).Potion!,service.Player))
                .OrderByDescending(b=>PotionPolicy.Rank(potionState,DescribePotion(((PotionReward)b.Reward!).Potion!))).FirstOrDefault();
            if(service.State.CurrentRoom is not MerchantRoom && bestPotion?.Reward is PotionReward {Potion:not null} incoming &&
                !service.Player.PotionSlots.Any(p=>p==null))
            {
                var belt=service.Player.PotionSlots.Select(p=>p!).ToArray();
                var index=PotionPolicy.Replacement(potionState,belt.Select(DescribePotion).ToArray(),DescribePotion(incoming.Potion));
                if(index>=0)
                {
                    var replace=belt[index];
                    Wait("接管中 · 药水升级替换："+replace.Title.GetFormattedText()+" → "+incoming.Potion.Title.GetFormattedText());
                    if(Claim("potion-replace:"+bestPotion.GetInstanceId()+":"+index+":"+replace.Id))
                    {service.RecordAuto("potion_replace",new{discard=replace.Id.ToString(),take=incoming.Potion.Id.ToString(),state=potionState});Track(()=>MegaCrit.Sts2.Core.Commands.PotionCmd.Discard(replace));}
                    return;
                }
            }
            var safe=buttons.FirstOrDefault(b=>b.Reward is GoldReward or CardReward ||
                b.Reward is RelicReward {Relic:not null} relic && relic.Relic.Rarity!=RelicRarity.Ancient && !LeaveHarmfulFakeRelic(b.Reward));
            if(safe==null&&!service.Player.Relics.Any(r=>r.Id.Entry=="SOZU")&&service.Player.PotionSlots.Any(p=>p==null))safe=bestPotion;
            if(safe==null)
            {
                // Evaluate AFTER ordinary rewards: a new card/relic can change whether recovery is useful.
                var state=SnapshotReader.Read(service.State,service.Player);
                var recoveries=buttons.Where(b=>IsStolenCardRecovery(b.Reward)).Select(b=>
                    (Button:b,Card:GameActions.Field(b.Reward!,"_card") as CardModel)).ToArray();
                if(recoveries.Any(r=>r.Card==null)){Manual("无法读取被偷的牌，请手动决定是否取回。");return;}
                var plans=recoveries.Select(r=>(r.Button,Plan:RecoveryPolicy.Evaluate(state,SnapshotReader.ReadCard(r.Card!),
                    continuous:FullAutomatic,skipAllowed:GameActions.Field(rewards,"_skipDisallowed") is not true))).ToArray();
                var take=plans.FirstOrDefault(r=>r.Plan.Action=="recover");
                var fingerprint=System.Text.Json.JsonSerializer.Serialize(new{state,plans=plans.Select(p=>p.Plan).ToArray()});
                if(take.Button!=null)
                {
                    Wait("接管中 · 取回 "+take.Plan.CardName+"："+take.Plan.Reasons[0]);
                    if(Claim("recover:"+take.Button.GetInstanceId()+":"+fingerprint))
                    {service.RecordAuto("recovery",new{state,plan=take.Plan});Track(()=>GameActions.ClaimReward(take.Button));}
                    return;
                }
                var uncertain=plans.FirstOrDefault(r=>r.Plan.Action=="manual");
                if(uncertain.Button!=null){Manual("取回牌需判断："+uncertain.Plan.CardName+"。"+uncertain.Plan.Reasons[0]);return;}
                var unsupportedReward=buttons.FirstOrDefault(b=>b.Reward is not PotionReward && !IsStolenCardRecovery(b.Reward) && !LeaveHarmfulFakeRelic(b.Reward));
                if(unsupportedReward!=null)
                {Manual("此奖励尚未适配："+unsupportedReward.Reward?.Description.GetFormattedText()+"（"+unsupportedReward.Reward?.GetType().Name+"），请手动处理。");return;}
                // No strictly better potion remains: leave the excess reward behind.
                var proceed=(NClickableControl)GameActions.Field(rewards,"_proceedButton")!;
                if(plans.Length>0)
                {
                    if(GameActions.Field(rewards,"_skipDisallowed") is true){Manual("建议不取回，但此奖励界面禁止跳过，请手动处理。");return;}
                    Wait("接管中 · 不取回 "+string.Join("、",plans.Select(p=>p.Plan.CardName))+"，保留免费删牌收益");
                    if(Claim("leave-recovery:"+rewards.GetInstanceId()+":"+fingerprint,proceed.IsEnabled && proceed.IsVisibleInTree()))
                    {service.RecordAuto("recovery_leave",new{state,plans=plans.Select(p=>p.Plan).ToArray()});GameActions.Invoke(rewards,"OnProceedButtonPressed",proceed);}
                    return;
                }
                Proceed(rewards,proceed,"OnProceedButtonPressed");return;
            }
            Wait("接管中 · 领取奖励（药水按等级与防守用途择优保留）");
            if(Claim("reward:"+safe.GetInstanceId()))
            {if(safe.Reward is RelicReward {Relic:not null} pickup)PrepareRelicSelection(pickup.Relic);Track(()=>GameActions.ClaimReward(safe));}return;
        }
        if(service.State.CurrentRoom is MerchantRoom merchant && screen is NMerchantRoom or NMerchantInventory)
        {
            var room=NMerchantRoom.Instance;
            if(room==null){gate.ResetPending();return;}
            // Reset per-merchant purchase state together, including Foul Potion uses.
            if(shoppingRoom!=merchant){shoppingRoom=merchant;shopArrivalGold=service.Player.Gold;shopBoughtCards=0;
                shopFoulUses=0;}
            if(TickFoulMerchant(room))return;
            if(shopFoulUses>0)shopArrivalGold=Math.Max(shopArrivalGold,service.Player.Gold);
            if(screen is NMerchantRoom)
            {
                if(leavingShop==room.GetInstanceId())
                {LeaveShop(room);return;}
                // Open once. When the user closes the inventory, leave control with them.
                if(Claim("shop-open:"+room.GetInstanceId()+":"+shopFoulUses,room.MerchantButton.IsEnabled))room.OpenInventory();
                else if(WaitingForManual) return;
                return;
            }
            var inventory=(NMerchantInventory)screen;
            if(!inventory.IsOpen || GameActions.Field(inventory,"_isInputBlocked") is true){gate.ResetPending();return;}
            var slots=inventory.GetAllSlots().Where(s=>s.IsVisibleInTree() && s.Hitbox.IsEnabled && s.Entry is {IsStocked:true}).ToArray();
            var removal=slots.FirstOrDefault(s=>s.Entry is MerchantCardRemovalEntry);
            var cardsInShop=slots.Where(s=>s.Entry is MerchantCardEntry {CreationResult:not null}).ToArray();
            var state=service.WithForesight(SnapshotReader.Read(service.State,service.Player));
            if(TickPriorityShop(room,slots,state))return;
            var finalShop=FullAutomatic&&service.State.CurrentMapPoint is {} shopPoint&&
                ShopBudget.Reserve(state,RouteAdapter.Nodes(service.State),RouteAdapter.Key(shopPoint),100)==0;
            var plan=ShopPolicy.Evaluate(state,cardsInShop.Select(s=>new ShopCard(s.GetInstanceId().ToString(),SnapshotReader.ReadCard(((MerchantCardEntry)s.Entry).CreationResult!.Card),s.Entry.Cost)).ToArray(),
                removal?.Entry.Cost,shopArrivalGold,shopBoughtCards,finalShop?0:null);
            if(FullAutomatic)plan=plan with{Notes=[plan.Notes[0],
                $"全局接管：先比较遗物、买牌和删牌预算；高优先级遗物先成交，普通商品预留 {(finalShop?0:100)} 金币。"]};
            service.ShowShop(state,plan);
            var choice=plan.Recommendation;
            if(choice==null)
            {
                // Remaining modeled relics keep the same deterministic value ordering.
                if(FullAutomatic){TickClimbShop(room,inventory,slots,state);return;}
                // 单局接管：这店里没值得买的了 → 自己走人。
                leavingShop=room.GetInstanceId();
                Wait("接管中 · 商店没有可买的了，准备离开");
                LeaveShop(room);
                return;
            }
            var slot=choice.Kind=="remove"?removal:cardsInShop.FirstOrDefault(s=>s.GetInstanceId().ToString()==choice.Key);
            if(slot==null){gate.ResetPending();return;}
            Wait($"接管中 · { (choice.Kind=="remove"?"删牌":"购买") } {choice.Name}（{choice.Price} 金币）");
            var key=$"shop:{room.GetInstanceId()}:{slot.GetInstanceId()}:{service.Player.Gold}:{shopBoughtCards}:{string.Join(',',service.Player.Deck.Cards.Select(c=>c.Id.ToString()))}:{choice.Price}:{choice.Key}";
            if(Claim(key,slot.Entry.EnoughGold,8000))
            {
                if(choice.Kind=="remove")removalTarget=service.Player.Deck.Cards[choice.DeckIndex];
                service.RecordAuto("shop",new{state,plan});
                Track(()=>BuyShopSlot(slot,choice.Kind=="card"));
            }
            return;
        }
        gate.ResetPending();
    }
    /// <summary>
    /// Picks a card on the event grid screen (<c>NChooseACardSelectionScreen</c>).
    /// </summary>
    /// <remarks>
    /// Used by events such as BrainLeech "share knowledge" and other "choose 1 of N cards" grids. The screen is
    /// not one of the selectors TickEvent owns, so without this branch the automation waited there forever.
    /// Selection goes through the native <c>SelectHolder</c> so the completion source the caller awaits resolves
    /// exactly as if the player clicked the card. Unknown card values fall back to the first holder rather than
    /// handing the screen back.
    /// </remarks>
    private bool TickChooseACardGrid(NChooseACardSelectionScreen grid)
    {
        var holders=GameActions.Descendants<NGridCardHolder>(grid)
            .Where(h=>h.IsVisibleInTree()&&h.CardModel!=null).ToArray();
        var skip=GameActions.Field(grid,"_skipButton") as NClickableControl;
        if(holders.Length==0)
        {
            if(skip is {IsEnabled:true}&&skip.IsVisibleInTree())
            {
                Wait("接管中 · 跳过这组选牌");
                if(Claim("grid-skip:"+grid.GetInstanceId())) GameActions.Invoke(grid,"OnSkipButtonReleased",skip);
                return true;
            }
            gate.ResetPending();return true;
        }
        NGridCardHolder? pick=null;
        try
        {
            var state=service.WithForesight(SnapshotReader.Read(service.State,service.Player));
            var v=new StrategicValue(state);
            pick=holders
                .Select(h=>(Holder:h,Value:v.Add(SnapshotReader.ReadCard(h.CardModel!))))
                .Where(x=>double.IsFinite(x.Value))
                .OrderByDescending(x=>x.Value)
                .Select(x=>x.Holder)
                .FirstOrDefault();
        }
        catch
        {
            pick=null;
        }
        pick??=holders[0];
        Wait("接管中 · 选牌 "+pick.CardModel!.Title);
        if(Claim("grid-card:"+grid.GetInstanceId()+":"+pick.CardModel.Id.Entry,pick.IsVisibleInTree()))
            GameActions.Invoke(grid,"SelectHolder",pick);
        return true;
    }

    private void LeaveShop(NMerchantRoom room)
    {
        var now=Time.GetTicksMsec()/1000.0;if(now<nextShopLeaveClick)return;nextShopLeaveClick=now+1.5;
        Proceed(room,room.ProceedButton,"OnProceedButtonReleased");
    }
    /// <summary>
    /// Deterministic card-reward choice when the model cannot score the whole screen.
    /// </summary>
    /// <remarks>
    /// The previous behaviour handed the screen back to the player the moment any candidate carried an
    /// unmodelled effect, so one unknown card stopped the whole run. The fallback keeps automation in control:
    /// the best scored option if any survives, otherwise skip (which is always executable and never adds an
    /// unknown card), otherwise the highest-rarity card. The reason is recorded with the decision.
    /// </remarks>
    private static DecisionOption? FallbackRewardChoice(DecisionRecord decision,out string reason)
    {
        var evaluation=decision.Evaluation.Options.ToDictionary(o=>o.Key);
        var enabled=decision.Options.Where(o=>!evaluation.TryGetValue(o.Key,out var score)||score.Enabled).ToArray();
        DecisionOption? scored=enabled
            .Where(o=>evaluation.TryGetValue(o.Key,out var score)&&score.Supported&&score.Score.HasValue)
            .OrderByDescending(o=>evaluation[o.Key].Score!.Value)
            .Cast<DecisionOption?>().FirstOrDefault();
        if(scored!=null){reason="最优可评分选项";return scored;}
        DecisionOption? skip=enabled.FirstOrDefault(o=>o.Kind=="skip");
        if(skip!=null){reason="没有可评分选项，跳过本次奖励";return skip;}
        DecisionOption? card=enabled
            .Where(o=>o.Kind=="card")
            .OrderByDescending(o=>o.Card?.Rarity switch{"Rare"=>3,"Uncommon"=>2,"Common"=>1,"Basic"=>0,_=>-1})
            .Cast<DecisionOption?>().FirstOrDefault();
        reason=card!=null?"没有可评分选项，取稀有度最高的牌":"没有可执行选项";
        return card;
    }
    /// <summary>本局是否已结束（RunManager 的 IsGameOver）。建筑师把玩家杀死后就是 true。</summary>
    private static bool RunOver()
    {
        try{return MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState()?.IsGameOver==true;}
        catch{return false;}
    }

    private void Proceed(Node owner,NClickableControl button,string method)
    {
        Wait("接管中 · 当前房间已处理，继续前进");
        if(Claim("proceed:"+owner.GetInstanceId(),button.IsEnabled && button.IsVisibleInTree()))GameActions.Invoke(owner,method,button);
    }
    private async Task BuyShopSlot(NMerchantSlot slot,bool isCard)
    {
        var before=service.Player.Deck.Cards.ToHashSet();
        if(slot.Entry is MerchantRelicEntry {Model:not null} relic)PrepareRelicSelection(relic.Model);
        await (Task)GameActions.Invoke(slot,"OnSelected")!;
        ownedPickup=null;ownedPickupRoom=null;
        if(isCard && service.Player.Deck.Cards.Any(c=>!before.Contains(c)))shopBoughtCards++;
    }
    internal static bool IsStolenCardRecovery(Reward? reward)=>reward?.GetType()==typeof(SpecialCardReward) &&
        GameActions.Field(reward,"_customDescriptionEncounterSourceId") is ModelId id &&
        id==ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.ThievingHopperWeak>().Id;
    internal static string RouteName(string kind)=>kind switch{"RestSite"=>"火堆","Elite"=>"精英","Monster"=>"普通战","Unknown"=>"问号","Shop"=>"商店","Treasure"=>"宝箱","Boss"=>"首领","MiniBoss"=>"骷髅小首领","Ancient"=>"先古",_=>kind};
}
