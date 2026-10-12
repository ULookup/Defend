"""Read-only, consistent reference photography for the saved production model."""
from pathlib import Path
import argparse
import math
import sys
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]

def aim(obj, point):
    obj.rotation_euler = (Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()

def setup():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 6
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.exposure = .45
    scene.view_settings.gamma = 1
    scene.render.resolution_x = 1536
    scene.render.resolution_y = 1024
    scene.render.image_settings.file_format = 'PNG'
    scene.world.use_nodes = True
    bg = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (.60,.60,.60,1)
    bg.inputs[1].default_value = 1
    floor = bpy.data.objects.get('StudioFloor_ExcludedFromExport')
    if floor:
        surface = next(n for n in floor.data.materials[0].node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        surface.inputs['Base Color'].default_value = (.55,.58,.62,1)
        surface.inputs['Roughness'].default_value = .88
    for name, energy, color in [('StudioKey',800,(1,.96,.91)),('StudioFill',650,(.91,.95,1)),('StudioRim',600,(1,.97,.91))]:
        lamp = bpy.data.objects.get(name)
        if lamp:
            lamp.data.energy=energy
            lamp.data.color=color
    return scene

def set_view(name):
    camera = bpy.context.scene.camera
    if name in ('Front','Rear'):
        target=Vector((0,0,.262))
        direction=Vector((0,-1 if name=='Front' else 1,.0087))
        scale=.925
    elif name in ('Left','Right'):
        target=Vector((0,-.065,.267))
        direction=Vector((1 if name=='Left' else -1,0,.0175))
        scale=.999
    else:
        target=Vector((0,-.040,.267))
        direction=Vector({'FrontThreeQuarter':(1.25,-1.9,1.02),
                          'RearThreeQuarter':(-1.2,1.8,.9),
                          'Gameplay70':(.1,-.7,2.0),
                          'Topology':(1.25,-1.9,1.02)}[name])
        scale=1.13 if name not in ('Gameplay70',) else 1.08
    camera.location=target+direction.normalized()*4
    aim(camera,target)
    camera.data.type='ORTHO'
    camera.data.ortho_scale=scale

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--source',default=str(ROOT/'ArtSource/Production/FuxiTurret/FuxiTurret.blend'))
    parser.add_argument('--views',default='Front,Rear,Left,Right,FrontThreeQuarter,RearThreeQuarter,Gameplay70')
    parser.add_argument('--output',default=str(ROOT/'Docs/Art/FuxiTurret/Previews'))
    parser.add_argument('--percentage',type=int,default=100)
    parser.add_argument('--samples',type=int,default=32)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    bpy.ops.wm.open_mainfile(filepath=args.source)
    scene=setup()
    scene.render.resolution_percentage=args.percentage
    scene.cycles.samples=args.samples
    output=Path(args.output)
    if not output.is_absolute():
        output=ROOT/output
    output.mkdir(parents=True,exist_ok=True)
    for name in args.views.split(','):
        if name=='Topology':
            solid=bpy.data.materials.new('ReviewTopologySurface');solid.use_nodes=True
            shader=next(n for n in solid.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
            shader.inputs['Base Color'].default_value=(.48,.51,.55,1)
            shader.inputs['Roughness'].default_value=.75
            wire=bpy.data.materials.new('ReviewTopologyEdges');wire.use_nodes=True
            shader=next(n for n in wire.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
            shader.inputs['Base Color'].default_value=(.015,.025,.035,1)
            shader.inputs['Roughness'].default_value=.7
            for obj in bpy.data.objects['FuxiTurret'].children_recursive:
                if obj.type=='MESH':
                    obj.data.materials.clear();obj.data.materials.append(solid);obj.data.materials.append(wire)
                    for face in obj.data.polygons:face.material_index=0
                    mod=obj.modifiers.new('RealMeshEdgesForReview','WIREFRAME')
                    mod.thickness=.00022;mod.use_replace=False;mod.material_offset=1
        set_view(name)
        scene.render.filepath=str(output/(name+'.png'))
        bpy.ops.render.render(write_still=True)
        print('FUXI_REVIEW_RENDER_READY',name,flush=True)

if __name__=='__main__':
    main()
