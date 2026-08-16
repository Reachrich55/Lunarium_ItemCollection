#!/usr/bin/env python3
"""Derive web-crop-to-game-map transforms from Lunarium's Unity assets.

Requires UnityPy on PYTHONPATH. This is a maintainer tool; the generated JSON is
small and is embedded into the released mod, so end users do not need Python.
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import statistics
import struct
from collections import defaultdict
from pathlib import Path

import UnityPy
from PIL import Image


BUNDLE_NAMES = (
    "defaultlocalgroup_assets_all_5b37a28582e49aa9b4086f3a363f9ea0.bundle",
    "__shared_2_assets_all_900526763b9dff6e88654aa1cb9ff9fa.bundle",
    "__shared_3_assets_all_d937aa60ea48da6f0cad6fad7b50fae8.bundle",
    "__shared_4_assets_all_17dc6dc8fa7e6adbfe040e1d57033fc9.bundle",
    "__shared_4_max_assets_all_ac582b8a37b0f10cc73f13cbc6a094c7.bundle",
    "分块地图_assets_all_0556182ce2d6725a5afd221f8801ce84.bundle",
)


def read_key(data: bytes, offset: int):
    kind = data[offset]
    length = struct.unpack_from("<i", data, offset + 1)[0]
    payload = data[offset + 5 : offset + 5 + length]
    if kind == 0:
        return payload.decode("ascii")
    if kind == 1:
        return payload.decode("utf-16le")
    return None


def catalog_guid_paths(catalog_path: Path) -> dict[str, str]:
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    key_data = base64.b64decode(catalog["m_KeyDataString"])
    bucket_data = base64.b64decode(catalog["m_BucketDataString"])
    entry_data = base64.b64decode(catalog["m_EntryDataString"])
    internal_ids = catalog["m_InternalIds"]

    bucket_count = struct.unpack_from("<i", bucket_data, 0)[0]
    bucket_offset = 4
    result: dict[str, str] = {}
    for _ in range(bucket_count):
        key_offset, entry_count = struct.unpack_from("<ii", bucket_data, bucket_offset)
        bucket_offset += 8
        entries = struct.unpack_from(f"<{entry_count}i", bucket_data, bucket_offset) if entry_count else ()
        bucket_offset += entry_count * 4
        key = read_key(key_data, key_offset)
        if not isinstance(key, str) or len(key) != 32:
            continue
        try:
            int(key, 16)
        except ValueError:
            continue
        if not entries:
            continue
        entry_index = entries[0]
        internal_id_index = struct.unpack_from("<i", entry_data, 4 + entry_index * 28)[0]
        result[key] = internal_ids[internal_id_index]
    return result


def vec2(value):
    return float(value.x), float(value.y)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-dir", type=Path, default=Path(r"D:\Steam\steamapps\common\Lunarium"))
    parser.add_argument(
        "--source-data",
        type=Path,
        default=Path(r"D:\Lunarium_ItemCollection\public\data\map-data.json"),
    )
    parser.add_argument("--output", type=Path)
    parser.add_argument("--debug-dir", type=Path)
    parser.add_argument("--debug-map", action="append", default=[])
    args = parser.parse_args()

    aa_root = args.game_dir / "Lunarium_Data" / "StreamingAssets" / "aa"
    bundle_root = aa_root / "StandaloneWindows64"
    paths = [bundle_root / name for name in BUNDLE_NAMES]
    for path in paths:
        if not path.is_file():
            raise FileNotFoundError(path)

    web_data = json.loads(args.source_data.read_text(encoding="utf-8"))
    maps = [item for world in web_data["worlds"] for item in world["maps"]]
    map_by_progress = {item["collectibleProgressKey"]: item for item in maps}
    map_by_id = {item["id"]: item for item in maps}
    guid_paths = catalog_guid_paths(aa_root / "catalog.json")
    environment = UnityPy.load(*(os.fspath(path) for path in paths))

    sprites: dict[str, tuple[int, int]] = {}
    sprite_readers = {}
    for asset_path, pointer in environment.container.items():
        try:
            reader = pointer.deref()
        except (FileNotFoundError, ValueError):
            continue
        if reader.type.name != "Sprite":
            continue
        sprite = reader.read()
        texture = sprite.m_RD.texture.deref().read()
        sprites[asset_path] = (int(texture.m_Width), int(texture.m_Height))
        sprite_readers[asset_path] = reader

    config_list = None
    for asset in environment.assets:
        for reader in asset.objects.values():
            if reader.type.name != "MonoBehaviour":
                continue
            try:
                tree = reader.read_typetree()
            except Exception:
                continue
            if "areaMapConfigs" in tree and "levelRegionConfigs" in tree:
                config_list = reader.read()
                break
        if config_list is not None:
            break
    if config_list is None:
        raise RuntimeError("AreaMapConfigList was not found")

    bounds: dict[str, list[float]] = {}
    tiles: defaultdict[str, list[tuple[str, float, float, int, int]]] = defaultdict(list)
    tile_details: defaultdict[str, list[dict]] = defaultdict(list)
    tile_counts: defaultdict[str, int] = defaultdict(int)
    for pointer in config_list.areaMapConfigs:
        config = pointer.deref().read()
        region = config.levelRegionConfig.deref().read()
        exploration = region.regionExplorationConfig.deref().read()
        progress_key = exploration.collectibleProgressSaveKey
        if progress_key not in map_by_progress:
            continue
        guid = config.mapSpriteReference.m_AssetGUID
        asset_path = guid_paths.get(guid)
        if asset_path not in sprites:
            raise RuntimeError(f"Sprite for GUID {guid} was not found: {asset_path}")
        width, height = sprites[asset_path]
        local_x, local_y = vec2(config.areaMapLocalPositionInMapUI)
        offset_x, offset_y = vec2(config.areaMapOffset)
        center_x = local_x + offset_x
        center_y = local_y + offset_y
        tiles[progress_key].append((asset_path, center_x, center_y, width, height))
        tile_details[progress_key].append({
            "name": config.m_Name,
            "tier": int(config.regionTier),
            "sort": int(config.sortOrder),
            "left": center_x - width / 2,
            "bottom": center_y - height / 2,
            "right": center_x + width / 2,
            "top": center_y + height / 2,
        })
        item_bounds = [
            center_x - width / 2,
            center_y - height / 2,
            center_x + width / 2,
            center_y + height / 2,
        ]
        if progress_key not in bounds:
            bounds[progress_key] = item_bounds
        else:
            current = bounds[progress_key]
            current[0] = min(current[0], item_bounds[0])
            current[1] = min(current[1], item_bounds[1])
            current[2] = max(current[2], item_bounds[2])
            current[3] = max(current[3], item_bounds[3])
        tile_counts[progress_key] += 1

    readers = {
        (asset.name.upper(), reader.path_id): reader
        for asset in environment.assets
        for reader in asset.objects.values()
    }
    result = {}
    for map_id, web_map in map_by_id.items():
        progress_key = web_map["collectibleProgressKey"]
        left, bottom, right, top = bounds[progress_key]
        width = right - left
        height = top - bottom
        center_x = (left + right) / 2
        center_y = (bottom + top) / 2
        native_centers_x = []
        native_centers_y = []
        native_samples = []
        residuals = []
        for native in web_map.get("native", []):
            prefix, cab, path_id = native["id"].split(":")
            reader = readers.get((cab.upper(), int(path_id)))
            if reader is None:
                continue
            tree = reader.read_typetree()
            position = tree["iconLocalPositionInMapUI"]
            x_pct = float(native["xPct"])
            y_pct = float(native["yPct"])
            native_centers_x.append(position["x"] - (x_pct / 100 - 0.5) * web_map["width"])
            native_centers_y.append(position["y"] - (0.5 - y_pct / 100) * web_map["height"])
            native_samples.append({
                "name": native.get("name", native["id"]),
                "position": [round(position["x"], 4), round(position["y"], 4)],
                "center": [round(native_centers_x[-1], 4), round(native_centers_y[-1], 4)],
            })
            predicted_x = 50 + (position["x"] - center_x) / width * 100
            predicted_y = 50 - (position["y"] - center_y) / height * 100
            residuals.append(max(abs(predicted_x - x_pct), abs(predicted_y - y_pct)))
        native_center = None
        if native_centers_x:
            native_center = [statistics.median(native_centers_x), statistics.median(native_centers_y)]
        alpha_crop = None
        if int(round(width)) != web_map["width"] or int(round(height)) != web_map["height"]:
            alpha_bounds = [float("inf"), float("inf"), float("-inf"), float("-inf")]
            for asset_path, tile_x, tile_y, tile_width, tile_height in tiles[progress_key]:
                image = sprite_readers[asset_path].read().image.convert("RGBA")
                box = image.getchannel("A").getbbox()
                if box is None:
                    continue
                image_left, image_top, image_right, image_bottom = box
                alpha_bounds[0] = min(alpha_bounds[0], tile_x - tile_width / 2 + image_left)
                alpha_bounds[1] = min(alpha_bounds[1], tile_y + tile_height / 2 - image_bottom)
                alpha_bounds[2] = max(alpha_bounds[2], tile_x - tile_width / 2 + image_right)
                alpha_bounds[3] = max(alpha_bounds[3], tile_y + tile_height / 2 - image_top)
            alpha_crop = {
                "width": int(round(alpha_bounds[2] - alpha_bounds[0])),
                "height": int(round(alpha_bounds[3] - alpha_bounds[1])),
                "centerX": round((alpha_bounds[0] + alpha_bounds[2]) / 2, 4),
                "centerY": round((alpha_bounds[1] + alpha_bounds[3]) / 2, 4),
            }
        result[map_id] = {
            "width": int(round(width)),
            "height": int(round(height)),
            "centerX": round(center_x, 4),
            "centerY": round(center_y, 4),
            "tiles": tile_counts[progress_key],
            "webWidth": web_map["width"],
            "webHeight": web_map["height"],
            "nativeCenter": [round(value, 4) for value in native_center] if native_center else None,
            "nativeSamples": native_samples,
            "maxNativeResidualPct": round(max(residuals), 6) if residuals else None,
            "alphaCrop": alpha_crop,
        }
        if map_id in args.debug_map:
            result[map_id]["debugTiles"] = tile_details[progress_key]
        if args.debug_dir and map_id in args.debug_map:
            args.debug_dir.mkdir(parents=True, exist_ok=True)
            canvas = Image.new("RGBA", (int(round(width)), int(round(height))))
            for asset_path, tile_x, tile_y, tile_width, tile_height in tiles[progress_key]:
                image = sprite_readers[asset_path].read().image.convert("RGBA")
                paste_x = int(round(tile_x - tile_width / 2 - left))
                paste_y = int(round(top - (tile_y + tile_height / 2)))
                canvas.alpha_composite(image, (paste_x, paste_y))
            canvas.save(args.debug_dir / f"{map_id}-composite.png")

    payload = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(payload, encoding="utf-8")
    else:
        print(payload, end="")


if __name__ == "__main__":
    main()
