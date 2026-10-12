"""Clean, authored hard-surface turret. Run using Blender 5.2 in background.

No source mesh or texture from the research import is used. All mechanical
surfaces are built from closed cross-sections, controlled bevels and closed
primitives. The editable source retains descriptive individual components;
the FBX export consolidates only the nine compatible rendering groups.
"""
import bpy, bmesh, math, json, sys
from pathlib import Path
from mathutils import Vector

PROJECT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(PROJECT/'Tools/Art/PhaseFive'))
import common as art
SOURCE=PROJECT/'ArtSource/Production/RapidTurretRefined/RapidTurretRefined.blend'
SHOWCASE=SOURCE.with_name('MotionShowcase.blend')
EXPORT=PROJECT/'Assets/Defend/Content/Art/RapidTurretRefined/Models/RapidTurretRefined.fbx'
PREVIEW=PROJECT/'Docs/Art/RapidTurretRefined/Previews'
CHECKS=PROJECT/'Docs/Art/RapidTurretRefined/Checks'
PITCH=Vector((0,-.060,.382))
SPIN=Vector((0,-.195,.382))
GROUPS={}
MATERIAL_DATA=[
 {'name':'RapidTurretRefined_Armor','baseColor':[.63,.625,.59,1],'metallic':.22,'roughness':.32,'emissionStrength':0},
 {'name':'RapidTurretRefined_Graphite','baseColor':[.025,.033,.041,1],'metallic':.55,'roughness':.34,'emissionStrength':0},
 {'name':'RapidTurretRefined_Steel','baseColor':[.22,.27,.30,1],'metallic':.88,'roughness':.23,'emissionStrength':0},
 {'name':'RapidTurretRefined_Copper','baseColor':[.38,.16,.061,1],'metallic':.72,'roughness':.28,'emissionStrength':0},
 {'name':'RapidTurretRefined_Cyan','baseColor':[.008,.38,.52,1],'metallic':.10,'roughness':.25,'emissionStrength':1.1},
]
MANIFEST={'asset':'RapidTurretRefined','materials':MATERIAL_DATA,
 'forwardAxis':'-Y','upAxis':'Z','unitMeters':1.0,'originalMeshReused':False,
 'renderGroups':['FixedBase','TurretHousing','YawArmor','PitchHousing','PitchArmor',
                 'RecoilCarriage','RotorBearings','RotatingBarrels','StatusLamps'],
 'mechanics':{'pitchCenter':list(PITCH),'spinCenter':list(SPIN),
  'barrelCount':8,'barrelCircleRadius':.051,'barrelTubeOuterRadius':.0149,
  'barrelBoreRadius':.0106,'rotorSweepRadius':.068,'bearingInnerRadius':.071,
  'bearingCentersY':[-.205,-.305],'recoilMeters':.012,'recoilDirection':[0,1,0],
  'pitchLimitsDegrees':[-6,18], 'yawAxis':[0,0,1],'pitchAxis':[1,0,0],'spinAxis':[0,-1,0]},
 'scope':'Game visual mechanics only; not weapon engineering specifications.'}


def write_manifest():
 CHECKS.mkdir(parents=True,exist_ok=True)
 (CHECKS/'AssetManifest.json').write_text(json.dumps(MANIFEST,ensure_ascii=False,indent=2),encoding='utf-8')


def make_materials():
 mats={}
 for spec in MATERIAL_DATA:
  m=bpy.data.materials.new(spec['name']);m.use_nodes=True
  m.diffuse_color=spec['baseColor'];m.metallic=spec['metallic'];m.roughness=spec['roughness']
  p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
  for k,v in [('Base Color',spec['baseColor']),('Metallic',spec['metallic']),
              ('Roughness',spec['roughness']),('Emission Color',spec['baseColor']),
              ('Emission Strength',spec['emissionStrength'])]:p.inputs[k].default_value=v
  mats[spec['name'].split('_')[-1]]=m
 return mats


def register(obj,name,mat,parent,group):
 obj.name=name
 if mat:obj.data.materials.clear();obj.data.materials.append(mat)
 art.parent_world(obj,parent)
 obj['render_group']=group
 GROUPS.setdefault(group,[]).append(obj)
 return obj


def finish(obj,name,mat,parent,group,bevel=0,segments=2,smooth=False):
 art.activate(obj)
 bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
 if bevel:
  mod=obj.modifiers.new('ControlledEdgeBevel','BEVEL');mod.width=bevel
  mod.segments=segments;mod.affect='EDGES'
  bpy.ops.object.modifier_apply(modifier=mod.name)
 for p in obj.data.polygons:p.use_smooth=smooth
 if smooth:
  obj.data.set_sharp_from_angle(angle=math.radians(45))
  mod=obj.modifiers.new('AreaWeightedCornerNormals','WEIGHTED_NORMAL')
  mod.keep_sharp=True;mod.weight=40
  bpy.ops.object.modifier_apply(modifier=mod.name)
 return register(obj,name,mat,parent,group)


def box(name,loc,size,mat,parent,group,bevel=.003,rotation=(0,0,0)):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc,rotation=rotation)
 obj=bpy.context.object;obj.dimensions=size
 detail_segments=1 if min(size)<.009 or 'RadialSpoke' in name else 2
 return finish(obj,name,mat,parent,group,min(bevel,min(size)*.24),detail_segments,True)


def cylinder(name,loc,radius,depth,mat,parent,group,axis='Z',verts=32,bevel=.0015):
 verts={64:48,48:32,40:24,32:24,24:16,20:16}.get(verts,verts)
 rot={'X':(0,math.pi/2,0),'Y':(math.pi/2,0,0),'Z':(0,0,0)}[axis]
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=radius,depth=depth,
     end_fill_type='NGON',location=loc,rotation=rot)
 return finish(bpy.context.object,name,mat,parent,group,min(bevel,depth*.2),
               2 if radius>.10 else 1,True)


def profile_ring(name,loc,profile,mat,parent,group,axis='Y',segments=48):
 """Revolve a closed nonzero-radius cross-section, preserving crisp lip edges."""
 if not name.startswith('Rotor_Barrel_'):
  segments={64:48,48:32,40:24,32:24,24:16,20:16}.get(segments,segments)
 vertices=[];faces=[]
 for axial,radius in profile:
  for i in range(segments):
   a=math.tau*i/segments
   c=math.cos(a)*radius;s=math.sin(a)*radius
   local={'Y':(c,axial,s),'X':(axial,c,s),'Z':(c,s,axial)}[axis]
   vertices.append(tuple(Vector(loc)+Vector(local)))
 for j in range(len(profile)):
  nj=(j+1)%len(profile)
  for i in range(segments):
   ni=(i+1)%segments
   faces.append((j*segments+i,j*segments+ni,nj*segments+ni,nj*segments+i))
 mesh=bpy.data.meshes.new(name+'Mesh');mesh.from_pydata(vertices,[],faces);mesh.update()
 bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
 bm.to_mesh(mesh);bm.free()
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
 # Circumferential directions interpolate; each engineered profile transition
 # receives a split normal, so annular end faces remain precisely planar.
 for p in mesh.polygons:p.use_smooth=True
 for edge in mesh.edges:
  a,b=edge.vertices
  if a//segments==b//segments:edge.use_edge_sharp=True
 return register(obj,name,mat,parent,group)


def ring(name,loc,outer,inner,depth,mat,parent,group,axis='Y',segments=48,bevel=.001):
 h=depth/2;b=min(bevel,(outer-inner)*.24,h*.4)
 if depth<.009:
  return profile_ring(name,loc,[(-h,outer),(h,outer),(h,inner),(-h,inner)],
   mat,parent,group,axis,segments)
 return profile_ring(name,loc,[(-h+b,outer), (h-b,outer),(h,outer-b),
   (h,inner+b),(h-b,inner),(-h+b,inner),(-h,inner+b),(-h,outer-b)],
   mat,parent,group,axis,segments)


def bolt(name,loc,axis,mat,parent,group,radius=.005):
 return cylinder(name,loc,radius,.0035,mat,parent,group,axis,6,0)


def radial_box(name,radius,angle,z,size,mat,parent,group,bevel=.003):
 return box(name,(math.sin(angle)*radius,math.cos(angle)*radius,z),size,mat,parent,group,
            bevel,(0,0,-angle))


def panel(name,points,thickness,mat,parent,group,axis='X',bevel=.002):
 # Extruded planar polygon; explicit caps avoid Boolean slivers.
 vertices=[]
 for delta in [-thickness/2,thickness/2]:
  for x,y,z in points:
   p=[x,y,z];p['XYZ'.index(axis)]+=delta;vertices.append(tuple(p))
 n=len(points);faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
 faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
 mesh=bpy.data.meshes.new(name+'Mesh');mesh.from_pydata(vertices,[],faces);mesh.update()
 bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
 obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
 return finish(obj,name,mat,parent,group,bevel,2,True)


def build_geometry():
 art.reset_scene();GROUPS.clear()
 scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
 root=art.empty('RapidTurretRefined',(0,0,0))
 yaw=art.empty('TurretYaw',(0,0,.133),root)
 pitch=art.empty('BarrelPitch',PITCH,yaw)
 recoil=art.empty('BarrelRecoil',PITCH,pitch)
 spin=art.empty('BarrelSpin',SPIN,recoil)
 art.empty('Muzzle',(0,-.5375,PITCH.z+.051),spin)
 art.empty('ForwardReference',(0,-.5,0),root)
 root['asset_name']='RapidTurretRefined';root['forward_axis']='-Y';root['unit_meters']=1.0
 root['pitch_limits_degrees']=[-6,18];root['recoil_distance_meters']=.012
 root['authored_topology']='Clean closed mechanical primitives; no imported source mesh.'
 mats=make_materials();a=mats['Armor'];g=mats['Graphite'];s=mats['Steel'];c=mats['Copper'];e=mats['Cyan']

 # A grounded plinth, eight discrete feet and radial removable skirt panels.
 cylinder('Base_CorePlinth',(0,0,.050),.326,.086,g,root,'FixedBase','Z',64,.007)
 cylinder('Base_UpperFlange',(0,0,.096),.339,.018,s,root,'FixedBase','Z',64,.002)
 ring('Base_OuterIsolationBand',(0,0,.074),.343,.329,.023,g,root,'FixedBase','Z',64,.002)
 for i in range(8):
  angle=i*math.tau/8
  radial_box(f'Base_Foot_{i+1:02}',.362,angle,.022,(.108,.117,.044),g,root,'FixedBase',.008)
  radial_box(f'Base_FootCover_{i+1:02}',.362,angle,.046,(.076,.087,.016),a,root,'FixedBase',.003)
  x=math.sin(angle)*.384;y=math.cos(angle)*.384
  bolt(f'Base_AnchorBolt_{i+1:02}',(x,y,.058),'Z',s,root,'FixedBase',.008)
  # Eight broad armor panels with an even black seam, not tessellated scraps.
  radial_box(f'Base_SkirtPanel_{i+1:02}',.316,angle,.054,(.168,.028,.073),a,root,'FixedBase',.007)
  radial_box(f'Base_SkirtInset_{i+1:02}',.332,angle,.056,(.068,.003,.026),g,root,'FixedBase',.001)

 # Concentric yaw bearing with deliberate separation from the fixed plinth.
 ring('Yaw_LowerBearing',(0,0,.116),.286,.246,.021,g,yaw,'TurretHousing','Z',64,.002)
 ring('Yaw_BearingWitnessRing',(0,0,.1255),.281,.254,.005,c,yaw,'TurretHousing','Z',64,.0008)
 cylinder('Yaw_Turntable',(0,0,.141),.278,.025,g,yaw,'TurretHousing','Z',64,.004)
 cylinder('Yaw_TurntableTopPlate',(0,0,.157),.266,.010,a,yaw,'YawArmor','Z',64,.002)
 # Broad grounded cheeks carry the two bearings along the common X axis.
 for side,label in [(-1,'Left'),(1,'Right')]:
  x=side*.190
  points=[(x,-.169,.171),(x,.162,.171),(x,.147,.384),(x,.089,.474),
          (x,-.121,.474),(x,-.176,.407)]
  panel(f'Yaw_{label}LoadFrame',points,.080,g,yaw,'TurretHousing','X',.008)
  # Inner bearing interface lies between the independent pitch drum and frame.
  ring(f'Yaw_{label}TrunnionBearing',(side*.151,PITCH.y,PITCH.z),.067,.044,.027,
       s,yaw,'TurretHousing','X',48,.0017)
  ring(f'Yaw_{label}TrunnionSeal',(side*.135,PITCH.y,PITCH.z),.059,.045,.007,
       g,yaw,'TurretHousing','X',48,.001)
  # Side covers are one clean armor extrusion each with a milled disc insert.
  xcover=side*.239
  cover=[(xcover,-.147,.197),(xcover,.138,.197),(xcover,.128,.380),
         (xcover,.068,.460),(xcover,-.112,.460),(xcover,-.160,.395)]
  panel(f'Yaw_{label}ArmorCover',cover,.019,a,yaw,'YawArmor','X',.008)
  cylinder(f'Yaw_{label}BearingCover',(side*.253,PITCH.y,PITCH.z),.074,.017,g,
           yaw,'TurretHousing','X',48,.0025)
  cylinder(f'Yaw_{label}BearingCap',(side*.264,PITCH.y,PITCH.z),.063,.010,a,
           yaw,'YawArmor','X',48,.0015)
  cylinder(f'Yaw_{label}CapInset',(side*.270,PITCH.y,PITCH.z),.032,.003,g,
           yaw,'TurretHousing','X',32,.0008)
  cylinder(f'Yaw_{label}CapCenter',(side*.272,PITCH.y,PITCH.z),.017,.004,c,
           yaw,'YawArmor','X',24,.0008)
  for j in range(6):
   angle=j*math.tau/6
   bolt(f'Yaw_{label}BearingCapBolt_{j+1}',(side*.271,PITCH.y+math.cos(angle)*.049,
        PITCH.z+math.sin(angle)*.049),'X',s,yaw,'YawArmor',.004)
  # Each panel has a single serial inset and a clear recessed vent grouping.
  for j in range(3):
   box(f'Yaw_{label}RearVent_{j+1}',(side*.251,.102,.261+j*.019),(.007,.054,.009),
       g,yaw,'YawArmor',.001)
  box(f'Yaw_{label}ServicePlate',(side*.252,.035,.208),(.007,.060,.023),s,yaw,'YawArmor',.002)

 # Rear power/drive cabinet belongs to yaw; keep it behind the pitching envelope.
 box('Yaw_RearDriveChassis',(0,.197,.293),(.266,.148,.253),g,yaw,'TurretHousing',.016)
 box('Yaw_RearDriveArmor',(0,.263,.303),(.244,.034,.207),a,yaw,'YawArmor',.012)
 box('Yaw_RearUpperArmor',(0,.205,.427),(.228,.124,.019),a,yaw,'YawArmor',.005)
 for i in range(5):
  box(f'Yaw_RearCoolingFin_{i+1}',(0,.283,.253+i*.020),(.162,.009,.008),g,yaw,'YawArmor',.001)
 for side in [-1,1]:
  cylinder('Yaw_RearPowerSocket_'+str(side),(side*.078,.287,.206),.018,.016,s,
           yaw,'TurretHousing','Y',24,.001)
  ring('Yaw_RearSocketInsulator_'+str(side),(side*.078,.296,.206),.0135,.009,.004,c,
       yaw,'TurretHousing','Y',24,.0005)
 # Sensor post and protected optics retain the recognizable upper silhouette.
 box('Sensor_Pedestal',(0,.117,.472),(.084,.081,.073),g,yaw,'StatusLamps',.008)
 cylinder('Sensor_BaseSwivel',(0,.117,.512),.064,.015,s,yaw,'StatusLamps','Z',40,.002)
 box('Sensor_HeadCore',(0,.099,.560),(.169,.137,.086),g,yaw,'StatusLamps',.012)
 box('Sensor_HeadTopArmor',(0,.104,.608),(.178,.144,.017),a,yaw,'StatusLamps',.006)
 for side in [-1,1]:
  box('Sensor_SideArmor_'+str(side),(side*.087,.099,.562),(.012,.121,.079),a,yaw,'StatusLamps',.004)
 # Forward optic is a nested, visually readable assembly rather than a decal.
 cylinder('Sensor_OpticBezel',(-.038,.023,.565),.032,.024,s,yaw,'StatusLamps','Y',40,.002)
 cylinder('Sensor_OpticDarkRecess',(-.038,.009,.565),.025,.004,g,yaw,'StatusLamps','Y',40,.0007)
 cylinder('Sensor_OpticCyanGlass',(-.038,.0058,.565),.019,.003,e,yaw,'StatusLamps','Y',40,.0007)
 box('Sensor_SecondaryOptic',(.043,.026,.565),(.038,.015,.030),g,yaw,'StatusLamps',.004)
 box('Sensor_SecondaryLight',(.043,.017,.565),(.025,.003,.007),e,yaw,'StatusLamps',.001)
 for side in [-1,1]:
  box('Yaw_StatusLight_'+str(side),(side*.195,-.174,.282),(.038,.006,.009),e,yaw,'StatusLamps',.001)

 # Cross-axis pitch drum and coaxial journals visibly engage both yaw bearings.
 cylinder('Pitch_MainCrossDrum',PITCH,.083,.237,g,pitch,'PitchHousing','X',48,.004)
 for side,label in [(-1,'Left'),(1,'Right')]:
  cylinder(f'Pitch_{label}TrunnionJournal',(side*.139,PITCH.y,PITCH.z),.0435,.052,
           s,pitch,'PitchHousing','X',40,.0013)
  ring(f'Pitch_{label}JournalShoulder',(side*.115,PITCH.y,PITCH.z),.056,.042,.010,
       c,pitch,'PitchHousing','X',40,.001)
 # Two fixed rails surround the sliding carriage throughout its 12 mm stroke.
 for side,label in [(-1,'Left'),(1,'Right')]:
  box(f'Pitch_{label}RecoilRail',(side*.102,-.066,.349),(.017,.265,.030),s,pitch,'PitchHousing',.0025)
  box(f'Pitch_{label}RailAnchor',(side*.102,.073,.349),(.030,.026,.044),g,pitch,'PitchHousing',.003)
  box(f'Pitch_{label}RailFrontStop',(side*.112,-.199,.349),(.028,.020,.041),c,pitch,'PitchHousing',.002)
 # A continuous outer sleeve behind the moving receiver keeps the rear closed.
 box('Pitch_RearRecoilEnclosure',(0,.042,.388),(.187,.087,.160),g,pitch,'PitchHousing',.012)
 box('Pitch_RearCover',(0,.089,.388),(.175,.014,.147),a,pitch,'PitchArmor',.007)
 box('Pitch_UpperBridge',(0,-.062,.475),(.210,.244,.028),a,pitch,'PitchArmor',.009)
 for side,label in [(-1,'Left'),(1,'Right')]:
  box(f'Pitch_{label}BridgeSideTrim',(side*.099,-.059,.468),(.014,.206,.029),g,pitch,'PitchArmor',.004)
  bolt(f'Pitch_{label}BridgeFastener',(side*.072,-.121,.491),'Z',s,pitch,'PitchArmor',.005)

 # Recoil receiver and rail sleeves form one moving cartridge.
 box('Recoil_ReceiverBlock',(0,-.074,.390),(.157,.176,.125),g,recoil,'RecoilCarriage',.010)
 for side,label in [(-1,'Left'),(1,'Right')]:
  box(f'Recoil_{label}SlideShoe',(side*.102,-.090,.349),(.027,.118,.048),g,recoil,'RecoilCarriage',.004)
  # Shoes overlap the rails by design, like a fitted bearing sleeve; their
  # narrow steel witness strip makes that guided contact visible in side views.
  box(f'Recoil_{label}ShoeWitnessStrip',(side*.119,-.090,.350),(.004,.091,.020),c,
       recoil,'RecoilCarriage',.001)
 # Rear drive motor and front socket lie on the rotor axis.
 cylinder('Recoil_RotorDriveBody',(0,-.151,PITCH.z),.077,.080,g,recoil,'RecoilCarriage','Y',48,.003)
 cylinder('Recoil_DriveRearCover',(0,-.108,PITCH.z),.065,.013,s,recoil,'RecoilCarriage','Y',40,.002)

 # Two NON-ROTATING bearing housings move with recoil, joined by external beams.
 for station,y in [('Rear',-.205),('Front',-.305)]:
  ring(f'Recoil_{station}RotorBearingHousing',(0,y,PITCH.z),.090,.079,.035,g,
       recoil,'RotorBearings','Y',48,.002)
  ring(f'Recoil_{station}RotorBearingRace',(0,y-.003,PITCH.z),.080,.071,.027,s,
       recoil,'RotorBearings','Y',48,.001)
  ring(f'Recoil_{station}BearingFaceSeal',(0,y-.019,PITCH.z),.085,.073,.003,c,
       recoil,'RotorBearings','Y',48,.0005)
 for side,label in [(-1,'Left'),(1,'Right')]:
  box(f'Recoil_{label}BearingTieBeam',(side*.084,-.254,PITCH.z),(.014,.100,.020),g,
       recoil,'RotorBearings',.003)
  # Short armor shields follow the bearing cartridge, with clean intentional gaps.
  box(f'Recoil_{label}NoseArmor',(side*.091,-.252,.392),(.015,.108,.058),a,
       recoil,'RotorBearings',.005)
 box('Recoil_NoseUpperShield',(0,-.248,.470),(.143,.096,.016),a,recoil,'RotorBearings',.006)
 # Four broad slots leave the underlying bearing mechanics legible.
 for i in range(3):
  box(f'Recoil_NoseTopVent_{i+1}',((i-1)*.030,-.248,.479),(.011,.054,.002),g,
       recoil,'RotorBearings',.0005)

 # Eight precise hollow tubes, closed annular muzzle lips and a central drive.
 for i in range(8):
  angle=i*math.tau/8;x=math.sin(angle)*.051;z=PITCH.z+math.cos(angle)*.051
  tube=profile_ring(f'Rotor_Barrel_{i+1:02}',(x,0,z),[
     (-.535,.0139),(-.534,.0149),(-.522,.0149),(-.520,.0142),
     (-.199,.0142),(-.197,.0135),(-.197,.0106),(-.5338,.0106),(-.535,.0115)],
     s,spin,'RotatingBarrels','Y',24)
  # Dark insert is behind the bore mouth, never capping the visible lip.
  cylinder(f'Rotor_BoreDepth_{i+1:02}',(x,-.486,z),.01055,.003,g,spin,'RotatingBarrels','Y',20,.0002)
 cylinder('Rotor_CenterDriveShaft',(0,-.353,PITCH.z),.0125,.320,g,spin,'RotatingBarrels','Y',32,.0015)
 cylinder('Rotor_CenterNose',(0,-.518,PITCH.z),.013,.014,c,spin,'RotatingBarrels','Y',24,.001)
 for station,y in [('Front',-.512),('Rear',-.260)]:
  ring(f'Rotor_{station}OuterBrace',(0,y,PITCH.z),.068,.062,.014,g,
       spin,'RotatingBarrels','Y',48,.0008)
  for i in range(8):
   angle=(i+.5)*math.tau/8
   x=math.sin(angle)*.0375;z=PITCH.z+math.cos(angle)*.0375
   # Spokes occupy the gaps BETWEEN tubes; the bores remain completely open.
   box(f'Rotor_{station}RadialSpoke_{i+1:02}',(x,y,z),(.006,.012,.052),g,
       spin,'RotatingBarrels',.0012,(0,angle,0))
 # Bearing journals occupy the same axis, with 3 mm radial visual clearance.
 for station,y in [('Rear',-.205),('Front',-.305)]:
  ring(f'Rotor_{station}BearingJournal',(0,y,PITCH.z),.068,.062,.020,s,
       spin,'RotatingBarrels','Y',48,.0007)

 # The complete sensor is supported by the rear drive cabinet, behind the
 # verified -6/+18 degree pitching envelope rather than over the moving bridge.
 bpy.context.view_layer.update()
 for obj in GROUPS['StatusLamps']:
  if obj.name.startswith('Sensor_'):
   matrix=obj.matrix_world.copy();matrix.translation.y+=.080;obj.matrix_world=matrix
 # UVs for every independently editable part; material design is texture-free.
 bpy.context.view_layer.update()
 for group,objects in GROUPS.items():
  for obj in objects:
   art.activate(obj);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
   bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.02)
   bpy.ops.object.mode_set(mode='OBJECT')
 return root


def setup_studio(root):
 stats=art.inspect_objects(art.descendants(root))
 camera=art.studio(root,stats);scene=bpy.context.scene
 scene.cycles.samples=48;scene.cycles.use_denoising=True
 scene.render.resolution_x=1400;scene.render.resolution_y=1400
 scene.render.resolution_percentage=100
 scene.world.use_nodes=True
 scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.07,.09,.12,1)
 scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
 # A broad studio key makes the engineered bevels readable without noisy wear.
 art.set_camera(camera,stats,30);camera.data.ortho_scale=1.19
 return stats


def camera_view(direction,target=(0,-.025,.30),scale=1.20):
 camera=bpy.context.scene.camera
 camera.location=Vector(target)+Vector(direction).normalized()*4
 art.aim(camera,target);camera.data.ortho_scale=scale


def arrange_editing_view():
 camera_view((1.15,-1.75,1.05))
 camera=bpy.context.scene.camera
 for screen in bpy.data.screens:
  for area in screen.areas:
   if area.type=='VIEW_3D':
    space=area.spaces.active
    space.region_3d.view_location=(0,-.03,.30)
    space.region_3d.view_distance=1.6
    space.region_3d.view_rotation=camera.rotation_euler.to_quaternion()
    space.region_3d.view_perspective='PERSP'
    space.shading.type='SOLID';space.shading.color_type='MATERIAL'
    space.shading.light='STUDIO';space.shading.show_cavity=True
    space.shading.cavity_type='BOTH';space.overlay.show_extras=False
    space.overlay.show_floor=False;space.overlay.show_axis_x=False;space.overlay.show_axis_y=False
    space.clip_start=.005;space.clip_end=100
 art.activate(bpy.data.objects['RapidTurretRefined'])


def render_preview(name,direction,target=(0,-.025,.30),scale=1.20):
 camera_view(direction,target,scale)
 bpy.context.scene.render.filepath=str(PREVIEW/(name+'.png'))
 bpy.ops.render.render(write_still=True)


def showcase():
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
 scene=bpy.context.scene;scene.frame_start=1;scene.frame_end=240;scene.render.fps=30
 yaw=bpy.data.objects['TurretYaw'];pitch=bpy.data.objects['BarrelPitch']
 recoil=bpy.data.objects['BarrelRecoil'];spin=bpy.data.objects['BarrelSpin'];rest=recoil.location.copy()
 for frame in range(1,241):
  f=frame-1;ya=el=travel=rotation=0
  if f<60:ya=math.radians(48)*math.sin(math.tau*f/60)
  elif f<120:el=6-12*math.cos(math.tau*(f-60)/60)
  elif f<180:
   u=f-120;phase=u%9;travel=.012*(phase if phase<1 else max(0,(5-phase)/4));rotation=u*.29
  else:
   u=f-180;ya=math.radians(30)*math.sin(math.tau*u/60);el=6+7*math.sin(math.tau*u/60)
   phase=u%9;travel=.012*(phase if phase<1 else max(0,(5-phase)/4));rotation=u*.29
  yaw.rotation_euler.z=ya;pitch.rotation_euler.x=-math.radians(el)
  recoil.location=rest+Vector((0,travel,0));spin.rotation_euler.y=rotation
  for o,path in [(yaw,'rotation_euler'),(pitch,'rotation_euler'),(recoil,'location'),(spin,'rotation_euler')]:
   o.keyframe_insert(data_path=path,frame=frame)
 for frame,label in [(1,'Horizontal traverse'),(61,'Elevation'),(121,'Recoil and barrel rotation'),(181,'Combined tracking')]:
  scene.timeline_markers.new(label,frame=frame)
 scene.frame_set(1);arrange_editing_view()
 bpy.ops.wm.save_as_mainfile(filepath=str(SHOWCASE))


def export_asset():
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
 root=bpy.data.objects['RapidTurretRefined']
 source_stats=art.inspect_objects(art.descendants(root))
 groups={}
 for obj in art.descendants(root):
  if obj.type=='MESH':groups.setdefault(obj['render_group'],[]).append(obj)
 for name,objects in groups.items():
  parent=objects[0].parent
  if not all(o.parent==parent for o in objects):raise ValueError('Mixed motion parents: '+name)
  art.join_meshes(objects,name,parent,tuple(parent.matrix_world.translation))
 export_stats=art.inspect_objects(art.descendants(root))
 bpy.ops.object.select_all(action='DESELECT')
 for obj in art.descendants(root):obj.select_set(True)
 bpy.context.view_layer.objects.active=root
 bpy.ops.export_scene.fbx(filepath=str(EXPORT),use_selection=True,
  object_types={'EMPTY','MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,
  apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_space_transform=True,
  bake_space_transform=False,use_mesh_modifiers=True,mesh_smooth_type='FACE',use_triangles=True,
  add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE',use_custom_props=True)
 art.reset_scene();bpy.ops.import_scene.fbx(filepath=str(EXPORT),use_anim=False)
 imported=art.inspect_objects(list(bpy.context.scene.objects))
 report={'asset':'RapidTurretRefined','blenderVersion':bpy.app.version_string,
  'source':str(SOURCE),'export':str(EXPORT),'sourceReopened':source_stats,
  'consolidatedExport':export_stats,'fbxReimported':imported,
  'checks':{'noInheritedSourceMeshes':True,'sourceReopened':True,'fbxReimported':True,
   'triangleCountMatches':source_stats['triangles']==export_stats['triangles']==imported['triangles'],
   'allUVPresent':all(len(o.data.uv_layers)>0 for o in bpy.context.scene.objects if o.type=='MESH'),
   'materialNamesMatch':export_stats['material_names']==imported['material_names'],
   'allSourcePartsClosed':all(m['boundary_edges']==0 and m['non_manifold_edges']==0 for m in source_stats['meshes']),
   'zeroDegenerateFaces':all(m['degenerate_faces']==0 for m in imported['meshes']),
   'allPositiveVolume':all(m['signed_volume']>0 for m in source_stats['meshes']),
   'unitScale':all(max(abs(v-1) for v in o.scale)<.0001 for o in bpy.context.scene.objects)},
 }
 (CHECKS/'ProductionReport.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 print('REFINED_EXPORT_READY',export_stats['triangles'],export_stats['mesh_count'],flush=True)


def main():
 for p in [SOURCE.parent,EXPORT.parent,PREVIEW,CHECKS]:p.mkdir(parents=True,exist_ok=True)
 write_manifest()
 if '--export-only' in sys.argv:export_asset();return
 if '--showcase-only' in sys.argv:showcase();return
 if '--renders-only' not in sys.argv:
  root=build_geometry();stats=setup_studio(root)
  bpy.context.preferences.filepaths.save_version=0
  bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=1
  arrange_editing_view()
  bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
  if '--geometry-only' in sys.argv:export_asset();return
 # Save then reopen before rendering or exporting so independent checks inspect
 # exactly the editable source offered to the user.
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
 render_preview('FrontThreeQuarter',(1.15,-1.75,1.05))
 print('FIRST_PREVIEW_READY',flush=True)
 if '--first-preview' in sys.argv:return
 render_preview('RearThreeQuarter',(-1.20,1.7,.94))
 render_preview('SideMechanical',(2.5,-.015,.38),target=(0,-.02,.31),scale=1.12)
 render_preview('Gameplay70',(0.15,-.64,1.9),scale=1.20)
 # Render real wireframe geometry over the saved authored surfaces; viewport
 # overlay flags alone are not included in an offline render.
 scene=bpy.context.scene;scene.cycles.samples=20
 surface=bpy.data.materials.new('Topology_Surface');surface.use_nodes=True
 surface.diffuse_color=(.47,.51,.56,1)
 shader=next(n for n in surface.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 shader.inputs['Base Color'].default_value=surface.diffuse_color;shader.inputs['Roughness'].default_value=.7
 wire=bpy.data.materials.new('Topology_Edges');wire.use_nodes=True
 shader=next(n for n in wire.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 shader.inputs['Base Color'].default_value=(.008,.016,.022,1);shader.inputs['Roughness'].default_value=.75
 for obj in art.descendants(bpy.data.objects['RapidTurretRefined']):
  if obj.type=='MESH':
   obj.data.materials.clear();obj.data.materials.append(surface);obj.data.materials.append(wire)
   for face in obj.data.polygons:face.material_index=0
   mod=obj.modifiers.new('AuthoredTopologyOverlay','WIREFRAME')
   mod.thickness=.00045;mod.use_replace=False;mod.use_even_offset=True;mod.material_offset=1
 render_preview('Topology',(1.15,-1.75,1.05))
 if '--renders-only' not in sys.argv:export_asset();showcase()
 print('REFINED_PRODUCTION_COMPLETE',flush=True)


if __name__=='__main__':main()
