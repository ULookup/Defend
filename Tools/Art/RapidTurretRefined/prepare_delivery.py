"""Assemble the final art review and accurately timed preview GIFs."""
from pathlib import Path
import json
import html
import shutil
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
DOCS = ROOT / 'Docs/Art/RapidTurretRefined'
PREVIEWS = DOCS / 'Previews'
CHECKS = DOCS / 'Checks'

def make_gif(paths, durations, destination):
    frames = [Image.open(path).convert('RGB') for path in paths]
    width, height = frames[0].size
    sheet = Image.new('RGB', (width * 3, height * 2))
    chosen = [min(len(frames) - 1, i * len(frames) // 6) for i in range(6)]
    for index, frame_index in enumerate(chosen):
        sheet.paste(frames[frame_index], (index % 3 * width, index // 3 * height))
    palette = sheet.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    indexed[0].save(destination, save_all=True, append_images=indexed[1:],
                    duration=durations, loop=0, optimize=False, disposal=2)
    sheet.resize((960, round(960 * sheet.height / sheet.width)), Image.Resampling.LANCZOS).save(destination.with_name(destination.stem + 'Contact.png'))
    return len(frames)

recording = json.loads((CHECKS / 'Unity/MotionFrames.json').read_text(encoding='utf-8-sig'))
entries = recording['frames']
durations = []
for index, entry in enumerate(entries):
    end = entries[index + 1]['realTime'] if index + 1 < len(entries) else recording['realDurationSeconds']
    durations.append(max(10, round((end - entry['realTime']) * 100) * 10))
make_gif([ROOT / entry['path'] for entry in entries], durations, PREVIEWS / 'UnityMotion.gif')
shutil.copyfile(CHECKS / 'Unity/CombatClose.png', PREVIEWS / 'UnityCombatClose.png')
mechanical = sorted((PREVIEWS / 'MechanicalMotionFrames').glob('frame-*.png'))
if mechanical:
    frame_numbers = [int(path.stem.split('-')[-1]) for path in mechanical]
    differences = [b-a for a,b in zip(frame_numbers, frame_numbers[1:])]
    differences.append(differences[-1] if differences else 4)
    make_gif(mechanical, [round(delta / 30 * 100) * 10 for delta in differences], PREVIEWS / 'MechanicalMotion.gif')

imported = json.loads((CHECKS / 'UnityImport.json').read_text(encoding='utf-8-sig'))
production = json.loads((CHECKS / 'ProductionReport.json').read_text(encoding='utf-8-sig'))
source_parts = production['sourceReopened']['mesh_count']
rows = []
total = 0
for filename, label in [('AssetInspection','资源绑定'), ('MotionChecks','机械动作'),
                        ('AlignmentRegression','大角度瞄准'), ('CombatChecks','实际战斗')]:
    data = json.loads((CHECKS / 'Unity' / (filename+'.json')).read_text(encoding='utf-8-sig'))
    if not data['passed']:
        raise RuntimeError('Cannot publish a passed delivery with failed ' + filename)
    total += data['checkCount']
    rows.append(f'<li><a href="Checks/Unity/{filename}.json">{label}</a>：{data["checkCount"]} 项通过</li>')

gallery = ''
for filename, label in [('FrontThreeQuarter','前侧视图'), ('RearThreeQuarter','后侧视图'),
                        ('SideMechanical','侧面机械结构'), ('Topology','真实网格边线'),
                        ('MechanicalExploded','机械分解图'), ('Gameplay70','俯视轮廓')]:
    if (PREVIEWS / (filename+'.png')).exists():
        gallery += f'<figure><a href="Previews/{filename}.png"><img src="Previews/{filename}.png" alt="{label}"></a><figcaption>{label}</figcaption></figure>'

mechanical_panel = ''
if (PREVIEWS / 'MechanicalMotion.gif').exists():
    mechanical_panel = '<figure><img src="Previews/MechanicalMotion.gif" alt="Blender 四组机械动作"><figcaption>Blender 动作演示：水平旋转 → 俯仰 → 后座与自转 → 组合动作。</figcaption></figure>'

page = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>速射炮重制 · 机械与美术交付</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#11191f;color:#dce4e7;font:16px/1.8 "Microsoft YaHei",sans-serif}
main{max-width:1180px;margin:auto;padding:46px 30px 80px}h1{font-size:42px;line-height:1.25;color:#fff;margin:12px 0 22px}h2{color:#fff;font-size:24px;margin-top:44px}
p{max-width:920px}.eyebrow{font-size:13px;letter-spacing:2px;color:#80cfd8}.muted,figcaption{color:#a0afb7}a{color:#8fd9e2}figure{margin:0}img{width:100%;display:block;border-radius:12px;background:#25313a}figcaption{font-size:13px;margin:8px 0 18px}
.metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin:26px 0}.metric{background:#202b34;padding:17px 20px;border-radius:10px}.metric b{display:block;color:#fff;font-size:28px}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}
table{border-collapse:collapse;width:100%;margin-top:18px}th,td{text-align:left;vertical-align:top;padding:12px;border-bottom:1px solid #35434c}th{color:#fff}li{margin:7px 0}code{color:#d3c4a0}
@media(max-width:740px){main{padding:26px 16px}.metrics{grid-template-columns:1fr 1fr}.grid{grid-template-columns:1fr}h1{font-size:30px}}
</style><main><div class="eyebrow">DEFEND / ART REBUILD / 2026.10.12</div>
<h1>速射炮重制<br>干净曲面，清楚的机械连接。</h1>
<p>保留八管速射炮、双侧支撑和圆形底座的识别轮廓，重新制作全部表面。装甲采用清晰分件与真实倒角；石墨结构件、钢制轴件、少量铜色和青色指示区分功能。外观不再依赖旧模型的磨损纹理。</p>
<div class="metrics"><div class="metric"><b>''' + f'{imported["triangles"]:,}' + '''</b>游戏三角面</div><div class="metric"><b>''' + str(source_parts) + '''</b>可编辑源分件</div><div class="metric"><b>''' + str(imported['renderers']) + ' / ' + str(len(imported['materials'])) + '''</b>游戏网格 / 材质</div><div class="metric"><b>4</b>独立机械运动层</div></div>
<img src="Previews/FrontThreeQuarter.png" alt="全新重制速射炮前侧视图">
<h2>结构与运动</h2><table><tr><th>结构</th><th>连接与动作</th></tr>
<tr><td>固定安装底座、旋转转台</td><td>固定地脚与底座不参与旋转；上层转台承托完整上装，以竖直轴转向。</td></tr>
<tr><td>双侧支撑、同轴耳轴</td><td>耳轴与俯仰框架同心；俯仰时炮管、滑架与驱动组件共同抬升，外侧支撑留在转台。</td></tr>
<tr><td>后座导向、滑架与套筒</td><td>后座组件沿局部炮轴滑移，导向与承托关系在整个行程保持连贯。</td></tr>
<tr><td>前后转子支承、八管组件</td><td>外侧支承承托转子；炮管、箍环与转子作为整体旋转，固定支承不会跟着自转。</td></tr>
<tr><td>实际炮口</td><td>挂点位于真实单根炮管前端，随俯仰、后座与自转移动，供游戏闪光和射线使用。</td></tr></table>
<p>游戏动作沿用已有表现时钟：平滑水平追踪，下俯 6°、上仰 18°，12 毫米后座，持续开火时炮管加速旋转，停火后减速。动作不改变模拟中的伤害、射速与能源消耗；大角度换目标的待播合并规则沿用现有实现。</p>
<h2>美术视图</h2><div class="grid">''' + gallery + '''</div>
<h2>动作与实际接入</h2><div class="grid">''' + mechanical_panel + '''<figure><img src="Previews/UnityMotion.gif" alt="Unity 实际战斗录制"><figcaption>工业防线实际 Update 录制。按测得帧间隔编码，不用作帧率评测。</figcaption></figure></div>
<h2>检查记录</h2><p>重制资源完成独立的网格审查与机械关系审查，再在现役预制体和实际战斗会话中验证。</p>
<ul><li><a href="Checks/TopologyReview.json">保存源文件与 FBX 再导入的拓扑审查</a>：各零件闭合情况、法线、退化面、重复和重叠表面、UV。</li>
<li><a href="Checks/MechanicalReview.json">机械结构审查</a>：枢轴、运动组、支承关系和指定动作范围的间隙。</li>''' + ''.join(rows) + '''</ul>
<p class="muted">检查针对游戏模型及其动作。可编辑源文件保留独立零件，游戏导出按运动归属合并；多个闭合零件组成装配体，不将整机称作一个连续实体。当前交付已接入 Unity 工程，没有重新构建独立试玩包，也没有进行密集布置的长期性能验收。</p>
<h2>打开与继续编辑</h2><ul>
<li><a href="../../../ArtSource/Production/RapidTurretRefined/RapidTurretRefined.blend">可编辑 Blender 源文件</a>：按部件命名，初始姿态无动画干扰。</li>
<li><a href="../../../ArtSource/Production/RapidTurretRefined/MotionShowcase.blend">Blender 四动作演示文件</a>：240 帧，30 帧/秒，时间线按动作分段。</li>
<li><a href="../../../Assets/Defend/Content/Art/RapidTurretRefined/Prefabs/RapidTurretRefined.prefab">Unity 规范轴向预制体</a> · <a href="../../../Assets/Defend/Content/PhaseFiveCombat/Buildings/Turret.prefab">现役速射炮预制体</a></li>
<li><a href="../../../Tools/Art/RapidTurretRefined/build_refined_turret.py">Blender 制作脚本</a> · <a href="../../../Tools/Art/RapidTurretRefined/RefinedTurretAssetBuilder.cs">Unity 导入脚本</a></li>
</ul><p class="muted">上一版源文件、研究文件和旧资源均保留。全部检查未进行哈希值校验。</p></main></html>'''
(DOCS / 'Delivery.html').write_text(page, encoding='utf-8')
summary = {'unityAssertions': total, 'triangles': imported['triangles'], 'editableParts': source_parts,
           'gameMeshes': imported['renderers'], 'materialSlots': imported['materialSlots'],
           'unityRecordingSeconds': recording['realDurationSeconds'], 'unityGifSeconds': sum(durations)/1000,
           'mechanicalPreviewFrames': len(mechanical)}
(CHECKS / 'DeliverySummary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(summary, ensure_ascii=False))
