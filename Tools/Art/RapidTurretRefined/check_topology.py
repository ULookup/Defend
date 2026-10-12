"""Independent read-only Blender audit for a turret source and delivered FBX.

Run with Blender --background --python-exit-code 2 --python this_file -- ... .
The checker opens saved files, never edits or re-saves their contents, and writes only its JSON report.
Disconnected closed shells are counted explicitly; this is not a claim that the assembled turret
is one continuous manifold solid. Opposed coplanar contacts between closed parts are reported
separately from duplicate or same-facing surfaces that can produce visible overlap artifacts.
"""
import argparse
import json
import math
import sys
import traceback
from collections import Counter, defaultdict
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


AREA_EPSILON = 1e-12
POSITION_EPSILON = 1e-7
COPLANAR_EPSILON = 2e-7
OVERLAP_AREA_EPSILON = 1e-10


def arguments():
    parser = argparse.ArgumentParser()
    project = Path(__file__).resolve().parents[3]
    parser.add_argument("--blend", type=Path, default=project / "ArtSource/Production/RapidTurretRefined/RapidTurretRefined.blend")
    parser.add_argument("--fbx", type=Path, default=project / "Assets/Defend/Content/Art/RapidTurretRefined/Models/RapidTurretRefined.fbx")
    parser.add_argument("--report", type=Path, default=project / "Docs/Art/RapidTurretRefined/Checks/TopologyReview.json")
    parser.add_argument("--root", default="RapidTurretRefined")
    parser.add_argument("--max-triangles", type=int, default=100000)
    parser.add_argument("--max-materials", type=int, default=6)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.max_triangles <= 0 or args.max_materials <= 0:
        parser.error("Asset budgets must be positive integers.")
    return args


def finite(values):
    return all(math.isfinite(float(value)) for value in values)


def point_key(point):
    return tuple(round(float(value) / POSITION_EPSILON) for value in point)


def asset_objects(root_name):
    root = bpy.data.objects.get(root_name)
    if root is None:
        candidates = [obj for obj in bpy.context.scene.objects if obj.parent is None and obj.name.startswith(root_name)]
        if len(candidates) != 1:
            raise RuntimeError("Cannot uniquely identify asset root: " + root_name)
        root = candidates[0]
    return root, [root] + list(root.children_recursive)


def mesh_review(obj, depsgraph, global_triangles, evaluate):
    evaluated = obj.evaluated_get(depsgraph) if evaluate else obj
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    try:
        mesh.calc_loop_triangles()
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        bm.edges.ensure_lookup_table()
        bm.faces.ensure_lookup_table()
        boundary = sum(len(edge.link_faces) == 1 for edge in bm.edges)
        wire_edges = sum(len(edge.link_faces) == 0 for edge in bm.edges)
        nonmanifold = sum(len(edge.link_faces) > 2 for edge in bm.edges)
        winding = sum(len(edge.link_faces) == 2 and not edge.is_contiguous for edge in bm.edges)
        loose_vertices = sum(not vertex.link_faces for vertex in bm.verts)
        visited = set()
        face_component = {}
        components = []
        for face in bm.faces:
            if face.index in visited:
                continue
            members = []
            pending = [face]
            visited.add(face.index)
            while pending:
                current = pending.pop()
                members.append(current)
                for edge in current.edges:
                    for adjacent in edge.link_faces:
                        if adjacent.index not in visited:
                            visited.add(adjacent.index)
                            pending.append(adjacent)
            component_id = len(components)
            for current in members:
                face_component[current.index] = component_id
            edges = {edge for current in members for edge in current.edges}
            components.append({
                "faces": len(members),
                "closed": all(len(edge.link_faces) == 2 for edge in edges),
                "consistent_winding": all(len(edge.link_faces) != 2 or edge.is_contiguous for edge in edges),
                "signed_volume_cubic_meters": 0.0,
                "reference": members[0].verts[0].co.copy(),
            })

        index_faces = Counter(tuple(sorted(poly.vertices)) for poly in mesh.polygons)
        duplicate_index_faces = sum(count - 1 for count in index_faces.values() if count > 1)
        geometric_faces = defaultdict(list)
        for poly in mesh.polygons:
            key = tuple(sorted(point_key(mesh.vertices[index].co) for index in poly.vertices))
            geometric_faces[key].append(poly.index)
        duplicate_geometric_faces = sum(len(indices) - 1 for indices in geometric_faces.values() if len(indices) > 1)
        nonfinite_coordinates = sum(not finite(vertex.co) for vertex in mesh.vertices)
        nonfinite_face_normals = sum(not finite(poly.normal) for poly in mesh.polygons)
        nonfinite_corner_normals = 0
        zero_corner_normals = 0
        opposed_corner_normals = 0
        if hasattr(mesh, "corner_normals"):
            for poly in mesh.polygons:
                for loop_index in poly.loop_indices:
                    normal = mesh.corner_normals[loop_index].vector
                    nonfinite_corner_normals += not finite(normal)
                    zero_corner_normals += normal.length_squared < 1e-12
                    opposed_corner_normals += normal.dot(poly.normal) < -.01

        zero_area = 0
        nonfinite_area = 0
        uv_collapsed_triangles = 0
        uv = mesh.uv_layers.active
        uv_nonfinite = 0 if uv is None else sum(not finite(loop.uv) for loop in uv.data)
        uv_unique = 0 if uv is None else len({tuple(round(float(value), 7) for value in loop.uv) for loop in uv.data})
        world_matrix = obj.matrix_world
        for tri in mesh.loop_triangles:
            coordinates = [mesh.vertices[index].co.copy() for index in tri.vertices]
            area = (coordinates[1] - coordinates[0]).cross(coordinates[2] - coordinates[0]).length * .5
            zero_area += area <= AREA_EPSILON
            nonfinite_area += not math.isfinite(area)
            component_id = face_component[tri.polygon_index]
            component = components[component_id]
            origin = component["reference"]
            a, b, c = (coordinate - origin for coordinate in coordinates)
            component["signed_volume_cubic_meters"] += a.dot(b.cross(c)) / 6.0
            if uv is not None:
                values = [uv.data[index].uv for index in tri.loops]
                uv_area = abs((values[1].x - values[0].x) * (values[2].y - values[0].y) - (values[1].y - values[0].y) * (values[2].x - values[0].x)) * .5
                uv_collapsed_triangles += uv_area <= 1e-12
            world_points = tuple(world_matrix @ coordinate for coordinate in coordinates)
            global_triangles.append({"object": obj.name, "polygon": tri.polygon_index, "component": component_id, "points": world_points})
        for component in components:
            del component["reference"]
            component["signed_volume_cubic_meters"] *= world_matrix.determinant()
        closed_components = [component for component in components if component["closed"] and component["consistent_winding"]]
        inverted_components = sum(component["signed_volume_cubic_meters"] < -1e-12 for component in closed_components)
        degenerate_closed_components = sum(abs(component["signed_volume_cubic_meters"]) <= 1e-12 for component in closed_components)
        bm.free()
        result = {
            "name": obj.name, "vertices": len(mesh.vertices), "edges": len(mesh.edges), "faces": len(mesh.polygons),
            "triangles": len(mesh.loop_triangles), "boundary_edges": boundary, "wire_edges": wire_edges,
            "edges_with_more_than_two_faces": nonmanifold, "inconsistent_winding_edges": winding,
            "loose_vertices": loose_vertices, "zero_area_triangles": zero_area,
            "duplicate_faces_same_vertex_indices": duplicate_index_faces, "coincident_polygon_coordinate_sets": duplicate_geometric_faces,
            "nonfinite_coordinates": nonfinite_coordinates, "nonfinite_areas": nonfinite_area,
            "nonfinite_face_normals": nonfinite_face_normals, "nonfinite_corner_normals": nonfinite_corner_normals,
            "zero_corner_normals": zero_corner_normals, "corner_normals_opposed_to_face": opposed_corner_normals,
            "uv_layers": [layer.name for layer in mesh.uv_layers], "uv_unique_coordinates": uv_unique,
            "uv_nonfinite_coordinates": uv_nonfinite, "uv_collapsed_triangles": uv_collapsed_triangles,
            "connected_face_components": len(components), "closed_components": len(closed_components),
            "inverted_closed_components": inverted_components, "degenerate_closed_components": degenerate_closed_components,
            "components": components,
            "modifiers": [modifier.type for modifier in obj.modifiers],
            "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
        }
        failure_fields = ("boundary_edges", "wire_edges", "edges_with_more_than_two_faces", "inconsistent_winding_edges", "loose_vertices",
                          "zero_area_triangles", "duplicate_faces_same_vertex_indices", "nonfinite_coordinates", "nonfinite_areas",
                          "nonfinite_face_normals", "nonfinite_corner_normals", "zero_corner_normals", "corner_normals_opposed_to_face",
                          "uv_nonfinite_coordinates", "uv_collapsed_triangles", "inverted_closed_components", "degenerate_closed_components")
        result["passed"] = result["faces"] > 0 and all(result[field] == 0 for field in failure_fields) and uv is not None and uv_unique >= 3
        result["failed_checks"] = [field for field in failure_fields if result[field] != 0]
        if uv is None or uv_unique < 3:
            result["failed_checks"].append("missing_or_empty_uv")
        return result
    finally:
        evaluated.to_mesh_clear()


def projected_overlap_area(first, second, normal):
    excluded_axis = max(range(3), key=lambda axis: abs(normal[axis]))
    axes = [axis for axis in range(3) if axis != excluded_axis]
    subject = [(float(point[axes[0]]), float(point[axes[1]])) for point in first]
    clip = [(float(point[axes[0]]), float(point[axes[1]])) for point in second]
    signed = sum(clip[index][0] * clip[(index + 1) % 3][1] - clip[(index + 1) % 3][0] * clip[index][1] for index in range(3))
    orientation = 1 if signed >= 0 else -1
    for index in range(3):
        a, b = clip[index], clip[(index + 1) % 3]
        def distance(point):
            return orientation * ((b[0] - a[0]) * (point[1] - a[1]) - (b[1] - a[1]) * (point[0] - a[0]))
        output = []
        if not subject:
            return 0.0
        previous = subject[-1]
        previous_distance = distance(previous)
        for current in subject:
            current_distance = distance(current)
            if (current_distance >= 0) != (previous_distance >= 0):
                denominator = previous_distance - current_distance
                if abs(denominator) > 1e-20:
                    ratio = previous_distance / denominator
                    output.append((previous[0] + ratio * (current[0] - previous[0]), previous[1] + ratio * (current[1] - previous[1])))
            if current_distance >= 0:
                output.append(current)
            previous, previous_distance = current, current_distance
        subject = output
    if len(subject) < 3:
        return 0.0
    projected_area = abs(sum(subject[index][0] * subject[(index + 1) % len(subject)][1] - subject[(index + 1) % len(subject)][0] * subject[index][1] for index in range(len(subject)))) * .5
    return projected_area / max(1e-12, abs(normal[excluded_axis]))


def surface_overlap_review(triangles):
    same_facing = 0
    opposed_contacts = 0
    exact_duplicates = 0
    examples = []
    object_pairs = {}
    reviewed = set()
    normals = []
    plane_groups = defaultdict(list)
    for triangle in triangles:
        a, b, c = triangle["points"]
        normal = (b - a).cross(c - a).normalized()
        normals.append(normal)
        axis = max(range(3), key=lambda value: abs(normal[value]))
        canonical_normal = normal if normal[axis] >= 0 else -normal
        normal_group = tuple(round(value, 4) for value in canonical_normal)
        plane_offset = round(canonical_normal.dot(a) / 1e-5)
        projected_axes = [value for value in range(3) if value != axis]
        box = (min(point[projected_axes[0]] for point in triangle["points"]), max(point[projected_axes[0]] for point in triangle["points"]),
               min(point[projected_axes[1]] for point in triangle["points"]), max(point[projected_axes[1]] for point in triangle["points"]))
        # A BVH triangle-intersection query can omit exactly coplanar surfaces. Plane grouping
        # plus a 2D rectangle sweep supplies candidates; precise plane and area tests follow.
        for offset in (plane_offset - 1, plane_offset, plane_offset + 1):
            plane_groups[(normal_group, offset, axis)].append((len(normals) - 1, box))
    candidate_pairs = set()
    for entries in plane_groups.values():
        active = []
        for index, box in sorted(entries, key=lambda item: item[1][0]):
            active = [(other_index, other_box) for other_index, other_box in active if other_box[1] >= box[0] - COPLANAR_EPSILON]
            for other_index, other_box in active:
                if other_box[3] >= box[2] - COPLANAR_EPSILON and box[3] >= other_box[2] - COPLANAR_EPSILON:
                    candidate_pairs.add((min(index, other_index), max(index, other_index)))
            active.append((index, box))
    for first_index, second_index in sorted(candidate_pairs):
        first, second = triangles[first_index], triangles[second_index]
        if first["object"] == second["object"] and first["polygon"] == second["polygon"]:
            continue
        pair = ((first["object"], first["polygon"]), (second["object"], second["polygon"]))
        if pair in reviewed:
            continue
        first_normal, second_normal = normals[first_index], normals[second_index]
        dot = first_normal.dot(second_normal)
        if abs(dot) < .99999:
            continue
        if any(abs(first_normal.dot(point - first["points"][0])) > COPLANAR_EPSILON for point in second["points"]):
            continue
        area = projected_overlap_area(first["points"], second["points"], first_normal)
        if area <= OVERLAP_AREA_EPSILON:
            continue
        reviewed.add(pair)
        exact = sorted(point_key(point) for point in first["points"]) == sorted(point_key(point) for point in second["points"])
        same_component = first["object"] == second["object"] and first["component"] == second["component"]
        if dot > 0 or same_component:
            same_facing += 1
            exact_duplicates += exact
            object_pair = (first["object"], second["object"])
            if object_pair not in object_pairs:
                object_pairs[object_pair] = {"first_object": first["object"], "second_object": second["object"],
                    "overlapping_face_pairs": 0, "exact_duplicate_triangles": 0,
                    "example_first_polygon": first["polygon"], "example_second_polygon": second["polygon"],
                    "example_world_point": list(first["points"][0]), "example_world_normal": list(first_normal)}
            object_pairs[object_pair]["overlapping_face_pairs"] += 1
            object_pairs[object_pair]["exact_duplicate_triangles"] += int(exact)
            if len(examples) < 40:
                examples.append({"first_object": first["object"], "first_polygon": first["polygon"], "second_object": second["object"],
                                 "second_polygon": second["polygon"], "same_component": same_component, "same_facing": dot > 0,
                                 "first_component": first["component"], "second_component": second["component"],
                                 "first_world_triangle": [list(point) for point in first["points"]],
                                 "second_world_triangle": [list(point) for point in second["points"]],
                                 "first_world_normal": list(first_normal),
                                 "overlap_area_square_meters": area, "exact_triangle_match": exact})
        else:
            opposed_contacts += 1
    return {"same_facing_or_same_component_overlapping_face_pairs": same_facing, "exact_duplicate_triangle_pairs": exact_duplicates,
            "opposed_contacts_between_separate_shells": opposed_contacts, "examples": examples,
            "object_pairs": list(object_pairs.values()),
            "passed": same_facing == 0,
            "method": "Plane grouping, projected bounding-rectangle sweep, and exact 2D polygon clipping; no reliance on coplanar BVH triangle intersections.",
            "scope": "Coplanar surface overlap, not a solid-union or all-intersections test; opposed contacts between separate closed shells are reported without claiming a single manifold assembly."}


def scene_review(root_name, evaluate):
    root, objects = asset_objects(root_name)
    meshes = sorted((obj for obj in objects if obj.type == "MESH"), key=lambda obj: obj.name)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    triangles = []
    reviews = [mesh_review(obj, depsgraph, triangles, evaluate) for obj in meshes]
    transform_issues = [{"name": obj.name, "local_scale": list(obj.scale), "world_determinant": obj.matrix_world.determinant()}
                        for obj in objects if not finite(obj.scale) or any(value <= 0 for value in obj.scale) or obj.matrix_world.determinant() <= 0]
    bounds_points = [point for triangle in triangles for point in triangle["points"]]
    dimensions = [max(point[axis] for point in bounds_points) - min(point[axis] for point in bounds_points) for axis in range(3)] if bounds_points else []
    overlap = surface_overlap_review(triangles)
    totals = {field: sum(review[field] for review in reviews) for field in ("vertices", "faces", "triangles", "boundary_edges", "wire_edges",
              "edges_with_more_than_two_faces", "loose_vertices", "zero_area_triangles", "inconsistent_winding_edges", "connected_face_components",
              "closed_components", "inverted_closed_components", "nonfinite_face_normals", "nonfinite_corner_normals", "uv_collapsed_triangles")}
    return {"root": root.name, "evaluated_modifiers": evaluate, "mesh_count": len(meshes), "totals": totals,
            "dimensions_meters": dimensions, "negative_zero_or_nonfinite_transforms": transform_issues,
            "materials": sorted({name for review in reviews for name in review["materials"] if name}),
            "meshes": reviews, "surface_overlaps": overlap,
            "passed": len(meshes) > 0 and all(review["passed"] for review in reviews) and not transform_issues and overlap["passed"]}


def main():
    args = arguments()
    report = {"blender_version": bpy.app.version_string, "source": str(args.blend.resolve()), "export": str(args.fbx.resolve()),
              "asset_budget": {"maximum_triangles": args.max_triangles, "maximum_materials": args.max_materials},
              "method": "Independently open the saved Blender source, inspect evaluated mesh data, then reset Blender and import the delivered FBX. No source asset is modified.",
              "assembly_scope": "Multiple closed hard-surface shells are intentional and counted. This review does not label the complete assembled model one manifold solid.",
              "tolerances": {"zero_area_square_meters": AREA_EPSILON, "coordinate_comparison_meters": POSITION_EPSILON,
                             "coplanar_distance_meters": COPLANAR_EPSILON, "overlap_area_square_meters": OVERLAP_AREA_EPSILON}}
    try:
        if not args.blend.is_file() or not args.fbx.is_file():
            raise FileNotFoundError("Both final saved .blend and delivered .fbx must exist before the independent audit.")
        bpy.ops.wm.open_mainfile(filepath=str(args.blend.resolve()))
        report["saved_source_reopened"] = scene_review(args.root, evaluate=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(args.fbx.resolve()), use_custom_normals=True)
        report["fbx_reimported"] = scene_review(args.root, evaluate=False)
        source, exported = report["saved_source_reopened"], report["fbx_reimported"]
        dimensions_match = len(source["dimensions_meters"]) == len(exported["dimensions_meters"]) == 3 and all(abs(a - b) < .0001 for a, b in zip(source["dimensions_meters"], exported["dimensions_meters"]))
        report["comparison"] = {
            "triangle_count_matches": source["totals"]["triangles"] == exported["totals"]["triangles"],
            "dimensions_match_within_0_1_millimeter": dimensions_match,
            "material_names_match": source["materials"] == exported["materials"],
            "triangles_within_declared_budget": 0 < exported["totals"]["triangles"] <= args.max_triangles,
            "materials_within_declared_budget": 0 < len(exported["materials"]) <= args.max_materials,
            "source_parts_and_export_groups": {"source_mesh_count": source["mesh_count"], "export_mesh_count": exported["mesh_count"],
                                                "note": "Export may join source parts into functional motion groups; equal mesh counts are not required."}}
        comparisons = report["comparison"]
        report["passed"] = source["passed"] and exported["passed"] and all(value for value in comparisons.values() if isinstance(value, bool))
    except Exception as error:
        report["passed"] = False
        report["error"] = str(error)
        report["traceback"] = traceback.format_exc()
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("TOPOLOGY_REVIEW " + json.dumps({"passed": report["passed"], "report": str(args.report.resolve()),
          "source_totals": report.get("saved_source_reopened", {}).get("totals"), "fbx_totals": report.get("fbx_reimported", {}).get("totals"),
          "error": report.get("error")}, ensure_ascii=False))
    if not report["passed"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
