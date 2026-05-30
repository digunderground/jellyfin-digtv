using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.DigTv.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DigTv;

/// <summary>
/// DIGtv plugin entry point. Builds MTV-style channels by interleaving music
/// videos with station bumpers into native Jellyfin playlists.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths provided by the host.</param>
    /// <param name="xmlSerializer">Xml serializer provided by the host.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "DIGtv";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b3f8e2a1-7c4d-4e6b-9a2f-1d5c8e0a4b7e");

    /// <inheritdoc />
    public override string Description =>
        "MTV-style channels: interleave a music-video playlist/folder with station bumpers into native playlists that play on every client.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        };
    }
}
