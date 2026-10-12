import bpy,bmesh,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Original_Inspection.blend'))
mesh=next(o.data for o in bpy.context.scene.objects if o.type=='MESH')
b=bmesh.new(); b.from_mesh(mesh)
bmesh.ops.remove_doubles(b,verts=list(b.verts),dist=.000001)
unseen=set(b.verts); count=0
while unseen:
    stack=[unseen.pop()];count+=1
    while stack:
        v=stack.pop()
        for e in v.link_edges:
            w=e.other_vert(v)
            if w in unseen:unseen.remove(w);stack.append(w)
report={'transient_weld_diagnostic_only':True,'weld_distance':.000001,'vertices_after_weld':len(b.verts),'components_after_weld':count,'boundary_edges_after_weld':sum(e.is_boundary for e in b.edges),'non_manifold_edges_after_weld':sum(not e.is_manifold for e in b.edges)}
b.free()
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RapidTurret_MotionStudy.blend'))
scene=bpy.context.scene;scene.frame_set(1)
pitch=bpy.data.objects['BarrelPitch'];recoil=bpy.data.objects['BarrelRecoil'];yaw=bpy.data.objects['TurretYaw']
for o in [pitch,recoil,yaw,bpy.data.objects['BarrelSpin']]:o.animation_data_clear()
scene.render.resolution_x=760;scene.render.resolution_y=760;scene.cycles.samples=12
cam=scene.camera;target=Vector((0,-.02,.26));cam.location=target+Vector((2.5,0,.12));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
for degrees,name in [(-6,'Extreme_DownAndRecoil'),(18,'Extreme_UpAndRecoil')]:
    pitch.rotation_euler.x=-math.radians(degrees);recoil.location.y=.012;bpy.context.view_layer.update()
    scene.render.filepath=str(ROOT/'Previews'/f'{name}.png');bpy.ops.render.render(write_still=True)
report['extreme_pose_renders']=[{'elevation_degrees':d,'recoil_meters':.012,'view':'right side'} for d in [-6,18]]
(ROOT/'ExtremePoseReview.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('EXTREMES',json.dumps(report))
