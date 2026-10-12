"""Compare studio lighting on the actual saved model without changing it."""
import sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
import render_review

root=Path(__file__).resolve().parents[3]
bpy.ops.wm.open_mainfile(filepath=str(root/'ArtSource/Production/FuxiTurret/FuxiTurret.blend'))
scene=render_review.setup()
scene.render.resolution_percentage=50
scene.cycles.samples=16
render_review.set_view('FrontThreeQuarter')
folder=root/'Docs/Art/FuxiTurret/Checks/LightingCalibration'
folder.mkdir(parents=True,exist_ok=True)
for name,background,fill in [('DirectionalSoftbox',.12,180),('BalancedSoftbox',.26,340)]:
    bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    bg.inputs[0].default_value=(background,background,background,1)
    bg.inputs[1].default_value=1
    bpy.data.objects['StudioKey'].data.energy=1000
    bpy.data.objects['StudioFill'].data.energy=fill
    bpy.data.objects['StudioRim'].data.energy=700
    scene.render.filepath=str(folder/(name+'.png'))
    bpy.ops.render.render(write_still=True)
    print('FUXI_LIGHTING_CALIBRATION',name,flush=True)
