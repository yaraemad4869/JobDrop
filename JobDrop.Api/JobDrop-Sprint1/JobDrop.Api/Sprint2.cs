using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

public enum Sprint2JobStatus
{
    Draft, Published, Closed
}

public enum Sprint2ApplicationStatus
{
    Applied,
    Queued,
    Delivered,
    Viewed,
    Screening,
    Shortlisted,
    Assessment,
    InterviewScheduled,
    InterviewCompleted,
    OfferExtended,
    Hired,
    Rejected,
    Withdrawn
}

public sealed class S2Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string EmploymentType { get; set; } = "FullTime";
    public string WorkArrangement { get; set; } = "OnSite";
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? SalaryCurrency { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string Status { get; set; } = "Draft";
}

public sealed class S2JobRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string RequirementType { get; set; } = "General";
    public string Description { get; set; } = "";
    public bool IsMandatory { get; set; } = true;
}

public sealed class S2JobPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string PreferenceType { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class S2Application
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CandidateProfileId { get; set; }
    public string Status { get; set; } = "Applied";
    public string? CoverLetter { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
}

public sealed class S2ApplicationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public Guid ActorUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S2QuotaUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CandidateProfileId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime UtcDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S2QueueEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public string Status { get; set; } = "Pending";
}

public sealed class S2RecruitmentDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S2JobConfiguration : IEntityTypeConfiguration<S2Job>
{
    public void Configure(EntityTypeBuilder<S2Job> b)
    {
        b.ToTable("S2Jobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
        b.HasIndex(x => new { x.Status, x.PublishedAtUtc });
    }
}

public sealed class S2RequirementConfiguration : IEntityTypeConfiguration<S2JobRequirement>
{
    public void Configure(EntityTypeBuilder<S2JobRequirement> b)
    {
        b.ToTable("S2JobRequirements");
        b.HasKey(x => x.Id);
        b.Property(x => x.RequirementType).HasMaxLength(60);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasIndex(x => x.JobId);
    }
}

public sealed class S2PreferenceConfiguration : IEntityTypeConfiguration<S2JobPreference>
{
    public void Configure(EntityTypeBuilder<S2JobPreference> b)
    {
        b.ToTable("S2JobPreferences");
        b.HasKey(x => x.Id);
        b.Property(x => x.PreferenceType).HasMaxLength(100);
        b.Property(x => x.Value).HasMaxLength(500);
        b.HasIndex(x => x.JobId);
    }
}

public sealed class S2ApplicationConfiguration : IEntityTypeConfiguration<S2Application>
{
    public void Configure(EntityTypeBuilder<S2Application> b)
    {
        b.ToTable("S2Applications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasMaxLength(40).IsRequired();
        b.Property(x => x.RejectionReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.CandidateProfileId, x.JobId }).IsUnique();
        b.HasIndex(x => new { x.CandidateProfileId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.CompanyId, x.DeliveredAtUtc, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
    }
}

public sealed class S2EventConfiguration : IEntityTypeConfiguration<S2ApplicationEvent>
{
    public void Configure(EntityTypeBuilder<S2ApplicationEvent> b)
    {
        b.ToTable("S2ApplicationEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.FromStatus).HasMaxLength(40);
        b.Property(x => x.ToStatus).HasMaxLength(40);
        b.HasIndex(x => new { x.ApplicationId, x.CreatedAtUtc });
    }
}

public sealed class S2QuotaConfiguration : IEntityTypeConfiguration<S2QuotaUsage>
{
    public void Configure(EntityTypeBuilder<S2QuotaUsage> b)
    {
        b.ToTable("S2QuotaUsages");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CandidateProfileId, x.UtcDate, x.CompanyId }).IsUnique();
    }
}

public sealed class S2QueueConfiguration : IEntityTypeConfiguration<S2QueueEntry>
{
    public void Configure(EntityTypeBuilder<S2QueueEntry> b)
    {
        b.ToTable("S2QueueEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc });
        b.HasIndex(x => x.ApplicationId).IsUnique();
    }
}

public sealed class S2DecisionConfiguration : IEntityTypeConfiguration<S2RecruitmentDecision>
{
    public void Configure(EntityTypeBuilder<S2RecruitmentDecision> b)
    {
        b.ToTable("S2RecruitmentDecisions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Decision).HasMaxLength(50);
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasIndex(x => new { x.ApplicationId, x.CreatedAtUtc });
    }
}

public static class Sprint2Schema
{
    public static void Configure(ModelBuilder b)
    {
        b.ApplyConfiguration(new S2JobConfiguration());
        b.ApplyConfiguration(new S2RequirementConfiguration());
        b.ApplyConfiguration(new S2PreferenceConfiguration());
        b.ApplyConfiguration(new S2ApplicationConfiguration());
        b.ApplyConfiguration(new S2EventConfiguration());
        b.ApplyConfiguration(new S2QuotaConfiguration());
        b.ApplyConfiguration(new S2QueueConfiguration());
        b.ApplyConfiguration(new S2DecisionConfiguration());
    }

    public static async Task InitializeAsync(JobDropDbContext db)
    {
        var sql = @"
IF OBJECT_ID(N'dbo.S2Jobs', N'U') IS NULL
CREATE TABLE dbo.S2Jobs (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 CompanyId uniqueidentifier NOT NULL,
 Title nvarchar(200) NOT NULL,
 Description nvarchar(max) NOT NULL,
 EmploymentType nvarchar(60) NOT NULL,
 WorkArrangement nvarchar(60) NOT NULL,
 Country nvarchar(150) NULL,
 City nvarchar(150) NULL,
 SalaryCurrency nvarchar(10) NULL,
 SalaryMin decimal(18,2) NULL,
 SalaryMax decimal(18,2) NULL,
 CreatedAtUtc datetime2 NOT NULL,
 PublishedAtUtc datetime2 NULL,
 ClosedAtUtc datetime2 NULL,
 ExpiresAtUtc datetime2 NULL,
 Status nvarchar(30) NOT NULL
);
IF OBJECT_ID(N'dbo.S2JobRequirements', N'U') IS NULL
CREATE TABLE dbo.S2JobRequirements (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 JobId uniqueidentifier NOT NULL,
 RequirementType nvarchar(60) NOT NULL,
 Description nvarchar(2000) NOT NULL,
 IsMandatory bit NOT NULL,
 CONSTRAINT FK_S2Requirements_Jobs FOREIGN KEY(JobId) REFERENCES dbo.S2Jobs(Id) ON DELETE CASCADE
);
IF OBJECT_ID(N'dbo.S2JobPreferences', N'U') IS NULL
CREATE TABLE dbo.S2JobPreferences (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 JobId uniqueidentifier NOT NULL,
 PreferenceType nvarchar(100) NOT NULL,
 Value nvarchar(500) NOT NULL,
 CONSTRAINT FK_S2Preferences_Jobs FOREIGN KEY(JobId) REFERENCES dbo.S2Jobs(Id) ON DELETE CASCADE
);
IF OBJECT_ID(N'dbo.S2Applications', N'U') IS NULL
CREATE TABLE dbo.S2Applications (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 JobId uniqueidentifier NOT NULL,
 CompanyId uniqueidentifier NOT NULL,
 CandidateProfileId uniqueidentifier NOT NULL,
 Status nvarchar(40) NOT NULL,
 CoverLetter nvarchar(max) NULL,
 RejectionReason nvarchar(2000) NULL,
 CreatedAtUtc datetime2 NOT NULL,
 DeliveredAtUtc datetime2 NULL,
 UpdatedAtUtc datetime2 NULL,
 WithdrawnAtUtc datetime2 NULL,
 CONSTRAINT FK_S2Applications_Jobs FOREIGN KEY(JobId) REFERENCES dbo.S2Jobs(Id),
 CONSTRAINT FK_S2Applications_Candidates FOREIGN KEY(CandidateProfileId) REFERENCES dbo.CandidateProfiles(Id)
);
IF OBJECT_ID(N'dbo.S2ApplicationEvents', N'U') IS NULL
CREATE TABLE dbo.S2ApplicationEvents (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 ApplicationId uniqueidentifier NOT NULL,
 FromStatus nvarchar(40) NOT NULL,
 ToStatus nvarchar(40) NOT NULL,
 ActorUserId uniqueidentifier NOT NULL,
 Reason nvarchar(2000) NULL,
 CreatedAtUtc datetime2 NOT NULL,
 CONSTRAINT FK_S2Events_Applications FOREIGN KEY(ApplicationId) REFERENCES dbo.S2Applications(Id) ON DELETE CASCADE
);
IF OBJECT_ID(N'dbo.S2QuotaUsages', N'U') IS NULL
CREATE TABLE dbo.S2QuotaUsages (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 CandidateProfileId uniqueidentifier NOT NULL,
 CompanyId uniqueidentifier NOT NULL,
 UtcDate datetime2 NOT NULL,
 CreatedAtUtc datetime2 NOT NULL,
 CONSTRAINT FK_S2Quota_Candidates FOREIGN KEY(CandidateProfileId) REFERENCES dbo.CandidateProfiles(Id),
 CONSTRAINT UQ_S2Quota UNIQUE(CandidateProfileId, UtcDate, CompanyId)
);
IF OBJECT_ID(N'dbo.S2QueueEntries', N'U') IS NULL
CREATE TABLE dbo.S2QueueEntries (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 ApplicationId uniqueidentifier NOT NULL,
 CompanyId uniqueidentifier NOT NULL,
 CreatedAtUtc datetime2 NOT NULL,
 ProcessedAtUtc datetime2 NULL,
 Status nvarchar(30) NOT NULL,
 CONSTRAINT FK_S2Queue_Applications FOREIGN KEY(ApplicationId) REFERENCES dbo.S2Applications(Id) ON DELETE CASCADE,
 CONSTRAINT UQ_S2Queue_Application UNIQUE(ApplicationId)
);
IF OBJECT_ID(N'dbo.S2RecruitmentDecisions', N'U') IS NULL
CREATE TABLE dbo.S2RecruitmentDecisions (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 ApplicationId uniqueidentifier NOT NULL,
 ActorUserId uniqueidentifier NOT NULL,
 Decision nvarchar(50) NOT NULL,
 Reason nvarchar(2000) NULL,
 CreatedAtUtc datetime2 NOT NULL,
 CONSTRAINT FK_S2Decisions_Applications FOREIGN KEY(ApplicationId) REFERENCES dbo.S2Applications(Id) ON DELETE CASCADE
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Jobs_Company_Status_Created' AND object_id=OBJECT_ID('dbo.S2Jobs'))
 CREATE INDEX IX_S2Jobs_Company_Status_Created ON dbo.S2Jobs(CompanyId,Status,CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Jobs_Status_Published' AND object_id=OBJECT_ID('dbo.S2Jobs'))
 CREATE INDEX IX_S2Jobs_Status_Published ON dbo.S2Jobs(Status,PublishedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Requirements_JobId' AND object_id=OBJECT_ID('dbo.S2JobRequirements'))
 CREATE INDEX IX_S2Requirements_JobId ON dbo.S2JobRequirements(JobId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Preferences_JobId' AND object_id=OBJECT_ID('dbo.S2JobPreferences'))
 CREATE INDEX IX_S2Preferences_JobId ON dbo.S2JobPreferences(JobId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Applications_Candidate_Job' AND object_id=OBJECT_ID('dbo.S2Applications'))
 CREATE UNIQUE INDEX IX_S2Applications_Candidate_Job ON dbo.S2Applications(CandidateProfileId,JobId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Applications_Company_Delivered' AND object_id=OBJECT_ID('dbo.S2Applications'))
 CREATE INDEX IX_S2Applications_Company_Delivered ON dbo.S2Applications(CompanyId,DeliveredAtUtc,Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Applications_Candidate_Created' AND object_id=OBJECT_ID('dbo.S2Applications'))
 CREATE INDEX IX_S2Applications_Candidate_Created ON dbo.S2Applications(CandidateProfileId,CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Events_Application_Created' AND object_id=OBJECT_ID('dbo.S2ApplicationEvents'))
 CREATE INDEX IX_S2Events_Application_Created ON dbo.S2ApplicationEvents(ApplicationId,CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Queue_Company_Status_Created' AND object_id=OBJECT_ID('dbo.S2QueueEntries'))
 CREATE INDEX IX_S2Queue_Company_Status_Created ON dbo.S2QueueEntries(CompanyId,Status,CreatedAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_S2Decisions_Application_Created' AND object_id=OBJECT_ID('dbo.S2RecruitmentDecisions'))
 CREATE INDEX IX_S2Decisions_Application_Created ON dbo.S2RecruitmentDecisions(ApplicationId,CreatedAtUtc);
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}

public sealed record S2CreateJobRequest(
    string Title,
    string Description,
    Guid CompanyId,
    string? EmploymentType,
    string? WorkArrangement,
    string? Country,
    string? City,
    string? SalaryCurrency,
    decimal? SalaryMin,
    decimal? SalaryMax,
    DateTime? ExpiresAtUtc,
    List<S2RequirementRequest>? Requirements,
    List<S2PreferenceRequest>? Preferences);

public sealed record S2RequirementRequest(string RequirementType, string Description, bool IsMandatory);
public sealed record S2PreferenceRequest(string PreferenceType, string Value);
public sealed record S2ApplyRequest(string? CoverLetter);
public sealed record S2TransitionRequest(string Status, string? Reason);
public sealed record S2PendingPolicyRequest(string Policy);

public static class Sprint2Api
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Applied","Queued","Delivered","Viewed","Screening","Shortlisted",
        "Assessment","InterviewScheduled","InterviewCompleted",
        "OfferExtended","Hired","Rejected","Withdrawn"
    };

    private static readonly Dictionary<string, string[]> Transitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Delivered"] = ["Viewed","Screening","Rejected","Withdrawn"],
        ["Viewed"] = ["Screening","Shortlisted","Rejected","Withdrawn"],
        ["Screening"] = ["Shortlisted","Assessment","InterviewScheduled","Rejected","Withdrawn"],
        ["Shortlisted"] = ["Assessment","InterviewScheduled","Rejected","Withdrawn"],
        ["Assessment"] = ["InterviewScheduled","InterviewCompleted","Rejected","Withdrawn"],
        ["InterviewScheduled"] = ["InterviewCompleted","Rejected","Withdrawn"],
        ["InterviewCompleted"] = ["OfferExtended","Rejected","Withdrawn"],
        ["OfferExtended"] = ["Hired","Rejected","Withdrawn"]
    };

    private static Guid CurrentUser(ClaimsPrincipal p) =>
        Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier) ??
                      p.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;

    private static async Task AcquireTransactionLockAsync(
        JobDropDbContext db,
        string resource)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @lockResult int;

EXEC @lockResult = sys.sp_getapplock
    @Resource = {resource},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;

IF @lockResult < 0
    THROW 51000, 'Could not acquire Sprint 2 application lock.', 1;
");
    }

    private static Task AcquireCandidateQuotaLockAsync(
        JobDropDbContext db,
        Guid candidateProfileId,
        DateTime utcDate)
    {
        var resource = $"s2:candidate:{candidateProfileId:N}:{utcDate.Date.Ticks}";
        return AcquireTransactionLockAsync(db, resource);
    }

    public static Task AcquireCompanyCapacityLockAsync(
        JobDropDbContext db,
        Guid companyId,
        DateTime utcDate)
    {
        var resource = $"s2:company:{companyId:N}:{utcDate.Date.Ticks}";
        return AcquireTransactionLockAsync(db, resource);
    }
    private static async Task<bool> CanManageCompany(
        JobDropDbContext db, Guid companyId, Guid userId)
    {
        return await db.CompanyMembers.AnyAsync(x =>
            x.CompanyId == companyId &&
            x.UserId == userId &&
            (x.Role == CompanyMemberRole.Owner ||
             x.Role == CompanyMemberRole.Admin ||
             x.Role == CompanyMemberRole.Recruiter));
    }

    private static async Task<bool> CanViewCompany(
        JobDropDbContext db, Guid companyId, Guid userId)
    {
        return await db.CompanyMembers.AnyAsync(x =>
            x.CompanyId == companyId && x.UserId == userId);
    }

    public static void MapSprint2(this WebApplication app)
    {
        var jobs = app.MapGroup("/api/jobs");

        jobs.MapPost("/", async (
            S2CreateJobRequest request,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);

            if (string.IsNullOrWhiteSpace(request.Title) ||
                string.IsNullOrWhiteSpace(request.Description))
                return Results.BadRequest(new { error = "Title and description are required." });

            if (request.SalaryMin < 0 || request.SalaryMax < 0 ||
                (request.SalaryMin.HasValue && request.SalaryMax.HasValue &&
                 request.SalaryMin > request.SalaryMax))
                return Results.BadRequest(new { error = "Invalid salary range." });

            var selectedCompany = request.CompanyId;

            if (selectedCompany == Guid.Empty)
                return Results.BadRequest(new { error = "CompanyId is required." });

            if (!await CanManageCompany(db, selectedCompany, userId))
                return Results.Forbid();

            var job = new S2Job
            {
                CompanyId = selectedCompany,
                Title = request.Title.Trim(),
                Description = request.Description.Trim(),
                EmploymentType = request.EmploymentType ?? "FullTime",
                WorkArrangement = request.WorkArrangement ?? "OnSite",
                Country = request.Country,
                City = request.City,
                SalaryCurrency = request.SalaryCurrency,
                SalaryMin = request.SalaryMin,
                SalaryMax = request.SalaryMax,
                ExpiresAtUtc = request.ExpiresAtUtc,
                Status = "Draft"
            };

            db.Set<S2Job>().Add(job);

            foreach (var item in request.Requirements ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Description)) continue;
                db.Set<S2JobRequirement>().Add(new S2JobRequirement
                {
                    JobId = job.Id,
                    RequirementType = item.RequirementType,
                    Description = item.Description.Trim(),
                    IsMandatory = item.IsMandatory
                });
            }

            foreach (var item in request.Preferences ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.PreferenceType) ||
                    string.IsNullOrWhiteSpace(item.Value)) continue;
                db.Set<S2JobPreference>().Add(new S2JobPreference
                {
                    JobId = job.Id,
                    PreferenceType = item.PreferenceType.Trim(),
                    Value = item.Value.Trim()
                });
            }

            await db.SaveChangesAsync();
            return Results.Created($"/api/jobs/{job.Id}", job);
        }).RequireAuthorization();

        jobs.MapGet("/", async (
            JobDropDbContext db,
            string? search = null,
            string? country = null,
            string? city = null,
            string? employmentType = null,
            int page = 1,
            int pageSize = 20) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize == 0 ? 20 : pageSize, 1, 100);

            var query = db.Set<S2Job>().AsNoTracking()
                .Where(x => x.Status == "Published" &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > DateTime.UtcNow));

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x => x.Title.Contains(search) || x.Description.Contains(search));
            if (!string.IsNullOrWhiteSpace(country))
                query = query.Where(x => x.Country == country);
            if (!string.IsNullOrWhiteSpace(city))
                query = query.Where(x => x.City == city);
            if (!string.IsNullOrWhiteSpace(employmentType))
                query = query.Where(x => x.EmploymentType == employmentType);

            var total = await query.CountAsync();
            var result = await query.OrderByDescending(x => x.PublishedAtUtc)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Results.Ok(new { total, page, pageSize, items = result });
        });

        jobs.MapGet("/{jobId:guid}", async (Guid jobId, JobDropDbContext db) =>
        {
            var job = await db.Set<S2Job>().AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == jobId &&
                    x.Status == "Published" &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > DateTime.UtcNow));
            if (job is null) return Results.NotFound();

            var requirements = await db.Set<S2JobRequirement>().AsNoTracking()
                .Where(x => x.JobId == jobId).ToListAsync();
            var preferences = await db.Set<S2JobPreference>().AsNoTracking()
                .Where(x => x.JobId == jobId).ToListAsync();

            return Results.Ok(new { job, requirements, preferences });
        });

        jobs.MapPut("/{jobId:guid}/publish", async (
            Guid jobId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            var job = await db.Set<S2Job>().FirstOrDefaultAsync(x => x.Id == jobId);
            if (job is null) return Results.NotFound();

            if (!await CanManageCompany(db, job.CompanyId, userId))
                return Results.Forbid();

            if (job.Status == "Closed")
                return Results.Conflict(new { error = "Closed jobs cannot be published." });

            job.Status = "Published";
            job.PublishedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(job);
        }).RequireAuthorization();

        jobs.MapPut("/{jobId:guid}/close", async (
            Guid jobId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            var job = await db.Set<S2Job>().FirstOrDefaultAsync(x => x.Id == jobId);
            if (job is null) return Results.NotFound();

            if (!await CanManageCompany(db, job.CompanyId, userId))
                return Results.Forbid();

            job.Status = "Closed";
            job.ClosedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(job);
        }).RequireAuthorization();

        var applications = app.MapGroup("/api/applications").RequireAuthorization();

        applications.MapPost("/jobs/{jobId:guid}", async (
            Guid jobId,
            S2ApplyRequest request,
            ClaimsPrincipal principal,
            JobDropDbContext db,
            IConfiguration config) =>
        {
            var userId = CurrentUser(principal);
            var profileId = await db.CandidateProfiles
                .Where(x => x.UserId == userId)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync();

            if (profileId is null) return Results.Forbid();

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var job = await db.Set<S2Job>().FirstOrDefaultAsync(x => x.Id == jobId);
            if (job is null || job.Status != "Published" ||
                (job.ExpiresAtUtc.HasValue && job.ExpiresAtUtc <= DateTime.UtcNow))
                return Results.NotFound(new { error = "Job is not available." });

            if (await db.Set<S2Application>().AnyAsync(x =>
                x.JobId == jobId && x.CandidateProfileId == profileId.Value))
                return Results.Conflict(new { error = "You have already applied to this job." });

            var now = DateTime.UtcNow;
            var dayStart = now.Date;
            var dayEnd = dayStart.AddDays(1);

            await AcquireCandidateQuotaLockAsync(
                db, profileId.Value, dayStart);

            await AcquireCompanyCapacityLockAsync(
                db, job.CompanyId, dayStart);

            // Recheck after acquiring the lock to handle simultaneous requests.
            if (await db.Set<S2Application>().AnyAsync(x =>
                x.JobId == jobId &&
                x.CandidateProfileId == profileId.Value))
                return Results.Conflict(new { error = "You have already applied to this job." });

            var alreadyAppliedToCompany = await db.Set<S2QuotaUsage>().AnyAsync(x =>
                x.CandidateProfileId == profileId.Value &&
                x.CompanyId == job.CompanyId && x.UtcDate == dayStart);

            var companyCount = await db.Set<S2QuotaUsage>().CountAsync(x =>
                x.CandidateProfileId == profileId.Value && x.UtcDate == dayStart);

            if (!alreadyAppliedToCompany && companyCount >= 5)
                return Results.Conflict(new { error = "Daily limit reached: five distinct companies per UTC day." });

            var deliveredCount = await db.Set<S2Application>().CountAsync(x =>
                x.CompanyId == job.CompanyId &&
                x.DeliveredAtUtc.HasValue &&
                x.DeliveredAtUtc >= dayStart &&
                x.DeliveredAtUtc < dayEnd);

            var policy = (config["Sprint2:PendingPolicy"] ??
                Environment.GetEnvironmentVariable("JOBDROP_PENDING_POLICY") ??
                "Queue").Trim();

            if (deliveredCount >= 20 && policy.Equals("Reject", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { error = "Company daily delivery capacity reached." });

            var status = deliveredCount >= 20 ? "Queued" : "Delivered";

            var application = new S2Application
            {
                JobId = jobId,
                CompanyId = job.CompanyId,
                CandidateProfileId = profileId.Value,
                CoverLetter = request.CoverLetter,
                Status = status,
                CreatedAtUtc = now,
                DeliveredAtUtc = status == "Delivered" ? now : null
            };

            db.Set<S2Application>().Add(application);

            // Persist the parent application before inserting dependent records.
            // The surrounding transaction keeps all writes atomic.
            await db.SaveChangesAsync();

            if (!alreadyAppliedToCompany)
            {
                db.Set<S2QuotaUsage>().Add(new S2QuotaUsage
                {
                    CandidateProfileId = profileId.Value,
                    CompanyId = job.CompanyId,
                    UtcDate = dayStart,
                    CreatedAtUtc = now
                });
            }

            db.Set<S2ApplicationEvent>().Add(new S2ApplicationEvent
            {
                ApplicationId = application.Id,
                FromStatus = "Applied",
                ToStatus = status,
                ActorUserId = userId,
                CreatedAtUtc = now
            });

            if (status == "Queued")
            {
                db.Set<S2QueueEntry>().Add(new S2QueueEntry
                {
                    ApplicationId = application.Id,
                    CompanyId = job.CompanyId,
                    CreatedAtUtc = now,
                    Status = "Pending"
                });
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return Results.Created($"/api/applications/{application.Id}", application);
        });

        applications.MapGet("/mine", async (
            ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            var profileId = await db.CandidateProfiles
                .Where(x => x.UserId == userId)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync();

            if (profileId is null) return Results.Forbid();

            var result = await db.Set<S2Application>().AsNoTracking()
                .Where(x => x.CandidateProfileId == profileId.Value)
                .OrderByDescending(x => x.CreatedAtUtc).ToListAsync();

            return Results.Ok(result);
        });

        applications.MapGet("/{applicationId:guid}/events", async (
            Guid applicationId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            var application = await db.Set<S2Application>()
                .FirstOrDefaultAsync(x => x.Id == applicationId);
            if (application is null) return Results.NotFound();

            var profileOwner = await db.CandidateProfiles.AnyAsync(x =>
                x.Id == application.CandidateProfileId && x.UserId == userId);
            var companyMember = await CanViewCompany(db, application.CompanyId, userId);

            if (!profileOwner && !companyMember &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            return Results.Ok(await db.Set<S2ApplicationEvent>().AsNoTracking()
                .Where(x => x.ApplicationId == applicationId)
                .OrderBy(x => x.CreatedAtUtc).ToListAsync());
        });

        applications.MapPost("/{applicationId:guid}/withdraw", async (
            Guid applicationId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            var profileId = await db.CandidateProfiles
                .Where(x => x.UserId == userId)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync();

            if (profileId is null) return Results.Forbid();

            var application = await db.Set<S2Application>().FirstOrDefaultAsync(x =>
                x.Id == applicationId && x.CandidateProfileId == profileId.Value);

            if (application is null) return Results.NotFound();
            if (application.Status is "Hired" or "Rejected" or "Withdrawn")
                return Results.Conflict(new { error = "This application cannot be withdrawn." });

            var previous = application.Status;
            application.Status = "Withdrawn";
            application.WithdrawnAtUtc = DateTime.UtcNow;
            application.UpdatedAtUtc = DateTime.UtcNow;

            var queue = await db.Set<S2QueueEntry>()
                .FirstOrDefaultAsync(x => x.ApplicationId == applicationId && x.Status == "Pending");
            if (queue is not null) queue.Status = "Cancelled";

            db.Set<S2ApplicationEvent>().Add(new S2ApplicationEvent
            {
                ApplicationId = applicationId,
                FromStatus = previous,
                ToStatus = "Withdrawn",
                ActorUserId = userId
            });

            await db.SaveChangesAsync();
            return Results.Ok(application);
        });

        applications.MapPost("/{applicationId:guid}/transition", async (
            Guid applicationId,
            S2TransitionRequest request,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            var target = request.Status?.Trim();

            if (string.IsNullOrWhiteSpace(target) || !AllowedStatuses.Contains(target))
                return Results.BadRequest(new { error = "Unknown pipeline status." });

            var application = await db.Set<S2Application>()
                .FirstOrDefaultAsync(x => x.Id == applicationId);
            if (application is null) return Results.NotFound();

            if (!await CanManageCompany(db, application.CompanyId, userId) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            if (!Transitions.TryGetValue(application.Status, out var allowed) ||
                !allowed.Contains(target, StringComparer.OrdinalIgnoreCase))
                return Results.Conflict(new
                {
                    error = "Invalid recruitment pipeline transition.",
                    currentStatus = application.Status,
                    requestedStatus = target
                });

            if (target.Equals("Rejected", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A rejection reason is mandatory." });

            var previous = application.Status;
            application.Status = target;
            application.UpdatedAtUtc = DateTime.UtcNow;
            application.RejectionReason = target.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                ? request.Reason!.Trim() : application.RejectionReason;

            db.Set<S2ApplicationEvent>().Add(new S2ApplicationEvent
            {
                ApplicationId = application.Id,
                FromStatus = previous,
                ToStatus = target,
                ActorUserId = userId,
                Reason = request.Reason
            });

            if (target.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("Hired", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("OfferExtended", StringComparison.OrdinalIgnoreCase))
            {
                db.Set<S2RecruitmentDecision>().Add(new S2RecruitmentDecision
                {
                    ApplicationId = application.Id,
                    ActorUserId = userId,
                    Decision = target,
                    Reason = request.Reason
                });
            }

            await db.SaveChangesAsync();
            return Results.Ok(application);
        });

        var companyRoutes = app.MapGroup("/api/companies").RequireAuthorization();

        companyRoutes.MapGet("/{companyId:guid}/applications", async (
            Guid companyId,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            if (!await CanViewCompany(db, companyId, userId) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            return Results.Ok(await db.Set<S2Application>().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedAtUtc).ToListAsync());
        });

        companyRoutes.MapGet("/{companyId:guid}/queue", async (
            Guid companyId,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            var userId = CurrentUser(principal);
            if (!await CanViewCompany(db, companyId, userId) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            var pending = await db.Set<S2QueueEntry>().CountAsync(x =>
                x.CompanyId == companyId && x.Status == "Pending");

            var today = DateTime.UtcNow.Date;
            var delivered = await db.Set<S2Application>().CountAsync(x =>
                x.CompanyId == companyId &&
                x.DeliveredAtUtc.HasValue &&
                x.DeliveredAtUtc >= today &&
                x.DeliveredAtUtc < today.AddDays(1));

            return Results.Ok(new
            {
                companyId,
                utcDate = today,
                deliveredToday = delivered,
                dailyCapacity = 20,
                remainingCapacity = Math.Max(0, 20 - delivered),
                pendingQueue = pending
            });
        });

        var recruitment = app.MapGroup("/api/recruitment");

        recruitment.MapGet("/pipeline", () => Results.Ok(new[]
        {
            "Applied","Queued","Delivered","Viewed","Screening","Shortlisted",
            "Assessment","InterviewScheduled","InterviewCompleted",
            "OfferExtended","Hired","Rejected","Withdrawn"
        }));

        recruitment.MapGet("/policy", (IConfiguration config) =>
            Results.Ok(new
            {
                pendingPolicy = config["Sprint2:PendingPolicy"] ??
                    Environment.GetEnvironmentVariable("JOBDROP_PENDING_POLICY") ?? "Queue",
                candidateDistinctCompaniesPerUtcDay = 5,
                companyDeliveredApplicationsPerUtcDay = 20
            }));
    }
}

public sealed class Sprint2QueueWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<Sprint2QueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueue(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sprint 2 queue processing failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task ProcessQueue(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JobDropDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var now = DateTime.UtcNow;
        var start = now.Date;
        var end = start.AddDays(1);

        var pending = await db.Set<S2QueueEntry>()
            .Where(x => x.Status == "Pending")
            .OrderBy(x => x.CreatedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            await Sprint2Api.AcquireCompanyCapacityLockAsync(
                db, entry.CompanyId, start);

            var delivered = await db.Set<S2Application>().CountAsync(x =>
                x.CompanyId == entry.CompanyId &&
                x.DeliveredAtUtc.HasValue &&
                x.DeliveredAtUtc >= start &&
                x.DeliveredAtUtc < end, cancellationToken);

            if (delivered >= 20) continue;

            var application = await db.Set<S2Application>()
                .FirstOrDefaultAsync(x => x.Id == entry.ApplicationId, cancellationToken);

            if (application is null || application.Status != "Queued")
            {
                entry.Status = "Cancelled";
                entry.ProcessedAtUtc = now;
                continue;
            }

            application.Status = "Delivered";
            application.DeliveredAtUtc = now;
            application.UpdatedAtUtc = now;

            entry.Status = "Processed";
            entry.ProcessedAtUtc = now;

            db.Set<S2ApplicationEvent>().Add(new S2ApplicationEvent
            {
                ApplicationId = application.Id,
                FromStatus = "Queued",
                ToStatus = "Delivered",
                ActorUserId = Guid.Empty,
                Reason = "Automatic FIFO queue dispatch",
                CreatedAtUtc = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
