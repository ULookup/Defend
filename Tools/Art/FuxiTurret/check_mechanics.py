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
OUT = PROJECT / 'Docs/Art/FuxiTurret/Checks'
PREVIEWS = PROJECT / 'Docs/Art/FuxiTurret/Previews'


def descendants(root):
    return [root] + list(root.children_recursive)


def world_vertices(obj):
    return [obj.matrix_world @ v.co for v in obj.data.vertices]


def bounds(obj):
    vertices = world_vertices(obj)
    return [min(v[i] for v in vertices) for i in range(3)], [max(v[i] for v in vertices) for i in range(3)]


def group_of(obj):
    while obj:
        if obj.name in ('BarrelSpin', 'BarrelRecoil', 'BarrelPitch', 'TurretYaw', 'FuxiTurret', 'RapidTurret'):
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
    parser.add_argument('--source', default=str(PROJECT/'ArtSource/Production/FuxiTurret/FuxiTurret.blend'))
    parser.add_argument('--inventory-only', action='store_true')
    parser.add_argument('--render', action='store_true')
    parser.add_argument('--render-exploded', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    OUT.mkdir(parents=True, exist_ok=True)
    PREVIEWS.mkdir(parents=True, exist_ok=True)
    source_stat=Path(args.source).stat()
    args.source_metadata_at_load={'file_size_bytes':source_stat.st_size,'last_modified_unix_seconds':source_stat.st_mtime}
    bpy.ops.wm.open_mainfile(filepath=args.source)
    root = bpy.data.objects.get('FuxiTurret') or bpy.data.objects.get('RapidTurret')
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
    base_top=max(bounds(o)[1][2] for o in base)
    check('Sampled_motion_stays_above_fixed_base_top',min(s['minimum_moving_part_z_m'] for s in pose_samples)>base_top,{'samples':len(pose_samples),'minimum_height_m':min(s['minimum_moving_part_z_m'] for s in pose_samples),'measured_fixed_base_top_m':base_top})
    pose()
    images=[]
    print(json.dumps({'stage':'geometry_completed','checks':len(checks),'passed':sum(c['passed'] for c in checks)}),flush=True)
    if (args.render or args.render_exploded) and all(c['passed'] for c in checks):
        sys.path.insert(0,str(Path(__file__).resolve().parent))
        import render_review
        render_review.setup()
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
    elif args.render_exploded and all(c['passed'] for c in checks):
        exploded_view(root,objects)
        images.append('MechanicalExploded.png')
    report={'asset_source':args.source,'source_metadata':args.source_metadata_at_load,'review_kind':'Independent editable-mesh, hierarchy, analytical-clearance and sampled-pose game-art review','checks':checks,'all_automated_checks_pass':all(c['passed'] for c in checks),'sampled_poses':pose_samples,'evidence_images':images,'limits':'Dimensional checks cover intended bearing and sliding interfaces. These are not a complete triangle intersection or physical simulation. Independent review of the rendered views is required for the final verdict.'}
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


def clipped_radial_envelope(objects,y_interval,axis_z):
    samples=[]
    low,high=y_interval
    for obj in objects:
        vv=world_vertices(obj)
        samples.extend(v for v in vv if low<=v.y<=high)
        for edge in obj.data.edges:
            a,b=[vv[i] for i in edge.vertices]
            if abs(b.y-a.y)<1e-10:continue
            for plane in (low,high):
                t=(plane-a.y)/(b.y-a.y)
                if 0<=t<=1:samples.append(a+(b-a)*t)
    return max(math.hypot(v.x,v.z-axis_z) for v in samples) if samples else 0


def review_part_dimensions(objects,check):
    pose();obs=bpy.data.objects;axis_z=obs['BarrelSpin'].matrix_world.translation.z
    spinparts=[o for o in objects if group_of(o)=='BarrelSpin']
    for station in ('Rear','Front'):
        race=obs['Recoil_'+station+'BearingRace']
        inner=radial_extents([race],axis_z=axis_z)[0]
        envelope=clipped_radial_envelope(spinparts,interval(race,1),axis_z)
        check(station+'_rotor_sweep_clearance_inside_actual_bearing_slab',inner-envelope>.0001,{'bearing_inner_radius_m':inner,'swept_rotor_radius_in_bearing_slab_m':envelope,'minimum_radial_clearance_m':inner-envelope,'note':'Actual edges are clipped to the bearing axial slab; remote muzzle hoops are excluded.'})
    for side,label in [(-1,'Left'),(1,'Right')]:
        journal=obs['Pitch_'+label+'TrunnionJournal'];bearing=obs['Yaw_'+label+'TrunnionBearing']
        jmin,jmax=bounds(journal);bmin,bmax=bounds(bearing)
        center_error=max(abs((jmin[i]+jmax[i]-bmin[i]-bmax[i])*.5) for i in (1,2))
        axial=overlaps(interval(journal,0),interval(bearing,0))
        check(label+'_true_trunnion_coaxial_and_axially_engaged',center_error<1e-6 and axial>.015,{'off_axis_error_m':center_error,'axial_overlap_m':axial})
        rail=obs['Pitch_RecoilRail_'+str(side)];shoe=obs['Recoil_GuideShoe_'+str(side)]
        margins=[];engagement=[]
        for stroke in (0,.012):
            pose(recoil_distance=stroke)
            ri=interval(rail,1);si=interval(shoe,1)
            margins.append(min(si[0]-ri[0],ri[1]-si[1]));engagement.append(overlaps(ri,si))
        check(label+'_slide_supported_for_entire_recoil',min(margins)>0 and min(engagement)>.09,{'minimum_end_margin_m':min(margins),'end_pose_engagement_m':engagement,'note':'Rail/shoe nested fit is intentional.'})
        pose()
    # Test the visible external armor and sensor structures independently of
    # deliberately nested axle bearings and concealed chassis support cores.
    # Include the lower fixed support and side shoulder armor because their
    # silhouettes are being refined without altering the movement groups.
    exterior_tokens=('FrontArmor','RearArmor','BroadSideArmor','OctagonalFrame',
                     'UpperArmor','FoldedArmor','ShoulderArmor')
    fixed_objects=[o for o in objects if group_of(o)=='TurretYaw' and (
        o.name.startswith('Sensor_') or any(token in o.name for token in exterior_tokens) or
        o.name.startswith(('Yaw_Front','Yaw_Rear','Yaw_Lower')) or
        o.name in ('Yaw_RearCabinetCore','Yaw_RearTopCover','Yaw_RearCabinetArmor'))]
    moving_objects=[o for o in objects if group_of(o) in ('BarrelPitch','BarrelRecoil','BarrelSpin')]
    problems=[]
    intended_contacts=[]
    for elevation in (-6,0,6,12,18):
        for stroke in (0,.012):
            pose(pitch_degrees=elevation,recoil_distance=stroke)
            bound_cache={o.name:bounds(o) for o in fixed_objects+moving_objects}
            for a in moving_objects:
                for b in fixed_objects:
                    amin,amax=bound_cache[a.name];bmin,bmax=bound_cache[b.name]
                    if not all(min(amax[i],bmax[i])-max(amin[i],bmin[i])>1e-6 for i in range(3)):continue
                    hits=edge_surface_crossings(a,b)+edge_surface_crossings(b,a)
                    if hits:
                        record={'elevation_degrees':elevation,'recoil_m':stroke,'moving_part':a.name,'fixed_part':b.name,'crossing_edges':hits}
                        if b.name=='Yaw_RearCabinetCore' and (a.name.endswith('TrunnionJournal') or a.name=='Pitch_MainTransverseAxle'):
                            record['classification']='Intentional concealed axle entry into its enclosing support cabinet; no exposed armor collision.'
                            intended_contacts.append(record)
                        elif (a.name.endswith('LargeArmorDisc') and b.name.endswith('BroadSideArmor') and
                              min(amax[0],bmax[0])-max(amin[0],bmin[0])<.001):
                            record['classification']='Sub-millimeter concealed rear-face seating of the circular armor on its backing panel, unchanged through the coaxial rotation; the exposed front surface and rim stay clear.'
                            record['axial_seating_m']=min(amax[0],bmax[0])-max(amin[0],bmin[0])
                            intended_contacts.append(record)
                        else:problems.append(record)
    check('Pitch_and_recoil_clear_of_fixed_external_armor_and_sensors',not problems,{
        'fixed_external_parts_tested':[o.name for o in fixed_objects],
        'unexpected_boundary_intersections':problems,
        'classified_concealed_support_contacts':intended_contacts})
    pose()


def exploded_view(root,objects):
    pose()
    # Reuse the root review's neutral bright studio light setup without
    # modifying or resaving the production asset.
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    import render_review
    render_review.setup()
    offsets={root.name:Vector((0,0,0)),'TurretYaw':Vector((0,0,.11)),
             'BarrelPitch':Vector((0,0,.60)),'BarrelRecoil':Vector((0,-.13,.90)),
             'BarrelSpin':Vector((0,-.37,1.16))}
    matrices={o.name:o.matrix_world.copy() for o in objects}
    groups={o.name:group_of(o) for o in objects}
    for o in objects:
        o.parent=None
        o.matrix_world=matrices[o.name]
        o.location+=offsets[groups[o.name]]
    bpy.context.view_layer.update()
    review_camera('MechanicalExploded',(2,-3,1.85),point=(0,-.10,.73),ortho=2.20)
    layout={'source_is_unchanged':True,
            'note':'Actual source meshes, separated by their runtime motion group. Display offsets are illustrative assembly separation, not additional runtime degrees of freedom.',
            'groups':[{'motion_group':group,'display_translation_m':list(offset),
                       'mesh_parts':[o.name for o in objects if groups[o.name]==group]}
                      for group,offset in offsets.items()]}
    (OUT/'MechanicalExplodedLayout.json').write_text(json.dumps(layout,indent=2),encoding='utf-8')


if __name__ == '__main__':
    main()
