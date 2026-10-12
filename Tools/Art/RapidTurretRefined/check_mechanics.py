"""Independent mechanical review of the rebuilt turret in Blender.

Reads the editable source. It never changes production geometry or saves over the
source. Analytical clearances and sampled pose evidence are game-art review,
not a physical dynamics or engineering certification.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

PROJECT = Path(__file__).resolve().parents[3]
OUT = PROJECT / 'Docs/Art/RapidTurretRefined/Checks'
PREVIEWS = PROJECT / 'Docs/Art/RapidTurretRefined/Previews'


def descendants(root):
    return [root] + list(root.children_recursive)


def world_vertices(obj):
    return [obj.matrix_world @ v.co for v in obj.data.vertices]


def bounds(obj):
    vertices = world_vertices(obj)
    return [min(v[i] for v in vertices) for i in range(3)], [max(v[i] for v in vertices) for i in range(3)]


def group_of(obj):
    while obj:
        if obj.name in ('BarrelSpin', 'BarrelRecoil', 'BarrelPitch', 'TurretYaw', 'RapidTurretRefined', 'RapidTurret'):
            return obj.name
        obj = obj.parent
    return None


def geometry_record(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    record = {
        'name': obj.name,
        'motion_group': group_of(obj),
        'parent': obj.parent.name if obj.parent else None,
        'vertices': len(bm.verts),
        'boundary_edges': sum(e.is_boundary for e in bm.edges),
        'non_manifold_edges': sum(not e.is_manifold for e in bm.edges),
        'degenerate_faces': sum(f.calc_area() < 1e-12 for f in bm.faces),
        'bounds': bounds(obj),
    }
    bm.free()
    return record


def overlaps(a, b):
    return min(a[1], b[1]) - max(a[0], b[0])


def interval(obj, axis):
    low, high = bounds(obj)
    return (low[axis], high[axis])


def radial_extents(objects, axis_y=-.195, axis_z=.382, y_interval=None):
    radii = []
    for obj in objects:
        for v in world_vertices(obj):
            if y_interval is None or y_interval[0] - 1e-6 <= v.y <= y_interval[1] + 1e-6:
                radii.append(math.hypot(v.x, v.z-axis_z))
    if not radii:
        return None
    return min(radii), max(radii)


def pose(pitch_degrees=0, recoil_distance=0, spin_degrees=0, yaw_degrees=0):
    yaw = bpy.data.objects['TurretYaw']
    pitch = bpy.data.objects['BarrelPitch']
    recoil = bpy.data.objects['BarrelRecoil']
    spin = bpy.data.objects['BarrelSpin']
    yaw.rotation_euler = (0, 0, math.radians(yaw_degrees))
    pitch.rotation_euler = (math.radians(-pitch_degrees), 0, 0)
    recoil.location = REST_RECOIL + Vector((0,recoil_distance,0))
    spin.rotation_euler = (0, math.radians(-spin_degrees), 0)
    bpy.context.view_layer.update()


def aim(camera, point):
    camera.rotation_euler = (Vector(point)-camera.location).to_track_quat('-Z','Y').to_euler()


def review_camera(name, position, point=(0,-.065,.3), ortho=1.03):
    scene = bpy.context.scene
    camera = scene.camera
    camera.location = position
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = ortho
    aim(camera, point)
    scene.cycles.samples = 24
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 4
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.filepath = str(PREVIEWS / (name+'.png'))
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', default=str(PROJECT/'ArtSource/Production/RapidTurretRefined/RapidTurretRefined.blend'))
    parser.add_argument('--inventory-only', action='store_true')
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    OUT.mkdir(parents=True, exist_ok=True)
    PREVIEWS.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=args.source)
    root = bpy.data.objects.get('RapidTurretRefined') or bpy.data.objects.get('RapidTurret')
    if root is None:
        raise RuntimeError('Missing named asset root')
    # Remove preview animation while examining deterministic independent poses.
    for obj in descendants(root):
        obj.animation_data_clear()
    global REST_RECOIL
    REST_RECOIL = bpy.data.objects['BarrelRecoil'].location.copy()
    pose()
    objects = [o for o in descendants(root) if o.type == 'MESH']
    inventory = [geometry_record(o) for o in objects]
    (OUT/'MechanicalInventory.json').write_text(json.dumps(inventory,ensure_ascii=False,indent=2),encoding='utf-8')
    if args.inventory_only:
        print('Mechanical inventory written: '+str(len(objects))+' editable mesh objects')
        return
    perform_review(root, objects, inventory, args)


def perform_review(root, objects, inventory, args):
    checks = []
    def check(name, passed, details):
        checks.append({'name':name, 'passed':bool(passed), 'details':details})
    for child, parent in [('TurretYaw',root.name),('BarrelPitch','TurretYaw'),('BarrelRecoil','BarrelPitch'),('BarrelSpin','BarrelRecoil'),('Muzzle','BarrelSpin')]:
        obj = bpy.data.objects.get(child)
        check('Hierarchy_'+child, obj is not None and obj.parent is not None and obj.parent.name == parent, {'required_parent':parent,'actual_parent':obj.parent.name if obj is not None and obj.parent is not None else None})
    check('Closed_non_degenerate_editable_parts', all(r['non_manifold_edges']==0 and r['degenerate_faces']==0 for r in inventory), {'problem_objects':[r for r in inventory if r['non_manifold_edges'] or r['degenerate_faces']]})
    rest = {o.name:[v.copy() for v in world_vertices(o)] for o in objects}
    base = [o for o in objects if group_of(o)==root.name]
    spin_objects = [o for o in objects if group_of(o)=='BarrelSpin']
    pitch_objects = [o for o in objects if group_of(o)=='BarrelPitch']
    recoil_objects = [o for o in objects if group_of(o)=='BarrelRecoil']
    pose(yaw_degrees=137,pitch_degrees=18,recoil_distance=.012,spin_degrees=211)
    base_error = max((v-rest[o.name][i]).length for o in base for i,v in enumerate(world_vertices(o)))
    check('Fixed_base_unchanged_by_all_four_motions',base_error<1e-7,{'maximum_vertex_displacement_m':base_error})
    pose()
    stationary = {o.name:[v.copy() for v in world_vertices(o)] for o in objects if group_of(o)!='BarrelSpin'}
    pose(spin_degrees=113)
    stationary_error=max((v-stationary[o.name][i]).length for o in objects if o.name in stationary for i,v in enumerate(world_vertices(o)))
    check('Spin_does_not_rotate_bearing_outers_or_non_rotor_parts',stationary_error<1e-7,{'maximum_vertex_displacement_m':stationary_error})
    pose(spin_degrees=360)
    spin_error=max((v-rest[o.name][i]).length for o in spin_objects for i,v in enumerate(world_vertices(o)))
    check('Full_360_spin_returns_to_same_geometry',spin_error<1e-6,{'maximum_vertex_difference_m':spin_error})
    pose(recoil_distance=.012)
    recoil_error = max(((v-rest[o.name][i])-Vector((0,.012,0))).length for o in recoil_objects+spin_objects for i,v in enumerate(world_vertices(o)))
    pitch_error = max((v-rest[o.name][i]).length for o in pitch_objects for i,v in enumerate(world_vertices(o)))
    check('Recoil_moves_slide_and_rotor_together_while_pitch_frame_stays',max(recoil_error,pitch_error)<1e-7,{'slide_displacement_error_m':recoil_error,'pitch_frame_vertex_displacement_m':pitch_error})
    pose()
    # Part-specific independent dimensional checks are configured after source inventory.
    review_part_dimensions(objects,check)
    pose_samples = []
    for pitch_angle in (-6,0,18):
        for recoil in (0,.012):
            for spin_angle in range(0,360,30):
                pose(pitch_degrees=pitch_angle,recoil_distance=recoil,spin_degrees=spin_angle)
                moving = [o for o in objects if group_of(o) in ('BarrelPitch','BarrelRecoil','BarrelSpin')]
                zmin=min(bounds(o)[0][2] for o in moving)
                pose_samples.append({'pitch_degrees':pitch_angle,'recoil_m':recoil,'spin_degrees':spin_angle,'minimum_moving_part_z_m':zmin})
    check('Sampled_motion_stays_above_fixed_base_top',min(s['minimum_moving_part_z_m'] for s in pose_samples)>.16,{'samples':len(pose_samples),'minimum_height_m':min(s['minimum_moving_part_z_m'] for s in pose_samples),'reference_base_top_m':.16})
    pose()
    images=[]
    print(json.dumps({'stage':'geometry_completed','checks':len(checks),'passed':sum(c['passed'] for c in checks)}),flush=True)
    if args.render and all(c['passed'] for c in checks):
        review_camera('MechanicalSideNeutral',(2,-.07,.46),ortho=1.1)
        images.append('MechanicalSideNeutral.png')
        pose(pitch_degrees=-6,recoil_distance=.012,spin_degrees=22.5)
        review_camera('MechanicalSideDownRecoil',(2,-.07,.46),ortho=1.1)
        images.append('MechanicalSideDownRecoil.png')
        pose(pitch_degrees=18,recoil_distance=.012,spin_degrees=22.5)
        review_camera('MechanicalSideUpRecoil',(2,-.07,.46),ortho=1.1)
        images.append('MechanicalSideUpRecoil.png')
        pose()
        exploded_view(root,objects)
        images.append('MechanicalExploded.png')
    report={'asset_source':args.source,'review_kind':'Independent editable-mesh, hierarchy, analytical-clearance and sampled-pose game-art review','checks':checks,'all_automated_checks_pass':all(c['passed'] for c in checks),'sampled_poses':pose_samples,'evidence_images':images,'limits':'Dimensional checks cover intended bearing and sliding interfaces. These are not a complete triangle intersection or physical simulation. Independent review of the rendered views is required for the final verdict.'}
    (OUT/'MechanicalReview.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'checks':len(checks),'passed':sum(c['passed'] for c in checks),'report':str(OUT/'MechanicalReview.json')}))
    if not report['all_automated_checks_pass']:
        raise RuntimeError('One or more independent mechanical checks failed; read MechanicalReview.json')


def edge_surface_crossings(a,b):
    va=world_vertices(a);vb=world_vertices(b)
    tree=BVHTree.FromPolygons(vb,[list(p.vertices) for p in b.data.polygons],all_triangles=False,epsilon=0.0)
    count=0
    for edge in a.data.edges:
        start,end=[va[i] for i in edge.vertices]
        delta=end-start;length=delta.length
        if length<=.000002:continue
        direction=delta/length
        hit,normal,index,distance=tree.ray_cast(start+direction*.000001,direction,length-.000002)
        if hit is not None:count+=1
    return count


def review_part_dimensions(objects,check):
    pose()
    obs=bpy.data.objects
    axis_z=obs['BarrelSpin'].matrix_world.translation.z
    spinparts=[o for o in objects if group_of(o)=='BarrelSpin']
    radius=radial_extents(spinparts,axis_z=axis_z)[1]
    for station in ('Rear','Front'):
        race=obs['Recoil_'+station+'RotorBearingRace']
        inner=radial_extents([race],axis_z=axis_z)[0]
        journal=obs['Rotor_'+station+'BearingJournal']
        axial=overlaps(interval(race,1),interval(journal,1))
        check(station+'_rotor_full_sweep_radial_clearance',inner-radius>.002,{'inner_bearing_radius_m':inner,'maximum_rotor_radius_m':radius,'minimum_radial_clearance_m':inner-radius,'note':'Maximum over ALL actual rotor vertices, including braces, journals and spokes; invariant under full axial rotation.'})
        check(station+'_rotor_journal_engaged_axially',axial>.01,{'journal_race_axial_overlap_m':axial})
    for side in ('Left','Right'):
        beam=obs['Recoil_'+side+'BearingTieBeam']
        beaminner=min(abs(v.x) for v in world_vertices(beam))
        check(side+'_tie_beam_outside_rotor_sweep',beaminner-radius>.005,{'inner_beam_abs_x_m':beaminner,'rotor_radius_m':radius,'minimum_clearance_m':beaminner-radius})
        journal=obs['Pitch_'+side+'TrunnionJournal']
        bearing=obs['Yaw_'+side+'TrunnionBearing']
        jmin,jmax=bounds(journal);bmin,bmax=bounds(bearing)
        centers_delta=max(abs((jmin[i]+jmax[i]-bmin[i]-bmax[i])*.5) for i in (1,2))
        axial=overlaps(interval(journal,0),interval(bearing,0))
        check(side+'_trunnion_coaxial_and_engaged',centers_delta<1e-6 and axial>.015,{'off_axis_error_m':centers_delta,'axial_journal_bearing_overlap_m':axial})
        rail=obs['Pitch_'+side+'RecoilRail'];shoe=obs['Recoil_'+side+'SlideShoe']
        overlaps_by_stroke=[]
        end_clearances=[]
        for stroke in (0,.012):
            pose(recoil_distance=stroke)
            rail_i=interval(rail,1);shoe_i=interval(shoe,1)
            overlaps_by_stroke.append(overlaps(rail_i,shoe_i))
            end_clearances.append(min(shoe_i[0]-rail_i[0],rail_i[1]-shoe_i[1]))
        check(side+'_slide_shoe_supported_for_entire_recoil',min(overlaps_by_stroke)>.11 and min(end_clearances)>0,{'axial_overlap_at_rest_and_recoil_m':overlaps_by_stroke,'minimum_shoe_to_rail_end_margin_m':min(end_clearances),'note':'Rail/shoe overlap is an intended sliding fit, not an unwanted intersection.'})
        pose()
    # Independent boundary intersections only between surfaces which must never
    # overlap. Intended bearings, rail sleeves and inserted fasteners excluded.
    fixed_names=['Yaw_RearDriveChassis','Yaw_RearUpperArmor','Sensor_Pedestal','Sensor_HeadCore']
    moving_names=['Pitch_UpperBridge','Pitch_RearCover','Pitch_RearRecoilEnclosure','Recoil_NoseUpperShield']
    intersections=[]
    for elevation in (-6,0,6,12,18):
        for stroke in (0,.012):
            pose(pitch_degrees=elevation,recoil_distance=stroke)
            for moving in moving_names:
                for fixed in fixed_names:
                    a=obs[moving];b=obs[fixed]
                    amin,amax=bounds(a);bmin,bmax=bounds(b)
                    if not all(min(amax[i],bmax[i])-max(amin[i],bmin[i])>1e-6 for i in range(3)):continue
                    crossings=edge_surface_crossings(a,b)+edge_surface_crossings(b,a)
                    if crossings:intersections.append({'pitch_degrees':elevation,'recoil_m':stroke,'parts':[moving,fixed],'boundary_crossings':crossings})
    check('Pitch_and_recoil_armor_clear_of_yaw_sensor_and_rear_cabinet',not intersections,{'sampled_elevations_degrees':[-6,0,6,12,18],'stroke_ends_m':[0,.012],'unexpected_boundary_crossings':intersections})
    pose()
    front_stop_crossings=[]
    for stroke in (0,.012):
        pose(recoil_distance=stroke)
        for side in ('Left','Right'):
            stop=obs['Pitch_'+side+'RailFrontStop']
            housing=obs['Recoil_RearRotorBearingHousing']
            count=edge_surface_crossings(stop,housing)+edge_surface_crossings(housing,stop)
            if count:front_stop_crossings.append({'side':side,'recoil_m':stroke,'boundary_crossings':count})
    check('Recoil_bearing_clears_fixed_rail_front_stops',not front_stop_crossings,{'unexpected_boundary_crossings':front_stop_crossings})
    pose()



def exploded_view(root,objects):
    pose()
    offsets={root.name:Vector((0,0,0)),'TurretYaw':Vector((0,0,.14)),'BarrelPitch':Vector((0,0,.52)),'BarrelRecoil':Vector((0,-.04,.77)),'BarrelSpin':Vector((0,-.40,.96))}
    matrices={o.name:o.matrix_world.copy() for o in objects}
    groups={o.name:group_of(o) for o in objects}
    for o in objects:
        o.parent=None
        o.matrix_world=matrices[o.name]
        o.location+=offsets[groups[o.name]]
    bpy.context.view_layer.update()
    review_camera('MechanicalExploded',(2,-3,1.65),point=(0,-.10,.73),ortho=1.95)


if __name__ == '__main__':
    main()
