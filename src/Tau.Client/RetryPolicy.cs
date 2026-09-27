namespace Tau.Client;

/// <summary>
/// Controls how <see cref="SystemOneClient"/> retries a request that receives a transient
/// server response. By default only HTTP 429 (rate limited) and 529 (overloaded) are retried; every
/// other 4xx and 5xx status is reported immediately via <see cref="SystemOneHttpException"/>.
/// <see cref="RetryServerErrors"/> widens that to every 5xx, for hosted endpoints reached over the network.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>The default policy: 3 attempts in total (the first try plus 2 retries), 0.5 second base delay.</summary>
    public static RetryPolicy Default { get; } = new();

    /// <summary>
    /// The total number of attempts, including the first (not-a-retry) one. Values below 1 are
    /// treated as 1 (no retries). Default 3.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// The delay before the first retry. Each subsequent retry doubles the previous delay
    /// (exponential backoff: <c>BaseDelay * 2^(attempt - 1)</c>). Default 0.5 seconds.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Also retry every 5xx status (500 to 599), not just 529. Off by default: a local Tau Runtime's 500 is a
    /// bug to report, while a hosted endpoint's 502 or 503 is usually the network or a busy server. Default false.
    /// </summary>
    public bool RetryServerErrors { get; init; }

    /// <summary>
    /// The function used to wait between attempts. Defaults to
    /// <see cref="Task.Delay(TimeSpan,CancellationToken)"/>. Overriding it — for example in a
    /// test — avoids waiting out the real delay while still exercising the retry logic.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = static (delay, ct) => Task.Delay(delay, ct);
}
