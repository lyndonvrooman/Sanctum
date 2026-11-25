using Microsoft.EntityFrameworkCore;
using Sanctum.Api.Domain.Entities;

namespace Sanctum.Api.Data;

public class SanctumDbContext(DbContextOptions<SanctumDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<DynamicSpace> DynamicSpaces => Set<DynamicSpace>();
    public DbSet<DynamicMember> DynamicMembers => Set<DynamicMember>();
    public DbSet<ConsentItem> ConsentItems => Set<ConsentItem>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<DailyStarRating> DailyStarRatings => Set<DailyStarRating>();
    public DbSet<RewardItem> Rewards => Set<RewardItem>();
    public DbSet<PointTransaction> PointTransactions => Set<PointTransaction>();
    public DbSet<RuleProtocol> Rules => Set<RuleProtocol>();
    public DbSet<AftercareItem> AftercareItems => Set<AftercareItem>();
    public DbSet<CorrectionLog> Corrections => Set<CorrectionLog>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<ScenePlan> ScenePlans => Set<ScenePlan>();
    public DbSet<ScenePlanActivity> ScenePlanActivities => Set<ScenePlanActivity>();
    public DbSet<SceneGearItem> SceneGearItems => Set<SceneGearItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<DeviceToken>().HasIndex(t => t.TokenHash).IsUnique();
    }
}

public static class SeedData
{
    public static async Task EnsureSeededAsync(SanctumDbContext db, string initialDomEmail, string initialSubEmail)
    {
        if (await db.DynamicSpaces.AnyAsync()) return;

        var domUser = new AppUser { Id = Guid.NewGuid(), Email = initialDomEmail.ToLowerInvariant(), DisplayName = "Sir" };
        var subUser = new AppUser { Id = Guid.NewGuid(), Email = initialSubEmail.Trim().ToLowerInvariant(), DisplayName = "Sub" };
        var space = new DynamicSpace { Id = Guid.NewGuid(), Name = "Sanctum" };

        var domMember = new DynamicMember
        {
            Id = Guid.NewGuid(),
            DynamicSpaceId = space.Id,
            UserId = domUser.Id,
            RoleTitle = "Sir",
            PointBalance = 150,
            CanManageRules = true,
            CanAssignTasks = true,
            CanApproveRewards = true,
            CanAssignCorrections = true
        };

        var subMember = new DynamicMember
        {
            Id = Guid.NewGuid(),
            DynamicSpaceId = space.Id,
            UserId = subUser.Id,
            RoleTitle = "Sub",
            PointBalance = 120,
            CanManageRules = false,
            CanAssignTasks = false,
            CanApproveRewards = false,
            CanAssignCorrections = false
        };

        db.Users.AddRange(domUser, subUser);
        db.DynamicSpaces.Add(space);
        db.DynamicMembers.AddRange(domMember, subMember);

        db.Tasks.AddRange(
            new TaskItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, AssignedToMemberId = subMember.Id, CreatedByMemberId = domMember.Id, Title = "Morning Check-In", Description = "Log morning mood and daily focus.", Frequency = TaskFrequency.Daily, PointValue = 10 },
            new TaskItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, AssignedToMemberId = subMember.Id, CreatedByMemberId = domMember.Id, Title = "Hydration & Wellness Goal", Description = "Meet daily water intake target.", Frequency = TaskFrequency.Daily, PointValue = 10 },
            new TaskItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, AssignedToMemberId = subMember.Id, CreatedByMemberId = domMember.Id, Title = "Evening Gratitude & Reflection", Description = "Write a brief reflection in the Journal.", Frequency = TaskFrequency.Daily, PointValue = 15 },
            new TaskItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, AssignedToMemberId = subMember.Id, CreatedByMemberId = domMember.Id, Title = "Import Yes/No/Maybe Checklist", Description = "Upload CSV or set initial boundaries.", Frequency = TaskFrequency.OneTime, PointValue = 50 }
        );

        db.Rewards.AddRange(
            new RewardItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Title = "Veto a Minor Task", Description = "Skip one non-critical daily task without breaking a streak.", PointCost = 40, IsRepeatable = true },
            new RewardItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Title = "30-Minute Back or Foot Massage", Description = "Uninterrupted massage from your partner.", PointCost = 60, IsRepeatable = true },
            new RewardItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Title = "Pick Dinner / Movie Night", Description = "Full control over dinner and evening entertainment.", PointCost = 75, IsRepeatable = true },
            new RewardItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Title = "Bedroom / Scene Control Ticket", Description = "Choose primary activity and pace for the next session.", PointCost = 150, IsRepeatable = true }
        );

        db.Rules.AddRange(
            new RuleProtocol { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, SortOrder = 1, Category = "Communication", Title = "Radical Honesty & Safeword Integrity", Details = "Safewords (Yellow and Red) override all other rules and protocols immediately." },
            new RuleProtocol { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, SortOrder = 2, Category = "Daily Structure", Title = "Baseline Self-Care & Hydration", Details = "Prioritize daily water intake, regular meals, and adequate rest." },
            new RuleProtocol { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, SortOrder = 3, Category = "Scene Etiquette", Title = "Pre-Scene Boundary Review", Details = "Review Hard Limits and Maybe notes before introducing new activities." }
        );

        db.AftercareItems.AddRange(
            new AftercareItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Category = "Hydration", Label = "Cold Water / Electrolytes" },
            new AftercareItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Category = "Hydration", Label = "Warm Tea or Hot Chocolate" },
            new AftercareItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Category = "Comfort", Label = "Weighted Blanket / Warm Layer" },
            new AftercareItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Category = "Comfort", Label = "Hold Me / Silent Cuddling" },
            new AftercareItem { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, Category = "Space", Label = "15 Min Alone to Decompress" }
        );

        db.Corrections.AddRange(
            new CorrectionLog { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, TargetMemberId = subMember.Id, Title = "Corner / Wall Time (10 Minutes)", Reason = "Quiet reflection without devices.", IsPoolOption = true },
            new CorrectionLog { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, TargetMemberId = subMember.Id, Title = "Write Lines (25x)", Reason = "Write an assigned protocol reminder 25 times in the Journal.", IsPoolOption = true },
            new CorrectionLog { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, TargetMemberId = subMember.Id, Title = "Extra Service Chore (0 pts)", Reason = "Complete an immediate household or service task for zero points.", IsPoolOption = true },
            new CorrectionLog { Id = Guid.NewGuid(), DynamicSpaceId = space.Id, TargetMemberId = subMember.Id, Title = "Early Screen Curfew", Reason = "Devices put away 1 hour earlier than normal.", IsPoolOption = true }
        );

        await db.SaveChangesAsync();
    }
}


