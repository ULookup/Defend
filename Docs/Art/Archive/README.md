# 垣城档案

从开始页「垣城档案」打开。按提供的三张参考图制作：左上标题与返回箭头，横向分类栏，左侧金属条目列表，右侧大阅读屏或大机械立绘，底部青色说明与属性面板。弹窗外点击不关闭、不穿透；仅左上返回箭头回到开始页，底部重复出口已隐藏并移出导航。

包含背景故事全文、7 种防御工事、5 种守城者及 12 张独立透明立绘。长故事、条目列表、底部完整资料均支持滚动；切换分类或条目重置正文滚动位置。选中分类展开名称，其余分类保留图标。方向键在弹窗内导航，聚焦到列表外条目时自动滚动。

## 配置与制作

- 文案与展示数值：`Assets/Defend/Content/Archive/ArchiveCatalog.json`。所有定稿正文原样保存，展示直接读取配置，不调用当前战斗定义生成文案。
- 预制体：`Assets/Defend/Content/Prefabs/UI/DefenseInterface.prefab`。
- 运行组件：`DefenseArchiveView`。只绑定预先制作的控件，不在运行时创建 UI。
- Unity 菜单「不眠垣城 / 应用垣城档案美术」可重复应用并保存档案页，不重建其他页面或玩法场景。
- `Generation.json` 保存生成立绘与图集的提示词。立绘是本次生成的概念美术，可按对应名称替换正式美术。

## 资源约定

`Assets/Defend/Content/Art/Archive/Illustrations` 包含用户命名的 7 张工事与 5 张守城者 PNG。镇载者保留原命名 `UI_Art_Archive_Enemy__Subjugator01`。

`Textures/UI_Archive_Atlas_01.png` 按已检查的真实透明边界切出分类图标、属性图标、属性槽与三种属性填充。切片矩形记录在 `ArchivePageArtAuthoring.ImportArt`。

`Sprites` 中的命名 Sprite 复用开始页、设置页金属材质和纹理，不重复修改原资源。

| Sprite | 九宫格边距（左 / 下 / 右 / 上，源像素） |
| --- | --- |
| UI_Panel_Archive_Main_01 | 38 / 38 / 38 / 38 |
| UI_Panel_Archive_Story_01 | 38 / 28 / 38 / 28 |
| UI_Panel_Archive_Readout_01（图集备用） | 34 / 34 / 34 / 34 |

当前阅读屏用原生 UI 材质 `ArchiveReadout` 生成浅青色渐变与细扫描线，金属边框采用主窗口九宫格。背景复用设置页的模糊背景与材质。装饰图形不接收点击；背景遮罩、滚动区和按钮接收点击。

## 内容与当前玩法的差异

本次按策划定稿展示数值与行为。仓库当前战斗模拟的平衡参数、敌人选目标逻辑、能源路由规则与这些资料存在差异；本次没有改动战斗参数。后续若要让实际玩法与定稿一致，应单独调整模拟和配置，并执行战斗验证。

## 验证

`Tools/Unity/ArchivePageValidation.cs` 在播放模式检查真实点击命中、全部 13 条正文、12 张立绘、属性值、选中高亮、长故事滚动到底、正文重置、弹窗输入拦截、唯一返回入口及设置页入口。检查后保存 `Validation.json` 和 `Previews` 下 1920×1080、1280×800 的实际 Game View 图像。旧两出口报告与截图为历史记录；最新单一出口与主菜单反馈修复见 `Docs/AI/MenuReturnFix.md`。
