using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DigTv.Configuration;

/// <summary>
/// Definition of a single MTV-style channel. The plugin can hold many of these.
/// </summary>
public class ChannelConfig
{
    /// <summary>Gets or sets the stable channel id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the display name (also the generated playlist name).</summary>
    public string Name { get; set; } = "New DIGtv Channel";

    /// <summary>Gets or sets a value indicating whether the scheduled task rebuilds this channel.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the source playlist(s)/folder(s) of music videos ("playlist A").</summary>
    public List<Guid> MusicVideoSourceIds { get; set; } = new();

    /// <summary>Gets or sets the source playlist(s)/folder(s) of station bumpers ("playlist B").</summary>
    public List<Guid> BumperSourceIds { get; set; } = new();

    /// <summary>Gets or sets how many music videos play between each bumper.</summary>
    public int BumperFrequency { get; set; } = 3;

    /// <summary>Gets or sets a value indicating whether sources are randomized when building.</summary>
    public bool Randomize { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether each rebuild produces a fresh random order.</summary>
    public bool ReshuffleOnRegenerate { get; set; } = true;

    /// <summary>Gets or sets the user who owns the generated playlist (empty = first admin).</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Gets or sets a value indicating whether the playlist is shared with all users.</summary>
    public bool Shared { get; set; } = true;

    /// <summary>Gets or sets the generated playlist id (reused across rebuilds).</summary>
    public Guid TargetPlaylistId { get; set; }

    /// <summary>Gets or sets an optional cap on total items (0 = unlimited).</summary>
    public int MaxItems { get; set; }

    // --- last run telemetry (read-only in UI) ---

    /// <summary>Gets or sets the ISO-8601 UTC timestamp of the last successful build.</summary>
    public string? LastRunUtc { get; set; }

    /// <summary>Gets or sets the total item count produced by the last build.</summary>
    public int LastItemCount { get; set; }

    /// <summary>Gets or sets a human-readable status string from the last build.</summary>
    public string? LastStatus { get; set; }
}
