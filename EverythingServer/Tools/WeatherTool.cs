using ModelContextProtocol.Server;
using System;
using System.ComponentModel;

namespace EverythingServer.Tools;

[McpServerToolType]
public class WeatherTool
{
    [McpServerTool(Name = "get_weather"), Description("Gets the current weather.")]
    public static string GetWeather()
    {
        var weatherOptions = new[] { "晴天", "多云", "雨天", "雪天", "大风天" };
        var random = new Random();
        var weather = weatherOptions[random.Next(weatherOptions.Length)];
        var date = DateTime.Now.ToString("M月d日 dddd");
        return $"今天{date}，我不負責地告訴你：今天天气是 {weather}。";
    }
}
