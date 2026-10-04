using Dalamud.Configuration;
using System;

namespace ChocoboTrainer;

public enum TrainingFeed
{
    Grade1Speed = 0,
    Grade1Acceleration = 1,
    Grade1Endurance = 2,
    Grade1Stamina = 3,
    Grade1Adaptability = 4,

    Grade3Speed = 5,
    Grade3Acceleration = 6,
    Grade3Endurance = 7,
    Grade3Stamina = 8,
    Grade3Adaptability = 9
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public TrainingFeed SelectedFeed { get; set; }
        = TrainingFeed.Grade1Speed;

    public bool PreventStatOverflow { get; set; } = true;

    public bool MaxSpeedLimitEnabled { get; set; }
    public int MaxSpeedLimit { get; set; } = 500;

    public bool AccelerationLimitEnabled { get; set; }
    public int AccelerationLimit { get; set; } = 500;

    public bool EnduranceLimitEnabled { get; set; }
    public int EnduranceLimit { get; set; } = 500;

    public bool StaminaLimitEnabled { get; set; }
    public int StaminaLimit { get; set; } = 500;

    public bool AdaptabilityLimitEnabled { get; set; }
    public int AdaptabilityLimit { get; set; } = 500;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}