"""Run Leonida's shader-only pipeline in the user's chosen project subfolder.

The all-in-one upstream CLI rejects a work folder under the game package.
This integration calls its shader stage, enforces a separate named project
subfolder, and never runs the asset-export or runtime-build stages.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys


def validated_paths(source, game, project):
    source, game, project = (Path(p).resolve() for p in (source, game, project))
    if not source.is_dir() or not game.is_dir():
        raise ValueError('Source and game folders must exist.')
    if project == game or game.is_relative_to(project):
        raise ValueError('The project must be a separate folder, not the game folder itself or its parent.')
    if project == source or project.is_relative_to(source) or source.is_relative_to(project):
        raise ValueError('Generated shaders must stay outside the source checkout.')
    # Inside the game, only a dedicated direct subfolder is accepted; never data/pack.
    if project.is_relative_to(game) and (project.parent != game or project.name.casefold() in {'data', 'pack'}):
        raise ValueError('Use a dedicated project subfolder directly under the game folder.')
    work = project / 'work'
    if work.resolve() != work or not (work / 'shaders').resolve().is_relative_to(work):
        raise ValueError('Shader output may not redirect through another folder link.')
    return source, game, project, work


def prepare(source, game, project, jobs, builder=None, check_only=False):
    source, game, project, work = validated_paths(source, game, project)
    if builder is None:
        sys.path.insert(0, str(source))
        spec = importlib.util.spec_from_file_location('leonida_shader_builder', source / 'make_game.py')
        builder = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = builder
        spec.loader.exec_module(builder)
    for name in ('check', 'preflight', 'shaders', 'wave64_settings', 'runtime_gpu_per_vertex', 'runtime_gpu_max_push_constants'):
        if not callable(getattr(builder, name, None)):
            raise ValueError(f'Upstream shader API changed ({name}); update the launcher integration before preparing.')
    builder.check(str(game), False)
    emulate, keep_emulated = builder.wave64_settings('auto', mesh_compute=False)
    fallbacks = []
    if builder.runtime_gpu_per_vertex() is False:
        fallbacks.append('--f16-interp-fallback')
    max_pc = builder.runtime_gpu_max_push_constants()
    if max_pc is not None and max_pc < 144:
        fallbacks.append('--pc-tail-ubo')
    builder.preflight(['shaders'], str(work), emulate=emulate, keep_emulated=keep_emulated, fallbacks=tuple(fallbacks), jobs=jobs)
    if check_only:
        print(f'Shader prerequisites passed. Output: {work / "shaders" / "spirv"}. No shader generation started.', flush=True)
        return
    builder.shaders(str(game), str(work), jobs, emulate=emulate, keep_emulated=keep_emulated, fallbacks=tuple(fallbacks))
    catalog = work / 'shaders' / 'spirv'
    with (catalog / 'index.json').open(encoding='utf-8') as file:
        index = json.load(file)
    if not isinstance(index, dict) or not index or not any(catalog.glob('*.spv')):
        raise ValueError('Preparation did not produce a usable shader catalog. Review the stage output.')
    print(f'Shader catalog ready: {catalog}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True)
    parser.add_argument('--game', required=True)
    parser.add_argument('--project', required=True)
    parser.add_argument('--jobs', type=int, default=4)
    parser.add_argument('--check-only', action='store_true')
    args = parser.parse_args()
    if args.jobs < 1 or args.jobs > 64:
        parser.error('--jobs must be between 1 and 64')
    prepare(args.source, args.game, args.project, args.jobs, check_only=args.check_only)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, json.JSONDecodeError) as error:
        print(f'ERROR: {error}', file=sys.stderr, flush=True)
        sys.exit(1)
