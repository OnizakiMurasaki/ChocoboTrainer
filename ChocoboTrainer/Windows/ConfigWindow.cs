using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ChocoboTrainer.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("Chocobo Trainer 设置###ChocoboTrainerConfig")
    {
        Size = new Vector2(480, 460);
        SizeCondition = ImGuiCond.FirstUseEver;

        configuration = plugin.Configuration;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.Text("训练饲料");

        var currentName =
            GetFeedName(configuration.SelectedFeed);

        if (
            ImGui.BeginCombo(
                "##TrainingFeed",
                currentName
            )
        )
        {
            foreach (
                TrainingFeed feed
                in Enum.GetValues<TrainingFeed>()
            )
            {
                var selected =
                    feed == configuration.SelectedFeed;

                if (
                    ImGui.Selectable(
                        GetFeedName(feed),
                        selected
                    )
                )
                {
                    configuration.SelectedFeed = feed;
                    configuration.Save();
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        ImGui.TextWrapped(
            "自动训练只会使用这里指定的饲料。找不到指定饲料时会结束训练。"
        );

        ImGui.Separator();

        var preventOverflow =
            configuration.PreventStatOverflow;

        if (
            ImGui.Checkbox(
                "自动防止属性溢出（保守）",
                ref preventOverflow
            )
        )
        {
            configuration.PreventStatOverflow =
                preventOverflow;

            configuration.Save();
        }

        ImGui.TextWrapped(
            "如果训练后预览已经到达属性上限，并存在隐藏小数造成属性浪费的可能，则取消本次训练。"
        );

        ImGui.Separator();

        ImGui.Text("属性停止值");

        ImGui.TextWrapped(
            "训练后的预览属性达到或超过设定值时，取消该次训练并结束自动训练。"
        );

        ImGui.Spacing();

        DrawLimit(
            "最大速度",
            configuration.MaxSpeedLimitEnabled,
            configuration.MaxSpeedLimit,
            (enabled, value) =>
            {
                configuration.MaxSpeedLimitEnabled =
                    enabled;

                configuration.MaxSpeedLimit =
                    value;
            }
        );

        DrawLimit(
            "加速力",
            configuration.AccelerationLimitEnabled,
            configuration.AccelerationLimit,
            (enabled, value) =>
            {
                configuration.AccelerationLimitEnabled =
                    enabled;

                configuration.AccelerationLimit =
                    value;
            }
        );

        DrawLimit(
            "体力",
            configuration.EnduranceLimitEnabled,
            configuration.EnduranceLimit,
            (enabled, value) =>
            {
                configuration.EnduranceLimitEnabled =
                    enabled;

                configuration.EnduranceLimit =
                    value;
            }
        );

        DrawLimit(
            "持久力",
            configuration.StaminaLimitEnabled,
            configuration.StaminaLimit,
            (enabled, value) =>
            {
                configuration.StaminaLimitEnabled =
                    enabled;

                configuration.StaminaLimit =
                    value;
            }
        );

        DrawLimit(
            "适应力",
            configuration.AdaptabilityLimitEnabled,
            configuration.AdaptabilityLimit,
            (enabled, value) =>
            {
                configuration.AdaptabilityLimitEnabled =
                    enabled;

                configuration.AdaptabilityLimit =
                    value;
            }
        );
    }

    private void DrawLimit(
        string name,
        bool enabledValue,
        int limitValue,
        Action<bool, int> setter)
    {
        var enabled = enabledValue;
        var limit = limitValue;

        if (
            ImGui.Checkbox(
                $"##Enable{name}",
                ref enabled
            )
        )
        {
            setter(
                enabled,
                limit
            );

            configuration.Save();
        }

        ImGui.SameLine();

        ImGui.Text(name);

        ImGui.SameLine(130);

        ImGui.SetNextItemWidth(100);

        if (
            ImGui.InputInt(
                $"##Limit{name}",
                ref limit
            )
        )
        {
            limit =
                Math.Clamp(
                    limit,
                    1,
                    500
                );

            setter(
                enabled,
                limit
            );

            configuration.Save();
        }

        ImGui.SameLine();

        ImGui.TextDisabled("以上停止");
    }

    private static string GetFeedName(
        TrainingFeed feed)
    {
        return feed switch
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
}