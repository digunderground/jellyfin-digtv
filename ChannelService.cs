using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DigTv.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DigTv;

/// <summary>
/// Result of building one channel.
/// </summary>
/// <param name="PlaylistId">The generated playlist id.</param>
/// <param name="VideoCount">Number of music videos used.</param>
/// <param name="BumperCount">Number of bumpers available.</param>
/// <param name="TotalItems">Total interleaved items written.</param>
public record BuildResult(Guid PlaylistId, int VideoCount, int BumperCount, int TotalItems);

/// <summary>
/// Core logic: read sources, interleave videos + bumpers, and materialize a
/// native Jellyfin playlist that plays on every client.
/// </summary>
public class ChannelService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<ChannelService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelService"/> class.
    /// </summary>
    public ChannelService(
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserManager userManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILogger<ChannelService> logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <summary>
    /// Gets the DIGtv data directory (/plugin/DIGtv). Holds the channels.json
    /// mirror and per-channel last-run logs for easy inspection.
    /// </summary>
    public string DataPath
    {
        get
        {
            var path = Path.Combine(_appPaths.PluginsPath, "DIGtv");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>
    /// Builds (or rebuilds / reshuffles) the playlist for a single channel.
    /// </summary>
    public async Task<BuildResult> BuildChannelAsync(ChannelConfig channel, CancellationToken cancellationToken)
    {
        var owner = ResolveOwner(channel.OwnerUserId);
        if (owner == Guid.Empty)
        {
            throw new InvalidOperationException("No Jellyfin user available to own the generated playlist.");
        }

        var videos = CollectVideos(channel.MusicVideoSourceIds);
        var bumpers = CollectVideos(channel.BumperSourceIds);

        if (videos.Count == 0)
        {
            throw new InvalidOperationException("No playable music videos were found in the configured sources.");
        }

        var ordered = Interleave(videos, bumpers, channel);
        var playlistId = await MaterializeAsync(channel, owner, ordered, cancellationToken).ConfigureAwait(false);

        channel.TargetPlaylistId = playlistId;
        channel.LastItemCount = ordered.Count;
        channel.LastRunUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        channel.LastStatus = string.Format(
            CultureInfo.InvariantCulture,
            "OK: {0} videos, {1} bumpers, {2} total items",
            videos.Count,
            bumpers.Count,
            ordered.Count);

        Plugin.Instance!.SaveConfiguration();
        WriteSidecars(channel, ordered.Count, videos.Count, bumpers.Count);

        _logger.LogInformation(
            "DIGtv built channel '{Name}': {Total} items ({Videos} videos / {Bumpers} bumpers) -> playlist {PlaylistId}",
            channel.Name,
            ordered.Count,
            videos.Count,
            bumpers.Count,
            playlistId);

        return new BuildResult(playlistId, videos.Count, bumpers.Count, ordered.Count);
    }

    private Guid ResolveOwner(Guid configured)
    {
        if (configured != Guid.Empty && _userManager.GetUserById(configured) is not null)
        {
            return configured;
        }

        // No (or stale) owner configured: fall back to the first user. We avoid a
        // PermissionKind admin check on purpose — that enum has moved namespaces
        // between Jellyfin versions, and any user can own a playlist anyway.
        return _userManager.Users.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    /// <summary>
    /// Resolves a set of source ids (playlists and/or folders) into a flat list
    /// of playable video items.
    /// </summary>
    private List<BaseItem> CollectVideos(IEnumerable<Guid> sourceIds)
    {
        var result = new List<BaseItem>();
        foreach (var id in sourceIds)
        {
            if (id == Guid.Empty)
            {
                continue;
            }

            var item = _libraryManager.GetItemById(id);
            switch (item)
            {
                case null:
                    continue;
                case Playlist playlist:
                    // Preserve playlist order; GetManageableItems yields (LinkedChild, BaseItem).
                    result.AddRange(playlist.GetManageableItems().Select(t => t.Item2));
                    break;
                case Folder folder:
                    result.AddRange(folder.GetRecursiveChildren(i => !i.IsFolder));
                    break;
                default:
                    if (!item.IsFolder)
                    {
                        result.Add(item);
                    }

                    break;
            }
        }

        return result
            .Where(i => i is not null && i.MediaType == MediaType.Video && i.SupportsAddingToPlaylist)
            .ToList();
    }

    /// <summary>
    /// Interleaves videos and bumpers: after every N videos, insert the next bumper.
    /// </summary>
    private static List<BaseItem> Interleave(List<BaseItem> videos, List<BaseItem> bumpers, ChannelConfig channel)
    {
        var vids = videos.ToList();
        var bmps = bumpers.ToList();

        if (channel.Randomize || channel.ReshuffleOnRegenerate)
        {
            Shuffle(vids);
            Shuffle(bmps);
        }

        var ordered = new List<BaseItem>(vids.Count * 2);
        var sinceBumper = 0;
        var bumperCursor = 0;
        var frequency = channel.BumperFrequency;

        foreach (var video in vids)
        {
            ordered.Add(video);

            if (frequency > 0 && bmps.Count > 0)
            {
                sinceBumper++;
                if (sinceBumper >= frequency)
                {
                    ordered.Add(bmps[bumperCursor % bmps.Count]);
                    bumperCursor++;
                    sinceBumper = 0;
                }
            }
        }

        if (channel.MaxItems > 0 && ordered.Count > channel.MaxItems)
        {
            ordered = ordered.Take(channel.MaxItems).ToList();
        }

        return ordered;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        var rng = Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Writes the exact interleaved order to the playlist. We set LinkedChildren
    /// directly (allowing the SAME bumper to repeat many times) because
    /// IPlaylistManager.AddItemToPlaylistAsync de-duplicates items, which would
    /// collapse a channel's repeated bumpers. This mirrors what Jellyfin's own
    /// PlaylistManager does internally, using only public APIs.
    /// </summary>
    private async Task<Guid> MaterializeAsync(ChannelConfig channel, Guid owner, List<BaseItem> ordered, CancellationToken cancellationToken)
    {
        var playlist = channel.TargetPlaylistId != Guid.Empty
            ? _libraryManager.GetItemById(channel.TargetPlaylistId) as Playlist
            : null;

        if (playlist is null)
        {
            // Seed with the distinct ids; contents are immediately replaced below.
            var creation = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = channel.Name,
                ItemIdList = ordered.Select(i => i.Id).Distinct().ToArray(),
                MediaType = MediaType.Video,
                UserId = owner,
                Public = channel.Shared
            }).ConfigureAwait(false);

            playlist = _libraryManager.GetItemById(Guid.Parse(creation.Id)) as Playlist
                ?? throw new InvalidOperationException("DIGtv failed to create the playlist.");
        }

        playlist.Name = channel.Name;
        playlist.LinkedChildren = ordered.Select(LinkedChild.Create).ToArray();

        await playlist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

        if (playlist.IsFile)
        {
            _playlistManager.SavePlaylistFile(playlist);
        }

        // Persist the manifest (playlist.xml) with the new, duplicate-containing order.
        await playlist.RefreshMetadata(
            new MetadataRefreshOptions(new DirectoryService(_fileSystem)) { ForceSave = true },
            cancellationToken).ConfigureAwait(false);

        return playlist.Id;
    }

    private void WriteSidecars(ChannelConfig channel, int total, int videoCount, int bumperCount)
    {
        try
        {
            var json = JsonSerializer.Serialize(
                Plugin.Instance!.Configuration.Channels,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(DataPath, "channels.json"), json);

            var log = string.Format(
                CultureInfo.InvariantCulture,
                "{0} channel='{1}' videos={2} bumpers={3} total={4} playlist={5:N}{6}",
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                channel.Name,
                videoCount,
                bumperCount,
                total,
                channel.TargetPlaylistId,
                Environment.NewLine);
            File.WriteAllText(Path.Combine(DataPath, channel.Id.ToString("N") + ".last-run.log"), log);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DIGtv: could not write sidecar files to {DataPath}", DataPath);
        }
    }
}
