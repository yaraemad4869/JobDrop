using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

public sealed class S5Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Type { get; set; } = "General";

    public string Title { get; set; } = "";

    public string Message { get; set; } = "";

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAtUtc { get; set; }

    public string? RecipientEmail { get; set; }

    public string EmailStatus { get; set; } = "Skipped";

    public int EmailAttempts { get; set; }

    public DateTime? NextEmailAttemptAtUtc { get; set; }

    public DateTime? EmailSentAtUtc { get; set; }

    public string? LastEmailError { get; set; }
}

public sealed class S5NotificationPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public bool InAppEnabled { get; set; } = true;

    public bool EmailEnabled { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S5AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ActorUserId { get; set; }

    public string Action { get; set; } = "";

    public string TargetType { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string? Details { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record S5NotificationPreferenceRequest(
    bool InAppEnabled,
    bool EmailEnabled
);

public sealed record S5AdminNotificationRequest(
    Guid UserId,
    string Type,
    string Title,
    string Message
);

public static class Sprint5Schema
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<S5Notification>()
            .ToTable("S5Notifications")
            .HasKey(x => x.Id);

        b.Entity<S5Notification>()
            .HasIndex(x => new { x.UserId, x.CreatedAtUtc });

        b.Entity<S5Notification>()
            .HasIndex(x => new { x.UserId, x.IsRead });

        b.Entity<S5Notification>()
            .HasIndex(x => new { x.EmailStatus, x.NextEmailAttemptAtUtc });

        b.Entity<S5Notification>()
            .Property(x => x.Title)
            .HasMaxLength(200);

        b.Entity<S5Notification>()
            .Property(x => x.Type)
            .HasMaxLength(50);

        b.Entity<S5Notification>()
            .Property(x => x.EmailStatus)
            .HasMaxLength(30);

        b.Entity<S5NotificationPreference>()
            .ToTable("S5NotificationPreferences")
            .HasKey(x => x.Id);

        b.Entity<S5NotificationPreference>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        b.Entity<S5AuditLog>()
            .ToTable("S5AuditLogs")
            .HasKey(x => x.Id);

        b.Entity<S5AuditLog>()
            .HasIndex(x => x.CreatedAtUtc);

        b.Entity<S5AuditLog>()
            .HasIndex(x => new { x.TargetType, x.TargetId });
    }

    public static async Task InitializeAsync(JobDropDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.S5Notifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.S5Notifications
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        Type nvarchar(50) NOT NULL,
        Title nvarchar(200) NOT NULL,
        Message nvarchar(max) NOT NULL,
        IsRead bit NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        ReadAtUtc datetime2 NULL,
        RecipientEmail nvarchar(256) NULL,
        EmailStatus nvarchar(30) NOT NULL,
        EmailAttempts int NOT NULL,
        NextEmailAttemptAtUtc datetime2 NULL,
        EmailSentAtUtc datetime2 NULL,
        LastEmailError nvarchar(2000) NULL
    );

    CREATE INDEX IX_S5Notifications_User_Created
    ON dbo.S5Notifications(UserId, CreatedAtUtc);

    CREATE INDEX IX_S5Notifications_User_Read
    ON dbo.S5Notifications(UserId, IsRead);

    CREATE INDEX IX_S5Notifications_Email_Status
    ON dbo.S5Notifications(EmailStatus, NextEmailAttemptAtUtc);
END;

IF OBJECT_ID(N'dbo.S5NotificationPreferences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.S5NotificationPreferences
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        InAppEnabled bit NOT NULL,
        EmailEnabled bit NOT NULL,
        UpdatedAtUtc datetime2 NOT NULL
    );

    CREATE UNIQUE INDEX IX_S5NotificationPreferences_User
    ON dbo.S5NotificationPreferences(UserId);
END;

IF OBJECT_ID(N'dbo.S5AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.S5AuditLogs
    (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        ActorUserId uniqueidentifier NULL,
        Action nvarchar(100) NOT NULL,
        TargetType nvarchar(50) NOT NULL,
        TargetId nvarchar(100) NOT NULL,
        Details nvarchar(2000) NULL,
        CreatedAtUtc datetime2 NOT NULL
    );

    CREATE INDEX IX_S5AuditLogs_Created
    ON dbo.S5AuditLogs(CreatedAtUtc);

    CREATE INDEX IX_S5AuditLogs_Target
    ON dbo.S5AuditLogs(TargetType, TargetId);
END;
";

        await db.Database.ExecuteSqlRawAsync(sql);
    }
}

public static class Sprint5NotificationService
{
    public static async Task<Guid?> EnqueueAsync(
        JobDropDbContext db,
        Guid userId,
        string type,
        string title,
        string message,
        bool sendEmail = true)
    {
        if (userId == Guid.Empty)
            return null;

        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(message))
            return null;

        var user = await db.Users
            .Where(x => x.Id == userId)
            .Select(x => new { x.Email })
            .FirstOrDefaultAsync();

        if (user == null)
            return null;

        var preference = await db.Set<S5NotificationPreference>()
            .FirstOrDefaultAsync(x => x.UserId == userId);

        var inAppEnabled = preference?.InAppEnabled ?? true;
        var emailEnabled = preference?.EmailEnabled ?? true;

        if (!inAppEnabled && (!sendEmail || !emailEnabled))
            return null;

        var email = sendEmail && emailEnabled &&
                    !string.IsNullOrWhiteSpace(user.Email)
            ? user.Email
            : null;

        var item = new S5Notification
        {
            UserId = userId,
            Type = type.Trim(),
            Title = title.Trim(),
            Message = message.Trim(),
            IsRead = false,
            RecipientEmail = email,
            EmailStatus = email == null ? "Skipped" : "Pending",
            NextEmailAttemptAtUtc = email == null
                ? null
                : DateTime.UtcNow
        };

        db.Set<S5Notification>().Add(item);

        await db.SaveChangesAsync();

        return item.Id;
    }
}

public sealed class Sprint5NotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Sprint5NotificationWorker> _logger;

    public Sprint5NotificationWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<Sprint5NotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEmails(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Sprint 5 notification worker failed.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(30),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessPendingEmails(
        CancellationToken cancellationToken)
    {
        var host = _configuration["Email:SmtpHost"];
        var from = _configuration["Email:From"];
        var port = int.TryParse(
            _configuration["Email:SmtpPort"],
            out var configuredPort)
            ? configuredPort
            : 587;

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(from))
            return;

        using var scope = _scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<JobDropDbContext>();

        var now = DateTime.UtcNow;

        var pending = await db.Set<S5Notification>()
            .Where(x =>
                x.EmailStatus == "Pending" &&
                x.RecipientEmail != null &&
                x.EmailAttempts < 5 &&
                (x.NextEmailAttemptAtUtc == null ||
                 x.NextEmailAttemptAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var mail = new MailMessage(
                    from,
                    item.RecipientEmail!,
                    item.Title,
                    item.Message);

                mail.IsBodyHtml = false;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = bool.TryParse(
                        _configuration["Email:EnableSsl"],
                        out var ssl) && ssl,
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                var username = _configuration["Email:Username"];
                var password = _configuration["Email:Password"];

                if (!string.IsNullOrWhiteSpace(username))
                {
                    client.Credentials = new NetworkCredential(
                        username,
                        password ?? "");
                }

                await client.SendMailAsync(mail, cancellationToken);

                item.EmailStatus = "Sent";
                item.EmailSentAtUtc = DateTime.UtcNow;
                item.LastEmailError = null;
                item.NextEmailAttemptAtUtc = null;
            }
            catch (Exception ex)
            {
                item.EmailAttempts++;

                item.LastEmailError = ex.Message.Length > 1900
                    ? ex.Message[..1900]
                    : ex.Message;

                if (item.EmailAttempts >= 5)
                {
                    item.EmailStatus = "Failed";
                    item.NextEmailAttemptAtUtc = null;
                }
                else
                {
                    item.EmailStatus = "Pending";

                    var minutes = Math.Pow(
                        2,
                        item.EmailAttempts);

                    item.NextEmailAttemptAtUtc =
                        DateTime.UtcNow.AddMinutes(minutes);
                }

                _logger.LogWarning(
                    ex,
                    "Could not send Sprint 5 email notification {Id}.",
                    item.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

public static class Sprint5Api
{
    private static Guid UserId(ClaimsPrincipal principal)
    {
        return Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue("sub"),
            out var id)
            ? id
            : Guid.Empty;
    }

    private static async Task<bool> IsAdmin(
        JobDropDbContext db,
        Guid userId)
    {
        if (userId == Guid.Empty)
            return false;

        return await db.UserRoles.AnyAsync(ur =>
            ur.UserId == userId &&
            db.Roles.Any(r =>
                r.Id == ur.RoleId &&
                r.Name == Roles.PlatformAdmin));
    }

    public static void MapSprint5(this WebApplication app)
    {
        var notifications = app
            .MapGroup("/api/sprint5/notifications")
            .RequireAuthorization();

        notifications.MapGet("/", async (
            ClaimsPrincipal principal,
            JobDropDbContext db,
            bool? unread,
            int? page,
            int? pageSize) =>
        {
            var uid = UserId(principal);

            if (uid == Guid.Empty)
                return Results.Unauthorized();

            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);

            var query = db.Set<S5Notification>()
                .Where(x => x.UserId == uid);

            if (unread == true)
                query = query.Where(x => !x.IsRead);

            var total = await query.CountAsync();

            var rows = await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .Select(x => new
                {
                    x.Id,
                    x.Type,
                    x.Title,
                    x.Message,
                    x.IsRead,
                    x.CreatedAtUtc,
                    x.ReadAtUtc,
                    x.EmailStatus,
                    x.EmailSentAtUtc
                })
                .ToListAsync();

            return Results.Ok(new
            {
                page = currentPage,
                pageSize = size,
                total,
                unreadCount = await db.Set<S5Notification>()
                    .CountAsync(x =>
                        x.UserId == uid && !x.IsRead),
                items = rows
            });
        });

        notifications.MapPost("/{id:guid}/read", async (
            Guid id,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var uid = UserId(principal);

            var item = await db.Set<S5Notification>()
                .FirstOrDefaultAsync(x =>
                    x.Id == id && x.UserId == uid);

            if (item == null)
                return Results.NotFound();

            if (!item.IsRead)
            {
                item.IsRead = true;
                item.ReadAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(new
            {
                item.Id,
                item.IsRead,
                item.ReadAtUtc
            });
        });

        notifications.MapPost("/read-all", async (
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var uid = UserId(principal);

            var unreadItems = await db.Set<S5Notification>()
                .Where(x => x.UserId == uid && !x.IsRead)
                .ToListAsync();

            var now = DateTime.UtcNow;

            foreach (var item in unreadItems)
            {
                item.IsRead = true;
                item.ReadAtUtc = now;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                updated = unreadItems.Count
            });
        });

        notifications.MapGet("/preferences", async (
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var uid = UserId(principal);

            var preference = await db
                .Set<S5NotificationPreference>()
                .FirstOrDefaultAsync(x => x.UserId == uid);

            return Results.Ok(new
            {
                inAppEnabled = preference?.InAppEnabled ?? true,
                emailEnabled = preference?.EmailEnabled ?? true
            });
        });

        notifications.MapPut("/preferences", async (
            S5NotificationPreferenceRequest request,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var uid = UserId(principal);

            if (uid == Guid.Empty)
                return Results.Unauthorized();

            var preference = await db
                .Set<S5NotificationPreference>()
                .FirstOrDefaultAsync(x => x.UserId == uid);

            if (preference == null)
            {
                preference = new S5NotificationPreference
                {
                    UserId = uid
                };

                db.Set<S5NotificationPreference>().Add(preference);
            }

            preference.InAppEnabled = request.InAppEnabled;
            preference.EmailEnabled = request.EmailEnabled;
            preference.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                preference.InAppEnabled,
                preference.EmailEnabled,
                preference.UpdatedAtUtc
            });
        });

        var admin = app
            .MapGroup("/api/sprint5/admin")
            .RequireAuthorization();

        admin.MapGet("/dashboard", async (
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var uid = UserId(principal);

            if (!await IsAdmin(db, uid))
                return Results.Forbid();

            var now = DateTime.UtcNow;

            var users = await db.Users.CountAsync();

            var candidates = await db.CandidateProfiles.CountAsync();

            var companies = await db.Companies.CountAsync();

            var jobs = await db.Set<S2Job>().CountAsync();

            var applications = await db.Set<S2Application>().CountAsync();

            var activeSubscriptions = await db.Set<S4Subscription>()
                .CountAsync(x =>
                    x.Status == "Active" &&
                    x.ExpiresAtUtc > now);

            var pendingVerifications = await db.Set<S4Verification>()
                .CountAsync(x => x.Status == "Pending");

            var unreadNotifications = await db.Set<S5Notification>()
                .CountAsync(x => !x.IsRead);

            var pendingEmails = await db.Set<S5Notification>()
                .CountAsync(x => x.EmailStatus == "Pending");

            var failedEmails = await db.Set<S5Notification>()
                .CountAsync(x => x.EmailStatus == "Failed");

            return Results.Ok(new
            {
                generatedAtUtc = now,
                users,
                candidates,
                companies,
                jobs,
                applications,
                activeSubscriptions,
                pendingVerifications,
                unreadNotifications,
                pendingEmails,
                failedEmails
            });
        });

        admin.MapGet("/audit", async (
            ClaimsPrincipal principal,
            JobDropDbContext db,
            string? targetType,
            string? targetId,
            int? page,
            int? pageSize) =>
        {
            var uid = UserId(principal);

            if (!await IsAdmin(db, uid))
                return Results.Forbid();

            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);

            var query = db.Set<S5AuditLog>().AsQueryable();

            if (!string.IsNullOrWhiteSpace(targetType))
                query = query.Where(x => x.TargetType == targetType);

            if (!string.IsNullOrWhiteSpace(targetId))
                query = query.Where(x => x.TargetId == targetId);

            var total = await query.CountAsync();

            var rows = await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .ToListAsync();

            return Results.Ok(new
            {
                page = currentPage,
                pageSize = size,
                total,
                items = rows
            });
        });

        admin.MapGet("/notifications", async (
            ClaimsPrincipal principal,
            JobDropDbContext db,
            string? emailStatus,
            int? page,
            int? pageSize) =>
        {
            var uid = UserId(principal);

            if (!await IsAdmin(db, uid))
                return Results.Forbid();

            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);

            var query = db.Set<S5Notification>().AsQueryable();

            if (!string.IsNullOrWhiteSpace(emailStatus))
                query = query.Where(x => x.EmailStatus == emailStatus);

            var total = await query.CountAsync();

            var rows = await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .Select(x => new
                {
                    x.Id,
                    x.UserId,
                    x.Type,
                    x.Title,
                    x.IsRead,
                    x.CreatedAtUtc,
                    x.EmailStatus,
                    x.EmailAttempts,
                    x.EmailSentAtUtc,
                    x.LastEmailError
                })
                .ToListAsync();

            return Results.Ok(new
            {
                page = currentPage,
                pageSize = size,
                total,
                items = rows
            });
        });

        admin.MapPost("/notifications", async (
            S5AdminNotificationRequest request,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var uid = UserId(principal);

            if (!await IsAdmin(db, uid))
                return Results.Forbid();

            if (request.UserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(request.Title) ||
                string.IsNullOrWhiteSpace(request.Message) ||
                request.Title.Length > 200 ||
                request.Type.Length > 50 ||
                request.Message.Length > 5000)
            {
                return Results.BadRequest(new
                {
                    error = "Invalid notification data."
                });
            }

            var id = await Sprint5NotificationService.EnqueueAsync(
                db,
                request.UserId,
                request.Type,
                request.Title,
                request.Message,
                true);

            if (!id.HasValue)
                return Results.NotFound(new
                {
                    error = "User not found or notification is disabled."
                });

            db.Set<S5AuditLog>().Add(new S5AuditLog
            {
                ActorUserId = uid,
                Action = "AdminNotificationCreated",
                TargetType = "User",
                TargetId = request.UserId.ToString(),
                Details = "Notification ID: " + id.Value
            });

            await db.SaveChangesAsync();

            return Results.Created(
                "/api/sprint5/notifications/" + id.Value,
                new { id = id.Value });
        });

        app.MapGet("/api/sprint5/health", async (
            JobDropDbContext db) =>
        {
            try
            {
                var connected = await db.Database.CanConnectAsync();

                if (!connected)
                    return Results.Problem(
                        "Database is unavailable.",
                        statusCode: 503);

                return Results.Ok(new
                {
                    status = "Healthy",
                    database = "Connected",
                    timestampUtc = DateTime.UtcNow
                });
            }
            catch
            {
                return Results.Problem(
                    "Database health check failed.",
                    statusCode: 503);
            }
        }).AllowAnonymous();
    }
}
