import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const sourcePath = path.resolve(process.argv[2] ?? "../Lunarium_ItemCollection/public/data/map-data.json");
const outputPath = path.resolve(process.argv[3] ?? "Data/collectibles.json");
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const transformPath = path.resolve(process.argv[4] ?? path.join(scriptDirectory, "map-transforms.json"));
const overridePath = path.join(scriptDirectory, "item-overrides.json");

if (!fs.existsSync(sourcePath)) {
  throw new Error(`找不到源数据：${sourcePath}`);
}
if (!fs.existsSync(transformPath)) {
  throw new Error(`找不到地图坐标变换：${transformPath}`);
}
if (!fs.existsSync(overridePath)) {
  throw new Error(`找不到逐点判定覆盖：${overridePath}`);
}

const source = JSON.parse(fs.readFileSync(sourcePath, "utf8"));
const transforms = JSON.parse(fs.readFileSync(transformPath, "utf8"));
const overrideData = JSON.parse(fs.readFileSync(overridePath, "utf8"));
const itemOverrides = overrideData.items ?? {};
const unusedOverrideKeys = new Set(Object.keys(itemOverrides));
if (transforms.gameBuildId !== source.gameBuildId) {
  throw new Error(`地图坐标变换的游戏构建不匹配：${transforms.gameBuildId} != ${source.gameBuildId}`);
}
const seenKeys = new Set();
let itemCount = 0;

const output = {
  version: source.version,
  gameBuildId: source.gameBuildId,
  categories: source.categories.map(({ id, color }) => ({ id, color })),
  worlds: source.worlds.map((world) => ({
    name: world.name,
    maps: world.maps.map((map) => {
      if (!/^native-\d+$/.test(map.id)) throw new Error(`无效的原生地图 ID：${map.id}`);
      if (!map.collectibleProgressKey) throw new Error(`地图缺少区域收集进度键：${map.id}`);
      const transform = transforms.maps[map.id];
      if (!transform) throw new Error(`地图缺少坐标变换：${map.id}`);
      if (transform.width !== map.width || transform.height !== map.height) {
        throw new Error(`地图尺寸与坐标变换不匹配：${map.id}`);
      }
      const items = map.items
        .filter((item) => item.mapVisible !== false)
        .map((item) => {
          const sourceKey = `${map.id}::${item.id}`;
          const override = itemOverrides[sourceKey] ?? {};
          unusedOverrideKeys.delete(sourceKey);
          const effectiveItem = { ...item, ...override };
          const key = `${map.id}::${effectiveItem.id}`;
          if (seenKeys.has(key)) throw new Error(`重复收集点：${key}`);
          seenKeys.add(key);
          itemCount += 1;
          return {
            id: effectiveItem.id,
            name: effectiveItem.name,
            category: effectiveItem.category,
            typeId: effectiveItem.typeId ?? "",
            guideNumber: effectiveItem.guideNumber,
            displayX: effectiveItem.displayX,
            displayY: effectiveItem.displayY,
            completionSignals: effectiveItem.completionSignals ?? [],
            successors: effectiveItem.successors ?? [],
          };
        });
      return {
        id: map.id,
        name: map.name,
        collectibleProgressKey: map.collectibleProgressKey,
        bossProgressKey: map.bossProgressKey,
        width: transform.width,
        height: transform.height,
        centerX: transform.centerX,
        centerY: transform.centerY,
        items,
      };
    }),
  })),
};

if (unusedOverrideKeys.size > 0) {
  throw new Error(`逐点判定覆盖没有匹配源数据：${[...unusedOverrideKeys].join(", ")}`);
}

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(output)}\n`, "utf8");
console.log(`已导出${output.worlds.flatMap((world) => world.maps).length} 张地图、${itemCount} 个可见收集点。`);
console.log(outputPath);
