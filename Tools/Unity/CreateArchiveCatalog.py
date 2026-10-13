import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
story = (root/'Assets/Defend/Content/EmergentDefense/ArchiveStory.txt').read_text(encoding='utf-8-sig').strip()
replacements = {
'比较全图建筑的破坏价值与接近代价,选定目标后前去拆解。':'锁定最近的非城结构,走过去拆。',
'优先锁定反应堆,比较绕行与拆障的代价,寻找合适的接近路线。':'优先锁定反应堆,绕过一切挡路的建筑,径直扑上去。',
'它们会比较目标的价值与接近代价,但它们太多,多到能逼你的每一座炮塔都为它们开火。':'它们不挑目标,只挑最近的,但它们太多,多到能逼你的每一座炮塔都为它们开火。',
'它不挑选目标,始终以防线核心为目标,沿较低风险的路线推进,清除必要的障碍。':'它不挑选目标,只朝防线核心直线前进,清除挡路的一切。',
'下游节点在耗尽已有储能后陆续暗下去。':'整片节点一盏一盏地暗下去。',
}
for old,new in replacements.items():
    assert story.count(old)==1, old
    story=story.replace(old,new)

builds = [
('Core','防线核心','占位：3X3','基地的保护目标','垣城留下的最后一座枢纽。它不生产能量,只把送来的能量化作护盾;护盾破碎之前,伤害不会触及它的生命。','接收能量补充护盾,不主动消耗;被攻击时优先扣护盾。','耐久 600 / 护盾上限 160'),
('Reactor','反应堆','占位：2X2','持续产能','仍在按旧指令运转的产能装置。它每隔一段时间凝出一粒能量,向三格内可接收的节点轮流送出。','周围没有可接收的节点时,它会停下等待,不会积攒。','耐久 180 / 储能 64 / 产能 8 点每 0.55 秒'),
('Relay','中继器','占位1X1','轮流转发','最不起眼的部件,也是整张网络的关节。它不判断能量该去哪里,只把收到的能量依次分给所有能接收的邻居。','轮流转发,不复制、不寻路;同一粒能量走过的节点不会回头;自身储能满了就拒收,让上游等待。','耐久 90 / 储能 48 / 感知 3 格'),
('Battery','电池','占位：2X1','缓冲储能','只在身边有节点缺能时放电的蓄能柜。它看不见全局,只看得到感知范围内的事——所以它能救急,也可能在错误的位置白白蓄满。','接收并储存附近送来的能量,按局部缺能情况决定是否释放,不需要玩家手动控制。','耐久 150 / 储能 120'),
('Turret','闪哨机','占位2X1','快速单体','旧城的信号塔,如今打出去的是光。它自动锁定射程内最近的守城者,每发只耗很少的能量,射速很快。','储能不足时停火,等待网络补充。','耐久 240 / 储能 72 / 攻击 16 / 射程 7.5 格 / 耗能 3 每发'),
('Crusher','崩岩机','占位3X2','慢速重击','灾后用来敲碎坍塌路段的重锤,如今用来对付重甲。它每开一炮都要先喝下大量能量。','单发耗能极高,需要一条稳定、低损耗的供电线才回得起枪;供能跟不上就会长时间停火。','耐久 300 / 储能 120 / 攻击 120 / 射程 6 格 / 耗能 20 每发'),
('Shaker','荡尘机','占位：3X1','范围溅射','广场除尘的旧机组,一吹就能清走一片积灰。它一次攻击会波及目标周围的一小片区域。','对成群的敌人最划算,对单个硬目标则很浪费——所以供能要算性价比。','耐久 260 / 储能 96 / 攻击 18 / 溅射 2.5 格 / 射程 5.5 格 / 耗能 10 每发'),
]
enemies = [
('Raptor','巡猎者','四足清扫单元','旧城留下的清障装置。它按最省力的路线走向最近的可到达建筑,到达后开始拆解。它不憎恨你,只是在执行几百年前的最后一道命令。','耐久 48 / 速度 0.85 / 攻击 12 / 目标偏好 最近建筑'),
('Breaker','拆迁者','双足工程单元','比巡猎者更专注的拆解者。它优先寻找中继器——城市最初造它们,是为了切断纠缠的线路,如今它切断的是你的。','耐久 75 / 速度 0.95 / 攻击 19 / 目标偏好 中继器'),
('Converger','收束者','履带回收单元','厚重的履带机体,身前挂着回收斗。它绕过一切挡路的建筑,径直扑向反应堆。它一倒下,整条供能线都会跟着缺电。','耐久 140 / 速度 0.55 / 攻击 25 / 目标偏好 反应堆'),
('Cleaner','拭净者','小型清扫虫','只有巴掌大的清扫虫,成群从崖缝里涌出。它们很难造成威胁,却会逼你的炮塔为每一只都开一枪——真正被消耗的,是你的储能。','耐久 12 / 速度 1.3 / 攻击 4 / 目标偏好 最近建筑'),
('Subjugator','镇载者','重型碾压单元','只会朝核心直线前进的重型机械。它不挑选目标,只清除挡路的建筑。想拦住它,唯一的办法是用布局把它困在火力网里。','耐久 400 / 速度 0.35 / 攻击 40 / 目标偏好 防线核心'),
]
maxima={'耐久':600,'护盾上限':160,'储能':160,'产能':8,'感知':3,'攻击':120,'射程':7.5,'耗能':20,'溅射':2.5,'速度':1.3}
def stats(raw,category):
    result=[]
    for part in raw.split(' / '):
        label,value=part.split(' ',1)
        numeric=0 if label=='目标偏好' else float(value.split(' ')[0])
        maximum = {'耐久':400,'攻击':40}.get(label,maxima.get(label,1)) if category==2 else maxima.get(label,1)
        icon={'耐久':'HP','护盾上限':'HP','速度':'Speed','攻击':'Atk'}.get(label,'Target')
        result.append(dict(label=label,value=value,ratio=min(1,numeric/maximum),icon=icon))
    return result
entries=[dict(id='Story',category=0,title='垣城往事',subtitle='背景故事',body=story,art='',stats=[])]
for ident,title,space,sub,description,behavior,raw in builds:
    entries.append(dict(id=ident,category=1,title=title,subtitle=sub,body=f'{space}\n\n- 副标:{sub}\n- 简介:{description}\n- 能量行为:{behavior}\n- 属性:{raw}',art=f'UI_Art_Archive_Build_{ident}_01',stats=stats(raw,1)))
for ident,title,sub,description,raw in enemies:
    art='UI_Art_Archive_Enemy__Subjugator01' if ident=='Subjugator' else f'UI_Art_Archive_Enemy_{ident}_01'
    entries.append(dict(id=ident,category=2,title=title,subtitle=sub,body=f'- 副标:{sub}\n- 简介:{description}\n- 属性:{raw}',art=art,stats=stats(raw,2)))
out=root/'Assets/Defend/Content/Archive/ArchiveCatalog.json'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(dict(categories=['背景故事','防御工事','守城者'],entries=entries),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
assert len(entries)==13
print('Saved complete archive catalog: 1 story + 7 buildings + 5 enemies')
