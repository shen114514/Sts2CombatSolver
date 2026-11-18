# 故障机器人抓牌策略（defect-draft-0.16）

机器人的选牌不是"看单卡强弱"，而是先算**当前牌组缺哪个端口**，再给候选卡打分。

## 1. 数据文件

`data/defect-draft-v0.14.json`（文件名保留，内容版本由 `DefectPolicy.Version` 标记，当前 `defect-draft-0.16-bilibili`）：

```jsonc
{
  "coefficients": { "cardTax": 2.5, "damageNeed": 2.0, "blockNeed": 2.0,
                    "duplicateQuadratic": 0.65, "synergyCap": 5.0 },
  "cards": {
    "Defragment": { "base": 8.5, "phase": [0, 1, 2], "duplicate": 4.0,
                    "upgradeDelta": 1, "tags": ["focus","scaling"] }
  }
}
```

字段含义：

| 字段 | 含义 |
|---|---|
| `base` | 卡牌基础先验 |
| `phase[0..2]` | 第一/二/三幕的额外需求权重 |
| `duplicate` | 每多一张的线性重复惩罚 |
| `upgradeDelta` | 这张牌升级后的机制增益 |
| `tags` | 端口标签（`attack`/`block`/`frost`/`dark`/`draw`/`energy`/`focus`/`slots`/`evoke`/`aoe`/`transition` 等） |

## 2. 打分公式

对候选卡 `c`：

```
score = base + phase[act-1] - cardTax
        + 输出缺口 + 防御缺口 + 群体缺口
        - (duplicate * n + duplicateQuadratic * n^2)          // n = 牌组里已有张数
        + 升级增益（若已升级）
        + 抽牌/能量/球位/集中的情境项
        + 组合加成（产物 → 收益牌）
        - 饱和度惩罚（过牌过多、能力过多、过渡牌过多）
```

其中：

- **输出缺口** = `damageNeed * max(0, 1 - offense/4) * min(1.5, Damage/8)`；
- **防御缺口** = `blockNeed * max(0, 1 - defense/5)`；
- **过牌饱和** = `-max(0, draw - (物理流 ? 12 : 8)) * 0.35`。

`offense`/`defense` 是把牌组按端口折算出来的"可覆盖回合数"，不是简单的牌数。

## 3. 组合模块（`src/automation/Core/`）

| 模块 | 作用 |
|---|---|
| `DefectOrbPlan` | 产球/球位/集中/激发的边际价值；集中饱和、过渡上限 |
| `DefectComposition` | 攻防端口、物理流判定、过渡牌递减、伤害引擎判定 |
| `DefectDamagePlan` | 当前输出是否够快（能否在敌人打死你之前打死它） |
| `DefectStrategy` | 非卡牌选项（升级、删牌、变化）的估值 |
| `PriorityShopPolicy` | 商店买牌/删牌/遗物预算比较 |

### 3.1 永久集中饱和

攻略共识是"永久集中只留一到两张"。实现上，牌组里已有 `focus` 张永久集中时，再抓一张额外扣 `2.5 * focus`，叠加 `duplicate` 后：

| 第几张 | 惩罚 |
|---|---|
| 第 2 张 | ≈ −6.5 |
| 第 3 张 | ≈ −13 |

### 3.2 过渡牌上限

一层允许 4 张、二层 6 张、三层 8 张 `transition` 标签的牌；超出后再抓额外 `-2.5 ~ -5.5`。对应攻略的"过渡牌再优质也不能抓很多，会拖低后期牌组质量"。

## 4. 先验来源

`defect-draft-0.16` 的调整来自三份实战攻略 + 我们自己的回归数据：

1. **老穆《硬核攻略系列 4》**：构筑目的是通关；三幕双 Boss 拖不起，看"出伤速度 = 牌效比 × 能量 × 出牌数"；小卡组被塞牌针对；能力牌有延迟，需要启动件；遗物是主要启动件、精英是遗物主要来源。
2. **晓夫九《故障机器人 nosl 20 连胜》**：球位 × 集中是指数关系；先填满球位再扩容；漆黑要多次打出再激发；一层火堆+精英、少走强怪池。
3. **PU_Ultima《全卡牌评级》**：S++ 漆黑/压缩，S+ 超频/快速检索，S 碎片整理/冰寒/万物一星/打碎/模组改造/火箭飞拳/扩容/启动流程/内核加速；C 爪击、集中打击、双倍能量、弹幕、暴风雨等。

音译不确定的卡牌**没有改动**（保持原值），全表 34 项数值调整。

## 5. 验证

- `tests/defect-orb-check`：7 项断言（球系优先于纯攻击、物理流兼容、集中饱和、奖励观察等）；
- 30 副真实牌组的离线探针：改版后"球系最高分 > 攻击最高分"从 28/30 提升到 **30/30**；
- A10 十局回归：**9 胜 1 负**（唯一失败为 Kaiser Crab），牌组产球 4–10、永久集中 0–2。

## 6. 扩展方式

新增一张机器人卡：

1. 在 `data/defect-draft-v0.14.json` 的 `cards` 里加条目（`base`/`phase`/`duplicate`/`upgradeDelta`/`tags`）；
2. 如需特殊逻辑，在 `DefectOrbPlan` 或 `DefectComposition` 加情境项；
3. 跑 `tests/defect-orb-check` 与 30 副牌组探针，确认方向没有反转；
4. 更新 `DefectPolicy.Version`。

未登记的卡牌不会崩：奖励界面会回退到"稀有度 + 基础评分"，并在日志里提示"未适配卡牌"。
