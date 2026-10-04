using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;

using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using ChocoboTrainer.Windows;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

using CSGameObject =
    FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace ChocoboTrainer;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static ITargetManager TargetManager { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/ctrain";

    private const uint TrainerBaseId = 1010465;

    public const float TrainerMaxDistance = 8.0f;

    private const string TrainerMenuAddonName =
        "SelectIconString";

    private const string ContextMenuAddonName =
        "ContextMenu";

    private const string TrainingAddonName =
        "ChocoboBreedTraining";

    private const string YesNoAddonName =
        "SelectYesno";

    private const string TalkAddonName =
        "Talk";

    private const uint StartTrainingNodeId = 10;
    private const uint StartTrainingEventParam = 1;

    private const uint YesButtonNodeId = 8;
    private const uint YesButtonEventParam = 0;

    private const long TrainerInteractionTimeoutMs = 5000;
    private const long StepTimeoutMs = 12000;

    private const long TalkGraceMs = 250;
    private const long TalkRepeatMs = 550;

    private const long FeedModeDelayMs = 700;
    private const long FeedContextReadyMs = 500;

    private const long TrainingClickDelayMs = 700;
    private const long TrainingFinishTimeoutMs = 20000;

    private const long PostTrainingQuietMs = 3000;
    private const long PostTalkQuietMs = 600;

    private const long NextTrainingDelayMs = 1200;

    private static readonly Regex StatRegex =
        new(
            @"(\d+)\s*/\s*(\d+)",
            RegexOptions.Compiled
        );

    private enum AutomationStep
    {
        Idle,
        WaitTrainerMenu,
        WaitFeedMode,
        WaitFeedContext,
        WaitTrainingWindow,
        WaitYesNo,
        WaitTrainingFinish,
        WaitNextTraining
    }

    private enum ChocoboStat
    {
        MaxSpeed = 0,
        Acceleration = 1,
        Endurance = 2,
        Stamina = 3,
        Adaptability = 4
    }

    private readonly struct StatValue
    {
        public int Current { get; }
        public int Max { get; }

        public StatValue(
            int current,
            int max)
        {
            Current = current;
            Max = max;
        }
    }

    private readonly struct StatTextEntry
    {
        public float X { get; }
        public float Y { get; }
        public StatValue Value { get; }

        public StatTextEntry(
            float x,
            float y,
            StatValue value)
        {
            X = x;
            Y = y;
            Value = value;
        }
    }

    private readonly struct TrainingPreview
    {
        public StatValue[] Before { get; }
        public StatValue[] After { get; }

        public TrainingPreview(
            StatValue[] before,
            StatValue[] after)
        {
            Before = before;
            After = after;
        }
    }

    public bool IsRunning { get; private set; }

    public string UiStatus { get; private set; }
        = "等待开始。";

    public int CompletedTrainingCount =>
        completedTrainingCount;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem =
        new("ChocoboTrainer");

    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private bool loopMode;

    private AutomationStep currentStep =
        AutomationStep.Idle;

    private long stepDeadline;
    private long nextActionAt;

    private long talkVisibleSince;
    private long nextTalkAdvanceAt;

    private long feedContextVisibleAt;

    private bool postTrainingTalkSeen;
    private bool trainingPreviewChecked;

    private bool cleanupPending;

    private int completedTrainingCount;

    public Plugin()
    {
        Configuration =
            PluginInterface.GetPluginConfig()
                as Configuration
            ?? new Configuration();

        ConfigWindow =
            new ConfigWindow(this);

        MainWindow =
            new MainWindow(this);

        WindowSystem.AddWindow(
            ConfigWindow
        );

        WindowSystem.AddWindow(
            MainWindow
        );

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage =
                    "→ 开始循环训练\n" +
                    "/ctrain once → 单次训练\n" +
                    "/ctrain stop → 结束训练\n" +
                    "/ctrain config → 打开设置\n" +
                    "/ctrain status → 打开主窗口"
            }
        );

        PluginInterface.UiBuilder.Draw +=
            WindowSystem.Draw;

        PluginInterface.UiBuilder.OpenConfigUi +=
            ToggleConfigUi;

        PluginInterface.UiBuilder.OpenMainUi +=
            ToggleMainUi;

        Framework.Update +=
            OnFrameworkUpdate;
    }

    public void Dispose()
    {
        IsRunning = false;
        loopMode = false;

        Framework.Update -=
            OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw -=
            WindowSystem.Draw;

        PluginInterface.UiBuilder.OpenConfigUi -=
            ToggleConfigUi;

        PluginInterface.UiBuilder.OpenMainUi -=
            ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(
            CommandName
        );
    }

    private void OnCommand(
        string command,
        string args)
    {
        var argument =
            args.Trim().ToLowerInvariant();

        switch (argument)
        {
            case "":
            case "start":

                StartTraining(true);

                break;

            case "once":

                StartTraining(false);

                break;

            case "stop":

                StopFromUi();

                break;

            case "config":
            case "settings":

                ToggleConfigUi();

                break;

            case "status":

                ToggleMainUi();

                break;

            default:

                UiStatus =
                    "未知命令。";

                break;
        }
    }

    public void StartFromUi()
    {
        StartTraining(true);
    }

    public void StopFromUi()
    {
        if (!IsRunning)
        {
            UiStatus =
                "当前没有运行。";

            cleanupPending = true;

            return;
        }

        StopInternal();

        UiStatus =
            "已手动结束训练。";
    }

    private void StartTraining(
        bool loop)
    {
        if (IsRunning)
        {
            UiStatus =
                "当前已经在运行。";

            return;
        }

        cleanupPending = false;

        if (
            !TryFindTrainer(
                out var trainer,
                out var distance,
                false
            )
            ||
            trainer == null
        )
        {
            UiStatus =
                "附近未检测到驯鸟师，未开始。";

            return;
        }

        if (distance > TrainerMaxDistance)
        {
            UiStatus =
                $"距离过远：{distance:F1} yalms，请靠近驯鸟师。";

            return;
        }

        if (!trainer.IsTargetable)
        {
            UiStatus =
                "驯鸟师当前不可交互，未开始。";

            return;
        }

        IsRunning = true;
        loopMode = loop;

        completedTrainingCount = 0;

        ResetRoundState();

        if (
            !BeginTrainerInteraction(
                trainer,
                distance
            )
        )
        {
            return;
        }

        UiStatus =
            loop
                ? "循环训练已开始，正在与驯鸟师交互。"
                : "单次训练已开始，正在与驯鸟师交互。";
    }

    private void ResetRoundState()
    {
        currentStep =
            AutomationStep.Idle;

        stepDeadline = 0;
        nextActionAt = 0;

        talkVisibleSince = 0;
        nextTalkAdvanceAt = 0;

        feedContextVisibleAt = 0;

        postTrainingTalkSeen = false;
        trainingPreviewChecked = false;
    }

    private void StopInternal()
    {
        IsRunning = false;
        loopMode = false;

        currentStep =
            AutomationStep.Idle;

        stepDeadline = 0;
        nextActionAt = 0;

        talkVisibleSince = 0;
        nextTalkAdvanceAt = 0;

        feedContextVisibleAt = 0;

        postTrainingTalkSeen = false;
        trainingPreviewChecked = false;

        cleanupPending = true;
    }

    private void StopWithStatus(
        string status)
    {
        StopInternal();

        UiStatus =
            status;
    }

    private void SetStepTimeout(
        long timeout = StepTimeoutMs)
    {
        stepDeadline =
            Environment.TickCount64
            + timeout;
    }

    private bool IsStepTimedOut()
    {
        return
            stepDeadline > 0
            &&
            Environment.TickCount64
            > stepDeadline;
    }

    public string GetTrainerProximityText()
    {
        if (
            !TryFindTrainer(
                out var trainer,
                out var distance,
                false
            )
            ||
            trainer == null
        )
        {
            return
                "附近未检测到驯鸟师。";
        }

        if (distance > TrainerMaxDistance)
        {
            return
                $"距离过远：{distance:F1} yalms。请靠近驯鸟师后再开始。";
        }

        if (!trainer.IsTargetable)
        {
            return
                $"驯鸟师距离：{distance:F1} yalms，但当前不可交互。";
        }

        return
            $"驯鸟师距离：{distance:F1} yalms，可以开始。";
    }

    private bool TryFindTrainer(
        out IGameObject? trainer,
        out float distance,
        bool requireTargetable)
    {
        trainer = null;
        distance = float.MaxValue;

        var player =
            ObjectTable.LocalPlayer;

        if (player == null)
        {
            return false;
        }

        foreach (var obj in ObjectTable)
        {
            if (obj.BaseId != TrainerBaseId)
            {
                continue;
            }

            if (
                requireTargetable
                &&
                !obj.IsTargetable
            )
            {
                continue;
            }

            var currentDistance =
                Vector3.Distance(
                    player.Position,
                    obj.Position
                );

            if (
                currentDistance
                >= distance
            )
            {
                continue;
            }

            trainer = obj;
            distance = currentDistance;
        }

        return trainer != null;
    }

    private unsafe bool BeginTrainerInteraction(
        IGameObject trainer,
        float distance)
    {
        if (distance > TrainerMaxDistance)
        {
            StopWithStatus(
                $"距离过远：{distance:F1} yalms，训练已结束。"
            );

            return false;
        }

        if (!trainer.IsTargetable)
        {
            StopWithStatus(
                "无法选中驯鸟师，训练已结束。"
            );

            return false;
        }

        TargetManager.Target =
            trainer;

        var selectedTarget =
            TargetManager.Target;

        if (
            selectedTarget == null
            ||
            selectedTarget.BaseId
                != TrainerBaseId
        )
        {
            StopWithStatus(
                "无法选中驯鸟师，训练已结束。"
            );

            return false;
        }

        var targetSystem =
            TargetSystem.Instance();

        if (targetSystem == null)
        {
            StopWithStatus(
                "无法调用游戏交互系统，训练已结束。"
            );

            return false;
        }

        var nativeObject =
            (CSGameObject*)trainer.Address;

        if (nativeObject == null)
        {
            StopWithStatus(
                "驯鸟师对象无效，训练已结束。"
            );

            return false;
        }

        try
        {
            targetSystem
                ->InteractWithObject(
                    nativeObject,
                    true
                );
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "InteractWithObject failed."
            );

            StopWithStatus(
                "无法与驯鸟师交互，训练已结束。"
            );

            return false;
        }

        currentStep =
            AutomationStep.WaitTrainerMenu;

        SetStepTimeout(
            TrainerInteractionTimeoutMs
        );

        UiStatus =
            "已选中驯鸟师，等待菜单或对话。";

        return true;
    }

    private void OnFrameworkUpdate(
        IFramework framework)
    {
        if (cleanupPending)
        {
            cleanupPending = false;

            ReturnToNeutralState();
        }

        if (!IsRunning)
        {
            return;
        }

        switch (currentStep)
        {
            case AutomationStep.WaitTrainerMenu:

                UpdateTrainerMenu();

                break;

            case AutomationStep.WaitFeedMode:

                UpdateFeedMode();

                break;

            case AutomationStep.WaitFeedContext:

                UpdateFeedContext();

                break;

            case AutomationStep.WaitTrainingWindow:

                UpdateTrainingWindow();

                break;

            case AutomationStep.WaitYesNo:

                UpdateYesNo();

                break;

            case AutomationStep.WaitTrainingFinish:

                UpdateTrainingFinish();

                break;

            case AutomationStep.WaitNextTraining:

                UpdateNextTraining();

                break;
        }
    }

    private unsafe void ReturnToNeutralState()
    {
        TryCloseAddon(
            ContextMenuAddonName
        );

        TryCloseAddon(
            TrainingAddonName
        );

        TryCloseAddon(
            YesNoAddonName
        );

        TryCloseAddon(
            TrainerMenuAddonName
        );

        TryCloseAddon(
            TalkAddonName
        );

        TryCloseAddon(
            "InventoryExpansion"
        );

        TryCloseAddon(
            "InventoryLarge"
        );

        TryCloseAddon(
            "Inventory"
        );

        TargetManager.Target =
            null;
    }

    private unsafe void TryCloseAddon(
        string addonName)
    {
        try
        {
            var addon =
                GameGui.GetAddonByName(
                    addonName,
                    1
                );

            if (
                addon.IsNull
                ||
                !addon.IsReady
                ||
                !addon.IsVisible
            )
            {
                return;
            }

            var addonPtr =
                (AtkUnitBase*)addon.Address;

            if (addonPtr == null)
            {
                return;
            }

            addonPtr->Close(true);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                $"Failed to close addon: {addonName}"
            );
        }
    }

    private unsafe void UpdateTrainerMenu()
    {
        if (HandleTalk())
        {
            SetStepTimeout(
                TrainerInteractionTimeoutMs
            );

            return;
        }

        var trainerMenu =
            GameGui.GetAddonByName(
                TrainerMenuAddonName,
                1
            );

        if (
            !trainerMenu.IsNull
            &&
            trainerMenu.IsReady
            &&
            trainerMenu.IsVisible
        )
        {
            var addon =
                (AtkUnitBase*)trainerMenu.Address;

            if (addon == null)
            {
                return;
            }

            addon->FireCallbackInt(0);

            UiStatus =
                "已选择“训练竞赛陆行鸟”，等待选择饲料。";

            currentStep =
                AutomationStep.WaitFeedMode;

            nextActionAt =
                Environment.TickCount64
                + FeedModeDelayMs;

            SetStepTimeout();

            return;
        }

        if (IsStepTimedOut())
        {
            StopWithStatus(
                "已选中驯鸟师，但无法触发菜单或对话，训练已结束。"
            );
        }
    }

    private void UpdateFeedMode()
    {
        if (HandleTalk())
        {
            nextActionAt =
                Environment.TickCount64
                + FeedModeDelayMs;

            SetStepTimeout();

            return;
        }

        if (IsStepTimedOut())
        {
            StopWithStatus(
                "等待饲料选择状态超时，训练已结束。"
            );

            return;
        }

        if (
            Environment.TickCount64
            < nextActionAt
        )
        {
            return;
        }

        if (!TryOpenFeedContextMenu())
        {
            StopWithStatus(
                $"找不到 {GetSelectedFeedName()} 或无法打开物品菜单，训练已结束。"
            );

            return;
        }

        feedContextVisibleAt = 0;

        currentStep =
            AutomationStep.WaitFeedContext;

        UiStatus =
            $"正在选择 {GetSelectedFeedName()}。";

        SetStepTimeout();
    }

    private unsafe void UpdateFeedContext()
    {
        if (HandleTalk())
        {
            return;
        }

        if (IsStepTimedOut())
        {
            StopWithStatus(
                "没有找到“选择饲料”选项，训练已结束。"
            );

            return;
        }

        var agent =
            AgentInventoryContext.Instance();

        if (agent == null)
        {
            return;
        }

        var contextMenu =
            GameGui.GetAddonByName(
                ContextMenuAddonName,
                1
            );

        if (
            contextMenu.IsNull
            ||
            !contextMenu.IsReady
            ||
            !contextMenu.IsVisible
        )
        {
            return;
        }

        var now =
            Environment.TickCount64;

        if (feedContextVisibleAt == 0)
        {
            feedContextVisibleAt = now;

            return;
        }

        var addon =
            (AtkUnitBase*)contextMenu.Address;

        if (addon == null)
        {
            return;
        }

        var count =
            Math.Min(
                agent->ContextItemCount,
                32
            );

        for (
            var i = 0;
            i < count;
            i++
        )
        {
            if (
                agent->IsContextItemDisabled(i)
            )
            {
                continue;
            }

            var param =
                agent->EventParams[
                    agent->ContexItemStartIndex + i
                ];

            if (
                param.Type
                is not (
                    AtkValueType.String
                    or AtkValueType.ManagedString
                    or AtkValueType.ConstString
                )
            )
            {
                continue;
            }

            var text =
                param.GetValueAsString();

            if (
                !text.Contains(
                    "选择饲料",
                    StringComparison.Ordinal
                )
                &&
                !text.Contains(
                    "Select Feed",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            FireContextMenuCallback(
                addon,
                i
            );

            trainingPreviewChecked =
                false;

            feedContextVisibleAt =
                0;

            currentStep =
                AutomationStep.WaitTrainingWindow;

            UiStatus =
                $"已选择 {GetSelectedFeedName()}，等待训练窗口。";

            nextActionAt = 0;

            SetStepTimeout();

            return;
        }

        if (
            now - feedContextVisibleAt
            >= FeedContextReadyMs
        )
        {
            StopWithStatus(
                "当前无法选择训练饲料，训练已结束。"
            );
        }
    }

    private unsafe bool TryOpenFeedContextMenu()
    {
        var itemId =
            GetFeedItemId(
                Configuration.SelectedFeed
            );

        if (
            !TryFindFeedSlot(
                itemId,
                out var inventoryType,
                out var slot,
                out _
            )
        )
        {
            return false;
        }

        var agent =
            AgentInventoryContext.Instance();

        if (agent == null)
        {
            return false;
        }

        agent->OpenForItemSlot(
            inventoryType,
            slot,
            0,
            0
        );

        return true;
    }

    private static unsafe void FireContextMenuCallback(
        AtkUnitBase* addon,
        int index)
    {
        var values =
            stackalloc AtkValue[5];

        values[0] =
            new AtkValue
            {
                Type = AtkValueType.Int,
                Int = 0
            };

        values[1] =
            new AtkValue
            {
                Type = AtkValueType.Int,
                Int = index
            };

        values[2] =
            new AtkValue
            {
                Type = AtkValueType.UInt,
                UInt = 0
            };

        values[3] =
            new AtkValue
            {
                Type = AtkValueType.Int,
                Int = 0
            };

        values[4] =
            new AtkValue
            {
                Type = AtkValueType.Int,
                Int = 0
            };

        addon->FireCallback(
            5,
            values
        );
    }

    private unsafe void UpdateTrainingWindow()
    {
        if (HandleTalk())
        {
            return;
        }

        var training =
            GameGui.GetAddonByName(
                TrainingAddonName,
                1
            );

        if (
            training.IsNull
            ||
            !training.IsReady
            ||
            !training.IsVisible
        )
        {
            if (IsStepTimedOut())
            {
                StopWithStatus(
                    "等待训练窗口超时，训练已结束。"
                );
            }

            return;
        }

        var addon =
            (AtkUnitBase*)training.Address;

        if (addon == null)
        {
            return;
        }

        if (!trainingPreviewChecked)
        {
            if (
                !TryReadTrainingPreview(
                    addon,
                    out var preview
                )
            )
            {
                if (IsStepTimedOut())
                {
                    StopWithStatus(
                        "无法读取训练属性预览，训练已结束。"
                    );
                }

                return;
            }

            if (
                !CheckTrainingSafety(
                    preview,
                    out var reason
                )
            )
            {
                StopWithStatus(
                    reason
                );

                return;
            }

            trainingPreviewChecked =
                true;

            nextActionAt =
                Environment.TickCount64
                + TrainingClickDelayMs;

            UiStatus =
                "属性检查通过，准备开始训练。";

            return;
        }

        if (
            Environment.TickCount64
            < nextActionAt
        )
        {
            return;
        }

        if (!TryStartTrainingClick())
        {
            return;
        }

        currentStep =
            AutomationStep.WaitYesNo;

        UiStatus =
            "已开始训练，等待最终确认。";

        nextActionAt = 0;

        SetStepTimeout();
    }

    private bool CheckTrainingSafety(
        TrainingPreview preview,
        out string reason)
    {
        reason =
            string.Empty;

        if (Configuration.PreventStatOverflow)
        {
            var stat =
                GetFeedStat(
                    Configuration.SelectedFeed
                );

            var index =
                (int)stat;

            var before =
                preview.Before[index];

            var after =
                preview.After[index];

            var rate =
                GetFeedIncreaseRate(
                    Configuration.SelectedFeed
                );

            var maximumVisibleGain =
                (int)Math.Ceiling(
                    before.Max * rate
                );

            var remaining =
                before.Max
                - before.Current;

            if (
                after.Current >= before.Max
                &&
                remaining <= maximumVisibleGain
            )
            {
                reason =
                    $"已取消本次训练：{GetStatName(stat)} " +
                    $"{before.Current}/{before.Max} → " +
                    $"{after.Current}/{after.Max}，可能发生属性溢出。";

                return false;
            }
        }

        for (
            var i = 0;
            i < 5;
            i++
        )
        {
            var stat =
                (ChocoboStat)i;

            if (
                !TryGetConfiguredLimit(
                    stat,
                    out var limit
                )
            )
            {
                continue;
            }

            var before =
                preview.Before[i];

            var after =
                preview.After[i];

            if (
                after.Current < limit
            )
            {
                continue;
            }

            reason =
                $"已取消本次训练：{GetStatName(stat)} " +
                $"{before.Current}/{before.Max} → " +
                $"{after.Current}/{after.Max}，" +
                $"达到停止值 {limit}。";

            return false;
        }

        return true;
    }

    private bool TryGetConfiguredLimit(
        ChocoboStat stat,
        out int limit)
    {
        limit = 0;

        switch (stat)
        {
            case ChocoboStat.MaxSpeed:

                if (
                    !Configuration.MaxSpeedLimitEnabled
                )
                {
                    return false;
                }

                limit =
                    Configuration.MaxSpeedLimit;

                return true;

            case ChocoboStat.Acceleration:

                if (
                    !Configuration.AccelerationLimitEnabled
                )
                {
                    return false;
                }

                limit =
                    Configuration.AccelerationLimit;

                return true;

            case ChocoboStat.Endurance:

                if (
                    !Configuration.EnduranceLimitEnabled
                )
                {
                    return false;
                }

                limit =
                    Configuration.EnduranceLimit;

                return true;

            case ChocoboStat.Stamina:

                if (
                    !Configuration.StaminaLimitEnabled
                )
                {
                    return false;
                }

                limit =
                    Configuration.StaminaLimit;

                return true;

            case ChocoboStat.Adaptability:

                if (
                    !Configuration.AdaptabilityLimitEnabled
                )
                {
                    return false;
                }

                limit =
                    Configuration.AdaptabilityLimit;

                return true;

            default:

                return false;
        }
    }

    private unsafe bool TryReadTrainingPreview(
        AtkUnitBase* addon,
        out TrainingPreview preview)
    {
        preview =
            default;

        var entries =
            new List<StatTextEntry>();

        var seen =
            new HashSet<nint>();

        CollectStatTextNodes(
            &addon->UldManager,
            0,
            0,
            entries,
            seen,
            0
        );

        if (
            entries.Count < 10
        )
        {
            return false;
        }

        entries.Sort(
            (a, b) =>
            {
                var y =
                    a.Y.CompareTo(
                        b.Y
                    );

                if (y != 0)
                {
                    return y;
                }

                return a.X.CompareTo(
                    b.X
                );
            }
        );

        var rows =
            new List<List<StatTextEntry>>();

        foreach (var entry in entries)
        {
            List<StatTextEntry>? rowFound =
                null;

            foreach (var row in rows)
            {
                if (
                    Math.Abs(
                        row[0].Y
                        - entry.Y
                    )
                    <= 8.0f
                )
                {
                    rowFound =
                        row;

                    break;
                }
            }

            if (rowFound == null)
            {
                rowFound =
                    new List<StatTextEntry>();

                rows.Add(
                    rowFound
                );
            }

            rowFound.Add(
                entry
            );
        }

        rows.Sort(
            (a, b) =>
                a[0].Y.CompareTo(
                    b[0].Y
                )
        );

        var before =
            new List<StatValue>();

        var after =
            new List<StatValue>();

        foreach (var row in rows)
        {
            row.Sort(
                (a, b) =>
                    a.X.CompareTo(
                        b.X
                    )
            );

            if (
                row.Count < 2
            )
            {
                continue;
            }

            var left =
                row[0];

            var right =
                row[
                    row.Count - 1
                ];

            if (
                left.Value.Max
                != right.Value.Max
            )
            {
                continue;
            }

            before.Add(
                left.Value
            );

            after.Add(
                right.Value
            );

            if (
                before.Count == 5
            )
            {
                break;
            }
        }

        if (
            before.Count != 5
            ||
            after.Count != 5
        )
        {
            return false;
        }

        preview =
            new TrainingPreview(
                before.ToArray(),
                after.ToArray()
            );

        return true;
    }

    private unsafe void CollectStatTextNodes(
        AtkUldManager* manager,
        float parentX,
        float parentY,
        List<StatTextEntry> entries,
        HashSet<nint> seen,
        int depth)
    {
        if (
            manager == null
            ||
            depth > 8
        )
        {
            return;
        }

        var count =
            Math.Min(
                (int)manager->NodeListCount,
                512
            );

        for (
            var i = 0;
            i < count;
            i++
        )
        {
            var node =
                manager->NodeList[i];

            if (
                node == null
                ||
                !node->IsVisible()
            )
            {
                continue;
            }

            var x =
                parentX
                + node->X;

            var y =
                parentY
                + node->Y;

            var textNode =
                node->GetAsAtkTextNode();

            if (
                textNode != null
                &&
                seen.Add(
                    (nint)textNode
                )
            )
            {
                var text =
                    textNode
                        ->NodeText
                        .ToString();

                if (
                    TryParseStatValue(
                        text,
                        out var value
                    )
                )
                {
                    entries.Add(
                        new StatTextEntry(
                            x,
                            y,
                            value
                        )
                    );
                }
            }

            var componentNode =
                node->GetAsAtkComponentNode();

            if (
                componentNode == null
                ||
                componentNode->Component == null
            )
            {
                continue;
            }

            CollectStatTextNodes(
                &componentNode
                    ->Component
                    ->UldManager,
                x,
                y,
                entries,
                seen,
                depth + 1
            );
        }
    }

    private static bool TryParseStatValue(
        string text,
        out StatValue value)
    {
        value =
            default;

        if (
            string.IsNullOrWhiteSpace(
                text
            )
        )
        {
            return false;
        }

        var match =
            StatRegex.Match(
                text
            );

        if (!match.Success)
        {
            return false;
        }

        if (
            !int.TryParse(
                match.Groups[1].Value,
                out var current
            )
            ||
            !int.TryParse(
                match.Groups[2].Value,
                out var max
            )
        )
        {
            return false;
        }

        if (
            max <= 0
            ||
            max > 500
            ||
            current < 0
            ||
            current > max
        )
        {
            return false;
        }

        value =
            new StatValue(
                current,
                max
            );

        return true;
    }

    private unsafe void UpdateYesNo()
    {
        if (
            IsStepTimedOut()
        )
        {
            StopWithStatus(
                "等待最终确认超时，训练已结束。"
            );

            return;
        }

        var yesNo =
            GameGui.GetAddonByName(
                YesNoAddonName,
                1
            );

        if (
            yesNo.IsNull
            ||
            !yesNo.IsReady
            ||
            !yesNo.IsVisible
        )
        {
            return;
        }

        var addon =
            (AtkUnitBase*)yesNo.Address;

        if (
            addon == null
        )
        {
            return;
        }

        var yesNode =
            addon->GetNodeById(
                YesButtonNodeId
            );

        if (
            yesNode == null
        )
        {
            return;
        }

        if (
            !TryClickButton(
                addon,
                yesNode,
                YesButtonEventParam
            )
        )
        {
            return;
        }

        currentStep =
            AutomationStep.WaitTrainingFinish;

        postTrainingTalkSeen =
            false;

        nextActionAt =
            0;

        UiStatus =
            "训练已确认，等待训练完成。";

        SetStepTimeout(
            TrainingFinishTimeoutMs
        );
    }

    private void UpdateTrainingFinish()
    {
        var now =
            Environment.TickCount64;

        if (
            HandleTalk()
        )
        {
            postTrainingTalkSeen =
                true;

            nextActionAt =
                0;

            SetStepTimeout(
                TrainingFinishTimeoutMs
            );

            return;
        }

        if (
            IsAddonVisible(
                YesNoAddonName
            )
            ||
            IsAddonVisible(
                TrainingAddonName
            )
        )
        {
            nextActionAt =
                0;

            return;
        }

        if (
            postTrainingTalkSeen
        )
        {
            if (
                nextActionAt == 0
            )
            {
                nextActionAt =
                    now
                    + PostTalkQuietMs;

                return;
            }

            if (
                now
                < nextActionAt
            )
            {
                return;
            }

            FinishOneTraining();

            return;
        }

        if (
            nextActionAt == 0
        )
        {
            nextActionAt =
                now
                + PostTrainingQuietMs;

            return;
        }

        if (
            now >= nextActionAt
        )
        {
            FinishOneTraining();

            return;
        }

        if (
            IsStepTimedOut()
        )
        {
            StopWithStatus(
                "等待训练结束超时，自动训练已结束。"
            );
        }
    }

    private void FinishOneTraining()
    {
        completedTrainingCount++;

        if (!loopMode)
        {
            StopInternal();

            UiStatus =
                $"单次训练完成。累计完成 {completedTrainingCount} 次。";

            return;
        }

        cleanupPending = true;

        currentStep =
            AutomationStep.WaitNextTraining;

        nextActionAt =
            Environment.TickCount64
            + NextTrainingDelayMs;

        UiStatus =
            $"第 {completedTrainingCount} 次训练完成，准备下一次。";

        SetStepTimeout(
            NextTrainingDelayMs
            + 10000
        );
    }

    private void UpdateNextTraining()
    {
        if (
            HandleTalk()
        )
        {
            nextActionAt =
                Environment.TickCount64
                + NextTrainingDelayMs;

            return;
        }

        if (
            Environment.TickCount64
            < nextActionAt
        )
        {
            return;
        }

        if (
            !TryFindTrainer(
                out var trainer,
                out var distance,
                false
            )
            ||
            trainer == null
        )
        {
            StopWithStatus(
                "下一轮附近未检测到驯鸟师，训练已结束。"
            );

            return;
        }

        if (
            distance > TrainerMaxDistance
        )
        {
            StopWithStatus(
                $"距离过远：{distance:F1} yalms，训练已结束。"
            );

            return;
        }

        if (
            !trainer.IsTargetable
        )
        {
            StopWithStatus(
                "下一轮无法选中驯鸟师，训练已结束。"
            );

            return;
        }

        ResetRoundState();

        BeginTrainerInteraction(
            trainer,
            distance
        );
    }

    private unsafe bool HandleTalk()
    {
        var talk =
            GameGui.GetAddonByName(
                TalkAddonName,
                1
            );

        if (
            talk.IsNull
            ||
            !talk.IsReady
            ||
            !talk.IsVisible
        )
        {
            talkVisibleSince =
                0;

            nextTalkAdvanceAt =
                0;

            return false;
        }

        var now =
            Environment.TickCount64;

        if (
            talkVisibleSince == 0
        )
        {
            talkVisibleSince =
                now;

            nextTalkAdvanceAt =
                now
                + TalkGraceMs;

            return true;
        }

        if (
            now
            < nextTalkAdvanceAt
        )
        {
            return true;
        }

        var addon =
            (AtkUnitBase*)talk.Address;

        if (
            addon == null
        )
        {
            return true;
        }

        addon->FireCallbackInt(
            0
        );

        nextTalkAdvanceAt =
            now
            + TalkRepeatMs;

        return true;
    }

    private bool IsAddonVisible(
        string addonName)
    {
        var addon =
            GameGui.GetAddonByName(
                addonName,
                1
            );

        return
            !addon.IsNull
            &&
            addon.IsReady
            &&
            addon.IsVisible;
    }

    private unsafe bool TryStartTrainingClick()
    {
        var training =
            GameGui.GetAddonByName(
                TrainingAddonName,
                1
            );

        if (
            training.IsNull
            ||
            !training.IsReady
            ||
            !training.IsVisible
        )
        {
            return false;
        }

        var addon =
            (AtkUnitBase*)training.Address;

        if (
            addon == null
        )
        {
            return false;
        }

        var buttonNode =
            addon->GetNodeById(
                StartTrainingNodeId
            );

        if (
            buttonNode == null
        )
        {
            return false;
        }

        return TryClickButton(
            addon,
            buttonNode,
            StartTrainingEventParam
        );
    }

    private unsafe bool TryClickButton(
        AtkUnitBase* addon,
        AtkResNode* buttonNode,
        uint expectedParam)
    {
        if (
            addon == null
            ||
            buttonNode == null
        )
        {
            return false;
        }

        var button =
            buttonNode
                ->GetAsAtkComponentButton();

        if (
            button == null
            ||
            !buttonNode->IsVisible()
            ||
            !button->IsEnabled
        )
        {
            return false;
        }

        var evt =
            (AtkEvent*)
            buttonNode
                ->AtkEventManager
                .Event;

        var checkedEvents =
            0;

        while (
            evt != null
            &&
            checkedEvents < 32
        )
        {
            if (
                evt->State.EventType
                    == AtkEventType.ButtonClick
                &&
                evt->Param
                    == expectedParam
            )
            {
                addon->ReceiveEvent(
                    evt->State.EventType,
                    (int)evt->Param,
                    evt
                );

                return true;
            }

            evt =
                evt->NextEvent;

            checkedEvents++;
        }

        return false;
    }

    private static ChocoboStat GetFeedStat(
        TrainingFeed feed)
    {
        return feed switch
        {
            TrainingFeed.Grade1Speed
                or TrainingFeed.Grade3Speed
                => ChocoboStat.MaxSpeed,

            TrainingFeed.Grade1Acceleration
                or TrainingFeed.Grade3Acceleration
                => ChocoboStat.Acceleration,

            TrainingFeed.Grade1Endurance
                or TrainingFeed.Grade3Endurance
                => ChocoboStat.Endurance,

            TrainingFeed.Grade1Stamina
                or TrainingFeed.Grade3Stamina
                => ChocoboStat.Stamina,

            TrainingFeed.Grade1Adaptability
                or TrainingFeed.Grade3Adaptability
                => ChocoboStat.Adaptability,

            _ =>
                ChocoboStat.MaxSpeed
        };
    }

    private static double GetFeedIncreaseRate(
        TrainingFeed feed)
    {
        return feed switch
        {
            TrainingFeed.Grade3Speed
                or TrainingFeed.Grade3Acceleration
                or TrainingFeed.Grade3Endurance
                or TrainingFeed.Grade3Stamina
                or TrainingFeed.Grade3Adaptability
                => 0.03,

            _ =>
                0.01
        };
    }

    private static string GetStatName(
        ChocoboStat stat)
    {
        return stat switch
        {
            ChocoboStat.MaxSpeed =>
                "最大速度",

            ChocoboStat.Acceleration =>
                "加速力",

            ChocoboStat.Endurance =>
                "体力",

            ChocoboStat.Stamina =>
                "持久力",

            ChocoboStat.Adaptability =>
                "适应力",

            _ =>
                "未知属性"
        };
    }

    private static uint GetFeedItemId(
        TrainingFeed feed)
    {
        return feed switch
        {
            TrainingFeed.Grade1Speed =>
                9615,

            TrainingFeed.Grade1Acceleration =>
                9618,

            TrainingFeed.Grade1Endurance =>
                9624,

            TrainingFeed.Grade1Stamina =>
                9627,

            TrainingFeed.Grade1Adaptability =>
                9621,

            TrainingFeed.Grade3Speed =>
                9617,

            TrainingFeed.Grade3Acceleration =>
                9620,

            TrainingFeed.Grade3Endurance =>
                9626,

            TrainingFeed.Grade3Stamina =>
                9629,

            TrainingFeed.Grade3Adaptability =>
                9623,

            _ =>
                0
        };
    }

    private unsafe bool TryFindFeedSlot(
        uint itemId,
        out InventoryType inventoryType,
        out int slot,
        out uint quantity)
    {
        inventoryType =
            InventoryType.Invalid;

        slot =
            -1;

        quantity =
            0;

        var manager =
            InventoryManager.Instance();

        if (
            manager == null
        )
        {
            return false;
        }

        var bags =
            new[]
            {
                InventoryType.Inventory1,
                InventoryType.Inventory2,
                InventoryType.Inventory3,
                InventoryType.Inventory4
            };

        foreach (
            var type in bags
        )
        {
            var container =
                manager
                    ->GetInventoryContainer(
                        type
                    );

            if (
                container == null
                ||
                !container->IsLoaded
                ||
                container->Size <= 0
            )
            {
                continue;
            }

            for (
                var i = 0;
                i < container->Size;
                i++
            )
            {
                var item =
                    manager
                        ->GetInventorySlot(
                            type,
                            i
                        );

                if (
                    item == null
                    ||
                    item->ItemId != itemId
                    ||
                    item->Quantity <= 0
                )
                {
                    continue;
                }

                inventoryType =
                    type;

                slot =
                    i;

                quantity =
                    (uint)item->Quantity;

                return true;
            }
        }

        return false;
    }

    public string GetSelectedFeedName()
    {
        return Configuration.SelectedFeed switch
        {
            TrainingFeed.Grade1Speed =>
                "1级速度饲料",

            TrainingFeed.Grade1Acceleration =>
                "1级加速饲料",

            TrainingFeed.Grade1Endurance =>
                "1级体力饲料",

            TrainingFeed.Grade1Stamina =>
                "1级持久饲料",

            TrainingFeed.Grade1Adaptability =>
                "1级适应饲料",

            TrainingFeed.Grade3Speed =>
                "3级秘制速度饲料",

            TrainingFeed.Grade3Acceleration =>
                "3级秘制加速饲料",

            TrainingFeed.Grade3Endurance =>
                "3级秘制体力饲料",

            TrainingFeed.Grade3Stamina =>
                "3级秘制持久饲料",

            TrainingFeed.Grade3Adaptability =>
                "3级秘制适应饲料",

            _ =>
                "未知饲料"
        };
    }

    public void ToggleConfigUi()
    {
        ConfigWindow.Toggle();
    }

    public void ToggleMainUi()
    {
        MainWindow.Toggle();
    }
}