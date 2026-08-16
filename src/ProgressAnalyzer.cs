using System;
using System.Collections.Generic;
using Lunarium;
using Lunarium.SaveSystem;

namespace LunariumItemCollectionMod;

internal static class ProgressAnalyzer
{
    public static AnalysisResult Analyze(CollectibleDefinition item, Data save)
    {
        AnalysisResult? completionResult = AnalyzeCompletionSignals(item, save, out string completionMissingEvidence);
        if (completionResult.HasValue)
        {
            return completionResult.Value;
        }

        if (save.sceneItems.TryGetValue(item.Id, out var sceneState))
        {
            return sceneState.active
                ? new AnalysisResult(CollectionStatus.Missing, "场景物品仍处于可拾取状态")
                : new AnalysisResult(CollectionStatus.Collected, "场景物品已被移除");
        }

        Dictionary<string, int> inventory = save.Ave?.inventory?.items ?? new Dictionary<string, int>();
        if (item.CompletionSignals.Count > 0)
        {
            if (HasCollectedSuccessor(item, save, inventory, out string completionSuccessorEvidence))
            {
                return new AnalysisResult(CollectionStatus.Collected, completionSuccessorEvidence);
            }

            return new AnalysisResult(CollectionStatus.Missing, completionMissingEvidence);
        }

        if (string.IsNullOrWhiteSpace(item.TypeId))
        {
            return new AnalysisResult(CollectionStatus.Missing, "存档中没有检测到该点的完成信号");
        }

        if (inventory.TryGetValue(item.TypeId, out int quantity) && quantity > 0)
        {
            return new AnalysisResult(CollectionStatus.Collected, $"背包持有 {item.TypeId} ×{quantity}");
        }

        if (HasCollectedSuccessor(item, save, inventory, out string evidence))
        {
            return new AnalysisResult(CollectionStatus.Collected, evidence);
        }

        return new AnalysisResult(CollectionStatus.Missing, "未检测到原物品或收集后的永久状态");
    }

    private static AnalysisResult? AnalyzeCompletionSignals(
        CollectibleDefinition item,
        Data save,
        out string missingEvidence)
    {
        missingEvidence = "任务、商店或战斗奖励尚未完成";
        if (item.CompletionSignals.Count == 0)
        {
            return null;
        }

        foreach (CompletionSignal signal in item.CompletionSignals)
        {
            string label = string.IsNullOrWhiteSpace(signal.Label) ? signal.Id : signal.Label;
            switch (signal.Kind)
            {
                case "inventory":
                    Dictionary<string, int> inventory = save.Ave?.inventory?.items ?? new Dictionary<string, int>();
                    if (inventory.TryGetValue(signal.Id, out int quantity) && quantity > 0)
                    {
                        return new AnalysisResult(CollectionStatus.Collected, $"{label}（×{quantity}）");
                    }

                    missingEvidence = $"未检测到：{label}";
                    break;

                case "scene-item":
                    if (save.sceneItems.TryGetValue(signal.Id, out var sceneItem) && !sceneItem.active)
                    {
                        return new AnalysisResult(CollectionStatus.Collected, label);
                    }

                    missingEvidence = $"尚未完成：{label}";
                    break;

                case "progress-bool":
                    if (save.boolProgresses.TryGetValue(signal.Id, out bool progress) && progress)
                    {
                        return new AnalysisResult(CollectionStatus.Collected, label);
                    }

                    missingEvidence = $"尚未完成：{label}";
                    break;

                case "savable-state":
                    if (save.savableStates.TryGetValue(signal.Id, out string state)
                        && string.Equals(state, signal.State, StringComparison.Ordinal))
                    {
                        return new AnalysisResult(CollectionStatus.Collected, label);
                    }

                    missingEvidence = $"尚未完成：{label}";
                    break;

                case "store":
                    if (save.storeItems.TryGetValue(signal.Id, out Dictionary<string, int> merchant)
                        && merchant.TryGetValue(signal.GoodsId, out int remaining))
                    {
                        if (remaining <= 0)
                        {
                            return new AnalysisResult(CollectionStatus.Collected, $"商店商品 {signal.GoodsId} 已售罄");
                        }

                        missingEvidence = $"商店商品 {signal.GoodsId} 剩余 {remaining}";
                    }
                    else
                    {
                        missingEvidence = $"商店 {signal.Id} 尚无购买记录";
                    }

                    break;

                case "dialogue":
                    if (save.playedDialogues.Contains(signal.Id))
                    {
                        return new AnalysisResult(CollectionStatus.Collected, $"奖励对话已完成：{signal.Id}");
                    }

                    missingEvidence = $"奖励对话尚未完成：{signal.Id}";
                    break;

                case "dialogue-flag":
                    if (save.dialogueLocalFlag.TryGetValue(signal.Id, out HashSet<string> flags)
                        && flags.Contains(signal.Flag))
                    {
                        return new AnalysisResult(CollectionStatus.Collected, $"任务话题已完成：{signal.Flag}");
                    }

                    missingEvidence = $"任务话题尚未完成：{signal.Flag}";
                    break;
            }
        }

        return null;
    }

    private static bool HasCollectedSuccessor(
        CollectibleDefinition item,
        Data save,
        Dictionary<string, int> inventory,
        out string evidence)
    {
        foreach (SuccessorSignal successor in item.Successors)
        {
            switch (successor.Kind)
            {
                case "inventory":
                    if (inventory.TryGetValue(successor.Id, out int quantity) && quantity > 0)
                    {
                        evidence = $"检测到后继物品 {successor.Id} ×{quantity}";
                        return true;
                    }

                    break;

                case "skill":
                    if (save.skillStatus.TryGetValue(successor.Id, out SkillNode.Status status)
                        && ReadSkillState(status, successor.State))
                    {
                        evidence = $"检测到后继技能状态 {successor.Id}.{successor.State}";
                        return true;
                    }

                    break;

                case "progress-int":
                    if (successor.Minimum.HasValue
                        && save.intProgresses.TryGetValue(successor.Id, out int progress)
                        && progress >= successor.Minimum.Value)
                    {
                        evidence = $"进度 {successor.Id} 已达到 {progress}/{successor.Minimum.Value}";
                        return true;
                    }

                    break;
            }
        }

        evidence = string.Empty;
        return false;
    }

    private static bool ReadSkillState(SkillNode.Status status, string state)
    {
        switch (state)
        {
            case "visible":
                return status.visible;
            case "unlocked":
                return status.unlocked;
            case "secret":
                return status.secret;
            case "playRevealAnim":
                return status.playRevealAnim;
            default:
                return false;
        }
    }
}
