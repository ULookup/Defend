import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'Previews'
OUT.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.import_scene.fbx(filepath=str(next((ROOT / 'Original').glob('*.fbx'))), use_anim=True)
report = {'blender_version': bpy.app.version_string, 'objects': [], 'images': [], 'actions': list(bpy.data.actions.keys())}
for obj in list(bpy.context.scene.objects):
    item = {'name': obj.name, 'type': obj.type, 'parent': obj.parent.name if obj.parent else None, 'location': list(obj.location), 'rotation': list(obj.rotation_euler), 'scale': list(obj.scale), 'dimensions': list(obj.dimensions)}
    if obj.type == 'MESH':
        obj.data.calc_loop_triangles()
        bm = bmesh.new(); bm.from_mesh(obj.data)
        bm.verts.ensure_lookup_table()
        unseen = set(bm.verts); components=[]
        while unseen:
            first=unseen.pop(); stack=[first]; group=[first]
            while stack:
                v=stack.pop()
                for e in v.link_edges:
                    other=e.other_vert(v)
                    if other in unseen:
                        unseen.remove(other); stack.append(other); group.append(other)
            coords=[obj.matrix_world @ v.co for v in group]
            components.append({'vertices':len(group), 'min':[min(c[i] for c in coords) for i in range(3)], 'max':[max(c[i] for c in coords) for i in range(3)]})
        item.update(vertices=len(obj.data.vertices), triangles=len(obj.data.loop_triangles), polygons=len(obj.data.polygons), uv_layers=list(obj.data.uv_layers.keys()), material_slots=[m.name for m in obj.data.materials], boundary_edges=sum(e.is_boundary for e in bm.edges), non_manifold_edges=sum(not e.is_manifold for e in bm.edges), components=sorted(components,key=lambda x:-x['vertices']))
        bm.free()
    report['objects'].append(item)
for img in bpy.data.images:
    report['images'].append({'name':img.name,'size':list(img.size),'filepath':img.filepath,'packed':bool(img.packed_file)})
(ROOT/'OriginalInspection.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('INSPECTION', json.dumps(report,ensure_ascii=False))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
points=[o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo=Vector([min(p[i] for p in points) for i in range(3)])
hi=Vector([max(p[i] for p in points) for i in range(3)])
center=(lo+hi)/2; size=max(hi-lo)
scene=bpy.context.scene
scene.render.engine='CYCLES'; scene.cycles.samples=24
scene.cycles.use_denoising=True
scene.render.resolution_x=900; scene.render.resolution_y=900; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('ReviewWorld'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.24,.27,.32,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.55
scene.view_settings.view_transform='AgX'
def area(name,pos,power,scale):
    d=bpy.data.lights.new(name,'AREA'); d.energy=power*size*size; d.shape='DISK'; d.size=scale*size
    o=bpy.data.objects.new(name,d); scene.collection.objects.link(o); o.location=center+Vector(pos)*size; o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
area('Key',(-1.3,-1.8,2),650,1.5); area('Fill',(1.8,-.2,1),350,1.5); area('Rim',(0,1.4,1.6),800,1)
data=bpy.data.cameras.new('ReviewCamera'); cam=bpy.data.objects.new('ReviewCamera',data); scene.collection.objects.link(cam); scene.camera=cam; data.type='ORTHO'; data.ortho_scale=size*1.28
for name,vec in [('Original_Front',(1,-1.6,.95)),('Original_Rear',(-1,1.6,.95)),('Original_Side',(2,0,.35)),('Original_Top',(0,-.001,3))]:
    cam.location=center+Vector(vec).normalized()*size*3; cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(OUT/(name+'.png')); bpy.ops.render.render(write_still=True)
for image in bpy.data.images:
    if image.source=='FILE':
        try: image.pack()
        except RuntimeError: pass
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Original_Inspection.blend'))
