using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lunarium;
using Lunarium.SaveSystem;
using Lunarium.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace LunariumItemCollectionMod;

internal sealed class MarkerOverlay
{
    private const float MissingMarkerSize = 104f;

    private sealed class MarkerRecord
    {
        public GameObject Object = null!;
        public Image Disc = null!;
        public TextMeshProUGUI Number = null!;
        public TextMeshProUGUI TooltipText = null!;
    }

    private readonly MapDatabase _database;
    private readonly Dictionary<string, Color> _categoryColors;
    private readonly Dictionary<string, MarkerRecord> _markers = new Dictionary<string, MarkerRecord>();
    private Sprite? _markerSprite;
    private TextMeshProUGUI? _banner;
    private MapUI? _bannerMapUi;
    private bool _visible;

    public MarkerOverlay(MapDatabase database)
    {
        _database = database;
        _categoryColors = database.Categories.ToDictionary(
            category => category.Id,
            category => ParseColor(category.Color),
            StringComparer.Ordinal);
    }

    public void Refresh(Data save)
    {
        Dictionary<string, LevelRegion> runtimeRegions = FindRuntimeRegions();
        var desiredKeys = new HashSet<string>(StringComparer.Ordinal);
        var coordinateSlots = new Dictionary<string, int>(StringComparer.Ordinal);
        MapDefinition? currentMap = null;
        var currentMapMissingItems = new List<CollectibleDefinition>();
        int missing = 0;

        foreach (WorldDefinition world in _database.Worlds)
        {
            foreach (MapDefinition map in world.Maps)
            {
                bool mapIsAvailable = runtimeRegions.TryGetValue(map.CollectibleProgressKey, out LevelRegion levelRegion);
                if (!mapIsAvailable && !string.IsNullOrEmpty(map.BossProgressKey))
                {
                    mapIsAvailable = runtimeRegions.TryGetValue(map.BossProgressKey, out levelRegion);
                }

                bool isCurrentMap = mapIsAvailable
                    && levelRegion != null
                    && levelRegion.gameObject.activeInHierarchy;
                if (isCurrentMap)
                {
                    currentMap = map;
                    currentMapMissingItems.Clear();
                }

                foreach (CollectibleDefinition item in map.Items)
                {
                    AnalysisResult analysis = ProgressAnalyzer.Analyze(item, save);
                    if (analysis.Status == CollectionStatus.Missing)
                    {
                        missing++;
                        if (isCurrentMap)
                        {
                            currentMapMissingItems.Add(item);
                        }
                    }

                    if (analysis.Status != CollectionStatus.Missing || !mapIsAvailable || levelRegion == null)
                    {
                        continue;
                    }

                    string key = map.Id + "::" + item.Id;
                    desiredKeys.Add(key);
                    string coordinateKey = map.Id + ":" + Math.Round(item.DisplayX, 1) + ":" + Math.Round(item.DisplayY, 1);
                    coordinateSlots.TryGetValue(coordinateKey, out int slot);
                    coordinateSlots[coordinateKey] = slot + 1;

                    Vector2 localPosition = PercentageToLocalPosition(map, item) + SlotOffset(slot);
                    SyncMarker(key, levelRegion.transform, item, analysis, localPosition);
                }
            }
        }

        RemoveMarkersNotIn(desiredKeys);
        RefreshBanner(missing, currentMap, currentMapMissingItems);
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        foreach (MarkerRecord marker in _markers.Values)
        {
            if (marker.Object != null)
            {
                marker.Object.SetActive(visible);
            }
        }

        if (_banner != null)
        {
            _banner.gameObject.SetActive(visible && IsMapVisible());
        }
    }

    public void Clear()
    {
        foreach (MarkerRecord marker in _markers.Values)
        {
            if (marker.Object != null)
            {
                UObject.Destroy(marker.Object);
            }
        }

        _markers.Clear();
        if (_banner != null)
        {
            UObject.Destroy(_banner.gameObject);
            _banner = null;
        }

        _bannerMapUi = null;
    }

    private static Dictionary<string, LevelRegion> FindRuntimeRegions()
    {
        var result = new Dictionary<string, LevelRegion>(StringComparer.Ordinal);
        foreach (LevelRegion levelRegion in Resources.FindObjectsOfTypeAll<LevelRegion>())
        {
            if (levelRegion == null
                || levelRegion.LevelRegionConfig == null
                || !levelRegion.gameObject.scene.IsValid())
            {
                continue;
            }

            RegionExplorationConfig exploration = levelRegion.LevelRegionConfig.RegionExplorationConfig;
            if (exploration == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(exploration.CollectibleProgressSaveKey))
            {
                result[exploration.CollectibleProgressSaveKey] = levelRegion;
            }

            if (!string.IsNullOrEmpty(exploration.BossProgressSaveKey))
            {
                result[exploration.BossProgressSaveKey] = levelRegion;
            }
        }

        return result;
    }

    private void SyncMarker(
        string key,
        Transform parent,
        CollectibleDefinition item,
        AnalysisResult analysis,
        Vector2 localPosition)
    {
        if (!_markers.TryGetValue(key, out MarkerRecord marker) || marker.Object == null)
        {
            marker = CreateMarker(parent, item, analysis);
            _markers[key] = marker;
        }
        else if (marker.Object.transform.parent != parent)
        {
            marker.Object.transform.SetParent(parent, false);
        }

        RectTransform rect = (RectTransform)marker.Object.transform;
        rect.localPosition = localPosition;
        marker.Object.transform.SetAsLastSibling();
        ApplyAppearance(marker, item, analysis);
    }

    private MarkerRecord CreateMarker(Transform parent, CollectibleDefinition item, AnalysisResult analysis)
    {
        var markerObject = new GameObject(
            "LunariumCollectionMarker_" + item.GuideNumber,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(MarkerHover));
        markerObject.transform.SetParent(parent, false);
        markerObject.transform.SetAsLastSibling();

        var markerRect = (RectTransform)markerObject.transform;
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.sizeDelta = new Vector2(MissingMarkerSize, MissingMarkerSize);

        Image disc = markerObject.GetComponent<Image>();
        disc.sprite = GetMarkerSprite();
        disc.raycastTarget = true;

        TextMeshProUGUI number = CreateText("Number", markerObject.transform);
        RectTransform numberRect = number.rectTransform;
        numberRect.anchorMin = Vector2.zero;
        numberRect.anchorMax = Vector2.one;
        numberRect.offsetMin = Vector2.zero;
        numberRect.offsetMax = Vector2.zero;
        number.alignment = TextAlignmentOptions.Center;
        number.fontSize = 40f;
        number.fontStyle = FontStyles.Bold;
        number.color = Color.white;

        GameObject tooltip = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        tooltip.transform.SetParent(markerObject.transform, false);
        var tooltipRect = (RectTransform)tooltip.transform;
        tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
        tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tooltipRect.pivot = new Vector2(0.5f, 0f);
        tooltipRect.anchoredPosition = new Vector2(0f, 64f);
        tooltipRect.sizeDelta = new Vector2(620f, 96f);
        Image tooltipBackground = tooltip.GetComponent<Image>();
        tooltipBackground.color = new Color(0.055f, 0.045f, 0.075f, 0.94f);
        tooltipBackground.raycastTarget = false;

        TextMeshProUGUI tooltipText = CreateText("Label", tooltip.transform);
        RectTransform tooltipTextRect = tooltipText.rectTransform;
        tooltipTextRect.anchorMin = Vector2.zero;
        tooltipTextRect.anchorMax = Vector2.one;
        tooltipTextRect.offsetMin = new Vector2(16f, 7f);
        tooltipTextRect.offsetMax = new Vector2(-16f, -7f);
        tooltipText.alignment = TextAlignmentOptions.Center;
        tooltipText.fontSize = 28f;
        tooltipText.color = Color.white;
        tooltipText.enableWordWrapping = false;
        tooltip.SetActive(false);
        markerObject.GetComponent<MarkerHover>().Tooltip = tooltip;

        var record = new MarkerRecord
        {
            Object = markerObject,
            Disc = disc,
            Number = number,
            TooltipText = tooltipText,
        };
        ApplyAppearance(record, item, analysis);
        return record;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        if (FontConfig.Singleton != null)
        {
            text.font = FontConfig.Singleton.DefaultFont;
        }

        text.raycastTarget = false;
        return text;
    }

    private void ApplyAppearance(MarkerRecord marker, CollectibleDefinition item, AnalysisResult analysis)
    {
        marker.Disc.color = GetCategoryColor(item.Category);
        marker.Number.text = item.GuideNumber.ToString();
        marker.TooltipText.text = $"{item.GuideNumber}. {item.Name} · 确认未收集\n{analysis.Evidence}";
        ((RectTransform)marker.Object.transform).sizeDelta = new Vector2(MissingMarkerSize, MissingMarkerSize);
    }

    private void RemoveMarkersNotIn(HashSet<string> desiredKeys)
    {
        foreach (string key in _markers.Keys.Where(key => !desiredKeys.Contains(key) || _markers[key].Object == null).ToArray())
        {
            MarkerRecord marker = _markers[key];
            if (marker.Object != null)
            {
                UObject.Destroy(marker.Object);
            }

            _markers.Remove(key);
        }
    }

    private void RefreshBanner(
        int missing,
        MapDefinition? currentMap,
        IReadOnlyCollection<CollectibleDefinition> currentMapMissingItems)
    {
        MapUI? mapUi = Resources.FindObjectsOfTypeAll<MapUI>()
            .Where(candidate => candidate != null && candidate.gameObject.scene.IsValid())
            .OrderByDescending(candidate => candidate.gameObject.activeInHierarchy)
            .FirstOrDefault();
        if (mapUi == null)
        {
            return;
        }

        Canvas? mapCanvas = mapUi.GetComponentInParent<Canvas>(true);
        Canvas? rootCanvas = mapCanvas != null ? mapCanvas.rootCanvas : null;
        Transform bannerParent = rootCanvas != null ? rootCanvas.transform : mapUi.transform.root;
        _bannerMapUi = mapUi;

        if (_banner == null || _banner.transform.parent != bannerParent)
        {
            if (_banner != null)
            {
                UObject.Destroy(_banner.gameObject);
            }

            _banner = CreateText("LunariumCollectionStatus", bannerParent);
            RectTransform rect = _banner.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -12f);
            rect.sizeDelta = new Vector2(1900f, 1000f);
            _banner.fontSize = 26f;
            _banner.alignment = TextAlignmentOptions.TopLeft;
            _banner.color = new Color(1f, 0.92f, 0.72f, 1f);
            _banner.enableWordWrapping = false;
            _banner.overflowMode = TextOverflowModes.Overflow;
            _banner.richText = true;
            var outline = _banner.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            _banner.transform.SetAsLastSibling();
        }

        if (_banner.font == null && FontConfig.Singleton != null)
        {
            _banner.font = FontConfig.Singleton.DefaultFont;
        }

        _banner.transform.SetAsLastSibling();
        _banner.text = BuildBannerText(missing, currentMap, currentMapMissingItems);
        _banner.gameObject.SetActive(_visible && IsMapVisible());
    }

    private string BuildBannerText(
        int missing,
        MapDefinition? currentMap,
        IReadOnlyCollection<CollectibleDefinition> currentMapMissingItems)
    {
        var text = new StringBuilder();
        text.Append("<size=28>收集标记已启用  ·  未收集物品数量 ")
            .Append(missing)
            .Append("</size>");

        if (currentMap == null)
        {
            text.AppendLine()
                .Append("当前地图尚未加载");
            return text.ToString();
        }

        text.AppendLine()
            .Append("<size=26>当前地图：")
            .Append(EscapeRichText(currentMap.Name))
            .Append("  ·  未收集 ")
            .Append(currentMapMissingItems.Count)
            .Append("</size>");

        if (currentMapMissingItems.Count == 0)
        {
            text.AppendLine()
                .Append("<color=#73D69A>此地图道具已全部收集</color>");
            return text.ToString();
        }

        foreach (CollectibleDefinition item in currentMapMissingItems
            .OrderBy(candidate => candidate.GuideNumber)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            string color = ColorUtility.ToHtmlStringRGB(GetCategoryColor(item.Category));
            text.AppendLine()
                .Append("<color=#")
                .Append(color)
                .Append('>')
                .Append(item.GuideNumber)
                .Append(". ")
                .Append(EscapeRichText(item.Name))
                .Append("</color>");
        }

        return text.ToString();
    }

    private static string EscapeRichText(string value)
    {
        return value.Replace("<", "＜").Replace(">", "＞");
    }

    private bool IsMapVisible()
    {
        return _bannerMapUi != null && _bannerMapUi.gameObject.activeInHierarchy;
    }

    private Sprite GetMarkerSprite()
    {
        if (_markerSprite != null)
        {
            return _markerSprite;
        }

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "LunariumCollectionMarkerTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float diamondDistance = Math.Abs(x - (size - 1) / 2f) + Math.Abs(y - (size - 1) / 2f);
                byte alpha = diamondDistance <= 29.5f ? (byte)255 : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        UObject.DontDestroyOnLoad(texture);
        _markerSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        _markerSprite.name = "LunariumCollectionMarkerSprite";
        UObject.DontDestroyOnLoad(_markerSprite);
        return _markerSprite;
    }

    private Color GetCategoryColor(string category)
    {
        return _categoryColors.TryGetValue(category, out Color color)
            ? color
            : new Color(0.87f, 0.33f, 0.29f, 1f);
    }

    private static Color ParseColor(string html)
    {
        return ColorUtility.TryParseHtmlString(html, out Color color)
            ? color
            : new Color(0.87f, 0.33f, 0.29f, 1f);
    }

    private static Vector2 PercentageToLocalPosition(
        MapDefinition map,
        CollectibleDefinition item)
    {
        float x = map.CenterX + (item.DisplayX / 100f - 0.5f) * map.Width;
        float y = map.CenterY + (0.5f - item.DisplayY / 100f) * map.Height;
        return new Vector2(x, y);
    }

    private static Vector2 SlotOffset(int slot)
    {
        switch (slot % 9)
        {
            case 1: return new Vector2(68f, 0f);
            case 2: return new Vector2(-68f, 0f);
            case 3: return new Vector2(0f, 68f);
            case 4: return new Vector2(0f, -68f);
            case 5: return new Vector2(52f, 52f);
            case 6: return new Vector2(-52f, 52f);
            case 7: return new Vector2(52f, -52f);
            case 8: return new Vector2(-52f, -52f);
            default: return Vector2.zero;
        }
    }
}
