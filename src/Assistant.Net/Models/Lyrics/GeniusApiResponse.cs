using System.Text.Json.Serialization;

namespace Assistant.Net.Models.Lyrics;

public class GeniusSearchResponse
{
    [JsonPropertyName("meta")] public GeniusMeta Meta { get; set; } = new();
    [JsonPropertyName("response")] public GeniusResponseData? Response { get; set; }
}

public class GeniusMeta
{
    [JsonPropertyName("status")] public int Status { get; set; }
}

public class GeniusResponseData
{
    [JsonPropertyName("hits")] public List<GeniusHit> Hits { get; set; } = [];
}

public class GeniusHit
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("result")] public GeniusSong Result { get; set; } = new();
}

public class GeniusSong
{
    // Fields from /search result
    [JsonPropertyName("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonPropertyName("artist_names")] public string ArtistNames { get; set; } = string.Empty;
    [JsonPropertyName("full_title")] public string FullTitle { get; set; } = string.Empty;

    [JsonPropertyName("header_image_thumbnail_url")]
    public string? HeaderImageThumbnailUrl { get; set; }

    [JsonPropertyName("header_image_url")] public string? HeaderImageUrl { get; set; }
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;

    [JsonPropertyName("primary_artist_names")]
    public string? PrimaryArtistNames { get; set; }

    [JsonPropertyName("song_art_image_thumbnail_url")]
    public string? SongArtImageThumbnailUrl { get; set; }

    [JsonPropertyName("song_art_image_url")]
    public string? SongArtImageUrl { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("featured_artists")] public List<GeniusArtist> FeaturedArtists { get; set; } = [];
    [JsonPropertyName("primary_artist")] public GeniusArtist PrimaryArtist { get; set; } = new();
    [JsonPropertyName("primary_artists")] public List<GeniusArtist> PrimaryArtists { get; set; } = [];

    [JsonPropertyName("featured_video")] public bool? FeaturedVideo { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }

    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; } // YYYY-MM-DD or null

    [JsonPropertyName("song_art_primary_color")]
    public string? SongArtPrimaryColor { get; set; }

    [JsonPropertyName("song_art_secondary_color")]
    public string? SongArtSecondaryColor { get; set; }

    [JsonPropertyName("song_art_text_color")]
    public string? SongArtTextColor { get; set; }

    [JsonPropertyName("album")] public GeniusAlbum? Album { get; set; }
}

public class GeniusArtist
{
    [JsonPropertyName("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonPropertyName("header_image_url")] public string? HeaderImageUrl { get; set; }
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
}

// --- Album ---
public class GeniusAlbum
{
    [JsonPropertyName("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonPropertyName("cover_art_url")] public string? CoverArtUrl { get; set; }
    [JsonPropertyName("full_title")] public string FullTitle { get; set; } = string.Empty;
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("primary_artist_names")]
    public string? PrimaryArtistNames { get; set; }

    [JsonPropertyName("release_date_for_display")]
    public string? ReleaseDateForDisplay { get; set; }

    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("artist")] public GeniusArtist? Artist { get; set; }
    [JsonPropertyName("primary_artists")] public List<GeniusArtist> PrimaryArtists { get; set; } = [];
}

// --- For fetching a single song by ID ---
public class GeniusSongResponse
{
    [JsonPropertyName("meta")] public GeniusMeta Meta { get; set; } = new();
    [JsonPropertyName("response")] public GeniusSingleSongData? Response { get; set; }
}

public class GeniusSingleSongData
{
    [JsonPropertyName("song")] public GeniusSong? Song { get; set; }
}