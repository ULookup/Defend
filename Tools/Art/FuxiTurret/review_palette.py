"""Temporary, unsaved surface color calibration against the supplied reference."""
from pathlib import Path
import sys
import bpy
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'Tools/Art/FuxiTurret'))
import render_review
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Production/FuxiTurret/FuxiTurret.blend'))
scene=render_review.setup()
scene.cycles.samples=16
scene.render.resolution_percentage=50
render_review.set_view('Front')
output=ROOT/'Docs/Art/FuxiTurret/Checks/SurfaceCalibration'
output.mkdir(parents=True,exist_ok=True)
mat=bpy.data.materials['Fuxi_Armor']
for name,color in [('WarmIvory',(.68,.54,.40,1)),('SoftIvory',(.68,.58,.46,1))]:
    mat.node_tree.nodes['SurfaceBaseColor'].inputs[1].default_value=color
    scene.render.filepath=str(output/(name+'.png'))
    bpy.ops.render.render(write_still=True)
    print('PALETTE_REVIEW_READY',name,flush=True)
