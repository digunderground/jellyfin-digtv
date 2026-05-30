using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public string Description => "Rebuilds and reshuffles all enabled DIGtv MTV channels.";

    /// <inheritdoc />
    public string Category => "DIGtv";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var channels = Plugin.Instance!.Configuration.Channels.Where(c => c.Enabled).ToList();
        if (channels.Count == 0)
        {
            progress.Report(100);
            return;
        }

        for (var i = 0; i < channels.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _channelService.BuildChannelAsync(channels[i], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DIGtv: scheduled rebuild failed for channel {Name}", channels[i].Name);
            }

            progress.Report((double)(i + 1) / channels.Count * 100);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
            }
        };
    }
}
