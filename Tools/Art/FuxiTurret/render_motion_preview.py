"""Render the authored mechanical animation without editing either source file."""
from pathlib import Path
import argparse
import bpy
import sys

root = Path(__file__).resolve().parents[3]
parser = argparse.ArgumentParser()
parser.add_argument('--source', default=str(root / 'ArtSource/Production/FuxiTurret/MotionShowcase.blend'))
parser.add_argument('--step', type=int, default=4)
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
bpy.ops.wm.open_mainfile(filepath=args.source)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
if hasattr(scene.eevee, 'taa_render_samples'):
    scene.eevee.taa_render_samples = 24
scene.render.resolution_x = 800
scene.render.resolution_y = 650
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
directory = root / 'Docs/Art/FuxiTurret/Previews/MechanicalMotionFrames'
directory.mkdir(parents=True, exist_ok=True)
for frame in range(scene.frame_start, scene.frame_end + 1, args.step):
    scene.frame_set(frame)
    scene.render.filepath = str(directory / f'frame-{frame:04d}.png')
    bpy.ops.render.render(write_still=True)
print('MECHANICAL_MOTION_RENDER_COMPLETE', str(directory), flush=True)
