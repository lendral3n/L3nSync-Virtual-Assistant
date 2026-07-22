# Batch convert BVH (Bandai Namco mocap) -> FBX untuk Unity Humanoid retarget.
# Jalankan headless:
#   /Applications/Blender.app/Contents/MacOS/Blender -b -P bvh_to_fbx.py -- <out_dir> <bvh1> <bvh2> ...
import bpy
import sys
import os


def clean_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.armatures, bpy.data.actions, bpy.data.objects):
        for item in list(block):
            try:
                block.remove(item)
            except Exception:
                pass


def enable_bvh_addon():
    try:
        import addon_utils
        addon_utils.enable("io_anim_bvh")
    except Exception:
        pass  # sudah built-in / enabled


def convert(bvh_path: str, out_dir: str) -> str:
    clean_scene()
    name = os.path.splitext(os.path.basename(bvh_path))[0]
    # scale 0.01: Bandai BVH dalam cm -> meter (tinggi skeleton ~1.7m)
    bpy.ops.import_anim.bvh(
        filepath=bvh_path,
        global_scale=0.01,
        frame_start=1,
        use_fps_scale=False,
        update_scene_fps=True,
        update_scene_duration=True,
        rotate_mode="NATIVE",
    )
    out_path = os.path.join(out_dir, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=out_path,
        use_selection=False,
        object_types={"ARMATURE"},
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_simplify_factor=0.0,  # no keyframe reduction
    )
    return out_path


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out_dir = argv[0]
    os.makedirs(out_dir, exist_ok=True)
    enable_bvh_addon()
    ok, fail = 0, 0
    for bvh in argv[1:]:
        try:
            p = convert(bvh, out_dir)
            print("OK:", p)
            ok += 1
        except Exception as e:
            print("FAIL:", bvh, "->", e)
            fail += 1
    print(f"SELESAI: {ok} ok, {fail} gagal")


main()
