namespace FrameworksPrac3.Config;

public sealed class RateLimitOptions
{
    public int ReadPerMinute { get; set; } = 60;

    public int WritePerMinute { get; set; } = 20;
}
