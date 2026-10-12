"""Publish actual renders, reference comparisons and recorded Unity evidence."""
from pathlib import Path
import json
import html
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
DOCS = ROOT / 'Docs/Art/FuxiTurret'
PREVIEWS = DOCS / 'Previews'
CHECKS = DOCS / 'Checks'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def make_gif(paths, durations, destination):
    frames = [Image.open(path).convert('RGB') for path in paths]
    palette = frames[len(frames)//2].quantize(colors=256)
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    indexed[0].save(destination, save_all=True, append_images=indexed[1:],
                    duration=durations, loop=0, optimize=False, disposal=2)

def main():
    imported = read(CHECKS / 'UnityImport.json')
    production = read(CHECKS / 'ProductionReport.json')
    topology = read(CHECKS / 'TopologyReview.json')
    mechanics = read(CHECKS / 'MechanicalReview.json')
    if not topology['passed'] or not all(check['passed'] for check in mechanics['checks']):
        raise RuntimeError('Geometry or mechanical review has unresolved failures.')
    source_parts = production['sourceReopened']['mesh_count']
    rows = []
    total = 0
    for filename, label in [('AssetInspection','资源绑定'), ('MotionChecks','机械动作'),
                            ('AlignmentRegression','大角度瞄准'), ('CombatChecks','实际战斗')]:
        data = read(CHECKS / 'Unity' / (filename+'.json'))
        if not data['passed']:
            raise RuntimeError('Unresolved validation: ' + filename)
        total += data['checkCount']
        rows.append(f'<li><a href="Checks/Unity/{filename}.json">{label}</a>：{data["checkCount"]} 项通过</li>')

    recording = read(CHECKS / 'Unity/MotionFrames.json')
    entries = recording['frames']
    durations = []
    for index, entry in enumerate(entries):
        end = entries[index+1]['realTime'] if index+1 < len(entries) else recording['realDurationSeconds']
        durations.append(max(10, round((end-entry['realTime'])*100)*10))
    make_gif([ROOT / e['path'] for e in entries], durations, PREVIEWS / 'UnityMotion.gif')
    shutil.copyfile(CHECKS / 'Unity/CombatClose.png', PREVIEWS / 'UnityCombatClose.png')
    frames = sorted((PREVIEWS / 'MechanicalMotionFrames').glob('frame-*.png'))
    if frames:
        indices = [int(path.stem.split('-')[-1]) for path in frames]
        steps = [b-a for a,b in zip(indices,indices[1:])]
        steps.append(steps[-1] if steps else 4)
        make_gif(frames, [round(d/30*100)*10 for d in steps], PREVIEWS / 'MechanicalMotion.gif')

    comparisons = ''
    for name, label in [('Front','正面'),('Rear','背面'),('Left','左向侧面'),('Right','右向侧面')]:
        if not (PREVIEWS / (name+'.png')).exists():
            raise RuntimeError('Missing production view: '+name)
        ref = f'../../../ArtSource/Production/FuxiTurret/Reference/{name}.png'
        comparisons += f'''<section class="comparison"><h3>{label}</h3><div class="pair">
<figure><a href="{ref}"><img src="{ref}" alt="{label}用户参考"></a><figcaption>用户参考</figcaption></figure>
<figure><a href="Previews/{name}.png"><img src="Previews/{name}.png" alt="{label}模型实渲染"></a><figcaption>Blender 实际模型渲染</figcaption></figure></div></section>'''

    gallery = ''
    for name,label in [('FrontThreeQuarter','前侧整体'),('RearThreeQuarter','后侧结构'),
                       ('Topology','真实网格边线'),('Gameplay70','游戏俯视轮廓'),
                       ('MechanicalExploded','机械分件')]:
        if (PREVIEWS / (name+'.png')).exists():
            gallery += f'<figure><a href="Previews/{name}.png"><img src="Previews/{name}.png" alt="{label}"></a><figcaption>{label}</figcaption></figure>'
    motion = ''
    if (PREVIEWS / 'MechanicalMotion.gif').exists():
        motion = '<figure><img src="Previews/MechanicalMotion.gif" alt="四组机械动作"><figcaption>Blender：水平转向、俯仰、后座与炮管旋转、组合动作。</figcaption></figure>'
    page = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>伏羲速射炮 · 四视图复刻与游戏接入</title><style>
*{box-sizing:border-box}body{margin:0;background:#181a1b;color:#dedbd4;font:16px/1.85 "Microsoft YaHei",sans-serif}main{max-width:1420px;margin:auto;padding:42px 28px 80px}h1{font-size:42px;line-height:1.25;color:#f5eee2}h2{font-size:26px;color:#f5eee2;margin-top:46px}h3{font-size:18px;font-weight:500}.eyebrow{font-size:12px;letter-spacing:3px;color:#edb465}.muted,figcaption{color:#a5a29d}a{color:#edc18c}figure{margin:0}img{width:100%;display:block;background:#888b8c;border-radius:8px}figcaption{font-size:13px;margin:8px 0 20px}.pair,.gallery{display:grid;grid-template-columns:1fr 1fr;gap:18px}.comparison{margin-bottom:36px}.metrics{display:flex;gap:12px;flex-wrap:wrap;margin:24px 0}.metric{flex:1;min-width:170px;padding:16px 20px;border:1px solid #494844;border-radius:8px}.metric b{display:block;color:#fff;font-size:28px}p{max-width:1020px}table{width:100%;border-collapse:collapse}th,td{text-align:left;vertical-align:top;padding:12px;border-bottom:1px solid #41403c}li{margin:8px 0}.lead{max-width:980px}nav{display:flex;gap:24px;flex-wrap:wrap;margin-bottom:28px}nav a{text-decoration:none}button{background:#3b3b36;border:1px solid #716856;color:#ead5b3;padding:8px 14px;border-radius:5px;cursor:pointer}body.large .pair{grid-template-columns:1fr}body.large .pair figure{max-width:1200px;margin:auto}@media(max-width:700px){main{padding:25px 14px}.pair,.gallery{grid-template-columns:1fr}h1{font-size:30px}}
</style><main><div class="eyebrow">DEFEND / FUXI TURRET / 2026.10.12</div>
<h1>伏羲速射炮<br>按四视图重建，接入实际战斗。</h1>
<p class="lead">以你提供的四张图片为造型依据：大型圆盘侧甲和八角护框、分段斜裙底座、八根黑色炮管、镂空护罩、偏置雷达圆盘与球形光学组件。装甲、轴承、俯仰框架、后座滑架和转子分别建模，暖米白漆面搭配深色金属与琥珀色灯光。</p>
<nav><a href="#comparison">参考与模型对照</a><a href="#motion">机械动作</a><a href="#evidence">实际检查</a><a href="#files">源文件</a></nav>
<div class="metrics">''' + f'''<div class="metric"><b>{imported['triangles']:,}</b>游戏三角面</div><div class="metric"><b>{source_parts}</b>可编辑零件</div><div class="metric"><b>{imported['renderers']} / {len(imported['materials'])}</b>游戏网格 / 材质</div><div class="metric"><b>4</b>独立运动层</div></div>''' + '''
<h2 id="comparison">参考与模型对照</h2><p>相似度目标为 90%。这里提供相同方向的真实模型渲染供逐项对照；比例测量和检查结果不等于一个已经客观测得的相似度百分比。</p>
<button onclick="document.body.classList.toggle('large')">切换大图排列</button>''' + comparisons + '''
<h2>整体与网格</h2><div class="gallery">''' + gallery + '''</div>
<h2 id="motion">机械动作与游戏表现</h2><table><tr><th>机械分组</th><th>实际连接与运动</th></tr>
<tr><td>固定底座 → 水平转台</td><td>地脚和斜裙留在地面，上装绕竖直轴平滑跟踪目标。</td></tr>
<tr><td>侧面支撑 → 俯仰框架</td><td>横向耳轴承托炮架，炮架在侧部装甲之间俯仰，游戏范围为下俯 6°至上仰 18°。</td></tr>
<tr><td>后座滑架 → 轴向导向</td><td>开火时沿炮轴后退 12 毫米并回位，导向结构固定在俯仰框架上。</td></tr>
<tr><td>固定轴承外圈 → 炮管转子</td><td>八管和箍环共同自转，外侧护罩与支承保持不转；停火时转子逐渐减速。</td></tr></table>
<p>实际炮口随完整关节层级移动。射击表现沿用游戏的暂停和倍速时钟，大角度换目标时待对齐后播放；伤害、射速与能源结算仍由 Simulation 决定。</p>
<div class="gallery">''' + motion + '''<figure><img src="Previews/UnityMotion.gif" alt="Unity实际战斗录制"><figcaption>工业防线实际 Update 画面，按测得帧间隔编码。</figcaption></figure></div>
<h2 id="evidence">检查记录</h2><ul>
<li><a href="Checks/ReferenceProportions.json">四视图主要比例依据</a></li>
<li><a href="Checks/TopologyReview.json">独立网格检查</a></li>
<li><a href="Checks/MechanicalReview.json">独立机械关系检查</a></li>
<li><a href="Checks/FinalVisualReview.json">四视图与参考差异复核</a></li>
<li><a href="Checks/SurfaceBake.json">共享表面图集与源文件一致性</a></li>''' + ''.join(rows) + '''</ul>
<p><a href="Checks/Unity/TextureDisplayReview.json">重复导入贴图显示复查</a>：已修复旧网格显示缓冲残留，最终游戏截图和录像使用修复后的实际资源。</p>
<p class="muted">检查针对当前模型、关节动作及游戏接入。该装配体由多个闭合零件组成；不把整机表述为一个连续实体。未重新构建独立试玩包，短时画面记录不证明密集布置的长期性能。</p>
<h2 id="files">可编辑交付</h2><ul>
<li><a href="../../../ArtSource/Production/FuxiTurret/FuxiTurret.blend">Blender 制作源文件</a></li>
<li><a href="../../../ArtSource/Production/FuxiTurret/MotionShowcase.blend">Blender 四动作演示文件</a></li>
<li><a href="../../../Assets/Defend/Content/Art/FuxiTurret/Models/FuxiTurret.fbx">游戏 FBX</a> · <a href="../../../Assets/Defend/Content/Art/FuxiTurret/Prefabs/FuxiTurret.prefab">规范轴向预制体</a></li>
<li><a href="../../../Tools/Art/FuxiTurret/build_fuxi_turret.py">建模脚本</a> · <a href="../../../Tools/Art/FuxiTurret/FuxiTurretAssetBuilder.cs">Unity 导入脚本</a></li>
</ul><p class="muted">上一版源文件、研究资产与旧试玩包均保留。检查未使用哈希值。</p></main></html>'''
    (DOCS / 'Delivery.html').write_text(page,encoding='utf-8')
    summary = {'unityAssertions':total,'triangles':imported['triangles'],'editableParts':source_parts,
               'gameMeshes':imported['renderers'],'materials':len(imported['materials']),
               'unityRecordingSeconds':recording['realDurationSeconds'],
               'mechanicalPreviewFrames':len(frames),'referenceSimilarityTarget':.9,
               'numericSimilarityMeasured':False}
    (CHECKS/'DeliverySummary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(summary,ensure_ascii=False))

if __name__ == '__main__':
    main()
