namespace EasyDay.Core.Models;

/// <summary>应用设置。整体序列化为 JSON 存在 Settings 表里。</summary>
public sealed class AppSettings
{
    // ---- AI ----
    // 默认不预置任何服务商：首次使用由用户自己填写
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string City { get; set; } = "北京";
    public int AiTimeoutSeconds { get; set; } = 45;

    /// <summary>额外请求头，每行 "Name: Value"。个别服务需要自定义头时用这里。</summary>
    public string ApiExtraHeaders { get; set; } = string.Empty;

    /// <summary>网络代理：空 = 直连；system = 跟随系统；其他 = 代理地址（如 http://127.0.0.1:7890）。</summary>
    public string AiProxy { get; set; } = string.Empty;

    // ---- 外观 ----
    public double WidgetOpacity { get; set; } = 0.96;

    /// <summary>floating = 普通窗口；topmost = 置顶；desktop = 嵌入桌面。</summary>
    public string WidgetMode { get; set; } = "desktop";

    /// <summary>旧字段，仅为兼容历史设置保留。</summary>
    public bool AlwaysOnTop { get; set; }
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }
    public double? WidgetWidth { get; set; }
    public double? WidgetHeight { get; set; }

    /// <summary>是否收起月历，只保留今日日程。</summary>
    public bool WidgetCollapsed { get; set; }

    // ---- 行为 ----
    public bool StartWithWindows { get; set; } = true;
    public int DefaultReminderMinutes { get; set; } = 15;
    public bool SmartReminders { get; set; } = true;
    public bool EnableReminderToasts { get; set; } = true;

    // ---- 简报 ----
    public bool EnableMorningBrief { get; set; } = true;
    public bool EnableEveningBrief { get; set; } = true;
    public string LastMorningBrief { get; set; } = string.Empty;
    public string LastEveningBrief { get; set; } = string.Empty;

    public bool HasAiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>接口地址、Key、模型都填齐才算配置完成。</summary>
    public bool HasAiConfig =>
        !string.IsNullOrWhiteSpace(ApiBaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Model);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public sealed class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
