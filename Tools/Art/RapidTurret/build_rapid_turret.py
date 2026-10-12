"""Produce the approved rapid-fire turret in Blender 5.2 from the preserved study.

Run: blender --background --python Tools/Art/RapidTurret/build_rapid_turret.py
Only the production source, intentional FBX/texture exports, and art evidence are
written. The original user archive and feasibility studies remain untouched.
"""
import bpy
import bmesh
import math
import json
import sys
from pathlib import Path
from mathutils import Vector
import numpy as np

PROJECT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(PROJECT / 'Tools/Art/PhaseFive'))
import common as art

SOURCE = PROJECT / 'ArtSource/Research/HeavyArtilleryRobot_20261011/RapidTurret_MotionStudy.blend'
OUTPUT = PROJECT / 'ArtSource/Production/RapidTurret/RapidTurret.blend'
FBX = PROJECT / 'Assets/Defend/Content/Art/RapidTurret/Models/RapidTurret.fbx'
TEXTURE = PROJECT / 'Assets/Defend/Content/Art/RapidTurret/Textures/RapidTurret_BaseColor.png'
PREVIEWS = PROJECT / 'Docs/Art/RapidTurret/Previews'
CHECKS = PROJECT / 'Docs/Art/RapidTurret/Checks'
PITCH_POINT = Vector((0, -0.1257407665, 0.2770954072))


def material(name, color, metallic, roughness, emission=0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = color
    mat.metallic = metallic
    mat.roughness = roughness
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = color
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Emission Color'].default_value = color
    shader.inputs['Emission Strength'].default_value = emission
    return mat


def build_texture(source_mat):
    original = next(n.image for n in source_mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image)
    image = original.copy()
    image.name = 'RapidTurret_BaseColor'
    image.scale(2048, 2048)
    pixels = np.empty(2048*2048*4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    rgba = pixels.reshape(-1, 4)
    # Retain original authored UV islands and panel variation, reduce fine wear
    # contrast, and move the warm near-white surfaces to a cooler grey-white.
    luminance = rgba[:, :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    desaturated = rgba[:, :3] * .25 + luminance[:, None] * .75
    rgba[:, :3] = np.clip((desaturated*.87+.055)*np.array([.76,.81,.85]), 0, 1)
    rgba[:, 3] = 1
    image.pixels.foreach_set(pixels)
    image.filepath_raw = str(TEXTURE)
    image.file_format = 'PNG'
    image.save()
    image.pack()
    return image


def optimize(obj, target):
    art.activate(obj)
    # Merge duplicate geometric points while UVs remain per-face-corner data.
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.000025)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=0.000002)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.calc_loop_triangles()
    before = len(obj.data.loop_triangles)
    if before > target:
        mod = obj.modifiers.new('GameMeshReduction', 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = target / before
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.calc_loop_triangles()
    return {'before_reduction':before, 'after_reduction':len(obj.data.loop_triangles)}


def ring(name, center, outer, inner, depth, mat, parent, vertices=20):
    # Tube along the forward axis. The inner wall and end annuli are geometry.
    cx, cy, cz = center
    verts = []
    for y, radius in [(-depth/2,outer),(depth/2,outer),(-depth/2,inner),(depth/2,inner)]:
        for i in range(vertices):
            angle = i*math.tau/vertices
            verts.append((cx+math.cos(angle)*radius,cy+y,cz+math.sin(angle)*radius))
    faces=[]
    for i in range(vertices):
        j=(i+1)%vertices
        faces.extend([(i,j,vertices+j,vertices+i),
                      (2*vertices+i,3*vertices+i,3*vertices+j,2*vertices+j),
                      (i,2*vertices+i,2*vertices+j,j),
                      (vertices+i,vertices+j,3*vertices+j,3*vertices+i)])
    obj=art.mesh_object(name, verts, faces, mat, parent)
    for index,face in enumerate(obj.data.polygons):
        face.use_smooth=index%4 in (0,1)
    return obj


def join_parts(objects, name, parent):
    obj=art.join_meshes(objects, name, parent, tuple(parent.matrix_world.translation))
    # The mechanical meshes use constant materials, but still receive valid UVs
    # for downstream lightmap/editor tools rather than an absent UV channel.
    if not obj.data.uv_layers:
        art.activate(obj)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.025)
        bpy.ops.object.mode_set(mode='OBJECT')
    return obj


def bind_muzzle_to_barrel():
    muzzle=bpy.data.objects['Muzzle']
    spin=bpy.data.objects['BarrelSpin']
    art.parent_world(muzzle,spin)
    muzzle.matrix_world.translation=Vector((0,-.5374253392,PITCH_POINT.z+.039))
    bpy.context.view_layer.update()
    muzzle['purpose']='Front of the upper physical barrel; follows the rotating barrel group.'


def refresh_muzzle_export():
    """Update an invisible firing attachment without rerendering unchanged art."""
    bpy.ops.wm.open_mainfile(filepath=str(OUTPUT))
    bpy.context.preferences.filepaths.save_version=0
    bind_muzzle_to_barrel()
    before=art.inspect_objects(art.descendants(bpy.data.objects['RapidTurret']))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT))
    bpy.ops.wm.open_mainfile(filepath=str(OUTPUT))
    root=bpy.data.objects['RapidTurret']
    reopened=art.inspect_objects(art.descendants(root))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in art.descendants(root):obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(FBX),use_selection=True,
        object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',
        global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        use_space_transform=True,bake_space_transform=False,use_mesh_modifiers=True,
        mesh_smooth_type='FACE',use_triangles=True,add_leaf_bones=False,bake_anim=False,
        path_mode='RELATIVE',use_custom_props=True)
    art.reset_scene()
    bpy.ops.import_scene.fbx(filepath=str(FBX),use_anim=False)
    exported=art.inspect_objects(list(bpy.context.scene.objects))
    report=json.loads((CHECKS/'RapidTurret.json').read_text(encoding='utf-8'))
    report['source_reopened']=reopened
    report['fbx_reimported']=exported
    delta=max(abs(reopened[k][i]-exported[k][i]) for k in ['bounds_min','bounds_max'] for i in range(3))
    report['checks'].update({
        'source_reopened':True,'fbx_reimported':True,
        'save_reopen_dimensions_equal':before['dimensions']==reopened['dimensions'],
        'export_bounds_maximum_difference':delta,'export_bounds_match':delta<.0001,
        'triangles_match':reopened['triangles']==exported['triangles'],
        'mesh_count_match':reopened['mesh_count']==exported['mesh_count'],
        'material_names_match':reopened['material_names']==exported['material_names'],
        'muzzle_follows_barrel_spin':bpy.data.objects['Muzzle'].parent.name=='BarrelSpin',
    })
    (CHECKS/'RapidTurret.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('MUZZLE_UPDATED',next(p for p in exported['pivots'] if p['name']=='Muzzle'),flush=True)


def build():
    for p in [OUTPUT.parent, FBX.parent, TEXTURE.parent, PREVIEWS, CHECKS]:
        p.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene
    scene.frame_set(1)
    keep={'RapidTurret_Research','TurretYaw','BarrelPitch','BarrelRecoil','BarrelSpin','Muzzle',
          'FixedBase','TurretHousing','GunSleeve','RotatingBarrels'}
    for obj in list(scene.objects):
        if obj.name not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)
    for obj in scene.objects:
        obj.animation_data_clear()
    root=bpy.data.objects['RapidTurret_Research']
    root.name='RapidTurret'
    for key in list(root.keys()):
        del root[key]
    yaw=bpy.data.objects['TurretYaw']; pitch=bpy.data.objects['BarrelPitch']
    recoil=bpy.data.objects['BarrelRecoil']; spin=bpy.data.objects['BarrelSpin']
    housing=bpy.data.objects['TurretHousing']; base=bpy.data.objects['FixedBase']
    sleeve=bpy.data.objects['GunSleeve']; barrels=bpy.data.objects['RotatingBarrels']
    image=build_texture(housing.data.materials[0])
    armor=material('RapidTurret_Armor',(.62,.67,.7,1),.25,.58)
    tex=armor.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
    armor.node_tree.links.new(tex.outputs['Color'],armor.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    dark=material('RapidTurret_DarkMetal',(.038,.055,.071,1),.72,.38)
    cyan=material('RapidTurret_EnergyCyan',(.012,.54,.68,1),.15,.3,.8)
    reduction={}
    for obj,target in [(base,800),(housing,2800),(sleeve,390),(barrels,650)]:
        reduction[obj.name]=optimize(obj,target)
        obj.data.materials.clear()
        obj.data.materials.append(dark if obj==barrels else armor)
        for face in obj.data.polygons:
            face.material_index=0
    art.parent_world(sleeve,pitch)
    # The source muzzle recesses are mostly baked shading and become ambiguous
    # under a darker material. Rebuild the eight-barrel rotor as hollow tubes,
    # retaining its radius/length and the original eight-tube circular layout.
    bpy.data.objects.remove(barrels,do_unlink=True)
    rotor_parts=[]
    for i in range(8):
        angle=i*math.tau/8
        x=math.sin(angle)*.039
        z=PITCH_POINT.z+math.cos(angle)*.039
        rotor_parts.append(ring('HollowBarrel',(x,-.407,z),.0125,.0088,.256,dark,spin,vertices=8))
    rotor_parts.append(art.cylinder('RotorCenter',(0,-.411,PITCH_POINT.z),.011,.246,dark,
        vertices=12,rotation=(math.pi/2,0,0),parent=spin))
    for y in [-.517,-.478]:
        rotor_parts.append(ring('RotorBrace',(0,y,PITCH_POINT.z),.057,.048,.011,dark,spin,vertices=20))
    rotor_parts.append(art.cylinder('RotorDrive',(0,-.281,PITCH_POINT.z),.053,.024,dark,
        vertices=20,rotation=(math.pi/2,0,0),parent=spin))
    barrels=join_parts(rotor_parts,'RotatingBarrels',spin)
    bind_muzzle_to_barrel()
    # Darken the original exposed turntable surface; this remains one mesh with
    # two material slots, preserving its existing outline and attachment feet.
    base.data.materials.append(dark)
    for face in base.data.polygons:
        world=base.matrix_world@face.center
        if world.z>.075 and face.normal.z>.3:
            face.material_index=1

    # A cylindrical cross-axis trunnion, recessed receiver and concentric sleeve
    # replace the visible spherical feasibility proxy. Their overlaps cover the
    # moving interface at both requested pitch limits and maximum recoil.
    yaw_parts=[housing]
    yaw_parts.append(art.box('ReceiverBackplate',(0,-.094,.278),(.150,.065,.134),dark,.006,yaw))
    for side in [-1,1]:
        yaw_parts.append(art.cylinder('TrunnionBearing', (side*.086,PITCH_POINT.y,PITCH_POINT.z),
            .063,.022,dark,vertices=20,rotation=(0,math.pi/2,0),parent=yaw))
    housing=join_parts(yaw_parts,'TurretHousing',yaw)
    pitch_parts=[sleeve]
    pitch_parts.append(art.cylinder('PitchAxle',tuple(PITCH_POINT),.051,.182,dark,
        vertices=20,rotation=(0,math.pi/2,0),parent=pitch))
    pitch_parts.append(ring('PitchSleeveLiner',(0,-.196,PITCH_POINT.z),.063,.045,.119,dark,pitch))
    for side in [-1,1]:
        pitch_parts.append(art.cylinder('TrunnionEndcap',(side*.097,PITCH_POINT.y,PITCH_POINT.z),
            .027,.010,dark,vertices=16,rotation=(0,math.pi/2,0),parent=pitch))
    sleeve=join_parts(pitch_parts,'PitchHousing',pitch)
    slider=ring('RecoilSlideTube',(0,-.255,PITCH_POINT.z),.044,.032,.094,dark,recoil,vertices=20)
    slider_ring=ring('RecoilStopRing',(0,-.267,PITCH_POINT.z),.050,.031,.012,dark,recoil,vertices=20)
    join_parts([slider,slider_ring],'RecoilCarriage',recoil)
    # Small controlled cyan signals support recognition at the gameplay camera.
    signals=[]
    for side in [-1,1]:
        signals.append(art.box('StatusLamp',(side*.196,-.186,.361),(.026,.009,.016),cyan,.002,yaw))
    join_parts(signals,'StatusLamps',yaw)
    root['asset_name']='RapidTurret'
    root['unit_meters']=1.0
    root['forward_axis']='-Y'
    root['root_pivot']='bottom_center'
    root['pitch_min_degrees']=-6.0
    root['pitch_max_degrees']=18.0
    root['recoil_distance_meters']=.012
    root['motion_notes']='Runtime drives empty transforms; no baked production animation.'
    root['source']='Approved heavy artillery robot; original archive retained in research.'
    art.empty('ForwardReference',(0,-.5,0),root)
    before=art.inspect_objects(art.descendants(root))
    camera=art.studio(root,before)
    scene.cycles.samples=24
    scene.render.resolution_x=1000;scene.render.resolution_y=1000
    art.set_camera(camera,before,36)
    camera.data.ortho_scale=1.18
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.15,.19,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
    scene.frame_start=1;scene.frame_end=1
    scene.timeline_markers.clear()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT))
    bpy.ops.wm.open_mainfile(filepath=str(OUTPUT))
    root=bpy.data.objects['RapidTurret'];pitch=bpy.data.objects['BarrelPitch']
    recoil=bpy.data.objects['BarrelRecoil'];spin=bpy.data.objects['BarrelSpin']
    reopened=art.inspect_objects(art.descendants(root))
    rest=recoil.location.copy()
    for name,elevation,pitch_degrees,recoil_distance,barrel_rotation in [
        ('RapidTurret_CloseView',36,0,0,0),
        ('RapidTurret_GameView',70,0,0,0),
        ('RapidTurret_ExtremeUp',22,18,.012,27),
        ('RapidTurret_ExtremeDown',22,-6,.012,0),
    ]:
        art.set_camera(bpy.context.scene.camera,reopened,elevation)
        bpy.context.scene.camera.data.ortho_scale=1.18
        pitch.rotation_euler.x=-math.radians(pitch_degrees)
        recoil.location=rest+Vector((0,recoil_distance,0))
        spin.rotation_euler.y=math.radians(barrel_rotation)
        scene=bpy.context.scene
        scene.render.filepath=str(PREVIEWS/(name+'.png'))
        bpy.ops.render.render(write_still=True)
    pitch.rotation_euler.x=0;recoil.location=rest;spin.rotation_euler.y=0
    bpy.context.view_layer.update()
    art.set_camera(bpy.context.scene.camera,reopened,70)
    bpy.context.scene.camera.data.ortho_scale=1.18
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in art.descendants(root):obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(FBX),use_selection=True,
        object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',
        global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        use_space_transform=True,bake_space_transform=False,use_mesh_modifiers=True,
        mesh_smooth_type='FACE',use_triangles=True,add_leaf_bones=False,bake_anim=False,
        path_mode='RELATIVE',use_custom_props=True)
    art.reset_scene()
    bpy.ops.import_scene.fbx(filepath=str(FBX),use_anim=False)
    exported=art.inspect_objects(list(bpy.context.scene.objects))
    delta=max(abs(reopened[k][i]-exported[k][i]) for k in ['bounds_min','bounds_max'] for i in range(3))
    report={'asset':'RapidTurret','blender_version':bpy.app.version_string,
        'source':str(OUTPUT.relative_to(PROJECT)),'export':str(FBX.relative_to(PROJECT)),
        'texture':str(TEXTURE.relative_to(PROJECT)),'texture_resolution':[2048,2048],
        'original_triangles':17731,'reduction_by_part':reduction,
        'source_reopened':reopened,'fbx_reimported':exported,
        'mechanical_envelope':{'pitch_degrees':[-6,18],'recoil_meters':.012,
            'pitch_sleeve_inner_radius':.045,'recoil_slide_outer_radius':.044,
            'recoil_slide_y_range':[-.302,-.208],'pitch_sleeve_y_range':[-.2555,-.1365],
            'minimum_slide_overlap_meters':.0475,
            'inspection':'Real rendered neutral and endpoint views; not a collision solver.'},
        'checks':{'source_reopened':True,'fbx_reimported':True,
            'save_reopen_dimensions_equal':before['dimensions']==reopened['dimensions'],
            'export_bounds_maximum_difference':delta,'export_bounds_match':delta<.0001,
            'triangles_match':reopened['triangles']==exported['triangles'],
            'mesh_count_match':reopened['mesh_count']==exported['mesh_count'],
            'material_names_match':reopened['material_names']==exported['material_names'],
            'within_triangle_budget':4000<=reopened['triangles']<=6000,
            'uv_present':all(len(o.data.uv_layers)>0 for o in bpy.context.scene.objects if o.type=='MESH'),
            'all_unit_scales':all(max(abs(v-1) for v in o.scale)<.0001 for o in bpy.context.scene.objects),
            'muzzle_follows_barrel_spin':bpy.data.objects['Muzzle'].parent.name=='BarrelSpin',
            'degenerate_faces':sum(m['degenerate_faces'] for m in reopened['meshes']),
            'non_finite_normals':sum(m['non_finite_normals'] for m in reopened['meshes'])}}
    (CHECKS/'RapidTurret.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('RAPID_TURRET_COMPLETE',reopened['triangles'],reopened['dimensions'],flush=True)


if __name__=='__main__':
    if '--refresh-muzzle' in sys.argv:refresh_muzzle_export()
    else:build()
