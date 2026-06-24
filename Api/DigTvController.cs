using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DigTv.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DigTv.Api;

/// <summary>A selectable source (playlist or library folder) for the UI pickers.</summary>
public record SourceDto(Guid Id, string Name, string Type);

/// <summary>A user option for the owner dropdown.</summary>
public record UserDto(Guid Id, string Name);

/// <summary>
/// Admin REST API backing the DIGtv configuration page.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("DigTv")]
[Produces("application/json")]
public class DigTvController : ControllerBase
{
    private readonly ChannelService _channelService;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<DigTvController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DigTvController"/> class.
    /// </summary>
    public DigTvController(
        ChannelService channelService,
        ILibraryManager libraryManager,
        IUserManager userManager,
        ILogger<DigTvController> logger)
    {
        _channelService = channelService;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Gets all configured channels.</summary>
    [HttpGet("channels")]
    public ActionResult<IReadOnlyList<ChannelConfig>> GetChannels()
        => Ok(Plugin.Instance!.Configuration.Channels);

    /// <summary>Replaces the full channel list (the UI sends the whole array).</summary>
    [HttpPost("channels")]
    public ActionResult SaveChannels([FromBody] List<ChannelConfig> channels)
    {
        Plugin.Instance!.Configuration.Channels = channels ?? new List<ChannelConfig>();
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    /// <summary>Deletes a single channel definition (does not delete its playlist).</summary>
    [HttpDelete("channels/{id}")]
    public ActionResult DeleteChannel([FromRoute] Guid id)
    {
        Plugin.Instance!.Configuration.Channels.RemoveAll(c => c.Id == id);
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    /// <summary>Rebuilds / reshuffles a single channel now.</summary>
    [HttpPost("channels/{id}/regenerate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Regenerate([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var channel = Plugin.Instance!.Configuration.Channels.FirstOrDefault(c => c.Id == id);
        if (channel is null)
        {
            return NotFound();
        }

        try
        {
            var result = await _channelService.BuildChannelAsync(channel, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DIGtv: regenerate failed for channel {Name}", channel.Name);
            channel.LastStatus = "ERROR: " + ex.Message;
            Plugin.Instance.SaveConfiguration();
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Lists playlists and library folders to populate the source pickers.</summary>
    [HttpGet("sources")]
    public ActionResult<object> GetSources()
    {
        var playlists = _libraryManager
            .GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Playlist },
                Recursive = true
            })
            .Select(i => new SourceDto(i.Id, i.Name, "Playlist"))
            .OrderBy(s => s.Name)
            .ToList();

        var folders = _libraryManager
            .GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.CollectionFolder },
                Recursive = true
            })
            .Select(i => new SourceDto(i.Id, i.Name, "Library"))
            .OrderBy(s => s.Name)
            .ToList();

        return Ok(new { playlists, folders });
    }

    /// <summary>Lists users for the owner dropdown.</summary>
    [HttpGet("users")]
    public ActionResult<IReadOnlyList<UserDto>> GetUsers()
        => Ok(_userManager.GetUsers().Select(u => new UserDto(u.Id, u.Username)).ToList());
}
