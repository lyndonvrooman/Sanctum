using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sanctum.Api.Auth;
using Sanctum.Api.Data;
using Sanctum.Api.Domain.Entities;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SanctumDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSignalR();

builder.Services.AddAuthentication("SanctumZeroTrust")
    .AddScheme<AuthenticationSchemeOptions, SanctumAuthHandler>("SanctumZeroTrust", _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SanctumDbContext>();
    await db.Database.EnsureCreatedAsync();
    var domEmail = builder.Configuration["Sanctum:DomEmail"]
                   ?? builder.Configuration["Sanctum:DefaultDevEmail"]
                   ?? "dom@kinkyfairytales.ca";
    var subEmail = builder.Configuration["Sanctum:SubEmail"]
                   ?? "sub@kinkyfairytales.ca";
    await SeedData.EnsureSeededAsync(db, domEmail, subEmail);
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<SanctumHub>("/hubs/sanctum").RequireAuthorization();

app.MapPost("/api/devices/claim", (ClaimPairingDto dto, IMemoryCache cache) =>
{
    if (!cache.TryGetValue($"pair:{dto.PairingCode}", out PairingPayload? payload) || payload is null)
        return Results.BadRequest("Invalid or expired pairing code.");

    cache.Remove($"pair:{dto.PairingCode}");
    return Results.Ok(payload);
});

var api = app.MapGroup("/api").RequireAuthorization();

// Current Session, Space Members & Role/Permission Settings
api.MapGet("/me", async (ClaimsPrincipal user, SanctumDbContext db, IHostEnvironment env) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers
        .Include(m => m.User)
        .Include(m => m.DynamicSpace)
        .FirstAsync(m => m.UserId == userId);

    var members = await db.DynamicMembers
        .Include(m => m.User)
        .Where(m => m.DynamicSpaceId == member.DynamicSpaceId)
        .Select(m => new
        {
            m.Id,
            m.RoleTitle,
            m.PointBalance,
            m.CanManageRules,
            m.CanAssignTasks,
            m.CanApproveRewards,
            m.CanAssignCorrections,
            m.User.Email,
            m.User.DisplayName
        })
        .ToListAsync();

    return Results.Ok(new
    {
        member.Id,
        member.RoleTitle,
        member.PointBalance,
        member.CanManageRules,
        member.CanAssignTasks,
        member.CanApproveRewards,
        member.CanAssignCorrections,
        IsDevelopment = env.IsDevelopment(),
        User = new { member.User.Email, member.User.DisplayName },
        Space = new
        {
            member.DynamicSpace.Id,
            member.DynamicSpace.Name,
            member.DynamicSpace.CurrentSafetyStatus,
            member.DynamicSpace.SafetyNote
        },
        Members = members
    });
});

api.MapPut("/space", async (UpdateSpaceDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var actor = await db.DynamicMembers.Include(m => m.DynamicSpace).FirstAsync(m => m.UserId == userId);
    if (!actor.CanManageRules) return Results.Forbid();

    if (!string.IsNullOrWhiteSpace(dto.Name))
        actor.DynamicSpace.Name = dto.Name.Trim();

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(actor.DynamicSpace);
});

api.MapPut("/members/{id:guid}", async (Guid id, UpdateMemberDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var actor = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var target = await db.DynamicMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == id);
    if (target is null) return Results.NotFound();

    // Any member can update their own RoleTitle/DisplayName; permission flags require CanManageRules
    if (actor.Id != target.Id && !actor.CanManageRules) return Results.Forbid();

    if (!string.IsNullOrWhiteSpace(dto.Email))
    {
        target.User.Email = dto.Email.Trim().ToLowerInvariant();
    }

    if (!string.IsNullOrWhiteSpace(dto.RoleTitle))
    {
        target.RoleTitle = dto.RoleTitle.Trim();
        target.User.DisplayName = dto.RoleTitle.Trim();
    }

    if (actor.CanManageRules)
    {
        target.CanManageRules = dto.CanManageRules;
        target.CanAssignTasks = dto.CanAssignTasks;
        target.CanApproveRewards = dto.CanApproveRewards;
        target.CanAssignCorrections = dto.CanAssignCorrections;
    }

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(target);
});
// Safeword Status Update
api.MapPost("/safety", async (SafetyUpdateDto dto, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var space = await db.DynamicSpaces.FirstAsync();
    space.CurrentSafetyStatus = dto.Status;
    space.SafetyNote = dto.Note;
    space.SafetyUpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    await hub.Clients.All.SendAsync("SafetyUpdated", new { space.CurrentSafetyStatus, space.SafetyNote });
    return Results.Ok(space);
});

// Daily Star Sticker Rating (Dom-awarded daily check-in)
api.MapGet("/daily-stars", async (SanctumDbContext db) =>
{
    var today = DateOnly.FromDateTime(DateTime.Now);
    var recent = await db.DailyStarRatings
        .OrderByDescending(s => s.Date)
        .Take(14)
        .ToListAsync();

    var todayRating = recent.FirstOrDefault(s => s.Date == today);
    return Results.Ok(new
    {
        Today = todayRating,
        History = recent
    });
});

api.MapPost("/daily-stars", async (AwardStarDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var dom = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!dom.CanAssignTasks) return Results.Forbid();

    var recipient = await db.DynamicMembers.FirstOrDefaultAsync(m => m.Id != dom.Id) ?? dom;
    var targetDate = dto.Date ?? DateOnly.FromDateTime(DateTime.Now);

    var pointsForTier = dto.Tier switch
    {
        StarTier.SilverStar => 5,
        StarTier.GoldStar => 15,
        StarTier.SuperGoldStar => 25,
        _ => 0
    };

    var tierLabel = dto.Tier switch
    {
        StarTier.SilverStar => "Silver Star (Basics)",
        StarTier.GoldStar => "Gold Star (Thrived)",
        StarTier.SuperGoldStar => "Super Gold Star (Amazing)",
        _ => "No Star (Existed)"
    };

    var existing = await db.DailyStarRatings
        .FirstOrDefaultAsync(s => s.RecipientMemberId == recipient.Id && s.Date == targetDate);

    int delta;
    if (existing is null)
    {
        existing = new DailyStarRating
        {
            Id = Guid.NewGuid(),
            DynamicSpaceId = dom.DynamicSpaceId,
            RecipientMemberId = recipient.Id,
            AwardedByMemberId = dom.Id,
            Date = targetDate,
            Tier = dto.Tier,
            PointsAwarded = pointsForTier,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()
        };
        db.DailyStarRatings.Add(existing);
        delta = pointsForTier;
    }
    else
    {
        delta = pointsForTier - existing.PointsAwarded;
        existing.Tier = dto.Tier;
        existing.PointsAwarded = pointsForTier;
        existing.AwardedByMemberId = dom.Id;
        if (dto.Note is not null) existing.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    if (delta != 0)
    {
        recipient.PointBalance += delta;
        db.PointTransactions.Add(new PointTransaction
        {
            Id = Guid.NewGuid(),
            DynamicSpaceId = recipient.DynamicSpaceId,
            MemberId = recipient.Id,
            Delta = delta,
            Reason = $"Daily Rating ({targetDate:MMM d}): {tierLabel}"
        });
    }

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(existing);
});

// Tasks (with Optional Dom Approval Queue & Delete)
api.MapGet("/tasks", async (SanctumDbContext db) =>
{
    var today = DateOnly.FromDateTime(DateTime.Now);
    var tasks = await db.Tasks.ToListAsync();
    return Results.Ok(tasks.Select(t => new
    {
        t.Id,
        t.Title,
        t.Description,
        t.Frequency,
        t.PointValue,
        t.CurrentStreak,
        t.RequiresDomApproval,
        t.IsPendingApproval,
        IsCompletedToday = t.Frequency == TaskFrequency.Daily ? t.LastCompletedDate == today : t.IsCompleted
    }));
});

api.MapPost("/tasks", async (CreateTaskDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var creator = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var target = await db.DynamicMembers.FirstOrDefaultAsync(m => m.Id != creator.Id) ?? creator;

    var task = new TaskItem
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = creator.DynamicSpaceId,
        AssignedToMemberId = target.Id,
        CreatedByMemberId = creator.Id,
        Title = dto.Title.Trim(),
        Description = dto.Description,
        Frequency = dto.Frequency,
        PointValue = dto.PointValue,
        RequiresDomApproval = dto.RequiresDomApproval
    };
    db.Tasks.Add(task);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(task);
});

api.MapDelete("/tasks/{id:guid}", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!member.CanAssignTasks) return Results.Forbid();

    var task = await db.Tasks.FindAsync(id);
    if (task is null) return Results.NotFound();
    db.Tasks.Remove(task);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.NoContent();
});

api.MapPost("/tasks/{id:guid}/complete", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var task = await db.Tasks.FindAsync(id);
    if (task is null) return Results.NotFound();

    var today = DateOnly.FromDateTime(DateTime.Now);
    if (task.Frequency == TaskFrequency.Daily)
    {
        if (task.LastCompletedDate == today || task.IsPendingApproval) return Results.BadRequest("Already completed or pending approval.");
        if (task.RequiresDomApproval && !member.CanAssignTasks)
        {
            task.IsPendingApproval = true;
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("StateChanged");
            return Results.Ok(new { member.PointBalance, pending = true });
        }
        task.CurrentStreak = task.LastCompletedDate == today.AddDays(-1) ? task.CurrentStreak + 1 : 1;
        task.LastCompletedDate = today;
    }
    else
    {
        if (task.IsCompleted || task.IsPendingApproval) return Results.BadRequest("Already completed or pending approval.");
        if (task.RequiresDomApproval && !member.CanAssignTasks)
        {
            task.IsPendingApproval = true;
            await db.SaveChangesAsync();
            await hub.Clients.All.SendAsync("StateChanged");
            return Results.Ok(new { member.PointBalance, pending = true });
        }
        task.IsCompleted = true;
    }

    member.PointBalance += task.PointValue;
    db.PointTransactions.Add(new PointTransaction
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = member.DynamicSpaceId,
        MemberId = member.Id,
        Delta = task.PointValue,
        Reason = $"Completed: {task.Title}"
    });

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(new { member.PointBalance, pending = false });
});

api.MapPost("/tasks/{id:guid}/approve", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var approver = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!approver.CanAssignTasks) return Results.Forbid();

    var task = await db.Tasks.FindAsync(id);
    if (task is null) return Results.NotFound();

    var assignee = await db.DynamicMembers.FirstOrDefaultAsync(m => m.Id == task.AssignedToMemberId) ?? approver;
    var today = DateOnly.FromDateTime(DateTime.Now);

    task.IsPendingApproval = false;
    if (task.Frequency == TaskFrequency.Daily)
    {
        task.CurrentStreak = task.LastCompletedDate == today.AddDays(-1) ? task.CurrentStreak + 1 : 1;
        task.LastCompletedDate = today;
    }
    else
    {
        task.IsCompleted = true;
    }

    assignee.PointBalance += task.PointValue;
    db.PointTransactions.Add(new PointTransaction
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = assignee.DynamicSpaceId,
        MemberId = assignee.Id,
        Delta = task.PointValue,
        Reason = $"Approved: {task.Title}"
    });

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(task);
});

api.MapPost("/tasks/{id:guid}/reject", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var approver = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!approver.CanAssignTasks) return Results.Forbid();

    var task = await db.Tasks.FindAsync(id);
    if (task is null) return Results.NotFound();

    task.IsPendingApproval = false;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(task);
});

// Consent (Yes / No / Maybe + Detailed Top/Bottom + Experience + Edit/Delete + CSV Import)
api.MapGet("/consent", async (SanctumDbContext db) =>
    Results.Ok(await db.ConsentItems.OrderBy(c => c.Category).ThenBy(c => c.ActivityName).ToListAsync()));

api.MapPost("/consent", async (CreateConsentDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var allMembers = await db.DynamicMembers.Where(m => m.DynamicSpaceId == member.DynamicSpaceId).ToListAsync();

    var category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim();
    var activityName = dto.ActivityName.Trim();
    ConsentItem? createdForActor = null;

    foreach (var m in allMembers)
    {
        var isActor = m.Id == member.Id;
        var existingItem = await db.ConsentItems.FirstOrDefaultAsync(c =>
            c.MemberId == m.Id &&
            c.Category.ToLower() == category.ToLower() &&
            c.ActivityName.ToLower() == activityName.ToLower());

        if (existingItem is not null)
        {
            if (isActor)
            {
                existingItem.Level = dto.Level;
                existingItem.GivingExperienceCode = CleanCode(dto.GivingExperienceCode);
                existingItem.GivingInterestCode = CleanCode(dto.GivingInterestCode);
                existingItem.ReceivingExperienceCode = CleanCode(dto.ReceivingExperienceCode);
                existingItem.ReceivingInterestCode = CleanCode(dto.ReceivingInterestCode);
                if (!string.IsNullOrWhiteSpace(dto.Notes)) existingItem.ConditionsOrNotes = dto.Notes.Trim();
                existingItem.UpdatedAtUtc = DateTime.UtcNow;
                createdForActor = existingItem;
            }
            continue;
        }

        var item = new ConsentItem
        {
            Id = Guid.NewGuid(),
            DynamicSpaceId = member.DynamicSpaceId,
            MemberId = m.Id,
            Category = category,
            ActivityName = activityName,
            Level = isActor ? dto.Level : ConsentLevel.Maybe,
            GivingExperienceCode = isActor ? CleanCode(dto.GivingExperienceCode) : null,
            GivingInterestCode = isActor ? CleanCode(dto.GivingInterestCode) : null,
            ReceivingExperienceCode = isActor ? CleanCode(dto.ReceivingExperienceCode) : null,
            ReceivingInterestCode = isActor ? CleanCode(dto.ReceivingInterestCode) : null,
            ConditionsOrNotes = isActor && !string.IsNullOrWhiteSpace(dto.Notes) ? dto.Notes.Trim() : null
        };
        db.ConsentItems.Add(item);
        if (isActor) createdForActor = item;
    }

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(createdForActor);
});

api.MapPut("/consent/{id:guid}", async (Guid id, UpdateConsentDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var item = await db.ConsentItems.FindAsync(id);
    if (item is null) return Results.NotFound();
    if (item.MemberId != member.Id) return Results.Forbid();

    var oldCat = item.Category;
    var oldName = item.ActivityName;
    var newCat = string.IsNullOrWhiteSpace(dto.Category) ? item.Category : dto.Category.Trim();
    var newName = string.IsNullOrWhiteSpace(dto.ActivityName) ? item.ActivityName : dto.ActivityName.Trim();

    if (!oldCat.Equals(newCat, StringComparison.OrdinalIgnoreCase) || !oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
    {
        var siblings = await db.ConsentItems
            .Where(c => c.DynamicSpaceId == item.DynamicSpaceId &&
                        c.Id != item.Id &&
                        c.Category.ToLower() == oldCat.ToLower() &&
                        c.ActivityName.ToLower() == oldName.ToLower())
            .ToListAsync();

        foreach (var sib in siblings)
        {
            sib.Category = newCat;
            sib.ActivityName = newName;
        }
    }

    item.Category = newCat;
    item.ActivityName = newName;
    item.Level = dto.Level;
    item.GivingInterestCode = CleanCode(dto.GivingInterestCode);
    item.GivingExperienceCode = CleanCode(dto.GivingExperienceCode);
    item.ReceivingInterestCode = CleanCode(dto.ReceivingInterestCode);
    item.ReceivingExperienceCode = CleanCode(dto.ReceivingExperienceCode);
    item.ConditionsOrNotes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
    item.UpdatedAtUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(item);
});

api.MapDelete("/consent/{id:guid}", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var item = await db.ConsentItems.FindAsync(id);
    if (item is null) return Results.NotFound();
    if (item.MemberId != member.Id) return Results.Forbid();

    var allMatching = await db.ConsentItems
        .Where(c => c.DynamicSpaceId == item.DynamicSpaceId &&
                    c.Category.ToLower() == item.Category.ToLower() &&
                    c.ActivityName.ToLower() == item.ActivityName.ToLower())
        .ToListAsync();

    db.ConsentItems.RemoveRange(allMatching);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.NoContent();
});

api.MapPost("/consent/import-csv", async (ImportCsvDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);

    var allSpaceMembers = await db.DynamicMembers
        .Where(m => m.DynamicSpaceId == member.DynamicSpaceId)
        .ToListAsync();

    var allSpaceConsent = await db.ConsentItems
        .Where(c => c.DynamicSpaceId == member.DynamicSpaceId)
        .ToListAsync();

    var existing = allSpaceConsent
        .Where(c => c.MemberId == member.Id)
        .ToDictionary(c => $"{c.Category}|{c.ActivityName}".ToLowerInvariant());

    var existingPartnerKeys = allSpaceConsent
        .GroupBy(c => c.MemberId)
        .ToDictionary(
            g => g.Key,
            g => g.Select(c => $"{c.Category}|{c.ActivityName}".ToLowerInvariant()).ToHashSet()
        );

    var lines = dto.CsvContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    var currentCategory = "General";
    var importedCount = 0;

    foreach (var rawLine in lines)
    {
        var cols = ParseCsvLine(rawLine);
        if (cols.Count == 0 || string.IsNullOrWhiteSpace(cols[0])) continue;

        var name = cols[0].Trim();
        if (name.StartsWith("Legend", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Thorough Sex", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Name:", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("To fill out", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("This list", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("If you want", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Role", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Activity", StringComparison.OrdinalIgnoreCase))
            continue;

        if (cols.Skip(1).All(string.IsNullOrWhiteSpace))
        {
            currentCategory = name;
            continue;
        }

        var expTop = CleanCode(cols.ElementAtOrDefault(1));
        var intTop = CleanCode(cols.ElementAtOrDefault(2));
        var expBot = CleanCode(cols.ElementAtOrDefault(3));
        var intBot = CleanCode(cols.ElementAtOrDefault(4));
        var notes = cols.ElementAtOrDefault(5)?.Trim();

        var level = DeriveOverallLevel(intTop, intBot);
        var key = $"{currentCategory}|{name}".ToLowerInvariant();

        if (existing.TryGetValue(key, out var item))
        {
            item.GivingExperienceCode = expTop;
            item.GivingInterestCode = intTop;
            item.ReceivingExperienceCode = expBot;
            item.ReceivingInterestCode = intBot;
            item.Level = level;
            if (!string.IsNullOrWhiteSpace(notes)) item.ConditionsOrNotes = notes;
            item.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            db.ConsentItems.Add(new ConsentItem
            {
                Id = Guid.NewGuid(),
                DynamicSpaceId = member.DynamicSpaceId,
                MemberId = member.Id,
                Category = currentCategory,
                ActivityName = name,
                Direction = !string.IsNullOrWhiteSpace(intBot) && string.IsNullOrWhiteSpace(intTop)
                    ? RoleDirection.Receiving
                    : !string.IsNullOrWhiteSpace(intTop) && string.IsNullOrWhiteSpace(intBot)
                        ? RoleDirection.Giving
                        : RoleDirection.Mutual,
                Level = level,
                GivingExperienceCode = expTop,
                GivingInterestCode = intTop,
                ReceivingExperienceCode = expBot,
                ReceivingInterestCode = intBot,
                ConditionsOrNotes = string.IsNullOrWhiteSpace(notes) ? null : notes
            });
        }

        foreach (var other in allSpaceMembers.Where(m => m.Id != member.Id))
        {
            var partnerSet = existingPartnerKeys.GetValueOrDefault(other.Id) ?? [];
            if (partnerSet.Add(key))
            {
                db.ConsentItems.Add(new ConsentItem
                {
                    Id = Guid.NewGuid(),
                    DynamicSpaceId = member.DynamicSpaceId,
                    MemberId = other.Id,
                    Category = currentCategory,
                    ActivityName = name,
                    Level = ConsentLevel.Maybe
                });
            }
        }
        importedCount++;
    }

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(new { importedCount });
});

// Rewards, Fulfillment, CRUD & Point Ledger
api.MapGet("/rewards", async (SanctumDbContext db) =>
    Results.Ok(await db.Rewards.OrderBy(r => r.PointCost).ThenBy(r => r.Title).ToListAsync()));

api.MapPost("/rewards", async (UpsertRewardDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!member.CanApproveRewards) return Results.Forbid();

    var reward = new RewardItem
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = member.DynamicSpaceId,
        Title = dto.Title.Trim(),
        Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
        PointCost = dto.PointCost > 0 ? dto.PointCost : 10,
        IsRepeatable = dto.IsRepeatable
    };
    db.Rewards.Add(reward);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(reward);
});

api.MapPut("/rewards/{id:guid}", async (Guid id, UpsertRewardDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!member.CanApproveRewards) return Results.Forbid();

    var reward = await db.Rewards.FindAsync(id);
    if (reward is null) return Results.NotFound();

    reward.Title = string.IsNullOrWhiteSpace(dto.Title) ? reward.Title : dto.Title.Trim();
    reward.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
    reward.PointCost = dto.PointCost > 0 ? dto.PointCost : reward.PointCost;
    reward.IsRepeatable = dto.IsRepeatable;

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(reward);
});

api.MapDelete("/rewards/{id:guid}", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!member.CanApproveRewards) return Results.Forbid();

    var reward = await db.Rewards.FindAsync(id);
    if (reward is null) return Results.NotFound();

    db.Rewards.Remove(reward);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.NoContent();
});

api.MapPost("/rewards/{id:guid}/redeem", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var reward = await db.Rewards.FindAsync(id);
    if (reward is null) return Results.NotFound();
    if (member.PointBalance < reward.PointCost) return Results.BadRequest("Insufficient points.");

    member.PointBalance -= reward.PointCost;
    reward.IsPendingFulfillment = true;
    if (!reward.IsRepeatable) reward.IsRedeemed = true;

    db.PointTransactions.Add(new PointTransaction
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = member.DynamicSpaceId,
        MemberId = member.Id,
        Delta = -reward.PointCost,
        Reason = $"Redeemed: {reward.Title}"
    });

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(new { member.PointBalance });
});

api.MapPost("/rewards/{id:guid}/fulfill", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var approver = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!approver.CanApproveRewards) return Results.Forbid();

    var reward = await db.Rewards.FindAsync(id);
    if (reward is null) return Results.NotFound();

    reward.IsPendingFulfillment = false;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(reward);
});

api.MapGet("/transactions", async (SanctumDbContext db) =>
{
    var members = await db.DynamicMembers.ToDictionaryAsync(m => m.Id, m => m.RoleTitle);
    var txs = await db.PointTransactions
        .OrderByDescending(t => t.CreatedAtUtc)
        .Take(30)
        .ToListAsync();

    return Results.Ok(txs.Select(t => new
    {
        t.Id,
        t.Delta,
        t.Reason,
        t.CreatedAtUtc,
        MemberRole = members.TryGetValue(t.MemberId, out var role) ? role : "Partner"
    }));
});

// Corrections & Punishment Jar (Full CRUD)
api.MapGet("/corrections", async (SanctumDbContext db) =>
    Results.Ok(await db.Corrections.OrderByDescending(c => c.CreatedAtUtc).ToListAsync()));

api.MapPost("/corrections", async (CreateCorrectionDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var creator = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!creator.CanAssignCorrections) return Results.Forbid();

    var target = await db.DynamicMembers.FirstOrDefaultAsync(m => m.Id != creator.Id) ?? creator;

    var item = new CorrectionLog
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = creator.DynamicSpaceId,
        TargetMemberId = target.Id,
        Title = dto.Title.Trim(),
        Reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim(),
        IsPoolOption = dto.IsPoolOption,
        IsResolved = false
    };
    db.Corrections.Add(item);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(item);
});

api.MapPut("/corrections/{id:guid}", async (Guid id, CreateCorrectionDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var creator = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!creator.CanAssignCorrections) return Results.Forbid();

    var item = await db.Corrections.FindAsync(id);
    if (item is null) return Results.NotFound();

    item.Title = string.IsNullOrWhiteSpace(dto.Title) ? item.Title : dto.Title.Trim();
    item.Reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(item);
});

api.MapDelete("/corrections/{id:guid}", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var creator = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!creator.CanAssignCorrections) return Results.Forbid();

    var item = await db.Corrections.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.Corrections.Remove(item);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.NoContent();
});

api.MapPost("/corrections/draw", async (ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var actor = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var target = await db.DynamicMembers.FirstOrDefaultAsync(m => !m.CanAssignCorrections) ?? actor;

    var pool = await db.Corrections.Where(c => c.IsPoolOption).ToListAsync();
    if (pool.Count == 0) return Results.BadRequest("Punishment Jar is empty.");

    var picked = pool[Random.Shared.Next(pool.Count)];
    var drawn = new CorrectionLog
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = actor.DynamicSpaceId,
        TargetMemberId = target.Id,
        Title = picked.Title,
        Reason = string.IsNullOrWhiteSpace(picked.Reason) ? "Drawn from Punishment Jar" : $"Drawn from Jar: {picked.Reason}",
        IsPoolOption = false,
        IsResolved = false
    };
    db.Corrections.Add(drawn);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(drawn);
});

api.MapPost("/corrections/{id:guid}/resolve", async (Guid id, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var item = await db.Corrections.FindAsync(id);
    if (item is null) return Results.NotFound();
    item.IsResolved = true;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(item);
});

// Scene & Session Planner
api.MapGet("/scenes", async (SanctumDbContext db) =>
{
    var scenes = await db.ScenePlans
        .Include(s => s.Activities)
        .Include(s => s.GearChecklist)
        .OrderByDescending(s => s.Status == SceneStatus.Active)
        .ThenBy(s => s.ScheduledForUtc)
        .ToListAsync();

    var consentLookup = await db.ConsentItems.ToDictionaryAsync(c => c.Id, c => new { c.ActivityName, c.Level, c.ConditionsOrNotes });

    return Results.Ok(scenes.Select(s => new
    {
        s.Id,
        s.Title,
        s.ScheduledForUtc,
        s.EstimatedDurationMinutes,
        s.Status,
        s.HeadspaceGoal,
        s.PreSceneNotes,
        s.PostSceneDebrief,
        Activities = s.Activities.Select(a => new
        {
            a.Id,
            a.ConsentItemId,
            ActivityName = consentLookup.TryGetValue(a.ConsentItemId, out var c) ? c.ActivityName : "Activity",
            ConsentLevel = consentLookup.TryGetValue(a.ConsentItemId, out var c2) ? c2.Level : ConsentLevel.Yes,
            a.IsWarmup,
            a.NegotiatedNotes
        }),
        GearChecklist = s.GearChecklist.OrderBy(g => g.Category).ThenBy(g => g.ItemName)
    }));
});

api.MapPost("/scenes", async (CreateSceneDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var creator = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);

    var scene = new ScenePlan
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = creator.DynamicSpaceId,
        CreatedByMemberId = creator.Id,
        Title = dto.Title,
        HeadspaceGoal = dto.HeadspaceGoal,
        EstimatedDurationMinutes = dto.EstimatedDurationMinutes > 0 ? dto.EstimatedDurationMinutes : 60,
        Status = SceneStatus.Scheduled
    };

    if (dto.IncludeSafetyEssentials)
    {
        scene.GearChecklist.AddRange([
            new SceneGearItem { Id = Guid.NewGuid(), ScenePlanId = scene.Id, Category = "Safety", ItemName = "EMT / Safety Shears" },
            new SceneGearItem { Id = Guid.NewGuid(), ScenePlanId = scene.Id, Category = "Aftercare", ItemName = "Cold Water / Electrolytes" },
            new SceneGearItem { Id = Guid.NewGuid(), ScenePlanId = scene.Id, Category = "Aftercare", ItemName = "Warm Blanket / Towel" }
        ]);
    }

    db.ScenePlans.Add(scene);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(scene);
});

api.MapPost("/scenes/{id:guid}/activities", async (Guid id, AddSceneActivityDto dto, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var scene = await db.ScenePlans.FindAsync(id);
    if (scene is null) return Results.NotFound();

    var consent = await db.ConsentItems.FindAsync(dto.ConsentItemId);
    if (consent is null) return Results.NotFound("Consent item not found.");
    if (consent.Level is ConsentLevel.HardLimit or ConsentLevel.No)
        return Results.BadRequest("Cannot add a Hard Limit or No activity to a scene plan.");

    db.ScenePlanActivities.Add(new ScenePlanActivity
    {
        Id = Guid.NewGuid(),
        ScenePlanId = scene.Id,
        ConsentItemId = consent.Id,
        IsWarmup = dto.IsWarmup,
        NegotiatedNotes = consent.ConditionsOrNotes
    });

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok();
});

api.MapPost("/scenes/{id:guid}/gear", async (Guid id, AddSceneGearDto dto, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var scene = await db.ScenePlans.FindAsync(id);
    if (scene is null) return Results.NotFound();

    db.SceneGearItems.Add(new SceneGearItem
    {
        Id = Guid.NewGuid(),
        ScenePlanId = scene.Id,
        Category = string.IsNullOrWhiteSpace(dto.Category) ? "Gear" : dto.Category,
        ItemName = dto.ItemName
    });

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok();
});

api.MapPost("/scenes/gear/{gearId:guid}/toggle", async (Guid gearId, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var gear = await db.SceneGearItems.FindAsync(gearId);
    if (gear is null) return Results.NotFound();
    gear.IsPackedOrReady = !gear.IsPackedOrReady;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(gear);
});

api.MapPost("/scenes/{id:guid}/status", async (Guid id, UpdateSceneStatusDto dto, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var scene = await db.ScenePlans.FindAsync(id);
    if (scene is null) return Results.NotFound();
    scene.Status = dto.Status;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(scene);
});

// Rules & Protocols (Full CRUD)
api.MapGet("/rules", async (SanctumDbContext db) =>
    Results.Ok(await db.Rules.OrderBy(r => r.SortOrder).ThenBy(r => r.Title).ToListAsync()));

api.MapPost("/rules", async (UpsertRuleDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!member.CanManageRules) return Results.Forbid();

    var maxOrder = await db.Rules.AnyAsync() ? await db.Rules.MaxAsync(r => r.SortOrder) : 0;
    var rule = new RuleProtocol
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = member.DynamicSpaceId,
        SortOrder = maxOrder + 1,
        Category = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category.Trim(),
        Title = dto.Title.Trim(),
        Details = dto.Details?.Trim() ?? string.Empty,
        IsActive = true
    };
    db.Rules.Add(rule);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(rule);
});

api.MapPut("/rules/{id:guid}", async (Guid id, UpsertRuleDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!member.CanManageRules) return Results.Forbid();

    var rule = await db.Rules.FindAsync(id);
    if (rule is null) return Results.NotFound();

    rule.Category = string.IsNullOrWhiteSpace(dto.Category) ? rule.Category : dto.Category.Trim();
    rule.Title = string.IsNullOrWhiteSpace(dto.Title) ? rule.Title : dto.Title.Trim();
    rule.Details = dto.Details?.Trim() ?? string.Empty;
    rule.IsActive = dto.IsActive;

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(rule);
});

api.MapDelete("/rules/{id:guid}", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    if (!member.CanManageRules) return Results.Forbid();

    var rule = await db.Rules.FindAsync(id);
    if (rule is null) return Results.NotFound();

    db.Rules.Remove(rule);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.NoContent();
});

// Aftercare Quick-Request Chips (Full CRUD)
api.MapGet("/aftercare", async (SanctumDbContext db) =>
    Results.Ok(await db.AftercareItems.OrderBy(a => a.Category).ThenBy(a => a.Label).ToListAsync()));

api.MapPost("/aftercare", async (UpsertAftercareDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);

    var item = new AftercareItem
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = member.DynamicSpaceId,
        Category = string.IsNullOrWhiteSpace(dto.Category) ? "Comfort" : dto.Category.Trim(),
        Label = dto.Label.Trim(),
        IsRequested = false
    };
    db.AftercareItems.Add(item);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(item);
});

api.MapPut("/aftercare/{id:guid}", async (Guid id, UpsertAftercareDto dto, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var item = await db.AftercareItems.FindAsync(id);
    if (item is null) return Results.NotFound();

    item.Category = string.IsNullOrWhiteSpace(dto.Category) ? item.Category : dto.Category.Trim();
    item.Label = string.IsNullOrWhiteSpace(dto.Label) ? item.Label : dto.Label.Trim();

    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(item);
});

api.MapDelete("/aftercare/{id:guid}", async (Guid id, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var item = await db.AftercareItems.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.AftercareItems.Remove(item);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.NoContent();
});

api.MapPost("/aftercare/{id:guid}/toggle", async (Guid id, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var item = await db.AftercareItems.FindAsync(id);
    if (item is null) return Results.NotFound();
    item.IsRequested = !item.IsRequested;
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(item);
});
// Journal (with Private Toggle, Author Attribution, Partner Comments & Delete)
api.MapGet("/journal", async (ClaimsPrincipal user, SanctumDbContext db) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var roleLookup = await db.DynamicMembers
        .Where(m => m.DynamicSpaceId == member.DynamicSpaceId)
        .ToDictionaryAsync(m => m.Id, m => m.RoleTitle);

    var entries = await db.JournalEntries
        .Where(j => j.DynamicSpaceId == member.DynamicSpaceId && (!j.IsPrivateToAuthor || j.AuthorMemberId == member.Id))
        .OrderByDescending(j => j.CreatedAtUtc)
        .Take(50)
        .ToListAsync();

    return Results.Ok(entries.Select(j => new
    {
        j.Id,
        j.AuthorMemberId,
        AuthorRole = roleLookup.TryGetValue(j.AuthorMemberId, out var role) ? role : "Partner",
        IsMine = j.AuthorMemberId == member.Id,
        j.Content,
        j.MoodTag,
        j.IsPrivateToAuthor,
        j.PartnerComment,
        j.CreatedAtUtc
    }));
});

api.MapPost("/journal", async (CreateJournalDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var entry = new JournalEntry
    {
        Id = Guid.NewGuid(),
        DynamicSpaceId = member.DynamicSpaceId,
        AuthorMemberId = member.Id,
        Content = dto.Content.Trim(),
        MoodTag = string.IsNullOrWhiteSpace(dto.MoodTag) ? null : dto.MoodTag.Trim(),
        IsPrivateToAuthor = dto.IsPrivateToAuthor
    };
    db.JournalEntries.Add(entry);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(entry);
});

api.MapPost("/journal/{id:guid}/comment", async (Guid id, CommentJournalDto dto, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var entry = await db.JournalEntries.FindAsync(id);
    if (entry is null) return Results.NotFound();
    if (entry.IsPrivateToAuthor && entry.AuthorMemberId != member.Id) return Results.Forbid();

    entry.PartnerComment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.Ok(entry);
});

api.MapDelete("/journal/{id:guid}", async (Guid id, ClaimsPrincipal user, SanctumDbContext db, IHubContext<SanctumHub> hub) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var member = await db.DynamicMembers.FirstAsync(m => m.UserId == userId);
    var entry = await db.JournalEntries.FindAsync(id);
    if (entry is null) return Results.NotFound();
    if (entry.AuthorMemberId != member.Id) return Results.Forbid();

    db.JournalEntries.Remove(entry);
    await db.SaveChangesAsync();
    await hub.Clients.All.SendAsync("StateChanged");
    return Results.NoContent();
});
// QR Code Device Pairing Session Generator
api.MapPost("/devices/pairing-session", async (CreateDeviceDto dto, ClaimsPrincipal user, SanctumDbContext db, IConfiguration config, IMemoryCache cache) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    db.DeviceTokens.Add(new DeviceToken
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DeviceName = string.IsNullOrWhiteSpace(dto.DeviceName) ? "Android Device" : dto.DeviceName,
        TokenHash = hash
    });
    await db.SaveChangesAsync();

    var pairingCode = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    var publicUrl = config["Sanctum:PublicUrl"] ?? "https://sanctum.kinkyfairytales.ca";

    cache.Set($"pair:{pairingCode}", new PairingPayload(
        publicUrl,
        config["Cloudflare:ServiceClientId"] ?? "",
        config["Cloudflare:ServiceClientSecret"] ?? "",
        rawToken
    ), TimeSpan.FromMinutes(3));

    return Results.Ok(new { serverUrl = publicUrl, pairingCode });
});

app.MapFallbackToFile("index.html");
app.Run();

static string? CleanCode(string? val) => string.IsNullOrWhiteSpace(val) ? null : val.Trim().ToUpperInvariant();

static ConsentLevel MapInterestCode(string? code) => code?.Trim().ToUpperInvariant() switch
{
    "X" => ConsentLevel.HardLimit,
    "1" => ConsentLevel.No,
    "3" or "4" or "5" or "!" => ConsentLevel.Yes,
    _ => ConsentLevel.Maybe
};

static ConsentLevel DeriveOverallLevel(string? topCode, string? botCode)
{
    var levels = new[] { topCode, botCode }
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .Select(MapInterestCode)
        .ToList();

    if (levels.Count == 0) return ConsentLevel.Maybe;
    if (levels.Contains(ConsentLevel.HardLimit)) return ConsentLevel.HardLimit;
    if (levels.Contains(ConsentLevel.Yes)) return ConsentLevel.Yes;
    if (levels.Contains(ConsentLevel.Maybe)) return ConsentLevel.Maybe;
    return ConsentLevel.No;
}

static List<string> ParseCsvLine(string line)
{
    var result = new List<string>();
    var current = new StringBuilder();
    bool inQuotes = false;
    foreach (char c in line)
    {
        if (c == '"') inQuotes = !inQuotes;
        else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
        else current.Append(c);
    }
    result.Add(current.ToString());
    return result;
}

public record SafetyUpdateDto(SafetyStatus Status, string? Note);
public record AwardStarDto(StarTier Tier, string? Note = null, DateOnly? Date = null);
public record CreateTaskDto(string Title, string? Description, TaskFrequency Frequency, int PointValue, bool RequiresDomApproval = false);
public record CreateConsentDto(string Category, string ActivityName, ConsentLevel Level, string? Notes, string? GivingExperienceCode = null, string? GivingInterestCode = null, string? ReceivingExperienceCode = null, string? ReceivingInterestCode = null);
public record UpdateConsentDto(
    string? Category,
    string? ActivityName,
    ConsentLevel Level,
    string? Notes,
    string? GivingInterestCode = null,
    string? GivingExperienceCode = null,
    string? ReceivingInterestCode = null,
    string? ReceivingExperienceCode = null);
public record ImportCsvDto(string CsvContent);
public record UpsertRuleDto(string Category, string Title, string? Details, bool IsActive = true);
public record CreateCorrectionDto(string Title, string? Reason, bool IsPoolOption);
public record CreateSceneDto(string Title, string? HeadspaceGoal, int EstimatedDurationMinutes, bool IncludeSafetyEssentials);
public record AddSceneActivityDto(Guid ConsentItemId, bool IsWarmup);
public record AddSceneGearDto(string Category, string ItemName);
public record UpdateSceneStatusDto(SceneStatus Status);
public record CreateJournalDto(string Content, string? MoodTag, bool IsPrivateToAuthor);
public record CreateDeviceDto(string DeviceName);
public record ClaimPairingDto(string PairingCode);
public record PairingPayload(string ServerUrl, string CfClientId, string CfClientSecret, string DeviceToken);
public class SanctumHub : Hub { }

public record UpsertRewardDto(string Title, string? Description, int PointCost, bool IsRepeatable = true);
public record UpsertAftercareDto(string Category, string Label);

public record UpdateSpaceDto(string Name);
public record UpdateMemberDto(string? Email, string RoleTitle, bool CanManageRules, bool CanAssignTasks, bool CanApproveRewards, bool CanAssignCorrections);
public record CommentJournalDto(string? Comment);

