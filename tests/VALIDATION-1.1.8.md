# 1.1.8 横祸与随机自动出牌

2026-11-16，游戏v0.113.1。本地构建、部署与打包，未写GitHub。构建、安装、两轮最终原生验收及包内DLL均为SHA-256：`0D57038B1B3C826876E9C32D0AC2983657CF60E4688CAB16BCC8B62CC693438A`。

## 真实停顿

用户机器人A10第35层TURRET_OPERATOR_WEAK，第2回合打横祸，回响形态重复外层卡牌。计划要求嵌套全息影像选择超频，但实际弃牌选项为烟囱、电击、分离，没有超频。NativeChoicePlanMismatchException使接管暂停，原生游戏和菜单仍可响应。完整日志已保存，未通过随意选择其他牌或取消严格匹配掩盖错误。

当前原生Catastrophe.OnPlay使用RunState.Rng.CombatCardSelection，镜像却使用Shuffle，随机打出的牌和其后的牌堆／选牌自然不同。同类静态检查发现Uproar和BeatDown也有相同的旧流残留，三者统一修正；SeekerStrike和PunchOfJoy在现有实现中已使用选牌流。

横祸循环在嵌套选择时原先只break，半截续接无法表达剩余次数。现在显式拒绝该动作的局部续接优化，由已有完整动作与选择游标重放，保留后续次数和Echo Form外层重复；痛殴和骚动也使用完整动作重放保护。没有重写模拟架构或改变策略评分。

## 原生边界与差分

[专项12项结果](results/catastrophe-native-1.1.8.json)包含十组场景及框架／自动搜索隔离检查。[逐组状态结果](results/catastrophe-models-1.1.8.json)覆盖普通、升级、嵌套全息影像、回响嵌套、升级回响嵌套、全不可打出、空牌堆、升级空牌堆、骚动和痛殴。整个续接状态、牌堆顺序、Fork及所有记录随机数状态与原生一致。嵌套选择分别2／4／6次，回响时横祸自身结算两次。结果中的outerPlays字段仅统计横祸ID，因此骚动、痛殴行记0；状态差分仍对两者的完整执行进行比较。

首个专项错误使用ThrowingPlayerChoiceContext，原生请求选择时抛NotImplementedException，修正测试为BlockingPlayerChoiceContext后重验。随后空牌堆案例暴露真实模型遗漏：原生仍每次执行两次空StableShuffle，空洗牌也消耗随机数；模拟提前break少耗两次（该案counter期望23、原生25）。改为原生次数继续循环，并增加升级空牌堆案。两轮失败保留，不计为通过。

## 原局副本

[同局副本5项检查](results/catastrophe-saved-native-1.1.8.json)使用正常保存退出后的机器人A10、坐标(6,1)、第35层同一TURRET_OPERATOR_WEAK，未增加正式重载次数，shouldSave=false。15秒主预算、梁宽30、最多50,000节点，1次搜索、3次exact_state_text续接，无漂移重算。横祸在第2／3／4回合实际执行，第4回合获胜，剩42HP，人工应答0。它没有将测试胜利写入正式历史，也不是自然通关率样本。

该分支复用原有保存副本诊断入口，部分历史检查名称仍含Glam或SecondCombat；新的Kind=Catastrophe、Encounter和坐标明确指向本案，不能误当作先前第一幕第二战。生产出牌仍走真实动作队列和NativeChoiceRuntime。

SolvedRouteCache身份包含求解器模块MVID，重新构建的模型不会命中旧错误模型的缓存；没有删除用户缓存或改配置。

## 保全与构建

用户授权代退出后，经游戏菜单“保存并退出”返回主菜单，再正常关闭程序；没有强杀或放弃。保留操作前saves-before、退出后saves-stable及settings-stable作为不同阶段基线。[验证审计](results/build-1.1.8.json)在重新继续正式游戏之前确认224个文件与退出后基线零增删改、当前存档哈希一致、个人设置哈希相同。若之后正常继续正式局，重载计数及进度的正常写入应另记，不能继续声称运行中的正式文件完全不变。

最终两个原生验证进程均Passed、shouldSave=false、退出0、异常转储0。175项既有回归通过：110开关／决策、9选路、7IL、10千足虫、7用药、23机器人、9华彩。构建0错误、8个既有／离线源查询警告。安装包仅DLL、manifest和LICENSE，内嵌ZIP校验一致，摘要见build结果。

继承1.1.7固定／随机角色连开，未更改玩家自动开局、难度、药水或暂停偏好。未知模型仍会明确隔离；不保证所有效果已经覆盖，不据此宣称90%胜率，也未解决此前独立的原生堆损坏和实验体旧生命差异。

复验可用tests/run-content-native.ps1的`-Mode CATASTROPHE`，或`-Mode CATASTROPHE_SAVED -ReplaySave "保存副本绝对路径"`。保存副本模式仅用于含横祸的本案战斗；全部测试非保存，须检查原生退出和转储，不能只看result.json。
