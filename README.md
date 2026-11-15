# Sts2CombatSolver

《杀戮尖塔2》Windows 独立战斗求解器，保留自动选路；奖励拿牌、商店、事件和火堆可分别开启自动操作。无需 RitsuLib、鲸鱼娘或外部服务。

## 安装

1. 关闭游戏，到 [Releases](https://github.com/shen114514/Sts2CombatSolver/releases/latest) 下载 `Sts2CombatSolver-Setup-1.1.0.exe`。
2. 双击运行。安装器自动查找 Steam 游戏目录；未找到时选择含 `SlayTheSpire2.exe` 的目录。
3. 启动游戏并启用 Mod。单人战斗默认自动计算、出牌；地图允许前进时自动选路。可在求解器面板暂停、关闭全自动或关闭自动选路。
4. **设置 → 常规 → 房间自动操作**：分别开启“自动奖励与拿牌”“自动商店”“自动事件”“自动火堆”。四项默认关闭，设置会保存；关闭某项后交还手动操作。已经提交的单次动作可能仍在完成。

已验证 Windows 游戏 **v0.113.1**，安装器会校验游戏程序集，版本不匹配时停止。安装 EXE 使用 Windows 自带的 .NET Framework 4.x，无需安装 .NET SDK、Python 或其他 Mod。安装器尚未签名，Windows 可能显示安全提示；请核对发布来源及 SHA256SUMS。

安装器会将现有 `Sts2CombatSolver`、`CombatSolver`、`Sts2Whale` 文件夹备份到游戏根目录的 `mod-backups/Sts2CombatSolver/时间编号/`，再安装独立版。不修改存档、用户配置或无关 Mod；不自动删除朋友可能用于其他 Mod 的 RitsuLib。本发布包不包含也不依赖它。

手动安装：将 ZIP 中三个文件放到 `游戏目录/mods/Sts2CombatSolver/`。卸载：关闭游戏后删除该文件夹；如需回退，从 `mod-backups` 取回原文件夹。

## 功能范围

- 保留：战斗分支模拟、搜索、用药决策、自动出牌、战斗内选牌、设置面板、本机战斗日志、可关闭的自动选路。
- 新增四项独立开关：自动奖励与拿牌、商店购买/删牌、事件选择、火堆休息/升级及已支持操作。只开启一个功能不会接管其他房间。
- 自动商店复用当前牌组估值和强遗物预算保护，购买后重新读取库存与价格；火堆复用愚者升级及后续风险评估。
- 自动选路使用当前生命、金币、牌组基础牌占比与升级数量做有限前瞻，属于启发式；不预读隐藏事件、商店或遭遇随机数。
- 新开关关闭时由玩家操作；未支持的事件、选牌或弹窗仍会交还手动。适配异常只隔离当前房间，进入新房间后可恢复，不保证所有分支都能无人值守。
- 打开求解器设置时，选路和房间自动操作暂停提交动作。
- 已移除鲸鱼娘、LLM 解说、气泡、语音、VTube Studio、皮套、HTTP 桥接、联网统计、上传与自动开下一局。
- 当前仅支持单人自动操作。预测异常会停止接管并交回原生操作，不会宣称所有模组内容都能精确模拟。

搜索可能花费几十秒至数分钟，可以在面板调整预算或暂停。没有超过90%通关率的验证结论。设置单独写入游戏用户目录的 `standalone_combat_solver_settings.json`，不迁移旧鲸鱼配置。

## 构建

需 .NET SDK 9或10及合法安装的 Windows 游戏；游戏程序集不随源码发布。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -GameDirectory "E:\SteamLibrary\steamapps\common\Slay the Spire 2" -Package
dotnet run --project .\tests\route-check\route-check.csproj -c Release
dotnet run --project .\tests\il-check\il-check.csproj -c Release -p:Sts2Dir="E:\SteamLibrary\steamapps\common\Slay the Spire 2"
dotnet run --project .\tests\automation-check\automation-check.csproj -c Release
```

`dist/`生成安装EXE、手动安装ZIP和SHA256SUMS。构建安装器还需要 Windows 自带的 .NET Framework 4.x 编译器。发布构建只引用游戏提供的 `sts2`、`GodotSharp` 和 `0Harmony`；构建期 `Krafs.Publicizer` 不随安装包分发。

## 来源与许可证

战斗引擎来自 Torch1230 及贡献者的 CombatSolver，含 hotwords123 的 Random Foreseer 衍生部分；游戏API兼容部分来自 motion-less 的 Sts2Whale；离线房间决策模型来自穷仔（shaokangqian4-art）的 SpireStrategist。MIT 许可及版权声明保留于 LICENSE。保留既有异常隔离、箭矢分支与愚者效果修复，独立替换框架接口，不启动鲸鱼或联网交互。新的选路适配、开关集成与安装器随本仓库按 MIT 许可发布。
