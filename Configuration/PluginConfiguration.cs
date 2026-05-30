using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DigTv.Configuration;

/// <summary>
/// Canonical, update-safe store for all DIGtv channel definitions.
/// Persisted by Jellyfin to plugins/configurations/Jellyfin.Plugin.DigTv.xml.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the configured MTV channels.</summary>
    public List<ChannelConfig> Channels { get; set; } = new();
}
