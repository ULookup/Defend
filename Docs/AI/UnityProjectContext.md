# 涌现防线 Unity 项目上下文

<!-- unity-onboarding:generated:start -->

当前规则核对日期：2026-10-13，Asia/Shanghai。工作区：`E:/UnityProject/Defend`。本轮完整玩法已接入工业防线，Unity 编译和限定的 8 个核心基础用例通过；编辑器正常八波完成 186 次击杀，简短自然失败、重开和返回流程已确认。当前依据为 [已确认玩法开发规格与阶段计划](../Planning/已确认玩法开发规格与阶段计划.md) 及用户后续正式开发授权；实际进度与证据见 [已确认玩法开发进度](../Planning/已确认玩法开发进度.md)。规格内“只写文档”的旧任务边界已被正式开发授权替代。未提交、推送、发布或制作新安装包。

本文后半部分的第一至第五阶段指历史开发批次，与本轮六阶段计划不同。历史测试数量、压力检查和旧安装包均不作为当前玩法通过证据，也不授权本轮重跑。美术与镜头记录保留。

## 已确认的工程事实

| 范围 | 当前状态 | 证据 |
| --- | --- | --- |
| Unity | 6000.6.5f1，revision 3ff58d469c8a | `ProjectSettings/ProjectVersion.txt`，CLI editor_status |
| 渲染 | URP 17.6.0；QualitySettings 的质量档引用 PC/Mobile RPAsset；全局 Graphics 字段为空不等于使用 Built-in | manifest，GraphicsSettings，QualitySettings，Assets/Settings |
| 输入 | Input System 1.20.0，activeInputHandler=1 | manifest，ProjectSettings.asset |
| UI | uGUI 2.6.0；正式 DefenseInterface.prefab 已保存并接入 IndustrialDefense，运行时只绑定、更新和显隐；旧面板保留 | manifest，Content/Prefabs/UI，IndustrialDefense.unity |
| 测试 | Test Framework 1.8.0；Defend.Simulation.EditModeTests 行为测试 | manifest，Tests/EditMode |
| 其他包 | AI Navigation 2.0.15、Timeline 6.6.0、Visual Scripting 1.9.12 | manifest；不能据此判定已用于游戏架构 |
| 工具桥接 | Pipeline 0.8.0-exp.1，Unity CLI 1.0.0-beta.8，现有 Editor 连接可用 | manifest，CLI version/status/命令响应 |
| 启动与构建 | EditorBuildSettings 已启用 IndustrialDefense；SampleScene 和 CombatStress 保留但停用。本轮不制作安装包 | ProjectSettings/EditorBuildSettings.asset |
| 序列化 | Force Text、Visible Meta Files | EditorSettings.asset，VersionControlSettings.asset |
| 版本管理 | 已有 Unity YAML 合并及 LFS 规则；本机 Git LFS 3.0.1；未查询到 unityyamlmerge 驱动配置 | .gitattributes，git config，git lfs version |

最初接手时 Editor ready、未播放、未编译，Console 0 错误、0 警告。这是历史快照。第一阶段证据见 `Docs/Validation/PhaseOneWhiteboxReport.md`；第二阶段最终结果集中在 [PhaseTwoWhiteboxReport.md](../Validation/PhaseTwoWhiteboxReport.md)，不要用初始环境、编译通过或旧版结果代替当前验收。

## 当前正式入口与本轮验证

- 正式入口：`Assets/Defend/Content/Scenes/Dev/IndustrialDefense.unity`。保留现役 FuxiTurret、工业模型、特效和透视镜头，接入已保存的 `Assets/Defend/Content/Prefabs/UI/DefenseInterface.prefab`。
- 场景注册 7 种建筑（含核心、电池及三种炮塔）和 5 类敌人表现。底部商店提供反应堆、中继器、电池、闪哨机、崩岩机、荡尘机；核心不作为商品。
- 正式波次为 8 波，敌人数依次为 12 / 16 / 18 / 20 / 24 / 28 / 32 / 36，共 186 名。默认同时在场上限为可配置 200；被占位或上限延后的敌人保留原队列和间隔，不补刷或丢失。
- `ConfirmedDefenseAuthoring` 接入正式内容，`DefenseInterfaceAuthoring` 制作完整可编辑界面。所有 UI 控件预存在场景或 Prefab，运行时不创建或实例化 UI 控件。
- 本轮 Unity 编译通过，限定的 8 个核心基础用例全部通过。不是全套历史测试通过，也不是完整玩法或性能结论。
- 已实测暂停准备时购买电池：资金从 840 变为 680，逻辑时间保持第 181 步。其余实际交互、普通清波和整局流程仍由主代理继续验收；不得据此宣称全部波次已试玩通过。

## 第一方内容与历史启动

- `Assets/Defend/Content/Scenes/Dev/EnergyWhitebox.unity` 是第一阶段启动场景；原 SampleScene 保留。
- `Assets/Defend/Content/Scenes/Dev/CombatWhitebox.unity` 是第二阶段可玩场景，初始 13 栋建筑。`CombatStress.unity` 是单独的 100 建筑、200 敌人压力场景，使用独立参数，不把其高耐久和高资金当作普通玩法。
- `Assets/Settings` 包含模板 URP 资源；`Assets/InputSystem_Actions.inputactions` 是现有输入资产。
- `Assets/Defend/Scripts` 分为无 Unity 依赖的 Simulation、Unity 表现/输入/配置和 Editor 制作工具；各有程序集定义。第一阶段七个 Prefab 保留复用，第二阶段新增战斗面板、敌人及射击表现 Prefab。
- 最初工程检查未发现已有 AGENTS.md、CLAUDE.md、CONTRIBUTING.md 或 .github 工作流；祖先目录亦未找到 AGENTS.md。用户在消息中提供的规则继续有效，当前架构文档已保存在 Docs。
- manifest 无 Entities、Addressables 或联机框架的直接声明；没有第一方使用证据。不能因间接依赖含 Burst/Collections 就宣称项目已采用 DOTS。
- `GameSessionRoot` 显式组合 `GameWorld`、输入、表现和面板；不使用全局单例或字符串事件总线。`GameWorld.Submit` 立即验证并执行事务，`ProcessCommands` 不推进时间；`Enqueue` 先落实较早输入，再按当前阶段拒绝非法建设。逻辑步执行战斗、能源以及完整出怪与结算，整局结束停止推进。暂停由会话时钟控制。正式页面不提供测试伤害、测试负载或单步入口；旧开发工具保留。

## 已确认的产品方向

游戏为自动基地防御与能源网络玩法。正式地图以 40×40 为起点，建筑可多格占地；反应堆产生能量，中继延伸网络，炮塔和核心消耗能量，电池保存批次并可同时充放电。详细原始内容参考用户提供的团队评审 HTML 和共享讨论；后续用户明确修订优先于旧方案。

能源按终端收到的有效量加权分配，缓冲池有缺口就补；自动规划低损耗路线，拥堵允许备用路线；旧能量可改道换目标但不得重访建筑。累计滞留超时包含断路和持续拥堵，正常运输及正常输出间隔不计；恢复运输不清零预算。

已经确认依附建筑运输：建筑被毁/出售，其内部能量损失；当前一跳失去接收节点而无法完成时，批次损失；存活节点中的旧能量可继续分配。终端自用缓存不退回运输网。核心护盾有限容量，无额外独立回复速度上限。

只有准备阶段允许购买、移动、出售、单座维修、全部维修和供能优先级调整；准备暂停时合法事务立即生效且不推进时间。战斗及暂停战斗立即拒绝建设，不延后到下一准备阶段执行。普通清波先进入结算，点击“继续准备”后才恢复建设权限，下一波必须主动开始。

普通建筑被毁后永久消失，不能维修、移动或出售；重新购买获得新的稳定身份。存活普通建筑的维修价为满额维修基价乘缺失耐久比例并向上取整；全部维修先逐座报价再合计，资金不足完全不执行。维修保留库存、历史和身份。核心唯一，不能移动、出售或维修生命。

核心护盾只由正常实际到货补充。准备、战斗和普通波次结算持续运行能源；主动暂停停止游戏时间，整局胜负确定后停止模拟。清波不补满护盾、不恢复生命，不取消预约或增加外部供给。击杀奖励累计至成功清波统一发放，失败波不发奖励。初始总预算包含初始布局成本。

电池以实际库存和有效充电承诺参与同一个加权调度，可经中继支援全网合法可达终端；电池互补按储能比例差及启停阈值限额。新到货最早在下一逻辑步输出，同一份能量不能重访已访问建筑，正常电池存放不累计运输滞留。

五类敌人为巡猎者、拆迁者、收束者、拭净者、镇载者，分别使用独立属性和行为配置。全图目标候选共享实际伤害与实际供能收益，路线同时比较风险、路程、拆除及拥堵；最终目标与当前挡路建筑分开保存。敌人具有体积、相互阻挡、避让排队、攻击站位及出生占位，镇载者不强推同类。

开始页、准备、战斗、暂停、说明、三栏档案、声音设置、普通清波及整局结算已经接入。建筑与敌人使用同一个详情侧栏；敌人资料与档案共用定义，详情另读实时生命、属性、行动和目标，并提供定位。声音设置持久化，对局不跨启动续玩。UI 必须预存在场景或 Prefab，运行时只绑定、更新及显隐。

当前已取消 100 座建筑硬上限和分类上限，建设仅受阶段、资金、完整占地及合法位置约束；同时在场敌人以可配置 200 为起点。本轮不做性能跑分、压力测试或规模验收。2026-10-12 按用户最新要求，工业防线主关卡改为 60° 透视俯视、50° 垂直视场角，默认缩放系数 0.4，聚焦基地附近 (26, 0, 20)；旧白盒关卡保留原镜头配置。滚轮沿视线移动镜头，中键拖动保持地面指向。验证见 `Docs/Validation/PerspectiveCameraReport.md`。该确认不等于性能已经达标，也不等于确定最低发行配置。

## 历史工程设计与实施记录

2026-10-09 用户确认损失规则并要求工程设计；2026-10-10 先完成第一阶段能源白盒，随后用户确认中型规模及斜俯视镜头，并授权团队实施第二阶段。设计入口：

- `Docs/Architecture/EngineeringDesign.md`：模块、程序集、时序、资产制作、协作、CLI 和实施顺序。
- `Docs/Architecture/EnergyNetwork.md`：数据模型、守恒、预约、寻路、加权调度、状态机、动态操作、性能及验收矩阵。

第一阶段接口说明见 `Docs/Architecture/WhiteboxDevelopmentContract.md`，第二阶段调用与顺序见 `EngineeringDesign.md`。已实现纯 C# 模拟、固定逻辑步、整数能量、共享访问历史、按目标共享的反向 Dijkstra 路径树，以及使用已完成路径树下界的受约束 A* 搜索。第二阶段补充二叉最小堆、临时工作集合复用、局部更新中继可用状态，以及长期加权余额修复；几何拓扑变化仍会重建全图，不能称为完全局部更新。详细证据见 `Tools/Performance/README.md`。

以下直到“工具使用”段的历史第一至第五阶段记录只用于追溯；其中敌人可重叠、局部候选、绕行硬限制、旧波次和旧验证规模均已被本轮规则替代。

第一阶段的历史范围为可交互能源试验场，含建筑网格、生命周期、费用、测试负载、核心有限护盾、优先级及调试控制；当时没有敌人与波次。第二阶段已加入以下工程实现，这些具体战斗规则仍是可调整的白盒方案，不代表最终玩法平衡：

- 基础敌人按四邻域共享距离场移动，可绕开建筑；区域完全封闭时，选择可接近且靠近核心的整栋阻挡建筑拆除。导航按建筑占用版本重建，不使用 NavMesh；拆障是启发式选择，不保证最小拆除代价。
- 敌人没有物理拥挤或相互阻挡，可以重叠。四个边缘出生格禁止建造，敌人当前经过及下一步经过的格子也不能新放置或恢复建筑。
- 炮塔选择射程内最近敌人，距离相同时按稳定敌人身份排序；攻击即时命中，不检查建筑遮挡。先整笔扣能再伤害，缺能等待，不积累补射；移动保留攻击冷却。
- 五波流程包含准备、战斗、清场奖励、下一波、胜利、核心失败与重新开始。每波奖励一次；波末不自动恢复建筑或耐久。核心唯一，战斗场景不能移动或出售，核心被毁立即失败。
- 默认五波为 20/35/55/80/110 个敌人，奖励为 300/400/500/600/800 资金，均存入 `WhiteboxBattleSettings.asset`。初始布局需要后续扩建，不能把不操作自动通关作为可玩验收要求。
- 重新开始创建新世界并恢复首次进入播放时记录的手工布局；下一波使用原世界。动态建筑、能量、敌人和射线复用表现对象，敌人血条是预制几何，不创建 UI。
- 战斗 UI 在 Editor 中完整保存，显示波次、敌人数、核心耐久与护盾、结果和炮塔缺能状态。全屏背景清除摄像机先绘制底色，局部地图摄像机随后绘制，避免地图外透明文字逐帧叠画。

美术继续采用白盒。即时付费修满耐久、空初始缓存、每 1000 小单位表示 1 点能量、1 点能量抵消 1 点核心伤害仍是可编辑或可调整的白盒参数。尚未实现存档、正式美术、多地图和科技树；第三阶段已加入三种敌人类型。

## 历史第三阶段入口

- 主关卡 `Assets/Defend/Content/Scenes/Dev/TwinPassDefense.unity`，六栋初始建筑、双入口、68 岩体格、八波共 374 敌人。旧 `EnergyWhitebox`、`CombatWhitebox` 与 `CombatStress` 保留。
- `Content/Definitions/PhaseThree` 保存独立建筑、波次与能源参数；`Content/Prefabs/PhaseThree`、`PhaseThreePanel.prefab`、`PhaseThreeEnemy.prefab` 保存可编辑表现。UI 57 个 RectTransform、15 个按钮，运行时不创建或实例化 UI。
- 新炮塔为连射、重击、范围，敌人为普通、快速、重装；仍用现有缓存、整笔扣能、即时伤害、共享导航与建筑生命周期。范围炮按命中点半径结算实际伤害。炮塔和敌人的取舍与完整参数见 `Docs/Architecture/PhaseThreeGameplay.md`。
- `SupplyAssessment` 读取有向拓扑及真实中继滞留；“发电不足风险”为满速耗能估算。`CommittedIncoming`/`ReservedIncoming` 是预约，`TotalReceived`/`ReceivedLastSecond` 是实际到货，两者不混用。
- 岩体配置决定不可通行，场景几何只显示；手动改格子需同步几何和入口标记。射击与能源仍不检查地形遮挡。
- `PhaseThreeSceneBuilder.CreateOrOpen()` 只在缺失时制作，已有场景保留手工修改。`PhaseThreeContent` 管理首次配置，不是运行时关卡重建器。
- `Tools/Unity/PhaseThreeValidation.cs` 提供内容、两布局纯模拟与 Editor 表现验证；`PhaseThreeInputValidation.cs` 验证真实 Input System。`PhaseThreeAcceptancePlan` 只由验收显式调用，通过普通命令购买和维修；正常游戏没有自动建设。
- Windows 开发包 `Builds/PhaseThree/Start-Defend.cmd`。独立程序只在 `-phase-three-smoke` 参数下开启验收探针，用正常 Unity Update 推进，不直接调用 Step。最终过程、耗时、限制以第三阶段验证报告为准。
- EditMode 新增八个第三阶段行为测试，总计 77/77 通过。纯模拟两套正常购买策略分别约 843 秒/726 秒胜利；原始初始布局不扩建在第四波失败。这些数字不等于人工游玩时长或通用性能认证。
- 最终 Windows 程序用正常 Unity Update 实际运行 728.943 秒完成八波、374 击杀，能源守恒差 0、UI/世界引用保持、退出码 0。完整运行窗口隐藏，黑色截图和后台帧耗时不认证可见渲染；另以普通游戏可见窗口完成第一波短交互及暂停检查。压缩包为 `Builds/Defend-PhaseThree-Windows.zip`，详细证据和边界见 `Docs/Validation/PhaseThreeReport.md`。
- 首次可见窗口关闭时暴露对象池清理空引用，随后修复并重新构建。九个对象池均通过子物体先销毁的回归；八波运行记录早于此退出清理修复，战斗及能源参数保持一致。运行中正常回收与场景退出的对象销毁顺序必须分别验证。
- 修复构建的可见战斗及退出复查通过，退出码 0、无 Exception，证据在 `Logs/PhaseThree/PlayerVisibleFinal`。URP 后处理着色器提示与引擎关闭时内存诊断仍保留，不能宣称所有后处理可用或没有任何资源泄漏。

## 历史第四阶段入口

- 用户于 2026-10-10 确认局部感知与能源破坏方向，并授权团队开发。主关卡为 `Assets/Defend/Content/Scenes/Dev/EnergyFrontDefense.unity`；第三阶段内容保留，旧配置默认关闭新行为。
- 设计及最终评分公式在 `Docs/Architecture/PhaseFourEnemyBehavior.md`。重装独立观察最近 3 秒现场伤害和实际完成传输；两端均须可感知，不读取隐藏拓扑或预约作为收益。普通与快速沿用核心推进。
- 默认感应 8 格、评估间隔 0.5 秒、目标保持 3 秒、更换比 1.35、额外绕行最多 6 格；每步最多 20 次完整评估、2 个新战略距离场，最多 6 个候选。路径失效且预算不足时暂时继续核心推进，不因远端连续变更僵住。
- `Content/Definitions/PhaseFour` 独立保存波次与能源配置，建筑资产继续复用第三阶段。`PhaseFourPanel.prefab`、`PhaseFourEnemy.prefab`、`EnemyTargetMarker.prefab` 保存界面与表现。UI 59 个 RectTransform、15 个按钮；目标标记按建筑汇总，实际攻击身份和位置快照防止建筑移动后旧闪光跟随。
- 完整 EditMode 102/102（原 77 + 新 25）通过。两套普通购买策略在纯模拟完成八波，核心无损；均在敌人攻击建筑前将其击杀，不能用来证明能源攻防难度充分。
- 100 建筑/200 重装密集诊断平均每怪观察 53.41 栋建筑，采样 30 秒模拟时间，评估/路径预算通过、真实供电和攻击发生，逻辑步 95 分位 8.7651 毫秒。仅为纯模拟短检查，不是渲染或长期性能认证。
- Windows 开发包为 `Builds/Defend-PhaseFour-Windows.zip`。显式 `-phase-four-smoke` 用临时诊断波次正常 Update 检查能源袭击、暂停、3 次重开和 UI/表现清理，约 26.20 秒通过，退出码 0。隐藏窗口截图为黑图，实际可观察画面证据来自 Editor 完整帧；没有重启第二阶段长验收。
- 139 个受保护基线文件逐字节一致，未执行哈希值校验。最终范围、失败对照、构建警告和性能边界见 `Docs/Validation/PhaseFourReport.md`。纯模拟两套胜利、合成高压三套失败、Editor 画面及隐藏 Player 短运行必须分别解释。

## 第五阶段模型资产入口

- 2026-10-10 已按批准的工业科幻方向组织美术团队交付 6 种一格建筑、3 类机械敌人与 4 个环境模块。可编辑源文件在 `ArtSource/PhaseFive`，Unity 资产在 `Assets/Defend/Content/Art/PhaseFive`，逐个文件与预览见 `Docs/Art/PhaseFive/README.md`。
- 13 个 FBX 和 13 个规范轴向 Prefab 使用 8 种共享材质，全套 9188 三角面、24 网格、76 材质槽。原始 Blender 为 Z 向上、-Y 向前；原始 FBX 在当前导入器中为 Unity -Z 向前，制作工具转换后的 Prefab 为 +Z 向前、+Y 向上，所有节点零旋转、单位缩放。接入时使用 Prefab，不混用原始 FBX 局部轴。
- 独立场景 `Assets/Defend/Content/Art/PhaseFive/Scenes/PhaseFiveModelInspection.unity` 已保存并离屏渲染检查。已完成全部单体 Blender 保存重开、真实双角度渲染、FBX 再导入和 Unity 11 项实际枢轴动作检查；没有进入播放模式或替换第四阶段关卡，不代表实际游戏整合或性能验收。
- `Tools/Art/PhaseFive` 保存制作与检查脚本。新敌人已经按最终尺寸制作，后续要处理 `EnemyView` 种类二次缩放、整渲染器颜色覆盖及旧表现引用。完整边界见 `Docs/Validation/PhaseFiveModelReport.md`。

## 第五阶段特效资产入口

- 2026-10-10 完成独立特效资源库：25 个效果 Prefab、9 个模型扩展 Prefab、7 个共享效果材质和 1 个 URP Shader。资源在 `Assets/Defend/Content/Art/PhaseFiveEffects`，运行时/制作代码在 `Assets/Defend/Effects`，未接入第四阶段实际战斗。
- 独立场景 `PhaseFiveEffectsShowcase.unity` 提供组合/单项循环、暂停、停止、重播和近景。统一对象池以代次句柄控制复用；上限为短效果64组、碎片128个、运输128个、状态32组。射击与命中同时出现，运输必须已确认，到货独立触发。
- 实际 Play Mode 的62项生命周期检查通过；600帧短时背景采样峰值208个活跃效果根对象、784粒子，表现代码平均0.714毫秒、最大6.963毫秒。五段动态GIF与关键帧在 `Docs/Art/PhaseFiveEffects/Previews`；这些结果不是可见60帧或完整战斗验收。
- 文档入口 `Docs/Art/PhaseFiveEffects/README.md`，完整结果与保护边界见 `Docs/Validation/PhaseFiveEffectsReport.md`。原527个文件按直接字节保护，禁止哈希值校验。后续真实接入仍须处理旧 EnemyView 缩放/颜色覆盖、死亡主体去留及状态跟随。

## 第五阶段实际战斗接入

- 新主关卡为 `Assets/Defend/Content/Scenes/Dev/IndustrialDefense.unity`，复用第四阶段玩法配置，绑定六类建筑、三类机械敌人与交付特效；战斗 Prefab 在 `Assets/Defend/Content/PhaseFiveCombat`。旧关卡和美术展示场景保留。
- `CombatArtPresentation` 逐逻辑步读取射击、受击、死亡、护盾、维修与每跳能源发送/到货快照，暂停及倍速共用模拟时钟。运输严格使用真实飞行时长，预约不触发到货。新敌人禁止旧白盒二次缩放和整材质变色。
- 108/108 EditMode 通过；100 建筑/200 敌人单步集成检查覆盖三类模型、池预算与重开，不能认证持续帧率。正常 Editor 和 Windows Update 探针通过射击、受击、护盾、维修、暂停与三次重开。Windows 探针 13.515 秒，退出码 0。
- Windows 包 `Builds/Defend-PhaseFiveCombat-Windows.zip`；入口 `Builds/PhaseFiveCombat/Start-Defend.cmd`。Player 隐藏截图为黑图，画面证据来自 Editor 实际战斗帧与 URP 近景渲染；没有重跑普通八波完整游玩。
- 14 个原有接入代码或程序集文件有意修改，其余 652 个基线文件按直接字节保持。结构和限制见 `Docs/Architecture/PhaseFiveCombatIntegration.md`、`Docs/Validation/PhaseFiveCombatReport.md`。

## 工具使用

- 2026-10-10 镜头修正：新版 Input System 默认滚轮每格为 1，`WorldCameraViewport` 使用 0.22 灵敏度（每格缩小视野约 19.75%），仅 Windows 原始输入模式换算 120 单位。本机 Game 窗口已关闭低分辨率预览，150% 屏幕缩放下由 979×498 提升到 1468×748，按 1:1 显示。Play Mode 输入与画面对比已验证；未重建独立程序。详见 `Docs/Validation/CameraClarityReport.md`。
- 随后应用户要求重新构建试玩包：`Builds/Defend-Windows-Trial-20261010.zip`，342 个文件、88,461,927 字节，包含上述镜头修正。解压后运行 `Start-Defend.cmd`，默认 1920×1080 窗口。Windows 构建 0 错误，独立程序诊断波次 13.52 秒通过，含暂停及三次重开，退出码 0；不代表完整八波或可见帧率验收。记录在 `Logs/WindowsTrial20261010`，包内有使用说明与验证报告；旧包保留。

## 现役速射炮模型与动作

- 2026-10-11 按批准方案使用下载的重型火炮机器人制作游戏版本。源文件在 `ArtSource/Production/RapidTurret/RapidTurret.blend`，Unity 资源在 `Assets/Defend/Content/Art/RapidTurret`，交付入口为 `Docs/Art/RapidTurret/Delivery.html`。
- 生产版为 5900 三角面、6 网格、3 材质、9 材质槽和一张 2048×2048 颜色贴图；原模型 17731 三角面。重建八根中空炮管、俯仰耳轴及后座套筒。规范预制体采用 +Y 向上、+Z 向前，所有活动节点单位缩放、零初始旋转。
- `Content/PhaseFiveCombat/Buildings/Turret.prefab` 已替换显示模型；选择碰撞体和建筑配置保留。`PhaseFiveCombatSceneBuilder` 后续生成关卡时也优先引用新模型。原研究资源、旧模型和既有试玩包保留。
- `RapidTurretMotion` 接收战斗表现时钟，驱动平滑水平转向、下俯 6 度至上仰 18 度、12 毫米后座和 1080 度/秒持续开火自转。炮口挂在真实炮管前端，随全部关节运动；停火后自转自然减速。暂停冻结，显式单步与倍速按模拟时间推进。
- 大角度换目标使用单条待播射击快照，在水平方向对齐后才触发射击表现，转向过久会合并部分视觉射击，避免积压补播。Simulation 的命中、射速、伤害和能源结算不受这层门控影响。移动、损毁、复用和重开须清除旧待播状态。
- 验证脚本为 `Tools/Unity/RapidTurretValidation.cs`，只检查本次资源与动作。实际结果保存在 `Docs/Art/RapidTurret/Checks`；真实 Update 画面在 `Previews/UnityMotion.gif`，按采集时间间隔编码，不能作为帧率或长时间性能验收。本次没有重打 Windows 程序。

## 按四视图重建的伏羲速射炮（当前现役）

- 2026-10-12 按用户提供的正面、背面、左右侧面参考，在 Blender 5.2.2 中重新制作 `FuxiTurret`。当前现役连射炮及 `PhaseFiveCombatSceneBuilder` 均引用 `Assets/Defend/Content/Art/FuxiTurret/Prefabs/FuxiTurret.prefab`；此前 `RapidTurret`、中间版 `RapidTurretRefined`、研究文件和旧试玩包保留。
- 可编辑源文件在 `ArtSource/Production/FuxiTurret/FuxiTurret.blend`，独立四动作展示为同目录 `MotionShowcase.blend`，对照与录像入口为 `Docs/Art/FuxiTurret/Delivery.html`。四张参考已随制作源保存，参考图仅作为造型依据，没有把参考图当贴片替代几何。
- 最终 99,248 三角面、644 个闭合可编辑零件，导出为 9 个功能网格、6 材质、27 材质槽。两张共享 2048×2048 图集保存颜色和金属度/光滑度；Blender 源文件内已打包，Unity 使用同一套图集。两侧圆盘装甲、八角护框、斜裙底座、八管转子、24 孔厚壁护罩、偏置雷达与光学组件均为真实网格。
- 固定底座 → 水平转台 → 俯仰炮架 → 后座滑架 → 炮管转子依次独立。规范预制体保持 +Y 向上、+Z 向前、零初始旋转和单位缩放；沿用 `RapidTurretMotion` 的平滑跟踪、下俯 6°/上仰 18°、12 毫米后座和自转启停。伤害及能源结算仍由 Simulation 决定，本轮未改变战斗数值。
- 最终源文件保存重开、FBX 再导入和独立拓扑检查通过；644 个零件均闭合，未发现退化面、异常法线、塌缩 UV 或同向共面重叠。机械检查 18/18 通过，额外核对新增低位细节与运动件至少 8.15 毫米的竖向间隙。此结论不等于把多个零件组成的整机认定为单一实体，也不代表现实武器工程认证。
- Unity 最终定向检查 56/56 通过：资产 20、动作 14、大角度瞄准 10、实际战斗 12。工业防线真实 Update 录像、1/2/4 倍速、暂停、移动取消待播、复用和重开均有记录；退出播放后 Console 为 0 错误、0 警告。未重新构建 Windows 包，也未做大量炮塔的长时间渲染性能验收。
- 最终画面复核曾发现重复导入后的图集错位：CPU 中纹理坐标已更新，而同名规范网格保留旧显示缓冲。`FuxiTurretAssetBuilder` 更新现有 Mesh 时现先清空原缓冲，再复制数据、显式提交新纹理坐标并上传。修复后再次正常导入、播放和录制的截图已目视确认；新增逐顶点纹理坐标检查，并保留 `BeforeMeshBufferFix.png` 失败对照。Blender→FBX 的 297,744 个三角角点纹理坐标及打包图集另有独立一致性记录。
- 用户的 90% 相似度是外观目标，不是已测得的百分比。最终四视图及 `Checks/FinalVisualReview.json` 保留可见差异：中央前框、个别护罩孔形、传感器细部与表面旧化仍是重建而非逐像素复现。不得用机械或代码检查通过数代替外观相似度结论。

CLI：`C:\Users\legion\AppData\Local\Unity\bin\unity.exe`。

Editor：此前已确认位于 `C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe`；本轮通过 CLI 实际连接版本再次核对。

已实际使用现有 CLI 执行编译、EditMode 测试、资产制作、Play Mode 和脚本化输入检查。`Tools/Unity/WhiteboxValidation.cs`、`Tools/Unity/CombatValidation.cs` 位于 Assets 外，通过 `run_script` 调用，不进入玩家构建。首次制作入口为 `CombatWhiteboxSceneBuilder.CreateOrOpen()`，已有场景保留手工修改；限定背景迁移入口为 `WhiteboxBackgroundMigration.ApplyToKnownScenes()`。CLI 最外层成功可能仅表示请求成功，必须检查内层结果。测试与构建的最终状态需要读取对应状态命令。

第一阶段历史记录为 EditMode 47/47 通过；`Builds/PhaseOneWhitebox` 构建成功，两个分辨率的独立程序正常推进 200 步、守恒且 UI 数量不变，但当时后台截图为黑图，可见画面未完成验收。第一阶段高密度 400/1600 建筑出现超时和供给不足，不能删除这项历史限制。

第二阶段 Unity EditMode 已通过 69/69，Windows 开发包构建成功，最终可见画面已检查。最终版本 100 建筑、200 敌人的实际运行最后记录为 22 分 21 秒，守恒差 0；用户关闭并明确确认验收完成后，没有再启动程序或追加测试。原始报告未执行结束排序，不能用其中为 0 的耗时百分位认证帧率；完整记录见 [第二阶段验证报告](../Validation/PhaseTwoWhiteboxReport.md)。`GameSessionRoot` 提供 `Defend.Simulation.Step` Profiler 样本及显式订阅的逻辑步耗时事件；独立 .NET 加速运行的 36000 步不能代替真实 30 分钟 Player 运行，也不能证明 60 帧目标已经达到。

已安装包文档位于 `Library/PackageCache/com.unity.pipeline@62c08c808737/Documentation~`，其中 `commands/build-and-compilation.md` 描述异步状态轮询。包缓存路径可随依赖解析变化，使用前重新定位。当前无需安装另一套 Unity MCP 来开展开发。

## 工作区保护与未知事项

第一阶段开始前已经存在以下用户改动，第二阶段继续保留：

```text
 D Assets/Editor.meta
 D Assets/Editor/HubForceResolve.cs
 D Assets/Editor/HubForceResolve.cs.meta
 M Assets/TutorialInfo/Icons/URP.png
 M ProjectSettings/ProjectSettings.asset
 M ProjectSettings/QualitySettings.asset
```

本轮六阶段开发及简短实际玩法验收已完成，不重新验证历史规模。价格、阈值、敌人属性和波次均已有可调配置起点；一次正常通关不是最终平衡结论。真实触屏设备、最低硬件与发行性能尚未验证。Windows64 是当前 Editor 目标，不等于已经承诺唯一发行平台。团队全部机器的 Smart Merge、LFS 和包缓存状态尚未验证。

用户要求：禁止哈希值校验；代码和注释用规范、清晰的完整术语，不创造无通用意义的缩写。验证直接比较状态字段、预约和数量。版本管理已有 OTF/TTF 的 LFS 规则；开源中文字体连同许可和 `.meta` 交付。本轮新增或实质改写的核心单元用例总计最多 8 个，已达到上限；仅正常编译、这 8 个基础用例及必要简短玩法验收。禁止基准校验、性能跑分、压力测试、旧大规模探针及长时间自动挂机，不以旧技能或历史记录扩张验证范围。不得自行提交、推送、发布或打包。

<!-- unity-onboarding:generated:end -->
