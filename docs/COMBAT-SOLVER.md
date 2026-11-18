# 战斗求解器：原理与算法

核心问题：给定当前战斗状态，找出一条**能赢且战损最小**的动作序列，并且这条序列能被游戏原生执行。

## 1. 为什么不直接用真实游戏状态搜索

游戏对象在推演过程中会被改写（打牌消耗手牌、触发遗物、随机数推进），直接在真实对象上搜索会污染存档和 RNG。所以求解器采用**镜像模拟（mirror simulation）**：

- `CombatRootSnapshot.Capture` 把真实状态读成不可变快照；
- `Engine/InCombat/Mirrors/` 里为每张卡、每个能力、每件遗物、每种敌人行为注册一个**镜像方法**，只改快照/预演对象，不碰原生对象；
- 需要原生判定（伤害修正、格挡、易伤、能量、抽牌顺序）时，调用注册过的原生静态方法（`MirrorMethodRegistry`），保证数值与游戏一致；
- 选中的动作在执行阶段再通过原生 API 提交。

这样搜索可以随意回滚、并行、剪枝，而执行仍然是"玩家点击"。

## 2. Beam 搜索

`CombatBeamSolver` 是主搜索器，结构是**逐动作扩展的 Beam**：

- 一个节点 = (预演状态, 已走动作串, 当前回合号, 分数)；
- 扩展动作包括：打出手牌（含目标选择）、使用药水（含药水选项）、选择（弃牌/发现/生成）、结束回合；
- 每层按 `SolverSearchProfile.BeamWidth` 保留节点，节点总数受 `MaxExpandedNodes` 限制；
- 分支数受 `MaxCardBranchesPerNode`、`MaxPileChoiceBranchesPerAction`、`MaxHandChoiceBranchesPerAction` 限制；
- 时间受 `SoftTimeBudgetMilliseconds` 限制，超时返回当前最好结果而不是报错。

档位（`SolverSettings`）：

| 档位 | Beam | 节点上限 | 软时间上限 |
|---|---|---|---|
| Default | 60 | 120 000 | 120 s |
| High | 45 | 60 000 | 60 s |
| VeryHigh | 90 | 250 000 | 180 s |
| 更高档 | 135 | 500 000 | 300 s |

没有找到任何胜利路线且预算还有剩余时，Beam 宽度与节点上限同时按 `NoVictoryEscalationFactor` 翻倍（最多数次），从根重搜。

## 3. 节点打分

排序用 `node.Score`，主要项来自 `SolverWeights`（单位是"分数"，只要相对大小有意义）：

| 项 | 值 | 作用 |
|---|---|---|
| `Hp` | 30 000 | 每点玩家生命 |
| `EnemyHp` | −10 000 | 每点敌人生命 |
| `VictoryBonus` | 1e10 | 全灭敌人 |
| `DeathPenalty` | −1e12 | 玩家死亡 |
| `ActionPenalty` | −1 | 每个动作（抑制无意义循环） |
| `CurrentEnergyBeamValue` | 60 000（上限 6 点） | 当回合能量，保证后续牌能打出 |
| `PersistentBuffDeltaBeamValue` | 150 000（标准档）/ 50 000（持续成长） | 能力生效后的实际增量 |
| `ReplayPotentialBeamValue` | 10 000 | 回收/重放潜力（只用于保留路线） |
| `LongTermResourceBeamValue` | 25 000（封顶） | 永久成长与战后资源 |

注意：这些是**排序权重，不是最终判据**。最终选路用 `PrimarySearchIncumbent` 的字典序：

```
(StrategicHpDeficit, CombatEndedTurn, ExplicitPotionStrategicCost)
```

`StrategicHpDeficit` 不是简单血量差，而是把"这一战之后还要走的路"折算进去（见 `ActEndingBossPolicy`：幕末 Boss 的战损按后续回血/路线价值缩放，A10 双 Boss 首战单独处理）。

## 4. 多轮补搜（CombatSearchCoordinator）

主搜索之外，协调器按需追加更贵的搜索，每次都用预算账本限流：

| 阶段 | 目的 |
|---|---|
| `RunPlanSearchPass` | 主搜索（完整动作空间） |
| `RunBeamWidthPortfolioPass` | 用不同 Beam 宽度再跑一遍，防止窄搜索错杀长线 |
| `RunNoveltyPortfolioPass` / `RunAdaptiveNoveltyRefinement` | 结构性探索：强制走不同的能力/回合顺序 |
| `RunEarlyTurnExploration` | 前几回合的多样开局 |
| `RunOpeningPowerRoutePortfolio` / `RunOpeningNightmarePortfolio` | 先铺垫能力再打的路线 |
| `RunGeneratedPotionChainSearchPass` | 药水生成链（喝一瓶再生出另一瓶） |
| `RunDeferredPowerPlanSearchPass` | 推迟能力的方案 |

搜索后（`RunPostSearchPasses`）：

| 补搜 | 触发条件 |
|---|---|
| `RunEarlyPotionPairRescue` | 单瓶药救不了，但两瓶能过 |
| `RunForcedPotionOpeningRescue` | 玩家强制指令 + 只在死亡路线时 |
| `RunTurnBoundaryRescue` | 回合边界处重新评估（保留 10 点安全血） |
| `RunZeroCostOpeningRescue` | 0 费开局被漏掉 |
| `RunMidCombatRefinement` | 中局精修 |
| `RunTurnEndChoicePosterior` | 结束回合时的选择后验 |
| `RunEarlierCopyDelayedDamage` | 更早拿到重复牌的延迟伤害 |

审计（`Audits.cs`）：

- `SearchSmartPotionGradient`：按"用 0、1、2…瓶"逐层搜索，形成药水收益梯度；
- `RunSpendAllPotionAudit`：终局允许梭哈时，检查用尽全部药水是否更优；
- 开局药水审计：只要腰带里有**整场增益**药水（力量/敏捷/专注/再生/鱼油/液态青铜/玛扎莱斯/球位/士兵炖），就一定会搜"第一回合喝掉"的路线。

## 5. 药水策略

`PotionSpendPolicy` 给出本场战斗的保留价（还要留多少血才值得喝药）：

| 模式 | 何时 | 普通腰带 | 有蕨草/白兽 |
|---|---|---|---|
| `SpendAll` | 终局、必须过 | 0 | 0 |
| `Conserve` | 一二幕 Boss（过关即可） | 12 | 5 |
| `DoubleBoss` | A10 双 Boss 首战 | 4 | 1 |
| `Normal` | 普通战斗 | 4 | 1 |

- **娇嫩蕨草**（每战前补满空药水栏）与**白兽雕像**（每战必掉药水）让药水可再生，所以保留价下调；
- 药水梯度只在"净收益 ≥ 保留价"时采纳；
- 生存不受保留价阻塞：如果不用药就是死，补搜会绕过保留价。

## 6. 容错

- **搜索隔离**：某张牌的镜像预测抛异常时，把该卡加入 `ExcludedCardIds` 重搜，成功则记录 `SEARCH_QUARANTINE_SUCCESS`，失败则放弃降级、保留原生操作；
- **无解不硬凑**：找不到可执行动作时返回"只有死亡路线"，由上层决定是否停手；
- **时间/内存兜底**：`SearchBudgetLedger` 与 `HeapReclaim` 确保搜索不会把进程拖死。

## 7. 确定性

- 快照冻结随机数状态；需要随机的地方用快照里的 RNG 推进；
- 同一输入 + 同一档位 → 同一结果（用于回归测试）；
- 原生对照测试逐项比较：伤害、格挡、抽牌、能量、易伤层数、遗物触发次数。
