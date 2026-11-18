# 架构总览

本仓库是一个《杀戮尖塔 2》的**单机战斗与房间自动化模组**，用 .NET 9 + Harmony 注入游戏进程。它由两套互相独立的引擎组成：

```
┌───────────────────────────── 游戏进程 (SlayTheSpire2.exe) ─────────────────────────────┐
│                                                                                        │
│  Harmony 补丁 (src/harness, src/shim)                                                   │
│      │                                                                                 │
│      ├── 战斗求解器  src/solver/                                                        │
│      │     ├── Runtime/      读取 native 战斗状态 → CombatRootSnapshot                   │
│      │     ├── Engine/InCombat/Mirrors/   用镜像方法重放卡牌/能力/遗物/敌人行为           │
│      │     ├── Search/       Beam 搜索 + 多轮补搜 + 药水梯度 + 审计                       │
│      │     └── Output/       把选中的 PlanAction 序列交回原生动作队列                     │
│      │                                                                                │
│      └── 房间自动化  src/automation/                                                    │
│            ├── RoomAutomation        200ms 轮询 + 守卫 + 故障房间隔离                     │
│            ├── Runtime/PartialAutopilot  按界面分派：奖励/商店/营火/事件/选牌/地图         │
│            ├── Runtime/AdvisorService    选项快照、评分、日志、故障记录                    │
│            └── Core/                 机器人抓牌先验、商店/路线/药水/遗物估值              │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

两套引擎共享一个原则：**不伪造游戏状态**。求解器在"镜像模拟"里推演，执行阶段只通过原生方法（`GameActions.Invoke`、`GameActions.ClaimReward`、药水 `EnqueueManualUse` 等）提交，等价于玩家点击。

## 数据流

1. **战斗开始** → `CombatRootSnapshot.Capture` 读取玩家、敌人、手牌/抽牌堆/弃牌堆、遗物、药水、敌人意图，冻结为不可变快照。
2. **搜索** → `CombatSearchCoordinator.Solve` 在快照上跑 Beam 搜索；每个节点是一串 `PlanAction`（打牌、喝药、选择、结束回合）。
3. **选路** → 结果按 `PrimarySearchIncumbent(StrategicHpDeficit, CombatEndedTurn, ExplicitPotionStrategicCost)` 字典序比较，生存优先，其次回合数，再次药水成本。
4. **执行** → 当前回合的动作交给原生动作队列；跨回合计划按回合边界逐步验证后再提交。
5. **战斗结束** → `RoomAutomation` 接管奖励、商店、营火、事件、地图。

## 线程与预算

- 搜索跑在后台线程，主线程通过 `SolverProgress` 回调更新面板；`MAIN_THREAD_FRAMES` 日志会记录主线程卡顿（p95/p99/max gap）。
- 所有搜索阶段共享 `SearchBudgetLedger`：按档位分配节点与时间片，阶段之间留 `reserveMilliseconds`，避免补搜把主线程拖死。
- 可选 no-GC 区域（`enableNoGcRegion`）在搜索密集段减少 GC 停顿；`SEARCH_GC_LIFECYCLE` / `HEAP_RECLAIM` 日志记录每次回收。

## 日志

`%APPDATA%\SlayTheSpire2\logs\CombatSolver\<pid>-<hash>\`

| 文件 | 内容 |
|---|---|
| `process.jsonl` | 进程级事件：初始化、设置、搜索生命周期、`[automation]` 提交/决策 |
| `combat-<id>.jsonl` | 单场战斗的逐动作、节点统计、搜索阶段耗时 |
| `godot.log` | 游戏自身输出（节点加载、存档写入、崩溃前最后动作） |

自动化日志统一是 `[automation] <type> <json>`，例如 `submit`、`event`、`shop_preview`、`shop_slots`、`climb_spend`、`reward_fallback`、`error`。

## 验证方式

- `tests/` 下有各自的离线检查工程（`defect-orb-check`、`necrobinder-check`、`shinies-check` 等）；
- 关键机制用**原生对照**：同一状态下让镜像模拟与原生命中/格挡/抽牌逐项比较（见 `tests/VALIDATION-<版本>.md`）；
- 每次发布前跑全量回归，历史上是 200+ 项断言语义级回归 + 十余项原生对照。

## 阅读顺序

1. 战斗部分 → [`COMBAT-SOLVER.md`](COMBAT-SOLVER.md)
2. 房间自动化 → [`ROOM-AUTOMATION.md`](ROOM-AUTOMATION.md)
3. 机器人抓牌 → [`DEFECT-POLICY.md`](DEFECT-POLICY.md)
