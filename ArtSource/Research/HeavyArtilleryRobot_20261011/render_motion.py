import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RapidTurret_MotionStudy.blend'))
scene=bpy.context.scene
report={'saved_blend_reopened':True,'blender_version':bpy.app.version_string,'pose_checks':[]}
names=['FixedBase','TurretHousing','GunSleeve','RotatingBarrels','Study_PitchJointCollar']
report['mesh_count']=len([o for o in scene.objects if o.type=='MESH'])
report['parts']={}
for name in names:
    o=bpy.data.objects[name]; o.data.calc_loop_triangles()
    report['parts'][name]={'triangles':len(o.data.loop_triangles),'parent':o.parent.name,'uv_layers':len(o.data.uv_layers),'scale':list(o.scale)}
report['packed_images']=[{'name':i.name,'width':i.size[0],'height':i.size[1],'packed':bool(i.packed_file)} for i in bpy.data.images if i.source=='FILE']
report['material_channels']={m.name:[{'target':link.to_socket.name,'source_type':link.from_node.type} for link in m.node_tree.links] for m in bpy.data.materials if m.use_nodes}
for frame in [1,16,46,61,91,121,122,125,196]:
    scene.frame_set(frame); bpy.context.view_layer.update()
    report['pose_checks'].append({'frame':frame,'yaw_degrees':math.degrees(bpy.data.objects['TurretYaw'].rotation_euler.z),'pitch_degrees':-math.degrees(bpy.data.objects['BarrelPitch'].rotation_euler.x),'recoil_local':list(bpy.data.objects['BarrelRecoil'].location),'muzzle_world':list(bpy.data.objects['Muzzle'].matrix_world.translation),'base_world_translation':list(bpy.data.objects['FixedBase'].matrix_world.translation)})
scene.frame_set(1)
base=bpy.data.objects['FixedBase']
basepoints=[base.matrix_world@v.co for v in base.data.vertices]
report['base_bounds']={'min':[min(v[i] for v in basepoints) for i in range(3)],'max':[max(v[i] for v in basepoints) for i in range(3)]}
points=[bpy.data.objects[name].matrix_world@v.co for name in names for v in bpy.data.objects[name].data.vertices]
report['neutral_dimensions']=[max(v[i] for v in points)-min(v[i] for v in points) for i in range(3)]
moving=[bpy.data.objects[name].matrix_world@v.co for name in names if name!='FixedBase' for v in bpy.data.objects[name].data.vertices]
report['yaw_full_rotation_radius']=max(math.hypot(v.x,v.y) for v in moving)
report['original_triangle_partition_total']=sum(report['parts'][name]['triangles'] for name in names if name!='Study_PitchJointCollar')
report['original_triangles_preserved']=report['original_triangle_partition_total']==17731
(ROOT/'MotionValidation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
scene.render.resolution_x=760;scene.render.resolution_y=760;scene.cycles.samples=12
cam=scene.camera; cammatrix=cam.matrix_world.copy(); target=Vector((0,-.02,.26))
cam.location=target+Vector((.15,-math.cos(math.radians(70)),math.sin(math.radians(70))))*2
cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ROOT/'Previews/Study_GameView.png');bpy.ops.render.render(write_still=True)
cam.matrix_world=cammatrix
scene.frame_set(61);scene.render.filepath=str(ROOT/'Previews/Study_PitchDown.png');bpy.ops.render.render(write_still=True)
scene.render.resolution_x=560;scene.render.resolution_y=560;scene.cycles.samples=8
frames=ROOT/'Frames';frames.mkdir(exist_ok=True)
for index in range(160):
    value=1+index*1.5
    scene.frame_set(int(value),subframe=value-int(value))
    scene.render.filepath=str(frames/f'{index:04d}.png');bpy.ops.render.render(write_still=True)
print('RENDER_COMPLETE 160 frames at 20 fps for 8 seconds; offline Blender preview')
