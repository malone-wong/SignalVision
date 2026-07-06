using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text;

namespace SignalVision
{
    public class Config
    {
        public static Config Read()
        {
            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            if (!File.Exists(configFilePath))
            {
                throw new Exception("Config file not found.");
            }
            // Read the config file and deserialize it into the Config object
            string json = File.ReadAllText(configFilePath);
            Config? config=System.Text.Json.JsonSerializer.Deserialize<Config>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            return config ?? throw new Exception("Failed to deserialize config file.");
        }

        public Config() { }
        public string PDF { get; set; } = string.Empty;
        public WindowsPanelConfig WindowsPanel { get; set; } = new();
    }

    public class WindowsPanelConfig
    {
        public string TitleColor { get; set; } = "#0072c5";
        public int TitleColorTolerance { get; set; } = 48;
    }
}
