using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Assistant.Net.Models.Lyrics;

public class GeniusSearchResponse
{
    [JsonProperty("meta")] public GeniusMeta Meta { get; set; } = new();

    [JsonProperty("response")] public GeniusResponseData? Response { get; set; }
}

public class GeniusMeta
{
    [JsonProperty("status")] public int Status { get; set; }
}

public class GeniusResponseData
{
    [JsonProperty("hits")] public List<GeniusHit> Hits { get; set; } = [];
}

public class GeniusHit
{
    [JsonProperty("highlights")] public List<object> Highlights { get; set; } = [];

    [JsonProperty("index")] public string Index { get; set; } = string.Empty;

    [JsonProperty("type")] public string Type { get; set; } = string.Empty;

    [JsonProperty("result")] public GeniusSong Result { get; set; } = new();
}

public class GeniusSong
{
    // Fields from /search result
    [JsonProperty("annotation_count")] public int AnnotationCount { get; set; }
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("artist_names")] public string ArtistNames { get; set; } = string.Empty;
    [JsonProperty("full_title")] public string FullTitle { get; set; } = string.Empty;

    [JsonProperty("header_image_thumbnail_url")]
    public string? HeaderImageThumbnailUrl { get; set; }

    [JsonProperty("header_image_url")] public string? HeaderImageUrl { get; set; }
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("lyrics_owner_id")] public long? LyricsOwnerId { get; set; }
    [JsonProperty("lyrics_state")] public string LyricsState { get; set; } = string.Empty;
    [JsonProperty("path")] public string Path { get; set; } = string.Empty;

    [JsonProperty("primary_artist_names")]
    public string? PrimaryArtistNames { get; set; } // Sometimes just "Primary Artist Names"

    [JsonProperty("pyongs_count")] public int? PyongsCount { get; set; }

    [JsonProperty("relationships_index_url")]
    public string? RelationshipsIndexUrl { get; set; }

    [JsonProperty("release_date_components")]
    public GeniusReleaseDateComponents? ReleaseDateComponents { get; set; }

    [JsonProperty("release_date_for_display")]
    public string? ReleaseDateForDisplay { get; set; }

    [JsonProperty("release_date_with_abbreviated_month_for_display")]
    public string? ReleaseDateWithAbbreviatedMonthForDisplay { get; set; }

    [JsonProperty("song_art_image_thumbnail_url")]
    public string? SongArtImageThumbnailUrl { get; set; }

    [JsonProperty("song_art_image_url")] public string? SongArtImageUrl { get; set; }
    [JsonProperty("stats")] public GeniusSongStats Stats { get; set; } = new();
    [JsonProperty("title")] public string Title { get; set; } = string.Empty;
    [JsonProperty("title_with_featured")] public string TitleWithFeatured { get; set; } = string.Empty;
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
    [JsonProperty("featured_artists")] public List<GeniusArtist> FeaturedArtists { get; set; } = [];
    [JsonProperty("primary_artist")] public GeniusArtist PrimaryArtist { get; set; } = new();
    [JsonProperty("primary_artists")] public List<GeniusArtist> PrimaryArtists { get; set; } = [];

    // Additional fields from /songs/{id} response
    [JsonProperty("apple_music_id")] public string? AppleMusicId { get; set; }

    [JsonProperty("apple_music_player_url")]
    public string? AppleMusicPlayerUrl { get; set; }

    [JsonProperty("description")] public GeniusDescription? Description { get; set; }
    [JsonProperty("embed_content")] public string? EmbedContent { get; set; }
    [JsonProperty("featured_video")] public bool? FeaturedVideo { get; set; }
    [JsonProperty("language")] public string? Language { get; set; }
    [JsonProperty("recording_location")] public string? RecordingLocation { get; set; }
    [JsonProperty("release_date")] public string? ReleaseDate { get; set; } // YYYY-MM-DD or null

    [JsonProperty("current_user_metadata")]
    public GeniusCurrentUserMetadata? CurrentUserMetadata { get; set; }

    [JsonProperty("song_art_primary_color")]
    public string? SongArtPrimaryColor { get; set; }

    [JsonProperty("song_art_secondary_color")]
    public string? SongArtSecondaryColor { get; set; }

    [JsonProperty("song_art_text_color")] public string? SongArtTextColor { get; set; }
    [JsonProperty("album")] public GeniusAlbum? Album { get; set; }
    [JsonProperty("custom_performances")] public List<GeniusCustomPerformance> CustomPerformances { get; set; } = [];

    [JsonProperty("description_annotation")]
    public GeniusDescriptionAnnotation? DescriptionAnnotation { get; set; }

    [JsonProperty("lyrics_marked_complete_by")]
    public GeniusUserReference? LyricsMarkedCompleteBy { get; set; }

    [JsonProperty("lyrics_marked_staff_approved_by")]
    public GeniusUserReference? LyricsMarkedStaffApprovedBy { get; set; }

    [JsonProperty("media")] public List<GeniusMediaItem> Media { get; set; } = [];
    [JsonProperty("producer_artists")] public List<GeniusArtist> ProducerArtists { get; set; } = [];
    [JsonProperty("song_relationships")] public List<GeniusSongRelationship> SongRelationships { get; set; } = [];
    [JsonProperty("translation_songs")] public List<GeniusTranslationSong> TranslationSongs { get; set; } = [];

    [JsonProperty("verified_annotations_by")]
    public List<GeniusUserReference> VerifiedAnnotationsBy { get; set; } = [];

    [JsonProperty("verified_contributors")]
    public List<GeniusVerifiedContributor> VerifiedContributors { get; set; } = [];

    [JsonProperty("verified_lyrics_by")] public List<GeniusUserReference> VerifiedLyricsBy { get; set; } = [];
    [JsonProperty("writer_artists")] public List<GeniusArtist> WriterArtists { get; set; } = [];
}

public class GeniusReleaseDateComponents
{
    [JsonProperty("year")] public int? Year { get; set; }
    [JsonProperty("month")] public int? Month { get; set; }
    [JsonProperty("day")] public int? Day { get; set; }
}

public class GeniusSongStats
{
    [JsonProperty("unreviewed_annotations")]
    public int UnreviewedAnnotations { get; set; }

    [JsonProperty("hot")] public bool Hot { get; set; }
    [JsonProperty("pageviews")] public long? Pageviews { get; set; }
    [JsonProperty("accepted_annotations")] public int? AcceptedAnnotations { get; set; } // From /songs/{id}
    [JsonProperty("contributors")] public int? Contributors { get; set; } // From /songs/{id}
    [JsonProperty("iq_earners")] public int? IqEarners { get; set; } // From /songs/{id}
    [JsonProperty("transcribers")] public int? Transcribers { get; set; } // From /songs/{id}
    [JsonProperty("verified_annotations")] public int? VerifiedAnnotations { get; set; } // From /songs/{id}
    [JsonProperty("concurrents")] public int? Concurrents { get; set; } // From /songs/{id}
}

public class GeniusArtist
{
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("header_image_url")] public string? HeaderImageUrl { get; set; }
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("image_url")] public string? ImageUrl { get; set; }
    [JsonProperty("is_meme_verified")] public bool IsMemeVerified { get; set; }
    [JsonProperty("is_verified")] public bool IsVerified { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
    [JsonProperty("iq")] public int? Iq { get; set; }
}

// --- Description DOM Structure ---
public class GeniusDescription
{
    [JsonProperty("dom")] public GeniusDomElement? Dom { get; set; }
}

public class GeniusDomElement
{
    [JsonProperty("tag")] public string Tag { get; set; } = string.Empty;
    [JsonProperty("attributes")] public GeniusDomAttributes? Attributes { get; set; } // Only for 'a' tags typically
    [JsonProperty("data")] public GeniusDomData? Data { get; set; } // Only for 'a' tags with api_path

    [JsonProperty("children")]
    public List<JToken> Children { get; set; } = []; // Can be strings or other GeniusDomElement
}

public class GeniusDomAttributes
{
    [JsonProperty("href")] public string? Href { get; set; }
    [JsonProperty("rel")] public string? Rel { get; set; }
}

public class GeniusDomData
{
    [JsonProperty("api_path")] public string? ApiPath { get; set; }
}

// --- Current User Metadata ---
public class GeniusCurrentUserMetadata
{
    [JsonProperty("permissions")] public List<string> Permissions { get; set; } = [];
    [JsonProperty("excluded_permissions")] public List<string> ExcludedPermissions { get; set; } = [];
    [JsonProperty("interactions")] public GeniusCurrentUserInteractions Interactions { get; set; } = new();
    [JsonProperty("relationships")] public JObject? Relationships { get; set; } // Assuming it can be dynamic or empty
    [JsonProperty("iq_by_action")] public JObject? IqByAction { get; set; } // Assuming it can be dynamic or empty
}

public class GeniusCurrentUserInteractions
{
    [JsonProperty("pyong")] public bool Pyong { get; set; }
    [JsonProperty("following")] public bool Following { get; set; }
    [JsonProperty("vote")] public string? Vote { get; set; } // Can be null, "up", "down"
}

// --- Album ---
public class GeniusAlbum
{
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("cover_art_url")] public string? CoverArtUrl { get; set; }
    [JsonProperty("full_title")] public string FullTitle { get; set; } = string.Empty;
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("primary_artist_names")] public string? PrimaryArtistNames { get; set; }

    [JsonProperty("release_date_for_display")]
    public string? ReleaseDateForDisplay { get; set; }

    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
    [JsonProperty("artist")] public GeniusArtist? Artist { get; set; }
    [JsonProperty("primary_artists")] public List<GeniusArtist> PrimaryArtists { get; set; } = [];
}

// --- Custom Performances ---
public class GeniusCustomPerformance
{
    [JsonProperty("label")] public string Label { get; set; } = string.Empty;
    [JsonProperty("artists")] public List<GeniusArtist> Artists { get; set; } = [];
}

// --- Description Annotation --- (This is a very complex, recursive structure)
public class GeniusDescriptionAnnotation
{
    [JsonProperty("_type")] public string Type { get; set; } = string.Empty;
    [JsonProperty("annotator_id")] public long AnnotatorId { get; set; }
    [JsonProperty("annotator_login")] public string AnnotatorLogin { get; set; } = string.Empty;
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("classification")] public string Classification { get; set; } = string.Empty;
    [JsonProperty("fragment")] public string Fragment { get; set; } = string.Empty;
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("is_description")] public bool IsDescription { get; set; }
    [JsonProperty("path")] public string Path { get; set; } = string.Empty;
    [JsonProperty("range")] public GeniusRange? Range { get; set; }
    [JsonProperty("song_id")] public long? SongId { get; set; }
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;

    [JsonProperty("verified_annotator_ids")]
    public List<long> VerifiedAnnotatorIds { get; set; } = [];

    [JsonProperty("annotatable")] public GeniusAnnotatable? Annotatable { get; set; }
    [JsonProperty("annotations")] public List<GeniusAnnotationDetail> Annotations { get; set; } = [];
}

public class GeniusRange
{
    [JsonProperty("content")] public string Content { get; set; } = string.Empty;
}

public class GeniusAnnotatable
{
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("client_timestamps")] public GeniusClientTimestamps? ClientTimestamps { get; set; }
    [JsonProperty("context")] public string? Context { get; set; }
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("image_url")] public string? ImageUrl { get; set; }
    [JsonProperty("link_title")] public string? LinkTitle { get; set; }
    [JsonProperty("title")] public string Title { get; set; } = string.Empty;
    [JsonProperty("type")] public string Type { get; set; } = string.Empty;
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
}

public class GeniusClientTimestamps
{
    [JsonProperty("updated_by_human_at")] public long UpdatedByHumanAt { get; set; }
    [JsonProperty("lyrics_updated_at")] public long LyricsUpdatedAt { get; set; }
}

public class GeniusAnnotationDetail
{
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("body")] public GeniusDescription? Body { get; set; } // Reusing GeniusDescription for its "dom"
    [JsonProperty("comment_count")] public int? CommentCount { get; set; }
    [JsonProperty("community")] public bool Community { get; set; }
    [JsonProperty("custom_preview")] public string? CustomPreview { get; set; }
    [JsonProperty("has_voters")] public bool HasVoters { get; set; }
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("pinned")] public bool Pinned { get; set; }
    [JsonProperty("share_url")] public string ShareUrl { get; set; } = string.Empty;
    [JsonProperty("source")] public JObject? Source { get; set; } // Can be complex, using JObject for now
    [JsonProperty("state")] public string State { get; set; } = string.Empty; // e.g., "accepted"
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
    [JsonProperty("verified")] public bool Verified { get; set; }
    [JsonProperty("votes_total")] public int VotesTotal { get; set; }

    [JsonProperty("current_user_metadata")]
    public GeniusCurrentUserMetadata? CurrentUserMetadata { get; set; } // Reusing

    [JsonProperty("authors")] public List<GeniusAnnotationAuthor> Authors { get; set; } = [];

    [JsonProperty("cosigned_by")]
    public List<object> CosignedBy { get; set; } = []; // Assuming UserReference if populated

    [JsonProperty("rejection_comment")] public string? RejectionComment { get; set; }
    [JsonProperty("verified_by")] public GeniusUserReference? VerifiedBy { get; set; }
}

public class GeniusAnnotationAuthor
{
    [JsonProperty("attribution")] public double Attribution { get; set; }
    [JsonProperty("pinned_role")] public string? PinnedRole { get; set; }
    [JsonProperty("user")] public GeniusUserReference? User { get; set; }
}

// --- User Reference (used in multiple places) ---
public class GeniusUserReference
{
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("avatar")] public GeniusAvatar? Avatar { get; set; }
    [JsonProperty("header_image_url")] public string? HeaderImageUrl { get; set; }

    [JsonProperty("human_readable_role_for_display")]
    public string? HumanReadableRoleForDisplay { get; set; }

    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("iq")] public int? Iq { get; set; }
    [JsonProperty("login")] public string Login { get; set; } = string.Empty;
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("role_for_display")] public string? RoleForDisplay { get; set; }
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;

    [JsonProperty("current_user_metadata")]
    public GeniusUserInteractionsMetadata? CurrentUserMetadata { get; set; }
}

public class GeniusUserInteractionsMetadata // Simplified for nested user references
{
    [JsonProperty("permissions")] public List<string> Permissions { get; set; } = [];
    [JsonProperty("excluded_permissions")] public List<string> ExcludedPermissions { get; set; } = [];
    [JsonProperty("interactions")] public GeniusUserInteractions? Interactions { get; set; }
}

public class GeniusUserInteractions
{
    [JsonProperty("following")] public bool Following { get; set; }
}

public class GeniusAvatar
{
    [JsonProperty("tiny")] public GeniusAvatarDetail? Tiny { get; set; }
    [JsonProperty("thumb")] public GeniusAvatarDetail? Thumb { get; set; }
    [JsonProperty("small")] public GeniusAvatarDetail? Small { get; set; }
    [JsonProperty("medium")] public GeniusAvatarDetail? Medium { get; set; }
}

public class GeniusAvatarDetail
{
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
    [JsonProperty("bounding_box")] public GeniusBoundingBox? BoundingBox { get; set; }
}

public class GeniusBoundingBox
{
    [JsonProperty("width")] public int Width { get; set; }
    [JsonProperty("height")] public int Height { get; set; }
}

// --- Media Item ---
public class GeniusMediaItem
{
    [JsonProperty("attribution")] public string? Attribution { get; set; }
    [JsonProperty("provider")] public string Provider { get; set; } = string.Empty;
    [JsonProperty("start")] public int? Start { get; set; }
    [JsonProperty("type")] public string Type { get; set; } = string.Empty; // e.g., "audio", "video"
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
}

// --- Song Relationship ---
public class GeniusSongRelationship
{
    [JsonProperty("relationship_type")] public string RelationshipType { get; set; } = string.Empty;
    [JsonProperty("type")] public string Type { get; set; } = string.Empty; // Often same as relationship_type

    [JsonProperty("url")]
    public string? Url { get; set; } // URL for the relationship type page, e.g. /sample/interpolations

    [JsonProperty("songs")] public List<GeniusSong> Songs { get; set; } = []; // Recursive, can be simplified if needed
}

// --- Translation Song ---
public class GeniusTranslationSong
{
    [JsonProperty("api_path")] public string ApiPath { get; set; } = string.Empty;
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("language")] public string Language { get; set; } = string.Empty;
    [JsonProperty("lyrics_state")] public string LyricsState { get; set; } = string.Empty;
    [JsonProperty("path")] public string Path { get; set; } = string.Empty;
    [JsonProperty("title")] public string Title { get; set; } = string.Empty;
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;
}

// --- Verified Contributor ---
public class GeniusVerifiedContributor
{
    [JsonProperty("contributions")] public List<string> Contributions { get; set; } = [];
    [JsonProperty("artist")] public GeniusArtist? Artist { get; set; }
    [JsonProperty("user")] public GeniusUserReference? User { get; set; }
}

// --- For fetching a single song by ID ---
public class GeniusSongResponse
{
    [JsonProperty("meta")] public GeniusMeta Meta { get; set; } = new();
    [JsonProperty("response")] public GeniusSingleSongData? Response { get; set; }
}

public class GeniusSingleSongData
{
    [JsonProperty("song")] public GeniusSong? Song { get; set; }
}