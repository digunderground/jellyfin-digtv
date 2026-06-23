using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DigTv.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DigTv.ScheduledTasks;

/// <summary>
/// Scheduled task that rebuilds (and reshuffles) every enabled DIGtv channel.
/// Appears under Dashboard -> Scheduled Tasks; runs nightly by default and can
/// be run manually.
/// </summary>
public class RegenerateChannelsTask : IScheduledTask
{
    private readonly ChannelService _channelService;
    private readonly ILogger<RegenerateChannelsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegenerateChannelsTask"/> class.
    /// </summary>
    public RegenerateChannelsTask(ChannelService channelService, ILogger<RegenerateChannelsTask> logger)
    {
        _channelService = channelService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "DIGtv – Regenerate channels";

    /// <inheritdoc />
    public string Key => "DigTvRegenerateChannels";

    /// <inheritdoc />
    public string Description => "Re-shuffles each DIGtv channel on its configured cadence (hourly / daily / weekly). Runs every hour and rebuilds only the channels that are due, so the running order changes without using client shuffle.";

    /// <inheritdoc />
    public string Category => "DIGtv";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Only channels that are enabled, have an auto-reshuffle cadence, and are due.
        var now = DateTime.UtcNow;
        var due = Plugin.Instance!.Configuration.Channels
            .Where(c => c.Enabled && IsDue(c, now))
            .ToList();

        if (due.Count == 0)
        {
            progress.Report(100);
            return;
        }

        for (var i = 0; i < due.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _channelService.BuildChannelAsync(due[i], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DIGtv: scheduled rebuild failed for channel {Name}", due[i].Name);
            }

            progress.Report((double)(i + 1) / due.Count * 100);
        }
    }

    /// <summary>
    /// True if the channel's auto-reshuffle cadence has elapsed since its last build.
    /// </summary>
    private static bool IsDue(ChannelConfig channel, DateTime nowUtc)
    {
        var interval = CadenceToInterval(channel.ReshuffleCadence);
        if (interval is null)
        {
            return false; // cadence "off"
        }

        if (string.IsNullOrEmpty(channel.LastRunUtc)
            || !DateTime.TryParse(
                channel.LastRunUtc,
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var last))
        {
            return true; // never built (or unparseable) -> build now
        }

        return nowUtc - last >= interval.Value;
    }

    private static TimeSpan? CadenceToInterval(string? cadence)
    {
        switch ((cadence ?? "off").Trim().ToLowerInvariant())
        {
            case "hourly": return TimeSpan.FromHours(1);
            case "daily": return TimeSpan.FromDays(1);
            case "weekly": return TimeSpan.FromDays(7);
            default: return null;
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                // Run hourly; per-channel cadence is enforced in ExecuteAsync.
                // 10.10 uses string trigger constants (10.11+ switched to an enum).
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(1).Ticks
            }
        };
    }
}
