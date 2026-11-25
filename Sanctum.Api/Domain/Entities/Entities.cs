namespace Sanctum.Api.Domain.Entities;

public enum ConsentLevel { Yes, Maybe, No, HardLimit }
public enum RoleDirection { Mutual, Giving, Receiving }
public enum TaskFrequency { OneTime, Daily }
public enum StarTier { Nothing = 0, SilverStar = 1, GoldStar = 2, SuperGoldStar = 3 }
public enum SafetyStatus { Green, Yellow, Red }
public enum SceneStatus { Draft, Scheduled, Active, Completed, Cancelled }

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DeviceToken> DeviceTokens { get; set; } = [];
    public List<DynamicMember> Memberships { get; set; } = [];
}

public class DeviceToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string DeviceName { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }
}

public class DynamicSpace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Our Sanctum";
    public SafetyStatus CurrentSafetyStatus { get; set; } = SafetyStatus.Green;
    public string? SafetyNote { get; set; }
    public DateTime SafetyUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DynamicMember> Members { get; set; } = [];
}

public class DynamicMember
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public DynamicSpace DynamicSpace { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string RoleTitle { get; set; } = string.Empty;
    public int PointBalance { get; set; } = 0;
    public bool CanManageRules { get; set; }
    public bool CanAssignTasks { get; set; }
    public bool CanApproveRewards { get; set; }
    public bool CanAssignCorrections { get; set; }
}

public class ConsentItem
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public Guid MemberId { get; set; }
    public string Category { get; set; } = "General";
    public string ActivityName { get; set; } = string.Empty;
    public RoleDirection Direction { get; set; } = RoleDirection.Mutual;
    public ConsentLevel Level { get; set; } = ConsentLevel.Maybe;

    // Granular Spreadsheet Scale: Exp (0-5, ?) and Interest (X, 1-5, !, ?)
    public string? GivingExperienceCode { get; set; }
    public string? GivingInterestCode { get; set; }
    public string? ReceivingExperienceCode { get; set; }
    public string? ReceivingInterestCode { get; set; }

    public string? ConditionsOrNotes { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class TaskItem
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public Guid AssignedToMemberId { get; set; }
    public Guid CreatedByMemberId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskFrequency Frequency { get; set; } = TaskFrequency.OneTime;
    public int PointValue { get; set; } = 10;
    public bool IsCompleted { get; set; }
    public DateOnly? LastCompletedDate { get; set; }
    public int CurrentStreak { get; set; } = 0;
    public bool RequiresDomApproval { get; set; }
    public bool IsPendingApproval { get; set; }
}

public class RewardItem
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PointCost { get; set; } = 50;
    public bool IsRepeatable { get; set; } = true;
    public bool IsRedeemed { get; set; }
    public bool IsPendingFulfillment { get; set; }
}

public class PointTransaction
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public Guid MemberId { get; set; }
    public int Delta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class RuleProtocol
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public int SortOrder { get; set; }
    public string Category { get; set; } = "General";
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class AftercareItem
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public string Category { get; set; } = "Comfort";
    public string Label { get; set; } = string.Empty;
    public bool IsRequested { get; set; }
}

public class CorrectionLog
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public Guid TargetMemberId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsPoolOption { get; set; }
    public bool IsResolved { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class JournalEntry
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public Guid AuthorMemberId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? MoodTag { get; set; }
    public bool IsPrivateToAuthor { get; set; }
    public string? PartnerComment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ScenePlan
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public Guid CreatedByMemberId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime? ScheduledForUtc { get; set; }
    public int EstimatedDurationMinutes { get; set; } = 60;
    public SceneStatus Status { get; set; } = SceneStatus.Draft;
    public string? HeadspaceGoal { get; set; }
    public string? PreSceneNotes { get; set; }
    public string? PostSceneDebrief { get; set; }
    public List<ScenePlanActivity> Activities { get; set; } = [];
    public List<SceneGearItem> GearChecklist { get; set; } = [];
}

public class ScenePlanActivity
{
    public Guid Id { get; set; }
    public Guid ScenePlanId { get; set; }
    public Guid ConsentItemId { get; set; }
    public bool IsWarmup { get; set; }
    public string? NegotiatedNotes { get; set; }
}

public class SceneGearItem
{
    public Guid Id { get; set; }
    public Guid ScenePlanId { get; set; }
    public string Category { get; set; } = "Safety";
    public string ItemName { get; set; } = string.Empty;
    public bool IsPackedOrReady { get; set; }
}

public class DailyStarRating
{
    public Guid Id { get; set; }
    public Guid DynamicSpaceId { get; set; }
    public Guid RecipientMemberId { get; set; }
    public Guid AwardedByMemberId { get; set; }
    public DateOnly Date { get; set; }
    public StarTier Tier { get; set; } = StarTier.Nothing;
    public int PointsAwarded { get; set; }
    public string? Note { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
