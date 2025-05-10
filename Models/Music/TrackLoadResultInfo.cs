using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;

namespace Assistant.Net.Models.Music;

public enum TrackLoadStatus
{
    TrackLoaded,
    PlaylistLoaded,
    SearchResults,
    LoadFailed,
    NoMatches
}

public record TrackLoadResultInfo
{
    public TrackLoadStatus Status { get; init; }
    public LavalinkTrack? LoadedTrack { get; init; }
    public IReadOnlyList<LavalinkTrack> Tracks { get; init; } = [];
    public PlaylistInformation? PlaylistInformation { get; init; }
    public string? ErrorMessage { get; init; }
    public string OriginalQuery { get; init; } = string.Empty;

    public static TrackLoadResultInfo FromSuccess(LavalinkTrack track, string query) => new()
        { Status = TrackLoadStatus.TrackLoaded, LoadedTrack = track, Tracks = [track], OriginalQuery = query };

    public static TrackLoadResultInfo FromPlaylist(IReadOnlyList<LavalinkTrack> tracks,
        PlaylistInformation playlistInfo, string query) => new()
    {
        Status = TrackLoadStatus.PlaylistLoaded, Tracks = tracks, PlaylistInformation = playlistInfo,
        OriginalQuery = query
    };

    public static TrackLoadResultInfo FromSearchResults(IReadOnlyList<LavalinkTrack> tracks, string query) => new()
        { Status = TrackLoadStatus.SearchResults, Tracks = tracks, OriginalQuery = query };

    public static TrackLoadResultInfo FromError(string errorMessage, string query) => new()
        { Status = TrackLoadStatus.LoadFailed, ErrorMessage = errorMessage, OriginalQuery = query };

    public static TrackLoadResultInfo FromNoMatches(string query) => new()
        { Status = TrackLoadStatus.NoMatches, OriginalQuery = query };
}