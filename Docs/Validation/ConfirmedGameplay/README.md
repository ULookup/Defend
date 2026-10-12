# 已确认玩法验证画面

日期：2026年10月13日。入口：`Assets/Defend/Content/Scenes/Dev/IndustrialDefense.unity`。

完整规则、实现状态、操作数据和限制见 [开发进度](../../Planning/已确认玩法开发进度.md)。以下为 Unity 编辑器中的真实画面，非概念图；普通场景以界面和地图操作推进，未改写数值制造结果。

| 画面 | 内容 |
| --- | --- |
| [开始界面](start.png) | 游戏名称、开始守望、操作说明、档案、设置、退出 |
| [三栏档案](archive.png) | 分类、条目、可滚动资料，敌人资料共用玩法定义 |
| [声音设置](settings.png) | 音乐、音效及静音，绑定实际播放通道 |
| [敌人详情](enemy-details.png) | 实际点击巡猎者模型后的生命、属性、最终目标、行动与定位 |
| [战斗与供能](combat-network.png) | 第七波的战场、镇载者实体和建筑供能线路 |
| [胜利结算](victory.png) | 完成八波、击杀 186、累计收入 2684、核心剩余生命 700 |
| [失败结算](defeat.png) | 出售初始炮塔后自然失败，正常显示重开与返回按钮 |

失败页面首次截图捕获了未完成刷新的按钮文字。检查时发现编辑器已失去焦点且项目不在后台运行；只在该次编辑器播放中临时允许后台更新后重新捕获，按钮文字与顶点正常。未修改 PlayerSettings 的后台运行设置。最终截图使用刷新后的图像，原始捕获保留在本地 `Logs/ConfirmedGameplay-20261013/`。

本轮限定的八个核心基础用例全部通过：

1. `WorldCommandTests.RepairQuotesRoundPerBuildingAndAllRepairIsAtomic`
2. `BattleSimulationTests.PreparingTransactionsDoNotAdvanceTimeAndCombatEditsCannotRemainQueued`
3. `BattleSimulationTests.WavesRewardOnceWithoutRestoringCoreAndRequireContinuePreparing`
4. `BatterySupplyTests.BatteryCommitmentsRespectCapacityAndBalanceStopsBeforeReversal`
5. `BatterySupplyTests.BatteryKeepsHistoryAndCannotDischargeAnArrivalInTheSameTick`
6. `ConfirmedEnemyRulesTests.ClearingObstacleKeepsFinalCoreAndResumesApproach`
7. `ConfirmedEnemyRulesTests.NarrowPassageNeverOverlapsAndDeathReleasesSpace`
8. `ConfirmedEnemyRulesTests.BlockedSpawnRetainsQueueAndResumesOriginalIntervals`

修复实战窄路死锁后，只重跑受影响的三个敌人用例，结果 3/3；没有扩展成全套测试或增加第九个核心用例。未做压力、跑分、哈希值校验或安装包，也未完成真实触屏设备验收。
