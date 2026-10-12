"""Create the separate four-motion animation from the final production source."""
from pathlib import Path
import math
import sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'Tools/Art/FuxiTurret'))
import render_review

bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/Production/FuxiTurret/FuxiTurret.blend'))
scene=render_review.setup()
render_review.set_view('FrontThreeQuarter')
scene.frame_start=1;scene.frame_end=240;scene.render.fps=30
yaw=bpy.data.objects['TurretYaw'];pitch=bpy.data.objects['BarrelPitch']
recoil=bpy.data.objects['BarrelRecoil'];spin=bpy.data.objects['BarrelSpin']
rest=recoil.location.copy()
for obj in (yaw,pitch,recoil,spin):
    obj.animation_data_clear()
for frame in range(1,241):
    step=frame-1
    traverse=elevation=travel=rotation=0
    if step<60:
        traverse=48*math.sin(math.tau*step/59)
    elif step<120:
        portion=(step-60)/59
        elevation=18*math.sin(math.pi*portion*1.5) if portion<2/3 else -6*math.sin(math.pi*(portion-2/3)*3)
    else:
        elapsed=step-120 if step<180 else step-180
        if step>=180:
            traverse=30*math.sin(math.tau*elapsed/60)
            elevation=6+8*math.sin(math.tau*elapsed/60)
        phase=elapsed%9
        travel=.012*(1-min(1,phase/4))
        rotation=elapsed*.29
    yaw.rotation_euler.z=math.radians(traverse)
    pitch.rotation_euler.x=-math.radians(elevation)
    recoil.location=rest+Vector((0,travel,0))
    spin.rotation_euler.y=-rotation
    for obj,path in ((yaw,'rotation_euler'),(pitch,'rotation_euler'),(recoil,'location'),(spin,'rotation_euler')):
        obj.keyframe_insert(data_path=path,frame=frame)
for frame,name in ((1,'01 Traverse'),(61,'02 Elevation'),(121,'03 Recoil and rotor'),(181,'04 Combined motion')):
    scene.timeline_markers.new(name,frame=frame)
scene.frame_set(1)
scene['demonstration_note']='Animation stages for inspecting mechanical motion. The game uses RapidTurretMotion and its simulation presentation clock.'
bpy.context.preferences.filepaths.save_version=0
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_location=(0,-.04,.267)
            area.spaces.active.region_3d.view_distance=1.5
            area.spaces.active.region_3d.view_rotation=scene.camera.rotation_euler.to_quaternion()
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.overlay.show_extras=False
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/Production/FuxiTurret/MotionShowcase.blend'))
print('FUXI_MOTION_SHOWCASE_SAVED',flush=True)
