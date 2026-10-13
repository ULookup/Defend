# 不眠垣城 · 设置页面

按照用户提供的设置参考图、功能表和资源命名表制作。设置页沿用正式界面预制体，并保留开始页和局内暂停入口。

## 已完成行为

- 背景音乐第一行、游戏音效第二行。滑杆范围 0–100，旁边显示当前数值，声音立即变化。
- 两路静音互相独立，图标切换为红斜杠。静音不改变音量或滑杆位置；拖动哪一路只解除哪一路静音。
- 首次没有设置存档时默认两路 70、静音关闭。已有音量保留；原来的总静音迁移为两路静音。
- 偏好保存到项目既有 PlayerPrefs。音量和静音写入内存后，停止操作 0.35 秒写盘；关闭页面、应用暂停和退出会立即写盘。连续拖动不会每帧写盘。
- 仅右上角 × 退出，底部重复出口已隐藏并移出导航。点击面板外不会关闭，也不会触发背后的开始按钮。
- 从开始页进入后返回开始页，从暂停进入后返回暂停。键盘焦点进入弹窗，控件上下导航保持在弹窗内。

## 接入位置

- 正式入口：`Assets/Defend/Content/Scenes/Dev/IndustrialDefense.unity`。
- 已保存界面：`Assets/Defend/Content/Prefabs/UI/DefenseInterface.prefab`。
- `DefenseAudio` 拥有音量、静音和持久化；`DefenseSettingsView` 只绑定现有控件和更新显示。
- `DefenseInterface.Initialize` 连接设置视图，保留没有新视图的旧预制体兼容路径。
- 编辑器「不眠垣城 / 应用设置页美术」可重复更新。通用 `DefenseInterfaceAuthoring` 制作工具也保留设置页美术。
- 全部控件预先保存，运行时不创建 UI。未新增包，未改玩法场景或根 CanvasScaler。

## 美术交付

目录：`Assets/Defend/Content/Art/Settings/`。

`Textures/UI_Settings_Atlas_01.png` 为透明 1254×1254 图集，包含命名表中全部 16 个 Sprite 子资源：主面板、分隔线、单个镜像角饰、顶部关闭 Normal/Pressed、Music/SFX 图标、Track/Fill、Handle Normal/Pressed、Mute Normal/Pressed/Off、底部关闭 Normal/Pressed。

九宫格边距（像素，左／下／右／上）：主面板 38/38/38/38；轨道及填充条 42/16/42/16；底部按钮 38/28/38/28。其余子资源不拉伸边角。
角饰由一个 Sprite 在预制体中镜像四份。文字是项目中文字体的可编辑 Text，未烘焙进图片。

图集由 Codex 内置 `image_gen.imagegen` 按用户参考图生成，完整提示词见 `Prompts.txt`。
`Textures/BG_Settings_Backdrop_01.png` 复制自已完成开始页的浮城背景，独立开启 mipmap，在 `SettingsBackdrop.shader` 中通过三线性采样显示柔焦和暗色调，不改变开始页纹理。
`SettingsBackdrop.mat` 已保存并绑定。模糊背景采用同一垣城插画，暂停入口仍返回原暂停状态。

## 验证

Unity 6000.6.5f1，正式工业场景中运行，报告见 `Checks/Review.json`、`Checks/Console.json`。
验证首次默认值、两路实际 AudioSource 的音量与静音、文字几何、真实点击区域、拖动事件、独立静音、红斜杠、0/100端点、偏好重新读取、旧偏好迁移、外部点击、暂停返回。验证后恢复用户全部原偏好。原两出口截图与报告保留为历史记录；最新单一出口与主菜单反馈修复见 `Docs/AI/MenuReturnFix.md`。

原生 Game View 固定分辨率预览：`Previews/Settings-1920x1080.png`、`Previews/Settings-1280x800.png`。捕获后恢复原 Game View 选项。
没有执行独立游戏构建、全套历史测试或压力测试。

PlayerPrefs 键：`Defend.MusicVolume`、`Defend.EffectsVolume`、`Defend.MusicMuted`、`Defend.EffectsMuted`、`Defend.AudioSettingsVersion`（2）。音量内部仍为 0–1，UI 展示 0–100；`Defend.Muted` 仅作旧版本迁移输入。
