# 房间自动化：原理与分派

战斗之外的一切（奖励、商店、营火、事件、选牌、地图）由 `RoomAutomation` + `PartialAutopilot` 处理。

## 1. 主循环

```
RoomAutomation.Tick()            // 每 200ms
 └─ TickCore()
     ├─ 单例/多人局/战斗中 → 直接返回（战斗归求解器管）
     ├─ 设置面板打开 → 交还人工
     ├─ 取当前前台界面，校验节点有效
     ├─ RunManager 为空/未开局/多人/已结束 → 清空服务后返回
     ├─ 战斗自动化开关有变化 → 重新启用/暂停
     ├─ 允许当前界面？不允许 → 交还人工并提示
     └─ service.Guard(service.Poll)   // 异常只暂停，不崩游戏
        service.Autopilot.Tick()
```

### 故障隔离

- `AdvisorService.Guard` 捕获异常 → `Autopilot.Pause(原因)` + 记录 `[automation] error`；
- 记录 error 时会把 `FaultedRoom` 设为当前房间；
- 同一房间出错后**15 秒重试一次**，仍失败才继续等；换房立即重试（避免一处异常永久停摆）。

## 2. 界面分派顺序

`PartialAutopilot.TickCore` 按固定顺序判断当前界面，先命中先处理：

1. **战斗 / 结算** 相关（交给求解器或等待）；
2. **`NRewardsScreen`**（战斗奖励列表）：逐项领取；卡牌奖励进入选牌界面；
3. **`NCardRewardSelectionScreen`**：按抓牌评分选牌；无正收益则跳过；分数全不可用时按稀有度回退（`reward_fallback` 日志）；
4. **商店**（`NMerchantRoom` / `NMerchantInventory` / 假商人）；
5. **营火**（`NRestSiteRoom`）：升级/休息/挖宝等；
6. **事件**（`NEventRoom`、`NFakeMerchant`、`NCrystalSphereScreen` 等自定义事件）；
7. **选牌界面**（`NDeckCardSelectScreen` / `NDeckUpgradeSelectScreen` / `NDeckTransformSelectScreen` / `NDeckEnchantSelectScreen` / `NSimpleCardSelectScreen` / `NChooseACardSelectionScreen`）；
8. **地图**：按路线评分选下一个节点。

### 动作去重：`Claim`

所有点击都通过 `Claim(key, condition, delay)` 提交：

- `key` 唯一标识一次动作（界面 id + 槽位 id + 当前金币/牌组指纹等）；
- 同一 key 只会成功一次，避免一帧内重复点击；
- `condition` 为 false 时不消费 key（例如金币不够、按钮未启用）；
- `delay` 让"买完立刻再买"有稳定的节奏。

### 三种等待状态

| 状态 | 含义 |
|---|---|
| `Wait(文本)` | 自动化正在处理，等下一帧继续 |
| `Manual(文本)` | 这个界面需要人工，自动化停手 |
| `gate.ResetPending()` | 界面还没准备好，下一帧重试 |

## 3. 商店

```
打开商店 → 读槽位（可见 + Hitbox 启用 + IsStocked）
  ├─ 槽位为空 → 等待载入（不再当"空店"直接走）
  ├─ TickPriorityShop：遗物/卡牌/删牌按预算比较，先买净收益最高的
  ├─ ShopPolicy：默认预留 150 金币的买牌/删牌计划
  └─ 没有推荐时（全局接管）
       ├─ 遗物：按 `RelicNetValue` 排序买
       ├─ 药水：按 `ShopAdvice.Potion` 补空位
       └─ 还是买不到 → 用预留 0 再算一次买牌/删牌（清仓）
            └─ 仍无 → Close 离开
```

日志：`shop_slots`（货架明细）、`shop_preview`（计划）、`climb_spend`（清仓购买）。

**假商人**（`NFakeMerchant`）是独立事件，流程是：查看陈列 → 买得起且净收益 > 2 就买 → 买不到就关店并走原生 `OnProceedButtonReleased`（内部 `RunManager.Proceed()`）。

## 4. 事件与选牌

- 事件选项由 `EventAdapter.Read` 读成 `EventPlan`，评分来自 `EventPolicy`（收益/代价/风险）；
- 评分不可用时的回退顺序：**能走就走** → 最佳原始收益 → 稀有度，且绝不选致命选项；
- 选牌界面的目标在打开期间保持稳定（不能每帧重算，否则会误判成"玩家已开始选牌"而交还人工）；
- 附魔选牌不依赖事件归属：营地、遗物、事件任何入口打开都会自动选，未知附魔按稀有度回退。

## 5. 地图与路线

- 每个可达节点的评分来自 `StrategicHpDeficit`：预计战损 + 后续路线的恢复价值；
- 精英/商店/营火/问号的权重按当前牌组状态调整（例如过渡输出不足时少走精英）；
- 选择后用原生地图投票提交（单人也走投票接口，保持与游戏一致）。

## 6. 自动下一局

`AutoNextRun` 只做"本局结束后的续开"：

- 不接管主菜单、不覆盖未完成存档；
- 失败/胜利结算 → 回主菜单 → 按设定角色开新局；
- 任何异常只暂停自己，不影响自动化主流程。

## 7. 已知边界

- 第三方模组新增的事件/遗物/卡牌：未建模时走通用回退或交还人工；
- 多人局：自动化只支持单人标准模式；
- 任何界面如果连续两帧都拿不到必要字段（`_cards`、`_prefs`、`_selectedCards`），会 `Manual` 而不是猜测。
