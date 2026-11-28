using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FootballGroup> FootballGroups => Set<FootballGroup>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchRegistration> MatchRegistrations => Set<MatchRegistration>();
    public DbSet<MonthlyMembership> MonthlyMemberships => Set<MonthlyMembership>();
    public DbSet<MembershipBillingPeriod> MembershipBillingPeriods => Set<MembershipBillingPeriod>();
    public DbSet<MembershipPayment> MembershipPayments => Set<MembershipPayment>();
    public DbSet<MatchFeePayment> MatchFeePayments => Set<MatchFeePayment>();
    public DbSet<PixCharge> PixCharges => Set<PixCharge>();
    public DbSet<TeamDraw> TeamDraws => Set<TeamDraw>();
    public DbSet<MatchTeam> MatchTeams => Set<MatchTeam>();
    public DbSet<MatchTeamMember> MatchTeamMembers => Set<MatchTeamMember>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
    public DbSet<PlayerMatchRating> PlayerMatchRatings => Set<PlayerMatchRating>();
    public DbSet<MatchResultRevision> MatchResultRevisions => Set<MatchResultRevision>();
    public DbSet<MatchLiveSheet> MatchLiveSheets => Set<MatchLiveSheet>();
    public DbSet<PlayerAward> PlayerAwards => Set<PlayerAward>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<NotificationCampaign> NotificationCampaigns => Set<NotificationCampaign>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationCooldown> NotificationCooldowns => Set<NotificationCooldown>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<FootballGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired();
            e.Property(x => x.SettingsJson).HasColumnType("text");
        });

        builder.Entity<GroupMembership>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
            e.HasOne(x => x.Group).WithMany(g => g.Memberships).HasForeignKey(x => x.GroupId);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        });

        builder.Entity<PlayerProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.SkillRating).HasPrecision(4, 2);
            e.Property(x => x.GoalkeeperRating).HasPrecision(4, 2);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.Bio).HasMaxLength(1000);
        });

        builder.Entity<Match>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GroupId, x.StartsAt });
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Location).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Group).WithMany(g => g.Matches).HasForeignKey(x => x.GroupId);
        });

        builder.Entity<MatchRegistration>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchId, x.PlayerProfileId }).IsUnique();
            e.HasIndex(x => new { x.MatchId, x.Status, x.WaitlistPosition });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.PriorityClass).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Match).WithMany(m => m.Registrations).HasForeignKey(x => x.MatchId);
            e.HasOne(x => x.PlayerProfile).WithMany().HasForeignKey(x => x.PlayerProfileId);
        });

        builder.Entity<MonthlyMembership>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GroupId, x.PlayerProfileId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
            e.HasOne(x => x.PlayerProfile).WithMany().HasForeignKey(x => x.PlayerProfileId);
        });

        builder.Entity<MembershipBillingPeriod>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GroupId, x.Year, x.Month }).IsUnique();
            e.Property(x => x.Label).HasMaxLength(40).IsRequired();
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
        });

        builder.Entity<MembershipPayment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MonthlyMembershipId, x.BillingPeriodId }).IsUnique();
            e.Property(x => x.Amount).HasPrecision(10, 2).IsConcurrencyToken();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsConcurrencyToken();
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.MonthlyMembership).WithMany().HasForeignKey(x => x.MonthlyMembershipId);
            e.HasOne(x => x.BillingPeriod).WithMany().HasForeignKey(x => x.BillingPeriodId);
        });

        builder.Entity<TeamDraw>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MatchId);
            e.Property(x => x.Seed).HasMaxLength(80).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.AlgorithmVersion).HasMaxLength(40);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
        });

        builder.Entity<MatchTeam>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.HasOne(x => x.TeamDraw).WithMany(d => d.Teams).HasForeignKey(x => x.TeamDrawId);
        });

        builder.Entity<MatchTeamMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchTeamId, x.PlayerProfileId }).IsUnique();
            e.Property(x => x.SkillRatingSnapshot).HasPrecision(4, 2);
            e.Property(x => x.GoalkeeperRatingSnapshot).HasPrecision(4, 2);
            e.HasOne(x => x.MatchTeam).WithMany(t => t.Members).HasForeignKey(x => x.MatchTeamId);
            e.HasOne(x => x.PlayerProfile).WithMany().HasForeignKey(x => x.PlayerProfileId);
        });

        builder.Entity<MatchResult>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MatchId).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
        });

        builder.Entity<MatchResultRevision>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchId, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.MatchId, x.IsCurrent });
            e.Property(x => x.SheetJson).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
        });

        builder.Entity<MatchLiveSheet>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MatchId).IsUnique();
            e.Property(x => x.SheetJson).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
        });

        builder.Entity<PlayerMatchRating>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchId, x.PlayerProfileId }).IsUnique();
            e.Property(x => x.Score).HasPrecision(4, 2);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
            e.HasOne(x => x.PlayerProfile).WithMany().HasForeignKey(x => x.PlayerProfileId);
        });

        builder.Entity<PlayerAward>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchId, x.Type }).IsUnique();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
            e.HasOne(x => x.PlayerProfile).WithMany().HasForeignKey(x => x.PlayerProfileId);
        });

        builder.Entity<AuditEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GroupId, x.CreatedAt });
            e.Property(x => x.Action).HasMaxLength(80).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("text");
        });

        builder.Entity<MatchFeePayment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MatchId, x.PlayerProfileId }).IsUnique();
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.Property(x => x.UpdatedAt).IsConcurrencyToken();
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.PaymentSource).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId);
            e.HasOne(x => x.PlayerProfile).WithMany().HasForeignKey(x => x.PlayerProfileId);
        });

        builder.Entity<PixCharge>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MpPaymentId).IsUnique();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.MembershipPaymentId, x.Status });
            e.HasIndex(x => new { x.MatchFeePaymentId, x.Status });
            e.HasIndex(x => new { x.GroupId, x.Status, x.ExpiresAt });
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsConcurrencyToken();
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.MpPaymentId).HasMaxLength(80).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.QrCode).HasColumnType("text").IsRequired();
            e.Property(x => x.QrCodeBase64).HasColumnType("text");
            e.Property(x => x.ProviderStatus).HasMaxLength(40);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
            e.HasOne(x => x.PlayerProfile).WithMany().HasForeignKey(x => x.PlayerProfileId);
            e.HasOne(x => x.MembershipPayment).WithMany().HasForeignKey(x => x.MembershipPaymentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.MatchFeePayment).WithMany().HasForeignKey(x => x.MatchFeePaymentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<UserSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.Property(x => x.SecurityStamp).HasMaxLength(256);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PushSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => new { x.UserId, x.IsActive });
            e.HasIndex(x => x.SessionId);
            e.Property(x => x.Endpoint).HasMaxLength(2048).IsRequired();
            e.Property(x => x.P256dh).HasMaxLength(256).IsRequired();
            e.Property(x => x.Auth).HasMaxLength(128).IsRequired();
            e.Property(x => x.VapidKeyId).HasMaxLength(16).IsRequired();
            e.Property(x => x.UserAgent).HasMaxLength(256);
            e.Property(x => x.DeviceLabel).HasMaxLength(60);
            e.Property(x => x.FailureReason).HasMaxLength(200);
        });

        builder.Entity<NotificationCampaign>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GroupId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.GroupId, x.Source, x.Category, x.CreatedAt });
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Audience).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Body).HasMaxLength(500).IsRequired();
            e.Property(x => x.ActionUrl).HasMaxLength(64).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
        });

        builder.Entity<UserNotification>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.GroupId, x.ReadAt });
            e.HasIndex(x => new { x.PushState, x.NextPushAt });
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.PushState).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Body).HasMaxLength(500).IsRequired();
            e.Property(x => x.ActionUrl).HasMaxLength(64).IsRequired();
            e.Property(x => x.DedupeKey).HasMaxLength(200);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
            e.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.DispatchVersion).IsConcurrencyToken();
        });

        builder.Entity<NotificationCooldown>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.Category });
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        builder.Entity<NotificationDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.NotificationId, x.SubscriptionId }).IsUnique();
            e.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Nickname).HasMaxLength(80);
        });
    }
}
