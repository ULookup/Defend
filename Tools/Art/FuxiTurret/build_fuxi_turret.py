"""Reference-led Fuxi turret, authored closed hard-surface geometry in Blender.

The four supplied pictures are visual references only. No imported mesh,
billboard or old texture is used. Editable parts remain separate in the source.
"""
import bpy,bmesh,math,json,sys
from pathlib import Path
from mathutils import Vector,Matrix
PROJECT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(PROJECT/'Tools/Art/RapidTurretRefined'))
import build_refined_turret as hs
art=hs.art
SOURCE=PROJECT/'ArtSource/Production/FuxiTurret/FuxiTurret.blend'
EXPORT=PROJECT/'Assets/Defend/Content/Art/FuxiTurret/Models/FuxiTurret.fbx'
PREVIEW=PROJECT/'Docs/Art/FuxiTurret/Previews'
CHECKS=PROJECT/'Docs/Art/FuxiTurret/Checks'
PITCH=Vector((0,.084,.258));AXIS=Vector((0,-.057,.293))
MATERIALS=[
 {'name':'Fuxi_Armor','baseColor':[.68,.54,.40,1],'metallic':.12,'roughness':.40,'emissionStrength':0},
 {'name':'Fuxi_Graphite','baseColor':[.028,.030,.031,1],'metallic':.58,'roughness':.38,'emissionStrength':0},
 {'name':'Fuxi_Steel','baseColor':[.15,.165,.17,1],'metallic':.85,'roughness':.28,'emissionStrength':0},
 {'name':'Fuxi_Accent','baseColor':[.48,.275,.075,1],'metallic':.06,'roughness':.55,'emissionStrength':0},
 {'name':'Fuxi_Amber','baseColor':[1.0,.25,.018,1],'metallic':0,'roughness':.25,'emissionStrength':2.1},
 {'name':'Fuxi_Glass','baseColor':[.007,.009,.012,1],'metallic':.24,'roughness':.10,'emissionStrength':0},
]

def mats():
 out={}
 for spec in MATERIALS:
  m=bpy.data.materials.new(spec['name']);m.use_nodes=True;m.diffuse_color=spec['baseColor']
  p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
  for k,v in [('Base Color',spec['baseColor']),('Metallic',spec['metallic']),('Roughness',spec['roughness']),
              ('Emission Color',spec['baseColor']),('Emission Strength',spec['emissionStrength'])]:p.inputs[k].default_value=v
  m.metallic=spec['metallic'];m.roughness=spec['roughness'];out[spec['name'][5:]]=m
 return out

def box(*args,**kw):return hs.box(*args,**kw)
def cyl(name,loc,radius,depth,mat,parent,group,axis='Z',verts=72,bevel=.0015):
 if radius<.035 and verts>=60:verts=36
 return hs.cylinder(name,loc,radius,depth,mat,parent,group,axis,verts,bevel)
def ring(name,loc,outer,inner,depth,mat,parent,group,axis='Y',segments=72,bevel=.001):
 if (outer<.035 or any(tag in name for tag in ['BearingRace','TrunnionBearing','BearingJournal'])) and segments>=60:segments=36
 return hs.ring(name,loc,outer,inner,depth,mat,parent,group,axis,segments,bevel)
def bolt(name,loc,axis,mat,parent,group,radius=.0028):
 return cyl(name,loc,radius,.0022,mat,parent,group,axis,6,.0004)

def mesh_part(name,verts,faces,mat,parent,group,bevel=.001):
 mesh=bpy.data.meshes.new(name+'Mesh');mesh.from_pydata(verts,[],faces);mesh.update()
 bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
 bevel_segments=1 if name.startswith(('Base_UpperVentFrame','Base_AnchorLiftingEye','Base_AnchorDiagonal','Guard_PerforatedShell')) else 2
 return hs.finish(o,name,mat,parent,group,bevel,bevel_segments,True)

def oct_frame(name,center,width,height,inner_width,inner_height,depth,mat,parent,group,axis='Y',corner=.024,bevel=.0014,omit_bottom=False):
 def points(w,h,c):
  return [(-w/2+c,-h/2),(w/2-c,-h/2),(w/2,-h/2+c),(w/2,h/2-c),
          (w/2-c,h/2),(-w/2+c,h/2),(-w/2,h/2-c),(-w/2,-h/2+c)]
 verts=[]
 for d in [-depth/2,depth/2]:
  for w,h in [(width,height),(inner_width,inner_height)]:
   for u,v in points(w,h,min(corner,w*.2,h*.2)):
    q=Vector((u,d,v) if axis=='Y' else (d,u,v));verts.append(tuple(Vector(center)+q))
 faces=[]
 for i in range(8):
  if omit_bottom and i==0:continue
  j=(i+1)%8
  faces.extend([(i,j,16+j,16+i),(8+i,24+i,24+j,8+j),
                (i,8+i,8+j,j),(16+i,16+j,24+j,24+i)])
 if omit_bottom:faces.extend([(1,17,25,9),(0,8,24,16)])
 return mesh_part(name,verts,faces,mat,parent,group,bevel)

def octagonal_bezel(mat,parent,group):
 verts=[];n=72
 profile=[(-.015,.0898,False),(-.013,.091,False),(.013,.091,False),(.015,.0898,False),(.015,.082,True),(-.015,.082,True)]
 for d,r,circular in profile:
  for i in range(n):
   a=math.tau*i/n
   radius=r if circular else r*math.cos(math.pi/8)/math.cos((a+math.pi/8)%(math.pi/4)-math.pi/8)
   verts.append((math.sin(a)*radius,-.09+d,.293+math.cos(a)*radius))
 faces=[]
 for j in range(len(profile)):
  nxt=(j+1)%len(profile)
  for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,nxt*n+(i+1)%n,nxt*n+i))
 obj=mesh_part('Pitch_FrontInnerArmorBezel',verts,faces,mat,parent,group,0)
 obj.data.set_sharp_from_angle(angle=math.radians(30))
 art.activate(obj);mod=obj.modifiers.new('OctagonalFaceNormals','WEIGHTED_NORMAL');mod.keep_sharp=True
 bpy.ops.object.modifier_apply(modifier=mod.name)
 return obj

def folded_armor(name,x,base_y,z0,z1,width,offset0,offset1,outward,mat,parent,group):
 # Thick removable bent sheet, with only a hairline seam between sections.
 c=.007;pts=[(-width/2+c,z0),(width/2-c,z0),(width/2,z0+c),(width/2,z1-c),
             (width/2-c,z1),(-width/2+c,z1),(-width/2,z1-c),(-width/2,z0+c)]
 verts=[]
 for inset in [0,.019]:
  for u,z in pts:
   offset=offset0+(offset1-offset0)*(z-z0)/(z1-z0)
   verts.append((x+u,base_y+outward*(offset-inset),z))
 faces=[tuple(range(8)),tuple(reversed(range(8,16)))]
 faces.extend((i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8))
 return mesh_part(name,verts,faces,mat,parent,group,.0017)

def base_anchor_structure(index,angle,mat,steel,parent,group):
 transform=Matrix.Rotation(-angle,4,'Z')
 outline=[(.324,.024),(.337,.079),(.353,.079),(.403,.024),(.386,.024),(.347,.061),(.341,.024)]
 for side in [-1,1]:
  points=[tuple(transform@Vector((side*.029,r,z))) for r,z in outline]
  obj=hs.panel(f'Base_AnchorDiagonal_{index}_{side}',[(side*.029,r,z) for r,z in outline],.009,mat,parent,group,'X',.0015)
  obj.matrix_world=transform@obj.matrix_world
 # Recessed middle load face sits behind the paired sloped side rails.
 p=[(-.024,.339,.061),(.024,.339,.061),(.024,.391,.028),(-.024,.391,.028)]
 verts=[tuple(transform@Vector((t,r+d,z))) for d in [0,.006] for t,r,z in p]
 mesh_part(f'Base_AnchorRecess_{index}',verts,[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],mat,parent,group,.001)
 hoist=oct_frame(f'Base_AnchorLiftingEye_{index}',(0,.402,.040),.030,.032,.016,.016,.009,steel,parent,group,'Y',.008,.0005)
 hoist.matrix_world=transform@hoist.matrix_world

def swept_front_frame(mat,parent,group):
 # The external armor stays on the yaw chassis. Its upper shoulder sweeps
 # rearward, leaving the internal pitch cradle free beneath the optical head.
 outer=[(-.115,.151),(.115,.151),(.143,.179),(.143,.375),(.115,.403),(-.115,.403),(-.143,.375),(-.143,.179)]
 inner=[(-.0895,.1735),(.0895,.1735),(.1175,.2015),(.1175,.3525),(.0895,.3805),(-.0895,.3805),(-.1175,.3525),(-.1175,.2015)]
 def fore(i):return .045 if i in [4,5] else -.068
 for i in range(8):
  if i==0:continue
  j=(i+1)%8
  # Tiny actual panel seams make the armor joints readable in oblique views.
  coords=[]
  for points in [outer,inner]:
   p=Vector((points[i][0],fore(i),points[i][1]));q=Vector((points[j][0],fore(j),points[j][1]))
   coords.extend([p.lerp(q,.003),p.lerp(q,.997)])
  verts=[tuple(v+Vector((0,d,0))) for d in [-.0125,.0125] for v in coords]
  faces=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
  mesh_part(f'Yaw_FrontArmorSegment_{i}',verts,faces,mat,parent,group,.0017)

def sloped_sector(name,angle,width,r_low,r_high,z_low,z_high,mat,parent,group):
 # Removable sloped skirt slab with a shallow stepped lower service notch.
 tang=Vector((math.cos(angle),-math.sin(angle),0));rad=Vector((math.sin(angle),math.cos(angle),0))
 outline=[(-width/2+.004,z_low),(-.037,z_low),(-.030,z_low+.012),(.030,z_low+.012),
          (.037,z_low),(width/2-.004,z_low),(width/2,z_low+.006),(width/2,z_high-.004),
          (width/2-.004,z_high),(-width/2+.004,z_high),(-width/2,z_high-.004),(-width/2,z_low+.006)]
 verts=[];n=len(outline)
 for inset in [0,.014]:
  for t,z in outline:
   radius=r_low+(r_high-r_low)*(z-z_low)/(z_high-z_low)-inset
   verts.append(tuple(rad*radius+tang*t+Vector((0,0,z))))
 faces=[tuple(range(n)),tuple(reversed(range(n,2*n)))]
 faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
 return mesh_part(name,verts,faces,mat,parent,group,.0025)

def radial_box(name,r,a,z,size,mat,parent,group,bevel=.002):
 return box(name,(math.sin(a)*r,math.cos(a)*r,z),size,mat,parent,group,bevel,(0,0,-a))

def service_cover(name,center,size,axis,mat,metal,parent,group):
 o=box(name,center,size,mat,parent,group,.003)
 c=Vector(center)
 outward=(1 if c.y>.10 else -1) if axis=='Y' else (1 if c.x>0 else -1)
 direction=Vector((0,outward,0) if axis=='Y' else (outward,0,0))
 axis_index=1 if axis=='Y' else 0
 gasket_size=[v*1.07 for v in size];gasket_size[axis_index]=.004
 box(name+'_InsetGasket',c-direction*(size[axis_index]*.36),gasket_size,
     bpy.data.materials['Fuxi_Graphite'],parent,group,.002)
 if axis=='Y':
  for u in [-1,1]:
   for v in [-1,1]:bolt(name+f'_Bolt_{u}_{v}',(c.x+u*size[0]*.36,c.y+outward*(size[1]/2+.001),c.z+v*size[2]*.35),'Y',metal,parent,group)
 else:
  s=1 if c.x>0 else -1
  for u in [-1,1]:
   for v in [-1,1]:bolt(name+f'_Bolt_{u}_{v}',(c.x+s*(size[0]/2+.001),c.y+u*size[1]*.35,c.z+v*size[2]*.35),'X',metal,parent,group)
 return o

def flat_panel(name,center,width,height,depth,mat,parent,group,axis='Y',corner=.006):
 x,y,z=center
 pts=[(-width/2+corner,-height/2),(width/2-corner,-height/2),(width/2,-height/2+corner),
      (width/2,height/2-corner),(width/2-corner,height/2),(-width/2+corner,height/2),
      (-width/2,height/2-corner),(-width/2,-height/2+corner)]
 world=[(x+u,y,z+v) if axis=='Y' else (x,y+u,z+v) for u,v in pts]
 return hs.panel(name,world,depth,mat,parent,group,axis,.0015)

def disc_sector(name,center,inner,outer,start,end,depth,mat,parent,group):
 x,y,z=center;steps=24;verts=[]
 for d in [-depth/2,depth/2]:
  for radius in [inner,outer]:
   for i in range(steps+1):
    a=start+(end-start)*i/steps;verts.append((x+math.sin(a)*radius,y+d,z+math.cos(a)*radius))
 n=steps+1;faces=[]
 for i in range(steps):
  faces.extend([(i,i+1,n+i+1,n+i),(2*n+i,3*n+i,3*n+i+1,2*n+i+1),
                (i,2*n+i,2*n+i+1,i+1),(n+i,n+i+1,3*n+i+1,3*n+i)])
 faces.extend([(0,n,3*n,2*n),(n-1,3*n-1,4*n-1,2*n-1)])
 return mesh_part(name,verts,faces,mat,parent,group,.00025)

def cage_strip(name,corners,mat,parent,group):
 # A solid strip on the tapering protective shroud, leaving real open windows.
 def radius(y):return .066+(.080-.066)*(y+.225)/.148
 verts=[]
 for inset in [0,.004]:
  for y,a in corners:
   r=radius(y)-inset;verts.append((math.sin(a)*r,y,.293+math.cos(a)*r))
 return mesh_part(name,verts,[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],mat,parent,group,.0011)

def perforated_guard(mat,parent,group):
 # A single closed conical sheet. Shared tile boundaries are welded; only
 # actual aperture boundaries and the two end rims receive thickness walls.
 positions=[];lookup={};surface=[]
 def vertex(u,v):
  y=-.225+.148*v
  angle=(u+.12*v)*math.tau/8
  r=.066+.014*v
  key=(round(math.sin(angle)*r,8),round(y,8),round(.293+math.cos(angle)*r,8))
  if key not in lookup:lookup[key]=len(positions);positions.append(key)
  return lookup[key]
 def chamfer(points,f=.16):
  out=[]
  for i,p in enumerate(points):
   p=Vector(p);prev=Vector(points[(i-1)%len(points)]);nxt=Vector(points[(i+1)%len(points)])
   out.extend([tuple(p.lerp(prev,f)),tuple(p.lerp(nxt,f))])
  return out
 levels=[0,.24,.67,1]
 for i in range(8):
  for row in range(3):
   v0,v1=levels[row:row+2]
   outer=[(0,0),(.5,0),(1,0),(1,.5),(1,1),(.5,1),(0,1),(0,.5)]
   # Chosen in the same counterclockwise order as the rectangular tile.
   if row==1:
    quad=[(.14,.12),(.80,.27),(.88,.85),(.24,.76)] if i%2==0 else [(.23,.26),(.87,.10),(.77,.76),(.12,.86)]
   else:quad=[(.13,.25),(.81,.17),(.89,.71),(.22,.82)]
   corners=chamfer(quad,.15)
   # chamfer starts just before bottom-left; rotate so each corner pair
   # corresponds to its nearby outer tile corner and edge midpoint.
   holes=corners[1:]+corners[:1]
   outer_ids=[vertex(i+u,v0+(v1-v0)*v) for u,v in outer]
   hole_ids=[vertex(i+u,v0+(v1-v0)*v) for u,v in holes]
   for j in range(8):surface.append((outer_ids[j],outer_ids[(j+1)%8],hole_ids[(j+1)%8],hole_ids[j]))
 n=len(positions);inner=[]
 for x,y,z in positions:
  r=math.hypot(x,z-.293);f=(r-.004)/r;inner.append((x*f,y,.293+(z-.293)*f))
 counts={};directions={}
 for face in surface:
  for k,a in enumerate(face):
   b=face[(k+1)%len(face)];edge=tuple(sorted((a,b)));counts[edge]=counts.get(edge,0)+1;directions[edge]=(a,b)
 faces=list(surface)+[tuple(j+n for j in reversed(face)) for face in surface]
 for edge,count in counts.items():
  if count==1:
   a,b=directions[edge];faces.append((a,a+n,b+n,b))
 obj=mesh_part('Guard_PerforatedShell',positions+inner,faces,mat,parent,group,.0008)
 # Triangulate warped bevel quads before the final custom-normal solution.
 art.activate(obj);mod=obj.modifiers.new('ExplicitConicalTriangles','TRIANGULATE')
 mod.keep_custom_normals=False;bpy.ops.object.modifier_apply(modifier=mod.name)
 obj.data.set_sharp_from_angle(angle=math.radians(45))
 mod=obj.modifiers.new('FinalTriangleWeightedNormals','WEIGHTED_NORMAL');mod.keep_sharp=True
 bpy.ops.object.modifier_apply(modifier=mod.name)
 return obj

def add_secondary_details(root,yaw,pitch,materials):
 """Reference-led secondary surfaces, bounded to 6,720 net triangles.

 No rig, primary silhouette, recoil guide, bore or bearing surface is changed.
 All additions are closed solids on existing mounting surfaces. Pitch disc
 details remain on Pitch; central fixed additions stay outside |X| < .110.
 The authoring coordinator runs the rebuild and independent checks.
 """
 a=materials['Armor'];g=materials['Graphite'];s=materials['Steel']
 def triangle_count():
  total=0
  for objects in hs.GROUPS.values():
   for obj in objects:
    obj.data.calc_loop_triangles();total+=len(obj.data.loop_triangles)
  return total
 before_triangles=triangle_count()
 before_names={obj.name for objects in hs.GROUPS.values() for obj in objects}
 categories={}
 def record(obj,category):
  obj['secondary_detail']=True;obj['secondary_detail_category']=category
  categories[category]=categories.get(category,0)+1
  return obj
 def strip(name,start,end,normal,width,material,parent,group,category):
  start=Vector(start);end=Vector(end);normal=Vector(normal).normalized()
  along=(end-start).normalized();across=normal.cross(along).normalized()*width/2
  # Deliberate end clearance prevents adjacent seam solids sharing cap faces.
  start+=along*.0003;end-=along*.0003
  verts=[tuple(p+edge+normal*d) for d in [-.00012,.00030]
         for p,edge in [(start,-across),(end,-across),(end,across),(start,across)]]
  faces=[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
  return record(mesh_part(name,verts,faces,material,parent,group,0),category)
 def well(name,point,normal,parent,group,radius=.0027):
  # One stepped, closed eight-sided shell: 60 triangles, no Boolean cuts,
  # coincident washer/bolt faces or bevel slivers. The steel floor is recessed.
  point=Vector(point);normal=Vector(normal).normalized()
  u=Vector((0,1,0)) if abs(normal.x)>.9 else Vector((1,0,0))
  u=(u-normal*u.dot(normal)).normalized();v=normal.cross(u);n=8;verts=[]
  for depth,r in [(-.00025,radius),(.00065,radius),(.00065,radius*.62),(.00020,radius*.53)]:
   for i in range(n):
    angle=math.tau*i/n
    verts.append(tuple(point+normal*depth+(u*math.cos(angle)+v*math.sin(angle))*r))
  faces=[]
  for j in range(3):
   for i in range(n):faces.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
  faces.extend([tuple(reversed(range(n))),tuple(range(3*n,4*n))])
  obj=mesh_part(name,verts,faces,g,parent,group,0);obj.data.materials.append(s)
  obj.data.polygons[-1].material_index=1
  return record(obj,'recessed_fastener')
 def arc_band(name,side,inner,outer,start,end):
  steps=6;verts=[];center=Vector((side*.3399,.084,.258))
  for depth in [-.0008,.0008]:
   for radius in [inner,outer]:
    for i in range(steps+1):
     angle=start+(end-start)*i/steps
     verts.append(tuple(center+Vector((side*depth,math.cos(angle)*radius,math.sin(angle)*radius))))
  n=steps+1;faces=[]
  for i in range(steps):
   faces.extend([(i,i+1,n+i+1,n+i),(2*n+i,3*n+i,3*n+i+1,2*n+i+1),
                 (i,2*n+i,2*n+i+1,i+1),(n+i,n+i+1,3*n+i+1,3*n+i)])
  faces.extend([(0,n,3*n,2*n),(n-1,3*n-1,4*n-1,2*n-1)])
  return record(mesh_part(name,verts,faces,a,pitch,'PitchArmor',0),'disc_pressure_band')
 def cut_cover(name):
  old=bpy.data.objects.get(name)
  if not old:raise RuntimeError('Expected editable disc cover: '+name)
  points=[old.matrix_world@v.co for v in old.data.vertices]
  low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
  high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
  c=(low+high)/2;size=high-low;parent=old.parent;group=old['render_group'];mat=old.data.materials[0]
  hs.GROUPS[group].remove(old);bpy.data.objects.remove(old,do_unlink=True)
  w=size.y;h=size.z;corner=.007 if not name.endswith('InsetGasket') else .0075
  outline=[(-w/2+corner,-h/2),(w/2-corner,-h/2),(w/2,-h/2+corner),(w/2,h/2-corner),
           (w/2-corner,h/2),(-w/2+corner,h/2),(-w/2,h/2-corner),(-w/2,-h/2+corner)]
  verts=[(c.x+d,c.y+y,c.z+z) for d in [-size.x/2,size.x/2] for y,z in outline]
  faces=[tuple(range(8)),tuple(reversed(range(8,16)))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
  obj=mesh_part(name,verts,faces,mat,parent,group,0)
  art.activate(obj);mod=obj.modifiers.new('SecondaryCoverChamfer','BEVEL')
  mod.width=.00035;mod.segments=1;mod.affect='EDGES';bpy.ops.object.modifier_apply(modifier=mod.name)
  obj.data.set_sharp_from_angle(angle=math.radians(45))
  mod=obj.modifiers.new('SecondaryCoverNormals','WEIGHTED_NORMAL');mod.keep_sharp=True
  bpy.ops.object.modifier_apply(modifier=mod.name)
  return record(obj,'chamfered_disc_cover')

 # Shoulder cladding: shallow inset perimeter seams, flush edge fasteners and
 # short return clips define the folded plates without changing their outline.
 for side,label in [(-1,'Left'),(1,'Right')]:
  x=side*.244
  for base_y,tag,outward in [(-.110,'Front',-1),(.243,'Rear',1)]:
   prefix=f'Yaw_{tag}{label}Secondary'
   normal=(0,outward,0);face_y=base_y+outward*.023
   outline=[(-.046,.205),(.046,.205),(.053,.212),(.053,.299),(.046,.306),(-.046,.306),(-.053,.299),(-.053,.212)]
   for i,p in enumerate(outline):
    q=outline[(i+1)%len(outline)]
    strip(prefix+f'_PanelSeam_{i}',(x+p[0],face_y,p[1]),(x+q[0],face_y,q[1]),normal,.00065,g,yaw,'YawArmor','shoulder_inset_seam')
   for dx in [-.048,.048]:
    for z in [.211,.300]:well(prefix+f'_MiddleFastener_{dx}_{z}',(x+dx,face_y,z),normal,yaw,'YawArmor')
   for idx,(z,z0,z1,o0,o1) in enumerate([(.390,.315,.400,.023,.002),(.151,.139,.196,.002,.023)]):
    offset=o0+(o1-o0)*(z-z0)/(z1-z0);normal_fold=(0,outward,-(o1-o0)/(z1-z0))
    for dx in [-.046,.046]:well(prefix+f'_FoldFastener_{idx}_{dx}',(x+dx,base_y+outward*offset,z),normal_fold,yaw,'YawArmor',.0023)
   for dx in [-.057,.057]:
    path=[(.307,.023),(.3145,.023),(.323,.023+(.002-.023)*(.323-.315)/(.400-.315))]
    verts=[(x+dx+edge,base_y+outward*(offset+depth),z)
           for depth in [-.0002,.0010] for z,offset in path for edge in [-.0025,.0025]]
    faces=[(0,1,3,2),(2,3,5,4),(6,8,9,7),(8,10,11,9),
           (0,6,7,1),(4,5,11,10),(0,2,8,6),(2,4,10,8),(1,7,9,3),(3,9,11,5)]
    clip=mesh_part(prefix+f'_ReturnClip_{dx}',verts,faces,a,yaw,'YawArmor',0)
    record(clip,'shoulder_return_clip')

 # Concentric compression bands belong to the same moving side disc; their
 # radius stays below .122, well inside the fixed frame's .140 inner boundary.
 for side,label in [(-1,'Left'),(1,'Right')]:
  for j in range(8):
   start=j*math.tau/8+.009;end=(j+1)*math.tau/8-.009
   arc_band(f'Pitch_{label}SecondaryOuterBand_{j}',side,.1193,.1217,start,end)
  inner=ring(f'Pitch_{label}SecondaryInnerBand',(side*.3398,.084,.258),.1140,.1129,.0016,s,pitch,'PitchArmor','X',48,0)
  record(inner,'disc_pressure_band')
  for offset in [-.040,.051]:
   name=f'Pitch_{label}DiscService_{offset}'
   cut_cover(name);cut_cover(name+'_InsetGasket')

 # Surface fittings on the fixed front armor. No new solid spans the clear
 # pitch aperture; mechanical ribs remain on the existing side actuator bays.
 for side in [-1,1]:
  for z in [.226,.338]:well(f'Yaw_FrontFrameSecondaryFastener_{side}_{z}',(side*.132,-.0805,z),(0,-1,0),yaw,'YawArmor',.0023)
  panel=box(f'Yaw_FrontFrameSecondaryAccess_{side}',(side*.132,-.0810,.278),(.007,.0016,.031),g,yaw,'YawArmor',0)
  record(panel,'frame_access_inlay')
  for dx in [.1485,.1905]:
   rib=box(f'Yaw_FrontActuatorSecondaryRib_{side}_{dx}',(side*dx,-.079,.276),(.002,.008,.154),s,yaw,'TurretHousing',0)
   record(rib,'actuator_backing_rib')
 for x in [-.067,.067]:well('Yaw_FrontUpperSecondaryFastener_'+str(x),(x,.0325,.392),(0,-1,0),yaw,'YawArmor',.0023)

 # Four shallow turntable access pads are above the existing yaw deck, below
 # the chassis. They do not raise the fixed skirt or intrude into a moving joint.
 for i in range(4):
  angle=math.pi/4+i*math.pi/2
  pad=radial_box(f'Yaw_TurntableSecondaryAccess_{i}',.283,angle,.1205,(.036,.014,.0014),g,yaw,'TurretHousing',0)
  record(pad,'turntable_access_pad')
  for sign in [-1,1]:
   point=(math.sin(angle)*.283+math.cos(angle)*sign*.012,
          math.cos(angle)*.283-math.sin(angle)*sign*.012,.1212)
   well(f'Yaw_TurntableSecondaryFastener_{i}_{sign}',point,(0,0,1),yaw,'TurretHousing',.0021)

 # Optical shell assembly seams and mounting fasteners stay within the
 # existing equatorial band's envelope; neither head position nor support moves.
 for i,dy in enumerate([-.019,.021]):
  radius=math.sqrt(.044**2-dy**2)+.00020
  obj=ring(f'Sensor_OpticSecondaryShellSeam_{i}',(-.066,-.017+dy,.464),radius,radius-.00085,.0007,g,yaw,'StatusLamps','Y',36,0)
  record(obj,'optic_shell_joint')
 for label,center_x,y,z,spacing in [('Optic',-.066,.066,.424,.021),('Radar',.077,.090,.428,.017)]:
  for side in [-1,1]:well(f'Sensor_{label}SecondaryMountFastener_{side}',(center_x+side*spacing,y,z),(0,0,1),yaw,'StatusLamps',.0021)

 added=triangle_count()-before_triangles
 root['secondary_detail_net_triangles']=added
 root['secondary_detail_new_objects']=sum(obj.name not in before_names for objects in hs.GROUPS.values() for obj in objects)
 root['secondary_detail_categories']=json.dumps(categories,sort_keys=True)
 if added>6720:raise RuntimeError(f'Secondary detail budget exceeded: {added} > 6720 triangles; source/export not saved.')
 print('FUXI_SECONDARY_DETAILS',added,root['secondary_detail_new_objects'],json.dumps(categories,sort_keys=True),flush=True)


def build():
 art.reset_scene();hs.GROUPS.clear()
 root=art.empty('FuxiTurret',(0,0,0));yaw=art.empty('TurretYaw',(0,0,.103),root)
 pitch=art.empty('BarrelPitch',PITCH,yaw);recoil=art.empty('BarrelRecoil',AXIS,pitch)
 spin=art.empty('BarrelSpin',(0,-.20,.293),recoil)
 art.empty('Muzzle',(0,-.542,.336),spin);art.empty('ForwardReference',(0,-.5,0),root)
 root['reference_basis']='User supplied front, rear, left, right images; no numeric similarity claim.'
 root['forward_axis']='-Y';root['pitch_limits_degrees']=[-6,18];root['recoil_distance_meters']=.012
 m=mats();a=m['Armor'];g=m['Graphite'];s=m['Steel'];accent=m['Accent'];amber=m['Amber'];glass=m['Glass']
 # Broad shallow base with slanted skirt armor and inset anchor legs.
 cyl('Base_LowerRim',(0,0,.012),.385,.020,g,root,'FixedBase','Z',72,.003)
 hs.profile_ring('Base_SlopedCore',(0,0,0),[(.014,.346),(.085,.307),(.085,.08),(.014,.08)],g,root,'FixedBase','Z',72)
 for i in range(8):
  ang=(i+.5)*math.tau/8
  sloped_sector(f'Base_SkirtPanel_{i+1:02}',ang,.221,.376,.337,.018,.086,a,root,'FixedBase')
  radial_box(f'Base_UpperVent_{i+1:02}',.349,ang,.079,(.070,.010,.007),g,root,'FixedBase',.001)
  ventframe=oct_frame(f'Base_UpperVentFrame_{i+1:02}',(0,0,0),.087,.015,.070,.006,.003,a,root,'FixedBase','Y',.003,0)
  ventframe.matrix_world=Matrix.Translation((math.sin(ang)*.352,math.cos(ang)*.352,.080))@Matrix.Rotation(-ang,4,'Z')@Matrix.Rotation(math.atan(.039/.068),4,'X')
  for side in [-1,1]:
   r=.361;t=.091*side
   p=Vector((math.sin(ang)*r+math.cos(ang)*t,math.cos(ang)*r-math.sin(ang)*t,.043))
   bolt(f'Base_PanelFastener_{i+1}_{side}',p,'Z',s,root,'FixedBase',.0024)
  footang=i*math.tau/8
  radial_box(f'Base_AnchorFoot_{i+1:02}',.377,footang,.012,(.093,.086,.024),g,root,'FixedBase',.004)
  base_anchor_structure(i+1,footang,g,s,root,'FixedBase')
  radial_box(f'Base_AnchorLamp_{i+1:02}',.380,footang,.032,(.025,.006,.006),amber,root,'FixedBase',.001)
  cyl(f'Base_AnchorBolt_{i+1:02}',(math.sin(footang)*.387,math.cos(footang)*.387,.029),.008,.009,s,root,'FixedBase','Z',12,.001)
 ring('Base_UpperRing',(0,0,.094),.337,.292,.009,s,root,'FixedBase','Z',72,.001)
 base_rotation=Matrix.Rotation(math.pi/8,4,'Z')
 for o in hs.GROUPS['FixedBase']:o.matrix_world=base_rotation@o.matrix_world
 bpy.context.view_layer.update()
 bounds=[o.matrix_world@v.co for o in hs.GROUPS['FixedBase'] for v in o.data.vertices]
 base_width=max(v.x for v in bounds)-min(v.x for v in bounds)
 base_scale=Matrix.Diagonal((.84/base_width,.84/base_width,1,1))
 for o in hs.GROUPS['FixedBase']:
  o.matrix_world=base_scale@o.matrix_world
  art.activate(o);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 cyl('Yaw_Turntable',(0,0,.105),.318,.020,g,yaw,'TurretHousing','Z',72,.003)
 cyl('Yaw_TurntableTop',(0,0,.117),.305,.006,s,yaw,'TurretHousing','Z',72,.001)
 # Two deep side housings, their huge discs and octagonal frames dominate.
 for side,label in [(-1,'Left'),(1,'Right')]:
  x=side*.244
  core_profile=[(x,-.120,.166),(x,-.120,.348),(x,-.066,.397),(x,.219,.397),
                (x,.247,.367),(x,.247,.165),(x,.218,.133),(x,-.077,.133)]
  hs.panel(f'Yaw_{label}SideCore',core_profile,.130,g,yaw,'TurretHousing','X',.008)
  side_profile=[(side*.310,-.110,.170),(side*.310,-.110,.344),(side*.310,-.061,.397),
                (side*.310,.215,.397),(side*.310,.243,.365),(side*.310,.243,.168),
                (side*.310,.214,.138),(side*.310,-.073,.138)]
  hs.panel(f'Yaw_{label}BroadSideArmor',side_profile,.014,a,yaw,'YawArmor','X',.003)
  box(f'Yaw_{label}UpperArmor',(x,.084,.397),(.120,.260,.015),a,yaw,'YawArmor',.004)
  for y,tag,outward in [(-.110,'Front',-1),(.243,'Rear',1)]:
   for idx,(z0,z1,w,o0,o1) in enumerate([(.315,.400,.120,.023,.002),(.197,.314,.126,.023,.023),(.139,.196,.124,.002,.023)]):
    folded_armor(f'Yaw_{label}{tag}ArmorSection_{idx}',x,y,z0,z1,w,o0,o1,outward,a,yaw,'YawArmor')
   service_cover(f'Yaw_{label}{tag}UpperService',(x,y+outward*.014,.377),(.072,.019,.043),'Y',a,s,yaw,'YawArmor')
   service_cover(f'Yaw_{label}{tag}LowerService',(x-side*.012,y+outward*.026,.231),(.057,.009,.049),'Y',a,s,yaw,'YawArmor')
  # Actual X trunnions share the large side-disc center but support a higher gun axis.
  cyl(f'Yaw_{label}TrunnionBody',(side*.178,.084,.258),.075,.074,g,yaw,'TurretHousing','X',72,.003)
  ring(f'Yaw_{label}TrunnionBearing',(side*.165,.084,.258),.067,.048,.026,s,yaw,'TurretHousing','X',72,.001)
  cyl(f'Pitch_{label}TrunnionJournal',(side*.150,.084,.258),.0475,.052,s,pitch,'PitchHousing','X',72,.001)
  cyl(f'Yaw_{label}DiscBack',(side*.309,.084,.258),.139,.030,g,yaw,'TurretHousing','X',72,.003)
  # Static outer frame, moving inner armor disc and small broad service plates.
  profile=[(side*.304,.160),(side*.320,.160),(side*.341,.153),(side*.345,.150),
           (side*.345,.142),(side*.337,.140),(side*.308,.141)]
  frame=hs.profile_ring(f'Yaw_{label}OctagonalFrame',(0,.084,.258),profile,a,yaw,'YawArmor','X',8)
  rot=Matrix.Rotation(math.pi/8,4,'X');center=Vector((0,.084,.258))
  for v in frame.data.vertices:v.co=center+rot.to_3x3()@(v.co-center)
  cyl(f'Pitch_{label}LargeArmorDisc',(side*.328,.084,.258),.126,.023,a,pitch,'PitchArmor','X',72,.002)
  ring(f'Pitch_{label}ArmorDiscLip',(side*.340,.084,.258),.128,.123,.004,s,pitch,'PitchArmor','X',72,.0006)
  for yoff,zoff in [(-.040,-.033),(.051,-.006)]:
   service_cover(f'Pitch_{label}DiscService_{yoff}',(side*.344,.084+yoff,.258+zoff),(.006,.051,.051),'X',a,s,pitch,'PitchArmor')
  for j in range(4):
   ang=math.pi/4+j*math.pi/2
   y=.084+math.cos(ang)*.141;z=.258+math.sin(ang)*.141
   box(f'Yaw_{label}RimLampSocket_{j}',(side*.338,y,z),(.009,.026,.010),g,yaw,'YawArmor',.002,(ang,0,0))
   box(f'Yaw_{label}RimLamp_{j}',(side*.344,y,z),(.003,.019,.004),amber,yaw,'StatusLamps',.0008,(ang,0,0))
  for j in range(12):
   ang=j*math.tau/12
   bolt(f'Pitch_{label}DiscPerimeterBolt_{j}',(side*.342,.084+math.cos(ang)*.117,.258+math.sin(ang)*.117),'X',s,pitch,'PitchArmor',.0023)
  if side<0:
   emblem=[[(0,.055),(.016,.027),(0,.001),(-.016,.027)],
           [(-.022,.017),(-.044,-.021),(-.023,-.021),(-.008,.003)],
           [(.022,.017),(.008,.003),(.023,-.021),(.044,-.021)],
           [(-.006,-.010),(-.020,-.032),(-.004,-.032),(0,-.025),(.004,-.032),(.020,-.032),(.006,-.010)]]
   for j,poly in enumerate(emblem):
    hs.panel(f'Pitch_RightOchreMark_{j}',[(side*.3402,.084+u,.300+v) for u,v in poly],.00025,accent,pitch,'PitchArmor','X',0)
  for z in [.164,.351]:
   box(f'Yaw_{label}FrontLampHousing_{z}',(side*.310,-.109,z),(.012,.029,.039),g,yaw,'YawArmor',.004)
   box(f'Yaw_{label}FrontLamp_{z}',(side*.318,-.124,z),(.004,.004,.025),amber,yaw,'StatusLamps',.001)
  # Exposed cross connectors unify the front shoulder silhouette.
  for z in [.161,.371]:
   cyl(f'Yaw_{label}CrossCoupling_{z}',(side*.169,-.065,z),.012,.095,g,yaw,'TurretHousing','X',36,.0015)
   ring(f'Yaw_{label}CouplingBand_{z}',(side*.186,-.065,z),.017,.011,.024,g,yaw,'TurretHousing','X',36,.001)
   for k in range(7):
    ring(f'Yaw_{label}CouplingCorrugation_{z}_{k}',(side*(.131+k*.0055),-.065,z),.0136,.0118,.0025,s,yaw,'TurretHousing','X',24,.0004)
  for j,z in enumerate([.218,.277,.335]):
   service_cover(f'Yaw_{label}InnerActuatorCover_{j}',(side*.170,-.080,z),(.034,.020,.046),'Y',g,s,yaw,'TurretHousing')
 # Rear armored receiver cabinet and horizontal black cooling grille.
 box('Yaw_RearCabinetCore',(0,.210,.263),(.281,.174,.270),g,yaw,'TurretHousing',.017)
 rear_outline=[(-.123,.301,.145),(-.055,.301,.145),(-.041,.301,.166),(.041,.301,.166),
  (.055,.301,.145),(.123,.301,.145),(.123,.301,.359),(.096,.301,.394),
  (-.096,.301,.394),(-.123,.301,.359)]
 hs.panel('Yaw_RearCabinetArmor',rear_outline,.022,a,yaw,'YawArmor','Y',.003)
 oct_frame('Yaw_RearPerimeterArmor',(0,.283,.277),.286,.252,.241,.212,.035,a,yaw,'YawArmor','Y',.028,omit_bottom=True)
 service_cover('Yaw_RearUpperService',(0,.312,.380),(.117,.013,.024),'Y',a,s,yaw,'YawArmor')
 box('Yaw_RearTopCover',(0,.182,.402),(.270,.199,.011),g,yaw,'YawArmor',.009)
 box('Yaw_RearGrilleInset',(0,.314,.330),(.159,.008,.033),g,yaw,'YawArmor',.006)
 for j in range(3):box(f'Yaw_RearGrilleSlat_{j}',(0,.319,.321+j*.009),(.145,.007,.0035),s,yaw,'YawArmor',.0006)
 for x in [-.067,.067]:service_cover('Yaw_RearService_'+str(x),(x,.317,.236),(.057,.008,.070),'Y',a,s,yaw,'YawArmor')
 # Offset pitch cradle: low transverse axle lifts the gun axis by 35 mm.
 cyl('Pitch_MainTransverseAxle',PITCH,.060,.304,g,pitch,'PitchHousing','X',72,.002)
 box('Pitch_ReceiverCradle',(0,.009,.268),(.210,.162,.114),g,pitch,'PitchHousing',.010)
 swept_front_frame(a,yaw,'YawArmor')
 octagonal_bezel(a,pitch,'PitchArmor')
 for side in [-1,1]:
  box('Pitch_FrontInnerCheek_'+str(side),(side*.092,-.070,.280),(.025,.040,.142),g,pitch,'PitchArmor',.006)
 jaw=[(-.107,.129),(-.048,.129),(-.039,.151),(.039,.151),(.048,.129),(.107,.129),
      (.129,.178),(.122,.211),(.110,.211),(.105,.168),(-.105,.168),(-.110,.211),(-.122,.211),(-.129,.178)]
 verts=[(x,-.170+(z-.129)*(.091/.082)+d,z) for d in [0,.040] for x,z in jaw];n=len(jaw)
 faces=[tuple(range(n)),tuple(reversed(range(n,n*2)))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 mesh_part('Yaw_LowerJawArmor',verts,faces,a,yaw,'YawArmor',.003)
 service_cover('Yaw_FrontLowerService',(0,-.139,.160),(.057,.009,.014),'Y',a,s,yaw,'YawArmor')
 # Steel guide rails and moving receiver overlap through the full recoil stroke.
 for side in [-1,1]:
  box('Pitch_RecoilRail_'+str(side),(side*.092,-.028,.236),(.015,.192,.021),s,pitch,'PitchHousing',.002)
  box('Recoil_GuideShoe_'+str(side),(side*.092,-.056,.236),(.028,.096,.034),g,recoil,'RecoilCarriage',.003)
 box('Recoil_ReceiverBlock',(0,-.026,.291),(.153,.119,.124),g,recoil,'RecoilCarriage',.010)
 cyl('Recoil_DriveBody',(0,-.076,.293),.074,.083,g,recoil,'RecoilCarriage','Y',72,.002)
 # Two non-spinning journals support the rotor inside the cream open guard.
 for label,y in [('Rear',-.098),('Front',-.225)]:
  r=.078 if label=='Rear' else .064
  ring(f'Recoil_{label}BearingHousing',(0,y,.293),r,.0595,.016,g,recoil,'RotorBearings','Y',72,.001)
  ring(f'Recoil_{label}BearingRace',(0,y-.003,.293),.061,.0567,.014,s,recoil,'RotorBearings','Y',72,.0006)
 # Continuous perforated sheet with real cut-outs, not overlapping struts.
 perforated_guard(a,recoil,'RotorBearings')
 for i in range(8):
  ang=i*math.tau/8
  for y in [-.223,-.079]:
   r=.066+(.080-.066)*(y+.225)/.148
   bolt(f'Guard_EndFastener_{i}_{y}',(math.sin(ang)*r,y,.293+math.cos(ang)*r),'Y',s,recoil,'RotorBearings',.0024)
 for label,y,r in [('Front',-.223,.066),('Rear',-.079,.080)]:
  ring(f'Guard_{label}Rim',(0,y,.293),r,r-.005,.014,a,recoil,'RotorBearings','Y',72,.001)
 # Eight long dark tubes and three distinctive forward retaining hoops.
 for i in range(8):
  ang=i*math.tau/8;x=math.sin(ang)*.043;z=.293+math.cos(ang)*.043
  hs.profile_ring(f'Rotor_Barrel_{i+1:02}',(x,0,z),[(-.539,.012),(-.538,.013),(-.533,.013),
       (-.532,.0119),(-.100,.0119),(-.098,.011),(-.098,.0089),(-.538,.0089),(-.539,.0099)],g,spin,'RotatingBarrels','Y',36)
  ring(f'Rotor_MuzzleLip_{i+1:02}',(x,-.5386,z),.0129,.0091,.0011,s,spin,'RotatingBarrels','Y',36,.00015)
  cyl(f'Rotor_BoreDepth_{i+1}',(x,-.493,z),.0088,.003,g,spin,'RotatingBarrels','Y',24,.0002)
 cyl('Rotor_CentralShaft',(0,-.316,.293),.010,.425,g,spin,'RotatingBarrels','Y',36,.001)
 cyl('Rotor_CentralNose',(0,-.540,.293),.0118,.006,s,spin,'RotatingBarrels','Y',36,.0007)
 for label,y,width in [('Muzzle',-.524,.017),('Forward',-.482,.016),('Thin',-.450,.007),('Rear',-.250,.016)]:
  ring('Rotor_'+label+'Hoop',(0,y,.293),.060,.054,.016 if label!='Thin' else .007,g,spin,'RotatingBarrels','Y',72,.001)
  if label in ['Muzzle','Rear']:
   for j in range(8):
    ang=(j+.5)*math.tau/8
    box(f'Rotor_{label}Spoke_{j}',(math.sin(ang)*.032,y,.293+math.cos(ang)*.032),(.004,.009,.045),g,
        spin,'RotatingBarrels',.0007,(0,ang,0))
 for label,y in [('Front',-.227),('Rear',-.101)]:
  ring('Rotor_'+label+'BearingJournal',(0,y,.293),.0563,.0521,.010,s,spin,'RotatingBarrels','Y',72,.0005)
 # Asymmetric sensor: thin large radar plate and forward spherical optic.
 box('Sensor_InstrumentBase',(0,.066,.407),(.196,.087,.013),g,yaw,'StatusLamps',.004)
 box('Sensor_OpticFooting',(-.066,.047,.416),(.062,.060,.016),g,yaw,'StatusLamps',.003)
 box('Sensor_RadarFooting',(.077,.071,.418),(.054,.060,.020),g,yaw,'StatusLamps',.003)
 for side in [-1,1]:
  box('Sensor_RadarSupportRail_'+str(side),(.077+side*.012,.080,.445),(.010,.023,.057),g,yaw,'StatusLamps',.002)
 cyl('Sensor_RadarDisk',(.077,.063,.481),.0756,.0105,g,yaw,'StatusLamps','Y',72,.001)
 cyl('Sensor_RadarRearFace',(.077,.0691,.481),.0753,.0018,a,yaw,'StatusLamps','Y',72,.0004)
 ring('Sensor_RadarRim',(.077,.057,.481),.077,.0735,.005,a,yaw,'StatusLamps','Y',72,.0005)
 for j in range(4):
  start=math.pi/4+j*math.pi/2+.004;end=start+math.pi/2-.008
  disc_sector(f'Sensor_RadarFacePanel_{j}',(.077,.0568,.481),.0115,.0734,start,end,.0014,a,yaw,'StatusLamps')
 cyl('Sensor_RadarCenterCover',(.077,.0559,.481),.0112,.0014,a,yaw,'StatusLamps','Y',72,.0002)
 cyl('Sensor_RadarRearHub',(.077,.075,.481),.028,.018,g,yaw,'StatusLamps','Y',72,.0015)
 ring('Sensor_RadarRearHubOuterRim',(.077,.084,.481),.028,.025,.003,s,yaw,'StatusLamps','Y',72,.0005)
 cyl('Sensor_RadarRearHubCap',(.077,.087,.481),.021,.005,g,yaw,'StatusLamps','Y',72,.0008)
 ring('Sensor_RadarRearHubCapRim',(.077,.090,.481),.016,.014,.002,s,yaw,'StatusLamps','Y',72,.0004)
 cyl('Sensor_RadarRearHubCenter',(.077,.0915,.481),.009,.003,g,yaw,'StatusLamps','Y',72,.0006)
 for j in range(4):
  ang=math.pi/4+j*math.pi/2
  box(f'Sensor_RadarRearBrace_{j}',(.077+math.sin(ang)*.042,.072,.481+math.cos(ang)*.042),(.004,.006,.069),a,
      yaw,'StatusLamps',.0008,(0,ang,0))
 for j in range(20):
  ang=j*math.tau/20
  bolt(f'Sensor_RadarRimFastener_{j}',(.077+math.sin(ang)*.073,.055,.481+math.cos(ang)*.073),'Y',s,yaw,'StatusLamps',.0012)
 box('Sensor_OpticSupport',(-.066,.015,.429),(.027,.033,.033),g,yaw,'StatusLamps',.004)
 for side in [-1,1]:
  hs.panel('Sensor_OpticFork_'+str(side),[(-.066+side*.026,.035,.417),(-.066+side*.026,-.002,.448),(-.066+side*.026,-.005,.465),(-.066+side*.026,.020,.465),(-.066+side*.026,.055,.420)],.008,s,yaw,'StatusLamps','X',.0015)
  cyl('Sensor_OpticSideJournal_'+str(side),(-.066+side*.048,-.010,.464),.014,.012,g,yaw,'StatusLamps','X',36,.001)
  cyl('Sensor_OpticSideCap_'+str(side),(-.066+side*.055,-.010,.464),.010,.003,a,yaw,'StatusLamps','X',36,.0005)
 bpy.ops.mesh.primitive_uv_sphere_add(segments=40,ring_count=20,radius=.044,location=(-.066,-.017,.464))
 hs.finish(bpy.context.object,'Sensor_OpticSphericalArmor',a,yaw,'StatusLamps',0,1,True)
 ring('Sensor_OpticFrontArmorRing',(-.066,-.046,.464),.040,.025,.010,a,yaw,'StatusLamps','Y',72,.0015)
 for j in range(10):
  ang=j*math.tau/10
  bolt(f'Sensor_OpticFrontRingBolt_{j}',(-.066+math.sin(ang)*.034,-.052,.464+math.cos(ang)*.034),'Y',s,yaw,'StatusLamps',.0015)
 ring('Sensor_OpticEquatorialBand',(-.066,-.017,.464),.0445,.039,.005,g,yaw,'StatusLamps','Y',72,.0007)
 cyl('Sensor_OpticFrontBezel',(-.066,-.054,.464),.025,.015,g,yaw,'StatusLamps','Y',72,.001)
 ring('Sensor_OpticFrontMetalRim',(-.066,-.063,.464),.022,.018,.005,s,yaw,'StatusLamps','Y',72,.0006)
 bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=12,radius=1,location=(-.066,-.064,.464))
 bpy.context.object.scale=(.0175,.004,.0175)
 hs.finish(bpy.context.object,'Sensor_OpticGlass',glass,yaw,'StatusLamps',0,1,True)
 ring('Sensor_OpticInnerLensRing',(-.066,-.0683,.464),.0058,.0046,.0005,accent,yaw,'StatusLamps','Y',36,.0001)
 box('Sensor_OpticSideLampHousing',(-.010,-.007,.464),(.014,.032,.028),g,yaw,'StatusLamps',.003)
 box('Sensor_OpticSideLamp',(-.008,-.025,.464),(.004,.003,.019),amber,yaw,'StatusLamps',.0008)
 flat_panel('Sensor_OpticRearCover',(-.066,.025,.464),.044,.042,.009,a,yaw,'StatusLamps','Y',.006)
 for xoff in [-.016,.016]:
  for zoff in [-.015,.015]:bolt('Sensor_OpticRearCoverBolt_'+str(xoff)+'_'+str(zoff),(-.066+xoff,.031,.464+zoff),'Y',s,yaw,'StatusLamps',.0018)
 add_secondary_details(root,yaw,pitch,m)
 for group,objects in hs.GROUPS.items():
  for o in objects:
   art.activate(o);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
   bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
 try:
  sys.path.insert(0,str(PROJECT/'Tools/Art/FuxiTurret'))
  import finish_surface
  finish_surface.apply_surface('FuxiTurret')
 except ImportError:pass
 return root

def studio(root):
 stats=art.inspect_objects(art.descendants(root));camera=art.studio(root,stats)
 scene=bpy.context.scene;scene.cycles.samples=16;scene.render.resolution_x=1536;scene.render.resolution_y=1024
 scene.world.use_nodes=True
 bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
 bg.inputs[0].default_value=(.20,.22,.25,1);bg.inputs[1].default_value=.45
 camera.data.type='ORTHO';camera.data.ortho_scale=1.015
 return stats

def set_view(name):
 target=Vector((0,0,.278));camera=bpy.context.scene.camera
 direction={'Front':(0,-1,.055),'Rear':(0,1,.055),'Left':(-1,0,.055),'Right':(1,0,.055),
            'FrontThreeQuarter':(1.2,-1.9,1.0)}[name]
 camera.location=target+Vector(direction).normalized()*4;art.aim(camera,target)
 camera.data.ortho_scale=1.015 if name in ['Front','Rear'] else 1.095
 if name=='FrontThreeQuarter':camera.data.ortho_scale=1.14

def export_asset():
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE));root=bpy.data.objects['FuxiTurret']
 source_stats=art.inspect_objects(art.descendants(root));groups={}
 for o in art.descendants(root):
  if o.type=='MESH':groups.setdefault(o['render_group'],[]).append(o)
 for name,objects in groups.items():
  parent=objects[0].parent
  if any(o.parent!=parent for o in objects):raise ValueError('Mixed parents: '+name)
  art.join_meshes(objects,name,parent,tuple(parent.matrix_world.translation))
 merged=art.inspect_objects(art.descendants(root))
 bpy.ops.object.select_all(action='DESELECT')
 for o in art.descendants(root):o.select_set(True)
 bpy.context.view_layer.objects.active=root
 bpy.ops.export_scene.fbx(filepath=str(EXPORT),use_selection=True,object_types={'EMPTY','MESH'},
  axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
  use_space_transform=True,bake_space_transform=False,use_mesh_modifiers=True,mesh_smooth_type='FACE',
  use_triangles=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE',use_custom_props=True)
 art.reset_scene();bpy.ops.import_scene.fbx(filepath=str(EXPORT),use_anim=False)
 imported=art.inspect_objects(list(bpy.context.scene.objects))
 report={'asset':'FuxiTurret','sourceReopened':source_stats,'consolidatedExport':merged,'fbxReimported':imported,
  'referencePhotos':['Front','Rear','Left','Right'],'referenceSimilarity':'Visual review required; no numerical claim.'}
 (CHECKS/'ProductionReport.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 print('FUXI_EXPORT_READY',merged['triangles'],merged['mesh_count'],flush=True)

def main():
 for p in [SOURCE.parent,EXPORT.parent,PREVIEW,CHECKS]:p.mkdir(parents=True,exist_ok=True)
 (CHECKS/'AssetManifest.json').write_text(json.dumps({'asset':'FuxiTurret','materials':MATERIALS,
  'referenceUnitDiameter':.84,'pitchCenter':list(PITCH),'gunAxis':list(AXIS)},indent=2),encoding='utf-8')
 if '--export-only' in sys.argv:export_asset();return
 if '--renders-only' not in sys.argv:
  root=build();stats=studio(root);set_view('FrontThreeQuarter')
  for screen in bpy.data.screens:
   for area in screen.areas:
    if area.type=='VIEW_3D':
     sp=area.spaces.active;sp.region_3d.view_location=(0,0,.28);sp.region_3d.view_distance=1.5
     sp.region_3d.view_rotation=bpy.context.scene.camera.rotation_euler.to_quaternion()
     sp.shading.type='SOLID';sp.shading.color_type='MATERIAL';sp.overlay.show_extras=False
  bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
  export_asset()
  if '--geometry-only' in sys.argv:return
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
 for name in ['Front','Rear','Left','Right']:
  set_view(name);bpy.context.scene.render.filepath=str(PREVIEW/(name+'.png'));bpy.ops.render.render(write_still=True)
  print('FUXI_VIEW_READY',name,flush=True)

if __name__=='__main__':main()

