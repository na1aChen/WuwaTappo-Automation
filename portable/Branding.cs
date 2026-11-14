using System.Drawing;

internal static class ProductBranding
{
    public const string Author = "by Phrolova2405";

    public const string MainName = "Wuwa Tappo 自动助手";
    public const string MainDisplayName = MainName + " " + Author;
    public const string MainSubtitle = "自动化控制台  ·  " + Author;

    public const string HostName = "Wuwa Tappo 原生计数 Host";
    public const string HostDisplayName = HostName + " " + Author;
    public const string HostHeading = "页面原生计数一次性补次数";
    public const string HostHeadingDisplay = HostHeading + "  ·  " + Author;

    public static readonly Color WatermarkColor =
        Color.FromArgb(105, 132, 148, 158);
}
