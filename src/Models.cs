using System.Collections.Generic;
using Newtonsoft.Json;

namespace LunariumItemCollectionMod;

internal sealed class MapDatabase
{
    [JsonProperty("version")]
    public int Version { get; set; }

    [JsonProperty("gameBuildId")]
    public string GameBuildId { get; set; } = string.Empty;

    [JsonProperty("categories")]
    public List<CategoryDefinition> Categories { get; set; } = new List<CategoryDefinition>();

    [JsonProperty("worlds")]
    public List<WorldDefinition> Worlds { get; set; } = new List<WorldDefinition>();
}

internal sealed class CategoryDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("color")]
    public string Color { get; set; } = "#df534b";
}

internal sealed class WorldDefinition
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("maps")]
    public List<MapDefinition> Maps { get; set; } = new List<MapDefinition>();
}

internal sealed class MapDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("collectibleProgressKey")]
    public string CollectibleProgressKey { get; set; } = string.Empty;

    [JsonProperty("bossProgressKey")]
    public string BossProgressKey { get; set; } = string.Empty;

    [JsonProperty("width")]
    public float Width { get; set; }

    [JsonProperty("height")]
    public float Height { get; set; }

    [JsonProperty("centerX")]
    public float CenterX { get; set; }

    [JsonProperty("centerY")]
    public float CenterY { get; set; }

    [JsonProperty("items")]
    public List<CollectibleDefinition> Items { get; set; } = new List<CollectibleDefinition>();
}

internal sealed class CollectibleDefinition
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("typeId")]
    public string TypeId { get; set; } = string.Empty;

    [JsonProperty("guideNumber")]
    public int GuideNumber { get; set; }

    [JsonProperty("displayX")]
    public float DisplayX { get; set; }

    [JsonProperty("displayY")]
    public float DisplayY { get; set; }

    [JsonProperty("completionSignals")]
    public List<CompletionSignal> CompletionSignals { get; set; } = new List<CompletionSignal>();

    [JsonProperty("successors")]
    public List<SuccessorSignal> Successors { get; set; } = new List<SuccessorSignal>();
}

internal sealed class CompletionSignal
{
    [JsonProperty("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("goods_id")]
    public string GoodsId { get; set; } = string.Empty;

    [JsonProperty("flag")]
    public string Flag { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;

    [JsonProperty("label")]
    public string Label { get; set; } = string.Empty;
}

internal sealed class SuccessorSignal
{
    [JsonProperty("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("state")]
    public string State { get; set; } = string.Empty;

    [JsonProperty("minimum")]
    public int? Minimum { get; set; }
}

internal enum CollectionStatus
{
    Collected,
    Missing,
}

internal readonly struct AnalysisResult
{
    public AnalysisResult(CollectionStatus status, string evidence)
    {
        Status = status;
        Evidence = evidence;
    }

    public CollectionStatus Status { get; }

    public string Evidence { get; }
}
