using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ChocoboTrainer.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Chocobo Trainer###ChocoboTrainerMain")
    {
        this.plugin = plugin;

        Size = new Vector2(520, 250);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var isRunning =
            plugin.IsRunning;

        ImGui.Text(
            $"训练饲料：{plugin.GetSelectedFeedName()}"
        );

        ImGui.Spacing();

        if (isRunning)
        {
            ImGui.BeginDisabled();
        }

        if (
            ImGui.Button(
                "开始训练",
                new Vector2(120, 0)
            )
        )
        {
            plugin.StartFromUi();
        }

        if (isRunning)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        if (!isRunning)
        {
            ImGui.BeginDisabled();
        }

        if (
            ImGui.Button(
                "结束训练",
                new Vector2(120, 0)
            )
        )
        {
            plugin.StopFromUi();
        }

        if (!isRunning)
        {
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped(
            plugin.GetTrainerProximityText()
        );

        ImGui.Spacing();

        ImGui.TextWrapped(
            $"状态：{plugin.UiStatus}"
        );

        ImGui.Text(
            $"已完成训练：{plugin.CompletedTrainingCount} 次"
        );

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (
            ImGui.Button(
                "设置",
                new Vector2(120, 0)
            )
        )
        {
            plugin.ToggleConfigUi();
        }
    }
}