using Assistant.Net.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data;

public class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users { get; set; }
    public DbSet<GuildEntity> Guilds { get; set; }

    public DbSet<TrackEntity> Tracks { get; set; }
    public DbSet<GuildMusicSettingsEntity> GuildMusicSettings { get; set; }
    public DbSet<PlayHistoryEntity> PlayHistories { get; set; }
    public DbSet<PlaylistEntity> Playlists { get; set; }
    public DbSet<PlaylistItemEntity> PlaylistItems { get; set; }

    public DbSet<GameStatEntity> GameStats { get; set; }

    public DbSet<ReminderEntity> Reminders { get; set; }

    public DbSet<StarboardConfigEntity> StarboardConfigs { get; set; }
    public DbSet<StarredMessageEntity> StarredMessages { get; set; }
    public DbSet<StarVoteEntity> StarVotes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<UserEntity>(entity => { entity.Property(e => e.Id).HasColumnType("numeric(20,0)"); });
        modelBuilder.Entity<GuildEntity>(entity => { entity.Property(e => e.Id).HasColumnType("numeric(20,0)"); });

        modelBuilder.Entity<TrackEntity>(entity => { entity.HasIndex(e => e.Uri).IsUnique(); });

        modelBuilder.Entity<GuildMusicSettingsEntity>(entity =>
        {
            entity.Property(e => e.GuildId).HasColumnType("numeric(20,0)");
        });

        modelBuilder.Entity<PlayHistoryEntity>(entity =>
        {
            entity.HasOne(e => e.Requester)
                .WithMany()
                .HasForeignKey(e => e.RequestedBy)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Track)
                .WithMany()
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Guild)
                .WithMany()
                .HasForeignKey(e => e.GuildId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaylistEntity>(entity =>
        {
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.GuildId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<PlaylistItemEntity>(entity =>
        {
            entity.HasOne(e => e.Playlist)
                .WithMany(p => p.Items)
                .HasForeignKey(e => e.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Track)
                .WithMany()
                .HasForeignKey(e => e.TrackId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GameStatEntity>(entity =>
        {
            entity.Property(e => e.GuildId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.UserId).HasColumnType("numeric(20,0)");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReminderEntity>(entity =>
        {
            entity.Property(e => e.CreatorId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.TargetUserId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.GuildId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.ChannelId).HasColumnType("numeric(20,0)");

            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TargetUser)
                .WithMany()
                .HasForeignKey(e => e.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.TriggerTime, e.IsActive });
        });

        modelBuilder.Entity<StarboardConfigEntity>(entity =>
        {
            entity.Property(e => e.GuildId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.StarboardChannelId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.LogChannelId).HasColumnType("numeric(20,0)");
        });

        modelBuilder.Entity<StarredMessageEntity>(entity =>
        {
            entity.Property(e => e.GuildId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.OriginalChannelId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.OriginalMessageId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.AuthorId).HasColumnType("numeric(20,0)");
            entity.Property(e => e.StarboardMessageId).HasColumnType("numeric(20,0)");

            entity.HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StarVoteEntity>(entity =>
        {
            entity.Property(e => e.UserId).HasColumnType("numeric(20,0)");

            entity.HasOne(e => e.StarredMessage)
                .WithMany(sm => sm.Votes)
                .HasForeignKey(e => e.StarredMessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}