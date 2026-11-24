"""Offline package inspection with generated project folders excluded."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys


def inventory(root, excluded):
    images = {}
    total = 0
    for directory, dirs, files in os.walk(root, followlinks=False):
        dirs[:] = [name for name in dirs if (Path(directory) / name).resolve() not in excluded]
        for name in files:
            path = Path(directory) / name
            if path.suffix.casefold() not in {'.dll', '.exe'}:
                continue
            if path.is_symlink() or not path.resolve().is_relative_to(root):
                raise ValueError('A PE candidate escapes the game folder.')
            size = path.stat().st_size
            total += size
            if size > 512 * 1024**2 or total > 2 * 1024**3 or len(images) >= 1024:
                raise ValueError('Package exceeds the upstream PE inspection limits.')
            images[path.relative_to(root).as_posix()] = path
    return images


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--suite', type=Path, required=True)
    parser.add_argument('--game-exe', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--exclude-directory', type=Path, action='append', default=[])
    args = parser.parse_args()
    image = args.game_exe.resolve(strict=True)
    root = image.parent
    output = args.output.resolve()
    reports = args.project.resolve() / 'launcher-data' / 'reports'
    if not output.is_relative_to(reports) or output.suffix != '.json':
        raise ValueError('Output must be a JSON report inside this project’s reports folder.')
    excluded = {path.resolve() for path in args.exclude_directory}
    if root in excluded or any(root.is_relative_to(path) for path in excluded):
        raise ValueError('Cannot exclude the game root or its parent.')
    tools = args.suite.resolve() / 'tools'
    sys.path.insert(0, str(tools))
    spec = importlib.util.spec_from_file_location('upstream_package_report', tools / 'pe_package_preflight.py')
    upstream = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(upstream)
    paths = inventory(root, excluded)
    report = upstream.summarize({name: upstream.parse_image(path) for name, path in sorted(paths.items())}, image.name)
    report['launcher_scope'] = {'excluded_generated_folders': sorted(str(path) for path in excluded), 'game_code_executed': False}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, sort_keys=True) + '\n', encoding='utf-8', newline='\n')
    print(f'Inspected {len(paths)} package PE images offline; generated project folders were excluded.')
    print(f'Report: {output}')


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        sys.exit(1)
