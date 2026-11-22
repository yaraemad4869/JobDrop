using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

public sealed class S3MatchRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string RequiredSkills { get; set; } = "";
    public int MinimumScore { get; set; } = 50;
    public bool RequireLocationMatch { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S3CompanyCalendar
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string WorkingDays { get; set; } = "1,2,3,4,5";
    public int WorkStartMinuteUtc { get; set; } = 540;
    public int WorkEndMinuteUtc { get; set; } = 1020;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S3CompanyHoliday
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S3ApplicationSla
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public string Status { get; set; } = "Pending";

    // Immutable calendar snapshot captured when the SLA starts.
    public string WorkingDaysSnapshot { get; set; } = "1,2,3,4,5";
    public string HolidaysSnapshot { get; set; } = "";
    public string TimeZoneIdSnapshot { get; set; } = "UTC";
}

public sealed class S3Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public Guid TargetId { get; set; }
    public string TargetType { get; set; } = "";
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModeratedAtUtc { get; set; }
    public Guid? ModeratedByUserId { get; set; }
    public string? ModerationReason { get; set; }
}

public sealed class S3ReviewReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReviewId { get; set; }
    public Guid ReporterUserId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class Sprint3Schema
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<S3MatchRule>().ToTable("S3MatchRules");
        b.Entity<S3MatchRule>().HasKey(x => x.Id);
        b.Entity<S3MatchRule>().HasIndex(x => x.JobId).IsUnique();

        b.Entity<S3CompanyCalendar>().ToTable("S3CompanyCalendars");
        b.Entity<S3CompanyCalendar>().HasKey(x => x.Id);
        b.Entity<S3CompanyCalendar>().HasIndex(x => x.CompanyId).IsUnique();

        b.Entity<S3CompanyHoliday>().ToTable("S3CompanyHolidays");
        b.Entity<S3CompanyHoliday>().HasKey(x => x.Id);
        b.Entity<S3CompanyHoliday>().HasIndex(x => new { x.CompanyId, x.HolidayDate }).IsUnique();

        b.Entity<S3ApplicationSla>().ToTable("S3ApplicationSlas");
        b.Entity<S3ApplicationSla>().HasKey(x => x.Id);
        b.Entity<S3ApplicationSla>().HasIndex(x => x.ApplicationId).IsUnique();
        b.Entity<S3ApplicationSla>().HasIndex(x => new { x.CompanyId, x.Status, x.DueAtUtc });

        b.Entity<S3Review>().ToTable("S3Reviews");
        b.Entity<S3Review>().HasKey(x => x.Id);
        b.Entity<S3Review>().HasIndex(x => new { x.ApplicationId, x.ReviewerUserId }).IsUnique();
        b.Entity<S3Review>().HasIndex(x => new { x.TargetId, x.TargetType, x.Status });

        b.Entity<S3ReviewReport>().ToTable("S3ReviewReports");
        b.Entity<S3ReviewReport>().HasKey(x => x.Id);
        b.Entity<S3ReviewReport>().HasIndex(x => new { x.ReviewId, x.ReporterUserId }).IsUnique();
    }

    public static async Task InitializeAsync(JobDropDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.S3MatchRules', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S3MatchRules(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  JobId uniqueidentifier NOT NULL UNIQUE,
  RequiredSkills nvarchar(4000) NOT NULL,
  MinimumScore int NOT NULL,
  RequireLocationMatch bit NOT NULL,
  UpdatedAtUtc datetime2 NOT NULL,
  CONSTRAINT FK_S3MatchRules_Jobs FOREIGN KEY(JobId) REFERENCES dbo.S2Jobs(Id) ON DELETE CASCADE
 );
END;

IF OBJECT_ID(N'dbo.S3CompanyCalendars', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S3CompanyCalendars(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  CompanyId uniqueidentifier NOT NULL UNIQUE,
  WorkingDays nvarchar(100) NOT NULL,
  WorkStartMinuteUtc int NOT NULL,
  WorkEndMinuteUtc int NOT NULL,
  UpdatedAtUtc datetime2 NOT NULL
 );
END;

IF OBJECT_ID(N'dbo.S3CompanyHolidays', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S3CompanyHolidays(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  CompanyId uniqueidentifier NOT NULL,
  HolidayDate date NOT NULL,
  Name nvarchar(200) NOT NULL,
  CreatedAtUtc datetime2 NOT NULL,
  CONSTRAINT UQ_S3Holiday UNIQUE(CompanyId, HolidayDate)
 );
END;

IF OBJECT_ID(N'dbo.S3ApplicationSlas', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S3ApplicationSlas(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  ApplicationId uniqueidentifier NOT NULL UNIQUE,
  CompanyId uniqueidentifier NOT NULL,
  OpenedAtUtc datetime2 NOT NULL,
  DueAtUtc datetime2 NOT NULL,
  RespondedAtUtc datetime2 NULL,
  Status nvarchar(30) NOT NULL,
  CONSTRAINT FK_S3Sla_Applications FOREIGN KEY(ApplicationId) REFERENCES dbo.S2Applications(Id) ON DELETE CASCADE
 );
 CREATE INDEX IX_S3Sla_Company_Status_Due ON dbo.S3ApplicationSlas(CompanyId,Status,DueAtUtc);
END;

IF OBJECT_ID(N'dbo.S3Reviews', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S3Reviews(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  ApplicationId uniqueidentifier NOT NULL,
  ReviewerUserId uniqueidentifier NOT NULL,
  TargetId uniqueidentifier NOT NULL,
  TargetType nvarchar(30) NOT NULL,
  Rating int NOT NULL,
  Comment nvarchar(2000) NOT NULL,
  Status nvarchar(30) NOT NULL,
  CreatedAtUtc datetime2 NOT NULL,
  ModeratedAtUtc datetime2 NULL,
  ModeratedByUserId uniqueidentifier NULL,
  ModerationReason nvarchar(1000) NULL,
  CONSTRAINT FK_S3Reviews_Applications FOREIGN KEY(ApplicationId) REFERENCES dbo.S2Applications(Id) ON DELETE CASCADE,
  CONSTRAINT UQ_S3Review_Reviewer UNIQUE(ApplicationId,ReviewerUserId)
 );
 CREATE INDEX IX_S3Reviews_Target_Status ON dbo.S3Reviews(TargetId,TargetType,Status);
END;

IF OBJECT_ID(N'dbo.S3ReviewReports', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S3ReviewReports(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  ReviewId uniqueidentifier NOT NULL,
  ReporterUserId uniqueidentifier NOT NULL,
  Reason nvarchar(1000) NOT NULL,
  CreatedAtUtc datetime2 NOT NULL,
  CONSTRAINT FK_S3Reports_Reviews FOREIGN KEY(ReviewId) REFERENCES dbo.S3Reviews(Id) ON DELETE CASCADE,
  CONSTRAINT UQ_S3Reports_Reporter UNIQUE(ReviewId,ReporterUserId)
 );
END;
";
        await db.Database.ExecuteSqlRawAsync(sql);

        // Additive migration: safe for existing databases and existing SLA records.
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.S3ApplicationSlas', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.S3ApplicationSlas', N'WorkingDaysSnapshot') IS NULL
        ALTER TABLE dbo.S3ApplicationSlas
        ADD WorkingDaysSnapshot nvarchar(100) NOT NULL
        CONSTRAINT DF_S3Sla_WorkingDays DEFAULT N'1,2,3,4,5' WITH VALUES;

    IF COL_LENGTH(N'dbo.S3ApplicationSlas', N'HolidaysSnapshot') IS NULL
        ALTER TABLE dbo.S3ApplicationSlas
        ADD HolidaysSnapshot nvarchar(max) NOT NULL
        CONSTRAINT DF_S3Sla_Holidays DEFAULT N'' WITH VALUES;

    IF COL_LENGTH(N'dbo.S3ApplicationSlas', N'TimeZoneIdSnapshot') IS NULL
        ALTER TABLE dbo.S3ApplicationSlas
        ADD TimeZoneIdSnapshot nvarchar(100) NOT NULL
        CONSTRAINT DF_S3Sla_TimeZone DEFAULT N'UTC' WITH VALUES;
END;
");

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.CandidateProfiles', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.CandidateProfiles', N'WorkEligibility') IS NULL
BEGIN
    ALTER TABLE dbo.CandidateProfiles
    ADD WorkEligibility nvarchar(500) NULL;
END;
");
    }
}

public sealed record S3MatchRuleRequest(
    string? RequiredSkills,
    int MinimumScore,
    bool RequireLocationMatch);

public sealed record S3CalendarRequest(
    string WorkingDays,
    int WorkStartMinuteUtc,
    int WorkEndMinuteUtc);

public sealed record S3HolidayRequest(DateOnly HolidayDate, string Name);
public sealed record S3ReviewRequest(int Rating, string Comment);
public sealed record S3ReportRequest(string Reason);
public sealed record S3ModerationRequest(string Status, string? Reason);

public static class Sprint3Api
{
    private static Guid UserId(ClaimsPrincipal p)
    {
        return Guid.TryParse(
            p.FindFirstValue(ClaimTypes.NameIdentifier) ??
            p.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
    }

    private static Task<bool> CanViewCompany(
        JobDropDbContext db, Guid companyId, Guid userId)
    {
        return db.CompanyMembers.AnyAsync(x =>
            x.CompanyId == companyId && x.UserId == userId);
    }

    private static Task<bool> CanManageCompany(
        JobDropDbContext db, Guid companyId, Guid userId)
    {
        return db.CompanyMembers.AnyAsync(x =>
            x.CompanyId == companyId &&
            x.UserId == userId &&
            (x.Role == CompanyMemberRole.Owner ||
             x.Role == CompanyMemberRole.Admin ||
             x.Role == CompanyMemberRole.Recruiter));
    }

    private static async Task<DateTime> CalculateSlaDueUtc(
        DateTime openedAtUtc,
        string workingDaysSnapshot,
        string holidaysSnapshot,
        string timeZoneIdSnapshot)
    {
        var workingDays = workingDaysSnapshot
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var day) ? day : -1)
            .Where(x => x >= 0 && x <= 6)
            .ToHashSet();

        if (workingDays.Count == 0)
            workingDays = new HashSet<int> { 1, 2, 3, 4, 5 };

        var holidays = holidaysSnapshot
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => DateOnly.TryParse(x, out var date) ? date : (DateOnly?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZoneIdSnapshot) ? "UTC" : timeZoneIdSnapshot);
        }
        catch
        {
            zone = TimeZoneInfo.Utc;
        }

        var currentUtc = DateTime.SpecifyKind(openedAtUtc, DateTimeKind.Utc);
        var remaining = TimeSpan.FromHours(48);
        var safety = 0;

        while (remaining > TimeSpan.Zero && safety++ < 20000)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, zone);
            var localDate = DateOnly.FromDateTime(local);

            var isWorkingDay =
                workingDays.Contains((int)local.DayOfWeek) &&
                !holidays.Contains(localDate);

            if (!isWorkingDay)
            {
                var nextLocalMidnight = local.Date.AddDays(1);
                var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(nextLocalMidnight, DateTimeKind.Unspecified), zone);

                if (nextUtc <= currentUtc)
                    nextUtc = currentUtc.AddHours(1);

                currentUtc = nextUtc;
                continue;
            }

            // Consume elapsed time only while the local date is a working day.
            var nextLocalMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified), zone);

            if (nextLocalMidnightUtc <= currentUtc)
                nextLocalMidnightUtc = currentUtc.AddHours(1);

            var available = nextLocalMidnightUtc - currentUtc;
            var consumed = available < remaining ? available : remaining;

            currentUtc = currentUtc.Add(consumed);
            remaining -= consumed;
        }

        if (remaining > TimeSpan.Zero)
            throw new InvalidOperationException("SLA deadline calculation exceeded its safety limit.");

        return DateTime.SpecifyKind(currentUtc, DateTimeKind.Utc);
    }

    private static async Task<(int Score, bool Eligible, string[] Missing)> ScoreCandidate(
        JobDropDbContext db, S2Job job, Guid profileId)
    {
        var candidate = await db.CandidateProfiles
            .AsNoTracking()
            .Where(x => x.Id == profileId)
            .Include(x => x.Skills)
            .Include(x => x.WorkExperiences)
            .Include(x => x.Educations)
                .ThenInclude(x => x.Institution)
            .Include(x => x.Certifications)
            .FirstOrDefaultAsync();
        if (candidate is null)
            return (0, false, new[] { "Candidate profile not found" });

        static string Normalize(string? value) =>
            (value ?? "").Trim().ToLowerInvariant();

        static bool ContainsEvidence(IEnumerable<string?> values, string required)
        {
            var target = Normalize(required);
            if (target.Length == 0) return true;

            return values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Normalize)
                .Any(x => x == target || x.Contains(target) || target.Contains(x));
        }

        var evidence = new List<string?>();

        evidence.AddRange(candidate.Skills.Select(x => x.Name));
        evidence.AddRange(candidate.WorkExperiences.SelectMany(x =>
            new[] { x.JobTitle, x.Description, x.EmployerName }));
        evidence.AddRange(candidate.Educations.SelectMany(x =>
            new[] { x.Qualification, x.FieldOfStudy, x.Program, x.Faculty, x.Campus, x.Institution?.Name }));
        evidence.AddRange(candidate.Certifications.SelectMany(x =>
            new[] { x.Name, x.Issuer }));

        var requirements = await db.Set<S2JobRequirement>()
            .AsNoTracking()
            .Where(x => x.JobId == job.Id)
            .ToListAsync();

        var preferences = await db.Set<S2JobPreference>()
            .AsNoTracking()
            .Where(x => x.JobId == job.Id)
            .ToListAsync();

        var rule = await db.Set<S3MatchRule>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.JobId == job.Id);

        var missing = new List<string>();
        var scoredCriteria = new List<bool>();

        foreach (var requirement in requirements)
        {
            var description = requirement.Description?.Trim() ?? "";
            if (description.Length == 0) continue;

            var matched = ContainsEvidence(evidence, description);

            if (requirement.IsMandatory && !matched)
                missing.Add(description);

            scoredCriteria.Add(matched);
        }

        var requiredSkills = (rule?.RequiredSkills ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var skill in requiredSkills)
        {
            var matched = ContainsEvidence(candidate.Skills.Select(x => x.Name), skill);
            if (!matched) missing.Add("Skill: " + skill);
            scoredCriteria.Add(matched);
        }

        var locationRequired = rule?.RequireLocationMatch ?? false;
        if (locationRequired && !string.IsNullOrWhiteSpace(job.Country))
        {
            var locationMatched = string.Equals(
                Normalize(candidate.ResidenceCountry),
                Normalize(job.Country),
                StringComparison.Ordinal);

            if (!locationMatched)
                missing.Add("Location: " + job.Country);

            scoredCriteria.Add(locationMatched);
        }

        if (!string.IsNullOrWhiteSpace(job.WorkArrangement))
        {
            var arrangementMatched =
                string.IsNullOrWhiteSpace(candidate.PreferredWorkArrangement) ||
                Normalize(candidate.PreferredWorkArrangement) == "any" ||
                Normalize(candidate.PreferredWorkArrangement) == Normalize(job.WorkArrangement);

            scoredCriteria.Add(arrangementMatched);
        }

        foreach (var preference in preferences)
        {
            var type = Normalize(preference.PreferenceType);
            var value = preference.Value?.Trim() ?? "";
            if (value.Length == 0) continue;

            bool matched;

            if (type.Contains("institution") || type.Contains("university"))
                matched = ContainsEvidence(
                    candidate.Educations.Select(x => x.Institution?.Name), value);
            else if (type.Contains("qualification") || type.Contains("education"))
                matched = ContainsEvidence(
                    candidate.Educations.SelectMany(x =>
                        new[] { x.Qualification, x.FieldOfStudy, x.Program }), value);
            else if (type.Contains("certification"))
                matched = ContainsEvidence(candidate.Certifications.Select(x => x.Name), value);
            else if (type.Contains("eligibility") || type.Contains("work eligibility"))
                matched = ContainsEvidence(new[] { candidate.WorkEligibility }, value);
            else if (type.Contains("location") || type.Contains("country"))
                matched = ContainsEvidence(new[] {
                    candidate.ResidenceCountry, candidate.ResidenceCity
                }, value);
            else if (type.Contains("arrangement") || type.Contains("work"))
                matched = string.IsNullOrWhiteSpace(candidate.PreferredWorkArrangement) ||
                    Normalize(candidate.PreferredWorkArrangement) == Normalize(value);
            else
                matched = ContainsEvidence(evidence, value);

            // Preferences influence ranking, not hard eligibility.
            scoredCriteria.Add(matched);
        }

        var score = scoredCriteria.Count == 0
            ? 100
            : (int)Math.Round(scoredCriteria.Count(x => x) * 100.0 / scoredCriteria.Count);

        var minimum = rule?.MinimumScore ?? 50;
        var distinctMissing = missing
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (score, distinctMissing.Length == 0 && score >= minimum, distinctMissing);
    }

    public static void MapSprint3(this WebApplication app)
    {
        var jobs = app.MapGroup("/api/jobs");

        jobs.MapPut("/{jobId:guid}/matching-rules", async (
            Guid jobId,
            S3MatchRuleRequest request,
            ClaimsPrincipal principal,
            JobDropDbContext db) =>
        {
            if (request.MinimumScore < 0 || request.MinimumScore > 100)
                return Results.BadRequest(new { error = "MinimumScore must be between 0 and 100." });

            var job = await db.Set<S2Job>().FirstOrDefaultAsync(x => x.Id == jobId);
            if (job is null) return Results.NotFound();

            if (!await CanManageCompany(db, job.CompanyId, UserId(principal)))
                return Results.Forbid();

            var rule = await db.Set<S3MatchRule>()
                .FirstOrDefaultAsync(x => x.JobId == jobId);

            if (rule is null)
            {
                rule = new S3MatchRule { JobId = jobId };
                db.Set<S3MatchRule>().Add(rule);
            }

            rule.RequiredSkills = request.RequiredSkills?.Trim() ?? "";
            rule.MinimumScore = request.MinimumScore;
            rule.RequireLocationMatch = request.RequireLocationMatch;
            rule.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(rule);
        }).RequireAuthorization();

        jobs.MapGet("/{jobId:guid}/matching-rules", async (
            Guid jobId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var job = await db.Set<S2Job>().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == jobId);
            if (job is null) return Results.NotFound();

            if (!await CanViewCompany(db, job.CompanyId, UserId(principal)) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            return Results.Ok(await db.Set<S3MatchRule>()
                .AsNoTracking().FirstOrDefaultAsync(x => x.JobId == jobId));
        }).RequireAuthorization();

        var matching = app.MapGroup("/api/matching").RequireAuthorization();

        matching.MapGet("/jobs", async (
            ClaimsPrincipal principal, JobDropDbContext db,
            int page = 1, int pageSize = 20) =>
        {
            var userId = UserId(principal);
            var profileId = await db.CandidateProfiles
                .Where(x => x.UserId == userId)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync();

            if (profileId is null) return Results.Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var published = await db.Set<S2Job>().AsNoTracking()
                .Where(x => x.Status == "Published" &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > DateTime.UtcNow))
                .OrderByDescending(x => x.PublishedAtUtc)
                .Take(500).ToListAsync();

            var results = new List<object>();

            foreach (var job in published)
            {
                var match = await ScoreCandidate(db, job, profileId.Value);
                if (!match.Eligible) continue;

                results.Add(new
                {
                    job.Id, job.Title, job.CompanyId, job.Country, job.City,
                    job.WorkArrangement, job.EmploymentType,
                    match.Score, match.Eligible, match.Missing
                });
            }

            return Results.Ok(new
            {
                total = results.Count,
                page,
                pageSize,
                items = results.Skip((page - 1) * pageSize)
                    .Take(pageSize).ToList()
            });
        });

        matching.MapGet("/jobs/{jobId:guid}/candidates", async (
            Guid jobId, ClaimsPrincipal principal, JobDropDbContext db,
            int page = 1, int pageSize = 50) =>
        {
            var userId = UserId(principal);
            var job = await db.Set<S2Job>().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == jobId);

            if (job is null) return Results.NotFound();

            if (!await CanViewCompany(db, job.CompanyId, userId) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var profileIds = await db.Set<S2Application>()
                .AsNoTracking()
                .Where(x => x.JobId == jobId &&
                    x.Status != "Withdrawn")
                .Select(x => x.CandidateProfileId)
                .Distinct().ToListAsync();

            var results = new List<(int Score, object Item)>();

            foreach (var id in profileIds)
            {
                var match = await ScoreCandidate(db, job, id);
                var profile = await db.CandidateProfiles.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (profile is null) continue;

                results.Add((match.Score, (object)new
                {
                    candidateProfileId = id,
                    profile.Headline,
                    profile.ResidenceCountry,
                    profile.ResidenceCity,
                    match.Score,
                    match.Eligible,
                    match.Missing
                }));
            }

            return Results.Ok(new
            {
                total = results.Count,
                page,
                pageSize,
                items = results.OrderByDescending(x => x.Score)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => x.Item)
                    .ToList()
            });
        });

        var companies = app.MapGroup("/api/companies")
            .RequireAuthorization();

        companies.MapPut("/{companyId:guid}/calendar", async (
            Guid companyId, S3CalendarRequest request,
            ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            if (!await CanManageCompany(db, companyId, UserId(principal)))
                return Results.Forbid();

            var days = request.WorkingDays.Split(',')
                .Select(x => int.TryParse(x.Trim(), out var d) ? d : -1)
                .ToArray();

            if (days.Length == 0 || days.Any(x => x < 0 || x > 6) ||
                request.WorkStartMinuteUtc < 0 ||
                request.WorkEndMinuteUtc > 1440 ||
                request.WorkStartMinuteUtc >= request.WorkEndMinuteUtc)
                return Results.BadRequest(new { error = "Invalid working calendar." });

            var calendar = await db.Set<S3CompanyCalendar>()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (calendar is null)
            {
                calendar = new S3CompanyCalendar { CompanyId = companyId };
                db.Set<S3CompanyCalendar>().Add(calendar);
            }

            calendar.WorkingDays = string.Join(",", days.Distinct());
            calendar.WorkStartMinuteUtc = request.WorkStartMinuteUtc;
            calendar.WorkEndMinuteUtc = request.WorkEndMinuteUtc;
            calendar.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(calendar);
        });

        companies.MapGet("/{companyId:guid}/calendar", async (
            Guid companyId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            if (!await CanViewCompany(db, companyId, UserId(principal)) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            var calendar = await db.Set<S3CompanyCalendar>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId);

            return Results.Ok(calendar ?? new S3CompanyCalendar
            {
                CompanyId = companyId
            });
        });

        companies.MapPost("/{companyId:guid}/calendar/holidays", async (
            Guid companyId, S3HolidayRequest request,
            ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            if (!await CanManageCompany(db, companyId, UserId(principal)))
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Holiday name is required." });

            var exists = await db.Set<S3CompanyHoliday>().AnyAsync(x =>
                x.CompanyId == companyId && x.HolidayDate == request.HolidayDate);

            if (exists)
                return Results.Conflict(new { error = "Holiday already exists." });

            var holiday = new S3CompanyHoliday
            {
                CompanyId = companyId,
                HolidayDate = request.HolidayDate,
                Name = request.Name.Trim()
            };

            db.Set<S3CompanyHoliday>().Add(holiday);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/calendar/holidays/{holiday.Id}", holiday);
        });

        companies.MapGet("/{companyId:guid}/calendar/holidays", async (
            Guid companyId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            if (!await CanViewCompany(db, companyId, UserId(principal)) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            return Results.Ok(await db.Set<S3CompanyHoliday>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.HolidayDate).ToListAsync());
        });

        var applications = app.MapGroup("/api/applications")
            .RequireAuthorization();

        applications.MapPost("/{applicationId:guid}/open", async (
            Guid applicationId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var application = await db.Set<S2Application>()
                .FirstOrDefaultAsync(x => x.Id == applicationId);

            if (application is null) return Results.NotFound();

            if (!await CanManageCompany(db, application.CompanyId, UserId(principal)) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            if (application.Status is "Withdrawn" or "Rejected" or "Hired")
                return Results.Conflict(new { error = "Application is already closed." });

            var sla = await db.Set<S3ApplicationSla>()
                .FirstOrDefaultAsync(x => x.ApplicationId == applicationId);

            if (sla is not null) return Results.Ok(sla);

            var now = DateTime.UtcNow;

            var calendar = await db.Set<S3CompanyCalendar>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == application.CompanyId)
                ?? new S3CompanyCalendar { CompanyId = application.CompanyId };

            var workingDaysSnapshot = string.Join(",",
                calendar.WorkingDays.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => int.TryParse(x, out var day) ? day : -1)
                    .Where(x => x >= 0 && x <= 6)
                    .Distinct());

            if (string.IsNullOrWhiteSpace(workingDaysSnapshot))
                workingDaysSnapshot = "1,2,3,4,5";

            var holidayDates = await db.Set<S3CompanyHoliday>()
                .AsNoTracking()
                .Where(x => x.CompanyId == application.CompanyId)
                .Select(x => x.HolidayDate)
                .ToListAsync();

            var holidaysSnapshot = string.Join(",",
                holidayDates.OrderBy(x => x).Select(x => x.ToString("yyyy-MM-dd")));

            var timeZoneSnapshot = "UTC";
            var due = await CalculateSlaDueUtc(
                now, workingDaysSnapshot, holidaysSnapshot, timeZoneSnapshot);

            sla = new S3ApplicationSla
            {
                ApplicationId = applicationId,
                CompanyId = application.CompanyId,
                OpenedAtUtc = now,
                DueAtUtc = due,
                Status = "Pending",
                WorkingDaysSnapshot = workingDaysSnapshot,
                HolidaysSnapshot = holidaysSnapshot,
                TimeZoneIdSnapshot = timeZoneSnapshot
            };

            db.Set<S3ApplicationSla>().Add(sla);

            if (application.Status == "Delivered")
            {
                application.Status = "Viewed";
                application.UpdatedAtUtc = now;

                db.Set<S2ApplicationEvent>().Add(new S2ApplicationEvent
                {
                    ApplicationId = applicationId,
                    FromStatus = "Delivered",
                    ToStatus = "Viewed",
                    ActorUserId = UserId(principal),
                    Reason = "Company opened application",
                    CreatedAtUtc = now
                });
            }

            await db.SaveChangesAsync();
            return Results.Ok(sla);
        });

        companies.MapGet("/{companyId:guid}/sla", async (
            Guid companyId, ClaimsPrincipal principal, JobDropDbContext db,
            string? status = null) =>
        {
            if (!await CanViewCompany(db, companyId, UserId(principal)) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            var now = DateTime.UtcNow;
            var query = db.Set<S3ApplicationSla>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            var items = await query.OrderBy(x => x.DueAtUtc).ToListAsync();

            var deliveredApplications = await db.Set<S2Application>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.DeliveredAtUtc.HasValue)
                .Select(x => new { x.Id, x.DeliveredAtUtc, x.Status })
                .ToListAsync();

            var openedIds = items.Select(x => x.ApplicationId).ToHashSet();
            var unopened = deliveredApplications
                .Where(x => !openedIds.Contains(x.Id))
                .ToList();

            var respondedItems = items
                .Where(x => x.RespondedAtUtc.HasValue)
                .ToList();

            var averageResponseHours = respondedItems.Count == 0
                ? (double?)null
                : respondedItems.Average(x =>
                    (x.RespondedAtUtc!.Value - x.OpenedAtUtc).TotalHours);

            return Results.Ok(new
            {
                totalDelivered = deliveredApplications.Count,
                opened = items.Count,
                unopened = unopened.Count,
                pendingResponse = items.Count(x => x.Status == "Pending"),
                overdue = items.Count(x =>
                    x.Status == "Pending" && x.DueAtUtc < now),
                responded = respondedItems.Count,
                responseRatePercent = items.Count == 0
                    ? 0
                    : Math.Round(respondedItems.Count * 100.0 / items.Count, 2),
                averageResponseHours,
                unopenedApplications = unopened,
                items
            });
        });

        applications.MapPost("/{applicationId:guid}/reviews", async (
            Guid applicationId, S3ReviewRequest request,
            ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            if (request.Rating < 1 || request.Rating > 5)
                return Results.BadRequest(new { error = "Rating must be between 1 and 5." });

            if (string.IsNullOrWhiteSpace(request.Comment) ||
                request.Comment.Trim().Length > 2000)
                return Results.BadRequest(new { error = "Comment is required and must be at most 2000 characters." });

            var userId = UserId(principal);
            var application = await db.Set<S2Application>()
                .FirstOrDefaultAsync(x => x.Id == applicationId);

            if (application is null) return Results.NotFound();

            if (application.Status is not ("Hired" or "Rejected" or "Withdrawn"))
                return Results.Conflict(new { error = "Reviews are allowed after the application reaches a final status." });

            var verifiedInteraction = await db.Set<S2ApplicationEvent>()
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ApplicationId == applicationId &&
                    (x.ToStatus == "Screening" ||
                     x.ToStatus == "Shortlisted" ||
                     x.ToStatus == "Assessment" ||
                     x.ToStatus == "InterviewScheduled" ||
                     x.ToStatus == "InterviewCompleted" ||
                     x.ToStatus == "OfferExtended" ||
                     x.ToStatus == "Hired"));

            if (!verifiedInteraction)
                return Results.Conflict(new {
                    error = "A review requires a verified recruitment interaction beyond Viewed."
                });

            var candidate = await db.CandidateProfiles
                .FirstOrDefaultAsync(x => x.Id == application.CandidateProfileId);

            if (candidate is null) return Results.NotFound();

            var candidateIsReviewer = candidate.UserId == userId;
            var companyIsReviewer = await CanViewCompany(db, application.CompanyId, userId);

            if (!candidateIsReviewer && !companyIsReviewer)
                return Results.Forbid();

            if (candidateIsReviewer && companyIsReviewer)
                return Results.Conflict(new { error = "A user cannot review their own company membership." });

            var targetType = candidateIsReviewer ? "Company" : "Candidate";
            var targetId = candidateIsReviewer
                ? application.CompanyId : application.CandidateProfileId;

            var duplicate = await db.Set<S3Review>().AnyAsync(x =>
                x.ApplicationId == applicationId &&
                x.ReviewerUserId == userId);

            if (duplicate)
                return Results.Conflict(new { error = "A review already exists for this application." });

            var review = new S3Review
            {
                ApplicationId = applicationId,
                ReviewerUserId = userId,
                TargetId = targetId,
                TargetType = targetType,
                Rating = request.Rating,
                Comment = request.Comment.Trim(),
                Status = "Pending"
            };

            db.Set<S3Review>().Add(review);
            await db.SaveChangesAsync();

            return Results.Created($"/api/reviews/{review.Id}", review);
        });

        applications.MapGet("/{applicationId:guid}/reviews", async (
            Guid applicationId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var application = await db.Set<S2Application>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == applicationId);

            if (application is null) return Results.NotFound();

            var candidate = await db.CandidateProfiles.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == application.CandidateProfileId);

            if (candidate is null) return Results.NotFound();

            var userId = UserId(principal);
            if (candidate.UserId != userId &&
                !await CanViewCompany(db, application.CompanyId, userId) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            return Results.Ok(await db.Set<S3Review>()
                .AsNoTracking()
                .Where(x => x.ApplicationId == applicationId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync());
        });

        var reviews = app.MapGroup("/api/reviews").RequireAuthorization();

        reviews.MapPost("/{reviewId:guid}/reports", async (
            Guid reviewId, S3ReportRequest request,
            ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "Report reason is required." });

            var review = await db.Set<S3Review>()
                .FirstOrDefaultAsync(x => x.Id == reviewId);

            if (review is null) return Results.NotFound();

            var userId = UserId(principal);
            if (review.ReviewerUserId == userId)
                return Results.BadRequest(new { error = "You cannot report your own review." });

            var duplicate = await db.Set<S3ReviewReport>().AnyAsync(x =>
                x.ReviewId == reviewId && x.ReporterUserId == userId);

            if (duplicate)
                return Results.Conflict(new { error = "You already reported this review." });

            var report = new S3ReviewReport
            {
                ReviewId = reviewId,
                ReporterUserId = userId,
                Reason = request.Reason.Trim()
            };

            db.Set<S3ReviewReport>().Add(report);
            await db.SaveChangesAsync();

            return Results.Created($"/api/reviews/{reviewId}/reports/{report.Id}", report);
        });

        var admin = app.MapGroup("/api/admin/sprint3")
            .RequireAuthorization()
            .RequireAuthorization(policy => policy.RequireRole(Roles.PlatformAdmin));

        admin.MapGet("/reviews", async (JobDropDbContext db, string? status = null) =>
        {
            var query = db.Set<S3Review>().AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            return Results.Ok(await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(500).ToListAsync());
        });

        admin.MapPut("/reviews/{reviewId:guid}/moderation", async (
            Guid reviewId, S3ModerationRequest request,
            ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var status = request.Status?.Trim();

            if (status is not ("Approved" or "Rejected" or "Hidden"))
                return Results.BadRequest(new { error = "Status must be Approved, Rejected or Hidden." });

            var review = await db.Set<S3Review>()
                .FirstOrDefaultAsync(x => x.Id == reviewId);

            if (review is null) return Results.NotFound();

            review.Status = status;
            review.ModeratedAtUtc = DateTime.UtcNow;
            review.ModeratedByUserId = UserId(principal);
            review.ModerationReason = request.Reason?.Trim();

            await db.SaveChangesAsync();
            return Results.Ok(review);
        });

        admin.MapGet("/review-reports", async (JobDropDbContext db) =>
        {
            return Results.Ok(await db.Set<S3ReviewReport>()
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(500).ToListAsync());
        });

        var analytics = app.MapGroup("/api/analytics")
            .RequireAuthorization();

        analytics.MapGet("/candidate", async (
            ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var userId = UserId(principal);
            var profileId = await db.CandidateProfiles
                .Where(x => x.UserId == userId)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync();

            if (profileId is null) return Results.Forbid();

            var apps = await db.Set<S2Application>()
                .AsNoTracking()
                .Where(x => x.CandidateProfileId == profileId.Value)
                .ToListAsync();

            return Results.Ok(new
            {
                totalApplications = apps.Count,
                queued = apps.Count(x => x.Status == "Queued"),
                delivered = apps.Count(x => x.DeliveredAtUtc.HasValue),
                interviews = apps.Count(x =>
                    x.Status is "InterviewScheduled" or "InterviewCompleted"),
                offers = apps.Count(x => x.Status == "OfferExtended"),
                hired = apps.Count(x => x.Status == "Hired"),
                rejected = apps.Count(x => x.Status == "Rejected"),
                withdrawn = apps.Count(x => x.Status == "Withdrawn")
            });
        });

        analytics.MapGet("/companies/{companyId:guid}", async (
            Guid companyId, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            if (!await CanViewCompany(db, companyId, UserId(principal)) &&
                !principal.IsInRole(Roles.PlatformAdmin))
                return Results.Forbid();

            var apps = await db.Set<S2Application>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .ToListAsync();

            var sla = await db.Set<S3ApplicationSla>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .ToListAsync();

            var reviews = await db.Set<S3Review>()
                .AsNoTracking()
                .Where(x => x.TargetId == companyId &&
                    x.TargetType == "Company" && x.Status == "Approved")
                .ToListAsync();

            return Results.Ok(new
            {
                companyId,
                totalApplications = apps.Count,
                byStatus = apps.GroupBy(x => x.Status)
                    .Select(g => new { status = g.Key, count = g.Count() }),
                averageResponseHours = sla
                    .Where(x => x.RespondedAtUtc.HasValue)
                    .Select(x => (double?)(x.RespondedAtUtc!.Value -
                        x.OpenedAtUtc).TotalHours)
                    .Average(),
                pendingSla = sla.Count(x => x.Status == "Pending"),
                overdueSla = sla.Count(x =>
                    x.Status == "Pending" && x.DueAtUtc < DateTime.UtcNow),
                approvedReviewCount = reviews.Count,
                averageReviewRating = reviews.Count == 0
                    ? (double?)null : reviews.Average(x => x.Rating)
            });
        });

        admin.MapGet("/analytics", async (JobDropDbContext db) =>
        {
            var apps = await db.Set<S2Application>()
                .AsNoTracking().ToListAsync();

            var reviews = await db.Set<S3Review>()
                .AsNoTracking().ToListAsync();

            var slas = await db.Set<S3ApplicationSla>()
                .AsNoTracking().ToListAsync();

            return Results.Ok(new
            {
                totalApplications = apps.Count,
                totalReviews = reviews.Count,
                pendingReviews = reviews.Count(x => x.Status == "Pending"),
                approvedReviews = reviews.Count(x => x.Status == "Approved"),
                reportedReviews = await db.Set<S3ReviewReport>()
                    .Select(x => x.ReviewId).Distinct().CountAsync(),
                pendingSla = slas.Count(x => x.Status == "Pending"),
                overdueSla = slas.Count(x =>
                    x.Status == "Pending" && x.DueAtUtc < DateTime.UtcNow),
                applicationStatuses = apps.GroupBy(x => x.Status)
                    .Select(g => new { status = g.Key, count = g.Count() })
            });
        });
    }
}
