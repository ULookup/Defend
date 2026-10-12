"""Local Blender feasibility study; does not modify any production asset."""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'Original_Inspection.blend'))
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene
source=next(o for o in scene.objects if o.type=='MESH')
bpy.context.view_layer.objects.active=source
source.select_set(True)
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
bm=bmesh.new(); bm.from_mesh(source.data); bm.verts.ensure_lookup_table()
unseen=set(bm.verts); groups={'FixedBase':set(),'TurretHousing':set(),'GunSleeve':set(),'RotatingBarrels':set()}
while unseen:
    v=unseen.pop(); stack=[v]; vs=[v]
    while stack:
        v=stack.pop()
        for e in v.link_edges:
            w=e.other_vert(v)
            if w in unseen: unseen.remove(w); stack.append(w); vs.append(w)
    lo=Vector([min(v.co[i] for v in vs) for i in range(3)])
    hi=Vector([max(v.co[i] for v in vs) for i in range(3)])
    if hi.z<.110: group='FixedBase'
    elif hi.y<-.061 and lo.z>.19 and max(abs(lo.x),abs(hi.x))<.12:
        group='RotatingBarrels' if lo.y<-.35 else 'GunSleeve'
    else: group='TurretHousing'
    groups[group].update(v.index for v in vs)
bm.free()

def parent_world(obj,parent):
    bpy.context.view_layer.update(); m=obj.matrix_world.copy(); obj.parent=parent; obj.matrix_world=m; bpy.context.view_layer.update()
def empty(name,point,parent=None):
    o=bpy.data.objects.new(name,None); scene.collection.objects.link(o); o.location=point; o.empty_display_size=.075; o.empty_display_type='ARROWS'
    if parent: parent_world(o,parent)
    return o
root=empty('RapidTurret_Research',(0,0,0))
yaw=empty('TurretYaw',(0,.075,.109),root)
pitch=empty('BarrelPitch',(0,-.060,.2975),yaw)
recoil=empty('BarrelRecoil',(0,-.060,.2975),pitch)
spin=empty('BarrelSpin',(0,-.208,.2975),recoil)
muzzle=empty('Muzzle',(0,-.502,.2975),recoil)
bindings={'FixedBase':root,'TurretHousing':yaw,'GunSleeve':recoil,'RotatingBarrels':spin}
report={'study_only':True,'original_triangles':17731,'parts':{},'pivots':{},'pitch_limits_degrees':[-6,18],'recoil_distance':.012,'shot_interval_seconds':.3,'notes':['Connected islands are selected by spatial bounds; original mesh is not mechanically authored.','Original base color texture is preserved. Added dark joint collar is a study proxy.','No production prefab, Unity scene, simulation parameter, or runtime code is modified.']}
for name,keep in groups.items():
    mesh=source.data.copy(); part=bpy.data.objects.new(name,mesh); scene.collection.objects.link(part)
    b=bmesh.new(); b.from_mesh(mesh); b.verts.ensure_lookup_table()
    bmesh.ops.delete(b,geom=[v for v in b.verts if v.index not in keep],context='VERTS'); b.to_mesh(mesh); b.free(); mesh.update()
    parent_world(part,bindings[name]); mesh.calc_loop_triangles()
    report['parts'][name]={'vertices':len(mesh.vertices),'triangles':len(mesh.loop_triangles)}
bpy.data.objects.remove(source,do_unlink=True)

# A small overlapping collar masks the original open mating surface during pitch.
mat=bpy.data.materials.new('Study_DarkSteel'); mat.diffuse_color=(.075,.09,.10,1); mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(.075,.09,.10,1); bs.inputs['Metallic'].default_value=.55; bs.inputs['Roughness'].default_value=.36
bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,radius=1,location=(0,-.06,.2975))
collar=bpy.context.object; collar.name='Study_PitchJointCollar'; collar.scale=(.082,.073,.082)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); collar.data.materials.append(mat)
for p in collar.data.polygons:p.use_smooth=True
parent_world(collar,pitch)

# Bake size and root-centering into the geometry and pivot translations.
# Base original width .901855 -> .84 m, centered on its circular plinth.
scale=.84/.90185546875
asset_objects=[o for o in scene.objects if o.type=='EMPTY' or o.type=='MESH']
for o in asset_objects:
    if o.type=='MESH':
        world=o.matrix_world.copy()
        for v in o.data.vertices: v.co=(world@v.co-Vector((0,.075,0)))*scale
        o.matrix_world.identity()
for o in asset_objects:
    if o.type=='MESH':
        parent=o.parent; o.parent=None; o.matrix_world.identity(); parent_world(o,parent)
# Recreate identity-rotation transform hierarchy with the baked geometry unchanged.
for obj,point in [(root,(0,0,0)),(yaw,(0,0,.109*scale)),(pitch,(0,-.135*scale,.2975*scale)),(recoil,(0,-.135*scale,.2975*scale)),(spin,(0,-.283*scale,.2975*scale)),(muzzle,(0,-.577*scale,.2975*scale))]:
    children=[(c,c.matrix_world.copy()) for c in obj.children]
    obj.matrix_world.translation=Vector(point); bpy.context.view_layer.update()
    for c,m in children:c.matrix_world=m
    bpy.context.view_layer.update()
for o in [yaw,pitch,recoil,spin,muzzle]:report['pivots'][o.name]={'world_position':list(o.matrix_world.translation),'parent':o.parent.name}
recoil_rest=recoil.location.copy()

scene.render.engine='CYCLES'; scene.cycles.samples=12; scene.cycles.use_denoising=True
scene.render.resolution_x=760; scene.render.resolution_y=760
scene.render.fps=30; scene.frame_start=1; scene.frame_end=240
cam=scene.camera; target=Vector((0,-.02,.26)); cam.data.ortho_scale=1.18
cam.location=target+Vector((1.15,-1.6,1.1)); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.15,.19,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5

# 0-2 s yaw; 2-4 s pitch; 4-6 s recoil; 6-8 s combined tracking/fire.
for frame in range(1,242):
    f=min(frame,241)-1
    ya=pi=travel=rotation=0.0
    if f<60: ya=48*math.sin(2*math.pi*f/60)
    elif f<120: pi=6-12*math.cos(2*math.pi*(f-60)/60)
    elif f<180:
        phase=(f-120)%9
        travel=.012*(phase if phase<=1 else max(0,(5-phase)/4))
        rotation=(f-120)*math.radians(19)
    else:
        u=f-180; ya=24*math.sin(2*math.pi*u/60); pi=7*(1-math.cos(2*math.pi*u/60))/2
        phase=u%9; travel=.012*(phase if phase<=1 else max(0,(5-phase)/4)); rotation=u*math.radians(19)
    yaw.rotation_euler.z=math.radians(ya); pitch.rotation_euler.x=-math.radians(pi)
    recoil.location=recoil_rest+Vector((0,travel,0)); spin.rotation_euler.y=rotation
    for o,path in [(yaw,'rotation_euler'),(pitch,'rotation_euler'),(recoil,'location'),(spin,'rotation_euler')]:o.keyframe_insert(data_path=path,frame=frame)
for frame,name in [(1,'01 Yaw sweep'),(61,'02 Elevation test'),(121,'03 Recoil and barrel spin'),(181,'04 Combined motion')]:scene.timeline_markers.new(name,frame=frame)
for obj in [yaw,pitch,recoil,spin]:
    if obj.animation_data and obj.animation_data.action:
        action=obj.animation_data.action
        if hasattr(action,'layers'):
            for layer in action.layers:
                for strip in layer.strips:
                    for bag in strip.channelbags:
                        for curve in bag.fcurves:
                            for key in curve.keyframe_points:key.interpolation='LINEAR'
root['Scope']='Motion feasibility study; original topology retained. Not a production-ready or Unity-validated asset.'
root['Readme']='Timeline markers: yaw, elevation, recoil, combined. Blender Z up / -Y forward. Added collar is a prototype.'
scene.frame_set(1)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=1.8; area.spaces.active.region_3d.view_location=target
            area.spaces.active.shading.type='MATERIAL'
report['scale_from_original']=scale
(ROOT/'MotionStudy.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'RapidTurret_MotionStudy.blend'))
for frame,name in [(1,'Study_Neutral'),(16,'Study_Yaw'),(91,'Study_PitchUp'),(122,'Study_Recoil')]:
    scene.frame_set(frame); scene.render.filepath=str(ROOT/'Previews'/f'{name}.png'); bpy.ops.render.render(write_still=True)
print('STUDY_SAVED',json.dumps(report,ensure_ascii=False))
