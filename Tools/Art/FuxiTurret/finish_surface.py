"""Subtle production surfaces and optional atlas baking for the Fuxi turret.

Authoring use, after creating the six named materials:
    import finish_surface
    finish_surface.apply_surface()

The optional command-line bake is implemented below. It must only run after the
geometry author explicitly hands over the saved source. Importing this module
does not open, save, export, or change any scene.
"""
import argparse
import json
import math
import shutil
import sys
import time
from pathlib import Path

import bpy


PROJECT = Path(__file__).resolve().parents[3]
MATERIAL_NAMES = ("Fuxi_Armor", "Fuxi_Graphite", "Fuxi_Steel", "Fuxi_Accent", "Fuxi_Amber", "Fuxi_Glass")
DEFAULTS = {
    "Fuxi_Armor": ((.68, .66, .60, 1), .12, .40, 0),
    "Fuxi_Graphite": ((.032, .039, .045, 1), .65, .43, 0),
    "Fuxi_Steel": ((.21, .23, .245, 1), .88, .29, 0),
    "Fuxi_Accent": ((.49, .28, .065, 1), .08, .55, 0),
    "Fuxi_Amber": ((.82, .22, .012, 1), .05, .28, 1.5),
    "Fuxi_Glass": ((.022, .033, .043, 1), .20, .14, 0),
}


def _node(nodes, kind, name, x, y):
    node = nodes.new(kind)
    node.name = name
    node.label = name
    node.location = (x, y)
    return node


def _math(nodes, links, name, operation, value, source, x, y):
    node = _node(nodes, "ShaderNodeMath", name, x, y)
    node.operation = operation
    node.inputs[1].default_value = value
    links.new(source, node.inputs[0])
    return node.outputs[0]


def _noise(nodes, links, coords, name, scale, detail, x, y):
    node = _node(nodes, "ShaderNodeTexNoise", name, x, y)
    node.noise_dimensions = "3D"
    node.inputs["Scale"].default_value = scale
    node.inputs["Detail"].default_value = detail
    node.inputs["Roughness"].default_value = .55
    links.new(coords, node.inputs["Vector"])
    return node.outputs["Fac"]


def _edge_wear(nodes, links, coords, base_color, metallic, family):
    """Sparse convex-edge handling wear, with no broad dirt or crack mask."""
    geometry = _node(nodes, "ShaderNodeNewGeometry", "ConvexEdgeCurvature", -1020, -700)
    curvature = _node(nodes, "ShaderNodeMapRange", "NarrowConvexEdges", -800, -680)
    curvature.clamp = True
    curvature.inputs["From Min"].default_value = .53
    curvature.inputs["From Max"].default_value = .57
    links.new(geometry.outputs["Pointiness"], curvature.inputs["Value"])
    noise = _noise(nodes, links, coords, "InterruptedEdgeWear", 280, 2, -1000, -980)
    islands = _node(nodes, "ShaderNodeMapRange", "SparseEdgeWearIslands", -790, -930)
    islands.clamp = True
    islands.inputs["From Min"].default_value = .53
    islands.inputs["From Max"].default_value = .66
    links.new(noise, islands.inputs["Value"])
    # Pointiness interpolates across large n-gons. Restrict its effect to the
    # actual bevel transition; otherwise a flat armor face receives tiny spots.
    alignment = _node(nodes, "ShaderNodeVectorMath", "BevelNormalAlignment", -1010, -1200)
    alignment.operation = "DOT_PRODUCT"
    links.new(geometry.outputs["Normal"], alignment.inputs[0])
    links.new(geometry.outputs["True Normal"], alignment.inputs[1])
    bend = _node(nodes, "ShaderNodeMath", "BevelNormalBend", -790, -1190)
    bend.operation = "SUBTRACT"
    bend.inputs[0].default_value = 1
    links.new(alignment.outputs["Value"], bend.inputs[1])
    bevel = _node(nodes, "ShaderNodeMapRange", "PhysicalBevelRestriction", -580, -1140)
    bevel.clamp = True
    bevel.inputs["From Min"].default_value = .0018
    bevel.inputs["From Max"].default_value = .016
    links.new(bend.outputs[0], bevel.inputs["Value"])
    curvature_bevel = _node(nodes, "ShaderNodeMath", "ConvexPhysicalBevel", -560, -840)
    curvature_bevel.operation = "MULTIPLY"
    curvature_floor = _math(nodes, links, "CurvatureWeight", "MULTIPLY", .45, curvature.outputs["Result"], -570, -1010)
    curvature_floor = _math(nodes, links, "BevelWearMinimum", "ADD", .55, curvature_floor, -360, -1010)
    links.new(curvature_floor, curvature_bevel.inputs[0])
    links.new(bevel.outputs["Result"], curvature_bevel.inputs[1])
    mask = _node(nodes, "ShaderNodeMath", "EdgeWearMask", -560, -660)
    mask.operation = "MULTIPLY"
    links.new(curvature_bevel.outputs[0], mask.inputs[0])
    links.new(islands.outputs["Result"], mask.inputs[1])
    strength = _math(nodes, links, "RestrainedEdgeWearStrength", "MULTIPLY", .92 if family == "Fuxi_Armor" else .68, mask.outputs[0], -350, -670)
    color = _node(nodes, "ShaderNodeMixRGB", "CoatingWithEdgeWear", 70, 390)
    color.blend_type = "MIX"
    color.inputs[2].default_value = (.16, .13, .092, 1) if family == "Fuxi_Armor" else (.25, .26, .27, 1)
    links.new(strength, color.inputs[0])
    links.new(base_color, color.inputs[1])
    exposed_metal = _math(nodes, links, "ExposedMetalContribution", "MULTIPLY", .8 - metallic, strength, -110, -640)
    exposed_metal = _math(nodes, links, "SurfaceMetallic", "ADD", metallic, exposed_metal, 90, -620)
    return color.outputs["Color"], exposed_metal, strength


def _contact_surface(nodes, links, coords, source_color, family):
    """Local fastener and seam contact tone; no directional light or broad shadow."""
    ao = _node(nodes, "ShaderNodeAmbientOcclusion", "LocalContactOcclusion", -780, -1450)
    ao.samples = 16
    ao.only_local = False
    ao.inputs["Distance"].default_value = .006
    ao.inputs["Color"].default_value = (1, 1, 1, 1)
    cavity = _node(nodes, "ShaderNodeMath", "ContactCavity", -560, -1430)
    cavity.operation = "SUBTRACT"
    cavity.inputs[0].default_value = 1
    links.new(ao.outputs["AO"], cavity.inputs[1])
    noise = _noise(nodes, links, coords, "UnevenSeamResidue", 180, 2, -790, -1690)
    variation = _math(nodes, links, "ContactResidueVariation", "MULTIPLY", .65, noise, -560, -1640)
    variation = _math(nodes, links, "ContactResidueFloor", "ADD", .35, variation, -350, -1640)
    residue = _node(nodes, "ShaderNodeMath", "ContactResidueMask", -350, -1430)
    residue.operation = "MULTIPLY"
    links.new(cavity.outputs[0], residue.inputs[0])
    links.new(variation, residue.inputs[1])
    amount = _math(nodes, links, "SubtleContactTone", "MULTIPLY", .28 if family == "Fuxi_Armor" else .16, residue.outputs[0], -130, -1440)
    color = _node(nodes, "ShaderNodeMixRGB", "PaintWithLocalContactTone", 310, 520)
    color.blend_type = "MIX"
    color.inputs[2].default_value = (.22, .19, .145, 1) if family == "Fuxi_Armor" else (.018, .018, .017, 1)
    links.new(amount, color.inputs[0])
    links.new(source_color, color.inputs[1])
    return color.outputs["Color"]


def apply_surface(root_name="FuxiTurret"):
    """Keep authored palette and replace each named material with a quiet surface.

    Existing material colors, metal values, and emission are preserved. The
    accent is explicitly matte painted ochre, never copper. All variation uses
    a common object coordinate system in meters and can be baked to a UV atlas.
    Only sparse convex-edge wear; no large stains, crack patterns, or randomized palette.
    Calling the function again gives the same node graph and palette.
    """
    root = bpy.data.objects.get(root_name)
    specs = []
    for name in MATERIAL_NAMES:
        material = bpy.data.materials.get(name)
        if material is None:
            material = bpy.data.materials.new(name)
            color, metallic, roughness, emission = DEFAULTS[name]
        elif "fuxi_surface_base_color" in material:
            color = tuple(material["fuxi_surface_base_color"])
            metallic = float(material["fuxi_surface_metallic"])
            roughness = float(material["fuxi_surface_roughness"])
            emission = float(material["fuxi_surface_emission"])
        else:
            principled = next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if material.use_nodes else None
            color = tuple(principled.inputs["Base Color"].default_value) if principled else tuple(material.diffuse_color)
            metallic = principled.inputs["Metallic"].default_value if principled else material.metallic
            roughness = principled.inputs["Roughness"].default_value if principled else material.roughness
            emission = principled.inputs["Emission Strength"].default_value if principled else DEFAULTS[name][3]
        if name == "Fuxi_Accent":
            metallic = min(metallic, .12)
            roughness = max(roughness, .52)
        if name == "Fuxi_Armor":
            roughness = max(roughness, .50)
            metallic = min(metallic, .10)
        elif name == "Fuxi_Graphite":
            metallic = max(metallic, .72)
        color = tuple(color[:3]) + (1,)
        material["fuxi_surface_base_color"] = list(color)
        material["fuxi_surface_metallic"] = metallic
        material["fuxi_surface_roughness"] = roughness
        material["fuxi_surface_emission"] = emission
        material["fuxi_surface_version"] = 3
        material.diffuse_color = color
        material.metallic = metallic
        material.roughness = roughness
        material.use_nodes = True
        nodes, links = material.node_tree.nodes, material.node_tree.links
        nodes.clear()
        coords = _node(nodes, "ShaderNodeTexCoord", "AssetCoordinatesInMeters", -1000, 80)
        coords.object = root
        macro = _noise(nodes, links, coords.outputs["Object"], "FineCoatingVariation", 24, 3, -790, 250)
        grain = _noise(nodes, links, coords.outputs["Object"], "MicroSurfaceGrain", 820, 2, -790, -60)
        stretch = _node(nodes, "ShaderNodeVectorMath", "BrushedMicroScratchDirection", -990, -360)
        stretch.operation = "MULTIPLY"
        # Authored in meters: the thresholded marks remain roughly 1-3 mm
        # across and 5-20 mm long, instead of disappearing below one atlas texel.
        stretch.inputs[1].default_value = (180, 24, 120)
        links.new(coords.outputs["Object"], stretch.inputs[0])
        scratches = _noise(nodes, links, stretch.outputs[0], "SparseFineHandlingMarks", 1, 1, -780, -370)
        scratch_mask = _node(nodes, "ShaderNodeValToRGB", "RestrainedScratchMask", -570, -390)
        scratch_mask.color_ramp.elements[0].position = .60
        scratch_mask.color_ramp.elements[1].position = .70
        links.new(scratches, scratch_mask.inputs["Fac"])
        scratch_regions = _noise(nodes, links, coords.outputs["Object"], "SparseHandlingRegions", 48, 2, -780, -520)
        region_mask = _node(nodes, "ShaderNodeMapRange", "IsolatedHandlingRegions", -570, -530)
        region_mask.clamp = True
        region_mask.inputs["From Min"].default_value = .55
        region_mask.inputs["From Max"].default_value = .66
        links.new(scratch_regions, region_mask.inputs["Value"])
        scratch_islands = _node(nodes, "ShaderNodeMath", "SparseShortHandlingMarks", -350, -390)
        scratch_islands.operation = "MULTIPLY"
        links.new(scratch_mask.outputs["Color"], scratch_islands.inputs[0])
        links.new(region_mask.outputs["Result"], scratch_islands.inputs[1])
        amplitude = .24 if name == "Fuxi_Armor" else .075
        if name in ("Fuxi_Amber", "Fuxi_Glass"):
            amplitude = .006
        color_variation = _math(nodes, links, "ColorVariationAmplitude", "MULTIPLY", amplitude, macro, -540, 250)
        color_variation = _math(nodes, links, "NearUniformCoating", "ADD", 1 - amplitude * .5, color_variation, -350, 250)
        base = _node(nodes, "ShaderNodeMixRGB", "SurfaceBaseColor", -120, 250)
        base.blend_type = "MULTIPLY"
        base.inputs[0].default_value = 1
        base.inputs[1].default_value = color
        links.new(color_variation, base.inputs[2])
        if name == "Fuxi_Armor":
            cloud = _node(nodes, "ShaderNodeValToRGB", "WarmGreyPaintClouds", -320, 540)
            cloud.color_ramp.elements[0].position = .30
            cloud.color_ramp.elements[0].color = (.86, .89, .94, 1)
            cloud.color_ramp.elements[1].position = .70
            cloud.color_ramp.elements[1].color = (1.14, 1.11, 1.06, 1)
            links.new(macro, cloud.inputs["Fac"])
            links.new(cloud.outputs["Color"], base.inputs[2])
        grain_amplitude = .12 if name not in ("Fuxi_Amber", "Fuxi_Glass") else .015
        rough = _math(nodes, links, "RoughnessGrainAmplitude", "MULTIPLY", grain_amplitude, grain, -550, -30)
        rough = _math(nodes, links, "CoatingRoughness", "ADD", roughness - grain_amplitude * .5, rough, -350, -30)
        if name in ("Fuxi_Armor", "Fuxi_Graphite"):
            cloud_roughness = _math(nodes, links, "CloudRoughnessAmplitude", "MULTIPLY", .14, macro, -540, 100)
            cloud_roughness = _math(nodes, links, "CenteredCloudRoughness", "ADD", -.07, cloud_roughness, -320, 100)
            layered_roughness = _node(nodes, "ShaderNodeMath", "LayeredCoatingRoughness", -120, 100)
            layered_roughness.operation = "ADD"
            links.new(rough, layered_roughness.inputs[0])
            links.new(cloud_roughness, layered_roughness.inputs[1])
            rough = layered_roughness.outputs[0]
        scratch_roughness = _math(nodes, links, "ScratchRoughnessAmplitude", "MULTIPLY", .10 if name == "Fuxi_Armor" else (-.10 if name == "Fuxi_Graphite" else .008), scratch_islands.outputs[0], -350, -210)
        roughness_node = _node(nodes, "ShaderNodeMath", "SurfaceRoughness", -120, -30)
        roughness_node.operation = "ADD"
        roughness_node.use_clamp = True
        links.new(rough, roughness_node.inputs[0])
        links.new(scratch_roughness, roughness_node.inputs[1])
        bump = _node(nodes, "ShaderNodeBump", "MicroscopicCoatingRelief", 80, -220)
        bump.inputs["Strength"].default_value = .12 if name not in ("Fuxi_Amber", "Fuxi_Glass") else .02
        bump.inputs["Distance"].default_value = .000018
        links.new(grain, bump.inputs["Height"])
        principled = _node(nodes, "ShaderNodeBsdfPrincipled", "ProductionSurface", 280, 160)
        principled.inputs["Metallic"].default_value = metallic
        principled.inputs["Emission Color"].default_value = color
        principled.inputs["Emission Strength"].default_value = emission
        principled.inputs["Coat Weight"].default_value = .3 if name == "Fuxi_Glass" else 0
        principled.inputs["Coat Roughness"].default_value = .3 if name == "Fuxi_Armor" else .12
        base_output = base.outputs["Color"]
        roughness_output = roughness_node.outputs[0]
        if name in ("Fuxi_Armor", "Fuxi_Graphite"):
            # Thin, sparse handling scratches affect color as well as roughness
            # so that the effect survives baking and moderate mip reduction.
            scratch_color = _node(nodes, "ShaderNodeMixRGB", "RareHandlingScratchColor", 90, 690)
            scratch_color.blend_type = "MIX"
            scratch_color.inputs[2].default_value = (.31, .27, .20, 1) if name == "Fuxi_Armor" else (.10, .105, .11, 1)
            scratch_amount = _math(nodes, links, "HandlingScratchVisibility", "MULTIPLY", .82 if name == "Fuxi_Armor" else .65, scratch_islands.outputs[0], -140, -470)
            links.new(scratch_amount, scratch_color.inputs[0])
            links.new(base_output, scratch_color.inputs[1])
            base_output, metallic_output, wear = _edge_wear(nodes, links, coords.outputs["Object"], scratch_color.outputs["Color"], metallic, name)
            links.new(metallic_output, principled.inputs["Metallic"])
            burnished = _math(nodes, links, "ExposedEdgePolish", "MULTIPLY", -.20, wear, 90, -810)
            final_roughness = _node(nodes, "ShaderNodeMath", "CoatingAndExposedEdgeRoughness", 320, -620)
            final_roughness.operation = "ADD"
            final_roughness.use_clamp = True
            links.new(roughness_output, final_roughness.inputs[0])
            links.new(burnished, final_roughness.inputs[1])
            roughness_output = final_roughness.outputs[0]
            base_output = _contact_surface(nodes, links, coords.outputs["Object"], base_output, name)
        links.new(base_output, principled.inputs["Base Color"])
        links.new(roughness_output, principled.inputs["Roughness"])
        links.new(bump.outputs["Normal"], principled.inputs["Normal"])
        output = _node(nodes, "ShaderNodeOutputMaterial", "MaterialOutput", 590, 160)
        links.new(principled.outputs["BSDF"], output.inputs["Surface"])
        specs.append({"name": name, "baseColor": list(color), "metallic": metallic, "roughness": roughness, "emissionStrength": emission})
    return specs


def _select(objects):
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def _geometry_record(meshes):
    records = {}
    for obj in meshes:
        obj.data.calc_loop_triangles()
        records[obj.name] = {"vertices": len(obj.data.vertices), "polygons": len(obj.data.polygons),
                             "triangles": len(obj.data.loop_triangles), "parent": obj.parent.name if obj.parent else None,
                             "positions": [tuple(vertex.co) for vertex in obj.data.vertices],
                             "polygonIndices": [tuple(polygon.vertices) for polygon in obj.data.polygons],
                             "worldMatrix": tuple(value for row in obj.matrix_world for value in row)}
    return records


def _pack_shared_uv(meshes):
    for obj in meshes:
        if len(obj.data.uv_layers) == 0:
            raise RuntimeError("Every authored part must have UVs before shared packing: " + obj.name)
        obj.data.uv_layers.active.name = "FuxiSurfaceUV"
        obj.data.uv_layers.active.active_render = True
    _select(meshes)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.select_all(action="SELECT")
        bpy.ops.uv.average_islands_scale()
        bpy.ops.uv.pack_islands(rotate=True, margin=.003, margin_method="FRACTION")
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    outside = 0
    nonfinite = 0
    for obj in meshes:
        for loop in obj.data.uv_layers.active.data:
            nonfinite += not all(math.isfinite(value) for value in loop.uv)
            outside += any(value < -1e-5 or value > 1.00001 for value in loop.uv)
    if outside or nonfinite:
        raise RuntimeError("Shared UV packing produced invalid coordinates.")
    return {"uvLayer": "FuxiSurfaceUV", "method": "Multi-object UV island scale averaging and joint nonoverlapping packing",
            "fractionalMargin": .003, "outsideUnitSquareLoops": outside, "nonfiniteLoops": nonfinite}


def _temporary_bake_mesh(meshes):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    duplicates = []
    for source in meshes:
        data = bpy.data.meshes.new_from_object(source.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
        obj = bpy.data.objects.new("BakeCopy_" + source.name, data)
        bpy.context.scene.collection.objects.link(obj)
        obj.matrix_world = source.matrix_world.copy()
        duplicates.append(obj)
    _select(duplicates)
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = "FuxiTemporaryAtlasBake"
    return obj


def _bake_pass(bake_mesh, materials, channel, size):
    image = bpy.data.images.new("FuxiBake_" + channel, width=size, height=size, alpha=True, float_buffer=True)
    image.colorspace_settings.name = "sRGB" if channel == "BaseColor" else "Non-Color"
    temporary = []
    original_links = []
    try:
        for material in materials:
            nodes, links = material.node_tree.nodes, material.node_tree.links
            surface = nodes["ProductionSurface"]
            output = nodes["MaterialOutput"]
            original_links.append((material, output, output.inputs["Surface"].links[0].from_socket))
            emission = _node(nodes, "ShaderNodeEmission", "TemporaryBakeEmission", 850, -250)
            texture = _node(nodes, "ShaderNodeTexImage", "TemporaryBakeTarget", 850, -500)
            texture.image = image
            nodes.active = texture
            texture.select = True
            if channel == "BaseColor":
                links.new(surface.inputs["Base Color"].links[0].from_socket, emission.inputs["Color"])
            elif channel == "Metallic":
                if surface.inputs["Metallic"].is_linked:
                    links.new(surface.inputs["Metallic"].links[0].from_socket, emission.inputs["Color"])
                else:
                    value = surface.inputs["Metallic"].default_value
                    emission.inputs["Color"].default_value = (value, value, value, 1)
            elif channel == "Smoothness":
                inverse = _node(nodes, "ShaderNodeMath", "TemporarySmoothness", 630, -250)
                inverse.operation = "SUBTRACT"
                inverse.inputs[0].default_value = 1
                links.new(surface.inputs["Roughness"].links[0].from_socket, inverse.inputs[1])
                links.new(inverse.outputs[0], emission.inputs["Color"])
                temporary.append((nodes, inverse))
            else:
                raise ValueError("Unsupported bake channel: " + channel)
            emission.inputs["Strength"].default_value = 1
            links.new(emission.outputs[0], output.inputs["Surface"])
            temporary.extend(((nodes, emission), (nodes, texture)))
        _select([bake_mesh])
        bpy.ops.object.bake(type="EMIT", use_clear=True, margin=4)
    finally:
        for material, output, socket in original_links:
            material.node_tree.links.new(socket, output.inputs["Surface"])
        for nodes, node in temporary:
            nodes.remove(node)
    return image


def _save_atlas(image, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    image.file_format = "PNG"
    image.filepath_raw = str(path.resolve())
    image.save()
    image.use_fake_user = True


def _bind_packed_atlases(materials, base_path, packed_path, blend_path):
    """Use the exact game PNGs in the saved source so moving parts cannot swim."""
    for previous in list(bpy.data.images):
        if previous.name.startswith("FuxiTurret_Game"):
            bpy.data.images.remove(previous, do_unlink=True)
    base = bpy.data.images.load(str(base_path.resolve()), check_existing=False)
    packed = bpy.data.images.load(str(packed_path.resolve()), check_existing=False)
    base.name = "FuxiTurret_GameBaseColor"
    packed.name = "FuxiTurret_GameMetallicSmoothness"
    base.colorspace_settings.name = "sRGB"
    packed.colorspace_settings.name = "Non-Color"
    packed.alpha_mode = "CHANNEL_PACKED"
    for image in (base, packed):
        image.filepath = bpy.path.relpath(image.filepath, start=str(blend_path.parent.resolve()))
        image.pack()
    for material in materials:
        nodes, links = material.node_tree.nodes, material.node_tree.links
        shader = nodes["ProductionSurface"]
        uv = _node(nodes, "ShaderNodeUVMap", "SharedGameAtlasUV", 800, 600)
        uv.uv_map = "FuxiSurfaceUV"
        color_map = _node(nodes, "ShaderNodeTexImage", "GameBaseColorAtlas", 1000, 650)
        color_map.image = base
        channel_map = _node(nodes, "ShaderNodeTexImage", "GameMetallicSmoothnessAtlas", 1000, 350)
        channel_map.image = packed
        separate = _node(nodes, "ShaderNodeSeparateColor", "GameMetallicRedChannel", 1240, 350)
        separate.mode = "RGB"
        roughness = _node(nodes, "ShaderNodeMath", "GameRoughnessFromSmoothness", 1240, 130)
        roughness.operation = "SUBTRACT"
        roughness.inputs[0].default_value = 1
        links.new(uv.outputs["UV"], color_map.inputs["Vector"])
        links.new(uv.outputs["UV"], channel_map.inputs["Vector"])
        links.new(color_map.outputs["Color"], shader.inputs["Base Color"])
        links.new(channel_map.outputs["Color"], separate.inputs["Color"])
        links.new(separate.outputs["Red"], shader.inputs["Metallic"])
        links.new(channel_map.outputs["Alpha"], roughness.inputs[1])
        links.new(roughness.outputs[0], shader.inputs["Roughness"])
        for link in list(shader.inputs["Normal"].links):
            links.remove(link)
        material["fuxi_source_uses_game_atlas"] = True
    return {"sourceUsesGameAtlases": True, "packedImages": [image.name for image in (base, packed)],
            "pathsRelativeToSource": [image.filepath for image in (base, packed)],
            "proceduralNodesRetainedDisconnected": True, "microscopicBumpConnected": False}


def _export_motion_groups(root, fbx_path):
    sys.path.insert(0, str(PROJECT / "Tools/Art/PhaseFive"))
    import common as art
    groups = {}
    for obj in root.children_recursive:
        if obj.type == "MESH":
            if "render_group" not in obj:
                raise RuntimeError("Authoring mesh has no functional export group: " + obj.name)
            groups.setdefault(obj["render_group"], []).append(obj)
    for name, objects in groups.items():
        parent = objects[0].parent
        if parent is None or not all(obj.parent == parent for obj in objects):
            raise RuntimeError("Export group mixes motion parents: " + name)
        art.join_meshes(objects, name, parent, tuple(parent.matrix_world.translation))
    _select([root] + list(root.children_recursive))
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(fbx_path.resolve()), use_selection=True,
        object_types={"EMPTY", "MESH"}, axis_forward="-Z", axis_up="Y", global_scale=1,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", use_space_transform=True,
        bake_space_transform=False, use_mesh_modifiers=True, mesh_smooth_type="FACE", use_triangles=True,
        add_leaf_bones=False, bake_anim=False, path_mode="RELATIVE", use_custom_props=True)
    return {"meshCount": len(groups), "groups": sorted(groups)}


def review_saved_surface(blend_path, fbx_path, manifest_path, report_path):
    """Reopen the packed source and import the final FBX before refreshing evidence."""
    sys.path.insert(0, str(PROJECT / "Tools/Art/PhaseFive"))
    import common as art
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    report = json.loads(report_path.read_text(encoding="utf-8")) if report_path.is_file() else {"asset": "FuxiTurret"}
    bpy.ops.wm.open_mainfile(filepath=str(blend_path.resolve()))
    root = bpy.data.objects["FuxiTurret"]
    source_objects = [root] + list(root.children_recursive)
    source = art.inspect_objects(source_objects)
    def uv_records(objects):
        result = []
        for obj in objects:
            if obj.type != "MESH":
                continue
            layer = obj.data.uv_layers.active
            valid = layer is not None and len(layer.data) > 0 and all(math.isfinite(value) and -1e-5 <= value <= 1.00001 for loop in layer.data for value in loop.uv)
            result.append({"mesh": obj.name, "activeLayer": layer.name if layer else None, "loopCount": len(layer.data) if layer else 0, "finiteUnitSquare": valid})
        return result
    source_uv = uv_records(source_objects)
    source_materials = []
    for spec in manifest["materials"]:
        material = bpy.data.materials[spec["name"]]
        nodes = material.node_tree.nodes
        shader = nodes["ProductionSurface"]
        base = nodes.get("GameBaseColorAtlas")
        packed = nodes.get("GameMetallicSmoothnessAtlas")
        valid = bool(base and packed and base.image and packed.image and base.image.packed_file and packed.image.packed_file and
                     base.image.filepath.startswith("//") and packed.image.filepath.startswith("//") and
                     shader.inputs["Base Color"].is_linked and shader.inputs["Base Color"].links[0].from_node == base and
                     shader.inputs["Metallic"].is_linked and shader.inputs["Roughness"].is_linked and not shader.inputs["Normal"].is_linked)
        source_materials.append({"name": material.name, "authoredBaseColor": list(material["fuxi_surface_base_color"]),
            "baseColorImage": base.image.name if base and base.image else None,
            "metallicSmoothnessImage": packed.image.name if packed and packed.image else None,
            "usesPackedRelativeGameAtlases": valid})
    packed_images = [{"name": image.name, "size": list(image.size), "channels": image.channels, "colorSpace": image.colorspace_settings.name,
                      "relativePath": image.filepath, "packedBytes": len(image.packed_file.data)}
                     for image in bpy.data.images if image.name.startswith("FuxiTurret_Game") and image.packed_file]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path.resolve()), use_custom_normals=True)
    imported_objects = list(bpy.context.scene.objects)
    imported = art.inspect_objects(imported_objects)
    imported_uv = uv_records(imported_objects)
    checks = {"sourceSavedAndReopened": True, "fbxReimported": True,
              "triangleCountMatches": source["triangles"] == imported["triangles"],
              "materialsMatchManifest": source["material_names"] == imported["material_names"] == sorted(spec["name"] for spec in manifest["materials"]),
              "allSourceMaterialsUsePackedGameAtlas": all(record["usesPackedRelativeGameAtlases"] for record in source_materials),
              "twoPackedAtlasRgbaImages": len(packed_images) == 2 and all(record["size"] == [manifest["surfaceAtlas"]["size"]] * 2 and record["channels"] == 4 for record in packed_images),
              "sourceAndFbxUvValid": all(record["finiteUnitSquare"] and record["activeLayer"] == "FuxiSurfaceUV" for record in source_uv + imported_uv)}
    report["sourceReopened"] = source
    # The delivered file is the canonical nine-group export; these statistics
    # are refreshed from its real reimport rather than copied from an old draft.
    report["consolidatedExport"] = imported
    report["fbxReimported"] = imported
    report["stage"] = "Final shared-atlas source and FBX reopened after baking"
    report["statisticsScope"] = "Geometry counts measured from saved files after atlas baking. Independent detailed topology and corner-normal checks are in TopologyReview.json."
    report["surfaceMaterials"] = source_materials
    report["packedSourceImages"] = packed_images
    report["surfaceUv"] = {"source": source_uv, "fbx": imported_uv}
    report["checks"] = checks
    report["passed"] = all(checks.values())
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("FUXI_SAVED_SURFACE_REVIEW " + json.dumps({"passed": report["passed"], "checks": checks,
        "sourceParts": source["mesh_count"], "exportGroups": imported["mesh_count"], "triangles": imported["triangles"], "report": str(report_path)}, ensure_ascii=False), flush=True)
    if not report["passed"]:
        raise RuntimeError("Final saved source or FBX surface review failed.")
    return report


def bake_atlas(blend_path, fbx_path, manifest_path, report_path, size=2048, samples=8, armor_color=None):
    """Pack original UVs, bake on a disposable merged copy, save parts, export groups."""
    import numpy as np
    blend_path, fbx_path, manifest_path, report_path = (path.resolve() for path in (blend_path, fbx_path, manifest_path, report_path))
    started = time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(blend_path.resolve()))
    root = bpy.data.objects.get("FuxiTurret")
    if root is None:
        raise RuntimeError("Source has no FuxiTurret root.")
    meshes = [obj for obj in root.children_recursive if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("Source has no mesh descendants.")
    visibility = {obj: (obj.hide_get(), obj.hide_viewport) for obj in meshes}
    before = _geometry_record(meshes)
    if armor_color is not None:
        if len(armor_color) != 3 or not all(math.isfinite(value) and 0 <= value <= 1 for value in armor_color):
            raise ValueError("Armor color must have three finite linear components between zero and one.")
        material = bpy.data.materials.get("Fuxi_Armor")
        if material is None:
            raise RuntimeError("Cannot apply final palette: source has no Fuxi_Armor material.")
        material["fuxi_surface_base_color"] = list(armor_color) + [1]
        material.diffuse_color = tuple(armor_color) + (1,)
    specs = apply_surface("FuxiTurret")
    materials = [bpy.data.materials[name] for name in MATERIAL_NAMES]
    unexpected = sorted({m.name for obj in meshes for m in obj.data.materials if m and m.name not in MATERIAL_NAMES})
    if unexpected:
        raise RuntimeError("Unspecified production materials: " + ", ".join(unexpected))
    uv = _pack_shared_uv(meshes)
    bake_mesh = _temporary_bake_mesh(meshes)
    # Ambient occlusion must see the single temporary assembled surface, not
    # coincident original parts or the presentation floor. Otherwise contact
    # tone would bake nearly black or include studio-specific shadows.
    render_visibility = [(obj, obj.hide_render) for obj in bpy.context.scene.objects if obj.type == "MESH" and obj != bake_mesh]
    for obj, _ in render_visibility:
        obj.hide_render = True
    scene = bpy.context.scene
    previous_engine, previous_samples = scene.render.engine, scene.cycles.samples
    previous_device = scene.cycles.device
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.use_selected_to_active = False
    scene.render.bake.target = "IMAGE_TEXTURES"
    images = []
    try:
        base = _bake_pass(bake_mesh, materials, "BaseColor", size)
        images.append(base)
        metallic = _bake_pass(bake_mesh, materials, "Metallic", size)
        images.append(metallic)
        smoothness = _bake_pass(bake_mesh, materials, "Smoothness", size)
        images.append(smoothness)
        metal_pixels = np.empty(size * size * 4, dtype=np.float32)
        smooth_pixels = np.empty_like(metal_pixels)
        metallic.pixels.foreach_get(metal_pixels)
        smoothness.pixels.foreach_get(smooth_pixels)
        packed = metal_pixels.reshape((-1, 4))
        packed[:, 1:3] = 0
        packed[:, 3] = smooth_pixels.reshape((-1, 4))[:, 0]
        combined = bpy.data.images.new("FuxiTurret_MetallicSmoothness", width=size, height=size, alpha=True, float_buffer=False)
        combined.colorspace_settings.name = "Non-Color"
        combined.alpha_mode = "CHANNEL_PACKED"
        combined.pixels.foreach_set(packed.ravel())
        combined.update()
        base.name = "FuxiTurret_BaseColor"
        texture_dir = fbx_path.parent.parent / "Textures"
        base_path = texture_dir / "FuxiTurret_BaseColor.png"
        packed_path = texture_dir / "FuxiTurret_MetallicSmoothness.png"
        _save_atlas(base, base_path)
        _save_atlas(combined, packed_path)
        base_pixels = np.empty_like(metal_pixels)
        base.pixels.foreach_get(base_pixels)
        occupancy = float(np.count_nonzero(np.any(base_pixels.reshape((-1, 4))[:, :3] > 1e-6, axis=1)) / (size * size))
        if not np.isfinite(packed).all() or occupancy < .05:
            raise RuntimeError("Baked atlas is empty or contains nonfinite surface values.")
    finally:
        bpy.data.objects.remove(bake_mesh, do_unlink=True)
        for obj, hidden in render_visibility:
            obj.hide_render = hidden
        for image in images[1:]:
            bpy.data.images.remove(image)
        scene.render.engine, scene.cycles.samples = previous_engine, previous_samples
        scene.cycles.device = previous_device
    after = _geometry_record(meshes)
    if before != after:
        raise RuntimeError("UV or bake operations changed authored geometry or motion parents.")
    for obj, (hidden, hidden_viewport) in visibility.items():
        obj.hide_set(hidden)
        obj.hide_viewport = hidden_viewport
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    for spec in specs:
        spec["baseColorTexture"] = base_path.relative_to(PROJECT).as_posix()
        spec["metallicSmoothnessTexture"] = packed_path.relative_to(PROJECT).as_posix()
    manifest["materials"] = specs
    manifest["surfaceAtlas"] = {"size": size, "baseColorEncoding": "sRGB", "metallicSmoothnessEncoding": "Linear: metallic in red, smoothness in alpha", "uvLayer": uv["uvLayer"]}
    backup = blend_path.with_name(blend_path.stem + ".BeforeAtlas.blend")
    if not backup.exists():
        shutil.copy2(blend_path, backup)
    source_surface = _bind_packed_atlases(materials, base_path, packed_path, blend_path)
    bpy.data.images.remove(base)
    bpy.data.images.remove(combined)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path.resolve()))
    export = _export_motion_groups(root, fbx_path)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    report = {"asset": "FuxiTurret", "source": str(blend_path), "export": str(fbx_path), "sourceBeforeAtlas": str(backup),
              "size": size, "samples": samples, "bakeEngine": "Cycles CPU EMIT", "passes": ["BaseColor", "Metallic", "Smoothness"],
              "surfaceVersion": 3, "contactOcclusionDistanceMeters": .006,
              "contactOcclusionScope": "Low-strength nondirectional local contact tone in base color; source duplicates and studio floor excluded from baking.",
              "sourcePartCount": len(meshes), "sourceGeometryAndMotionParentsUnchanged": before == after, "uv": uv, "exportGroups": export,
              "baseColorTexture": str(base_path), "metallicSmoothnessTexture": str(packed_path), "occupiedFractionIncludingPadding": occupancy,
              "sourceSurface": source_surface,
              "elapsedSeconds": time.perf_counter() - started,
              "scope": "The saved Blender source and Unity use the same base-color and metallic-smoothness PNGs. Images are packed in the source with relative paths. Original procedural nodes remain disconnected for editing; microscopic bump is disconnected to prevent texture swimming on mechanical motion."}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("FUXI_SURFACE_BAKE " + json.dumps(report, ensure_ascii=False), flush=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bake", action="store_true")
    parser.add_argument("--review-only", action="store_true", help="Only reopen delivered files and refresh ProductionReport.json.")
    parser.add_argument("--geometry-final", action="store_true", help="Set only after the geometry author has handed over these files.")
    parser.add_argument("--blend", type=Path, default=PROJECT / "ArtSource/Production/FuxiTurret/FuxiTurret.blend")
    parser.add_argument("--fbx", type=Path, default=PROJECT / "Assets/Defend/Content/Art/FuxiTurret/Models/FuxiTurret.fbx")
    parser.add_argument("--manifest", type=Path, default=PROJECT / "Docs/Art/FuxiTurret/Checks/AssetManifest.json")
    parser.add_argument("--report", type=Path, default=PROJECT / "Docs/Art/FuxiTurret/Checks/SurfaceBake.json")
    parser.add_argument("--size", type=int, default=2048)
    parser.add_argument("--samples", type=int, default=8)
    parser.add_argument("--armor-color", type=float, nargs=3, help="Optional approved final armor palette in linear RGB.")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.review_only:
        review_saved_surface(args.blend, args.fbx, args.manifest, args.manifest.parent / "ProductionReport.json")
        return
    if not args.bake or not args.geometry_final:
        parser.error("Atlas baking requires --bake --geometry-final after the author's explicit geometry handover.")
    if args.size <= 0 or args.size > 4096 or args.samples <= 0:
        parser.error("Atlas size must be 1-4096 pixels and samples must be positive.")
    bake_atlas(args.blend, args.fbx, args.manifest, args.report, args.size, args.samples, args.armor_color)


if __name__ == "__main__":
    main()
