using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

public sealed class S4Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "USD";
    public int DurationDays { get; set; } = 30;
    public bool IsActive { get; set; } = true;
}

public sealed class S4Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? CompanyId { get; set; }
    public string Audience { get; set; } = "";
    public string PlanCode { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime StartedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class S4Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "Paymob";
    public string MerchantReference { get; set; } = "";
    public string? ProviderTransactionId { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Pending";
    public string? CheckoutUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }
}

public sealed class S4WebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventKey { get; set; } = "";
    public string Provider { get; set; } = "Paymob";
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class S4Verification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmittedByUserId { get; set; }
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    public string VerificationType { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? AdminNotes { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}

public sealed class S4AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class Sprint4Schema
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<S4Plan>().ToTable("S4Plans").HasKey(x => x.Id);
        b.Entity<S4Plan>().HasIndex(x => x.Code).IsUnique();

        b.Entity<S4Subscription>().ToTable("S4Subscriptions").HasKey(x => x.Id);
        b.Entity<S4Subscription>().HasIndex(x => new { x.UserId, x.Status, x.ExpiresAtUtc });
        b.Entity<S4Subscription>().HasIndex(x => new { x.CompanyId, x.Status, x.ExpiresAtUtc });

        b.Entity<S4Payment>().ToTable("S4Payments").HasKey(x => x.Id);
        b.Entity<S4Payment>().HasIndex(x => x.MerchantReference).IsUnique();
        b.Entity<S4Payment>().HasIndex(x => x.ProviderTransactionId);

        b.Entity<S4WebhookEvent>().ToTable("S4WebhookEvents").HasKey(x => x.Id);
        b.Entity<S4WebhookEvent>().HasIndex(x => x.EventKey).IsUnique();

        b.Entity<S4Verification>().ToTable("S4Verifications").HasKey(x => x.Id);
        b.Entity<S4Verification>().HasIndex(x => new { x.TargetType, x.TargetId, x.VerificationType, x.Status });

        b.Entity<S4AuditLog>().ToTable("S4AuditLogs").HasKey(x => x.Id);
        b.Entity<S4AuditLog>().HasIndex(x => x.CreatedAtUtc);
    }

    public static async Task InitializeAsync(JobDropDbContext db)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.S4Plans', N'U') IS NULL
CREATE TABLE dbo.S4Plans(
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 Code nvarchar(50) NOT NULL UNIQUE,
 Name nvarchar(100) NOT NULL,
 Audience nvarchar(30) NOT NULL,
 AmountCents int NOT NULL,
 Currency nvarchar(10) NOT NULL,
 DurationDays int NOT NULL,
 IsActive bit NOT NULL
);

IF OBJECT_ID(N'dbo.S4Subscriptions', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S4Subscriptions(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  UserId uniqueidentifier NOT NULL,
  CompanyId uniqueidentifier NULL,
  Audience nvarchar(30) NOT NULL,
  PlanCode nvarchar(50) NOT NULL,
  Status nvarchar(30) NOT NULL,
  StartedAtUtc datetime2 NOT NULL,
  ExpiresAtUtc datetime2 NOT NULL,
  CreatedAtUtc datetime2 NOT NULL,
  UpdatedAtUtc datetime2 NULL
 );
 CREATE INDEX IX_S4Subscriptions_User_Status_Expiry
 ON dbo.S4Subscriptions(UserId,Status,ExpiresAtUtc);
 CREATE INDEX IX_S4Subscriptions_Company_Status_Expiry
 ON dbo.S4Subscriptions(CompanyId,Status,ExpiresAtUtc);
END;

IF OBJECT_ID(N'dbo.S4Payments', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S4Payments(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  SubscriptionId uniqueidentifier NOT NULL,
  UserId uniqueidentifier NOT NULL,
  Provider nvarchar(30) NOT NULL,
  MerchantReference nvarchar(100) NOT NULL UNIQUE,
  ProviderTransactionId nvarchar(100) NULL,
  AmountCents int NOT NULL,
  Currency nvarchar(10) NOT NULL,
  Status nvarchar(30) NOT NULL,
  CheckoutUrl nvarchar(2000) NULL,
  CreatedAtUtc datetime2 NOT NULL,
  PaidAtUtc datetime2 NULL
 );
 CREATE INDEX IX_S4Payments_ProviderTransactionId
 ON dbo.S4Payments(ProviderTransactionId);
END;

IF OBJECT_ID(N'dbo.S4WebhookEvents', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S4WebhookEvents(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  EventKey nvarchar(200) NOT NULL UNIQUE,
  Provider nvarchar(30) NOT NULL,
  ReceivedAtUtc datetime2 NOT NULL
 );
END;

IF OBJECT_ID(N'dbo.S4Verifications', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S4Verifications(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  SubmittedByUserId uniqueidentifier NOT NULL,
  TargetType nvarchar(30) NOT NULL,
  TargetId uniqueidentifier NOT NULL,
  VerificationType nvarchar(50) NOT NULL,
  EvidenceReference nvarchar(1000) NOT NULL,
  Status nvarchar(30) NOT NULL,
  AdminNotes nvarchar(1000) NULL,
  ReviewedByUserId uniqueidentifier NULL,
  CreatedAtUtc datetime2 NOT NULL,
  ReviewedAtUtc datetime2 NULL
 );
 CREATE INDEX IX_S4Verifications_Target
 ON dbo.S4Verifications(TargetType,TargetId,VerificationType,Status);
END;

IF OBJECT_ID(N'dbo.S4AuditLogs', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.S4AuditLogs(
  Id uniqueidentifier NOT NULL PRIMARY KEY,
  ActorUserId uniqueidentifier NULL,
  Action nvarchar(100) NOT NULL,
  TargetType nvarchar(50) NOT NULL,
  TargetId nvarchar(100) NOT NULL,
  CreatedAtUtc datetime2 NOT NULL
 );
 CREATE INDEX IX_S4AuditLogs_CreatedAtUtc
 ON dbo.S4AuditLogs(CreatedAtUtc);
END;
""";

        await db.Database.ExecuteSqlRawAsync(sql);

        var plans = new[]
        {
            new { Code = "CANDIDATE_PREMIUM", Name = "Candidate Premium", Audience = "Candidate",
                Amount = ReadAmount("PAYMOB_CANDIDATE_AMOUNT_CENTS", 100) },
            new { Code = "EMPLOYER_PREMIUM", Name = "Employer Premium", Audience = "Employer",
                Amount = ReadAmount("PAYMOB_EMPLOYER_AMOUNT_CENTS", 500) }
        };

        foreach (var p in plans)
        {
            var existing = await db.Set<S4Plan>().FirstOrDefaultAsync(x => x.Code == p.Code);
            if (existing == null)
            {
                db.Set<S4Plan>().Add(new S4Plan
                {
                    Code = p.Code, Name = p.Name, Audience = p.Audience,
                    AmountCents = p.Amount,
                    Currency = Environment.GetEnvironmentVariable("PAYMOB_CURRENCY") ?? "USD",
                    DurationDays = 30, IsActive = true
                });
            }
            else
            {
                existing.Name = p.Name;
                existing.Audience = p.Audience;
                existing.AmountCents = p.Amount;
                existing.Currency = Environment.GetEnvironmentVariable("PAYMOB_CURRENCY") ?? "USD";
                existing.DurationDays = 30;
                existing.IsActive = true;
            }
        }

        await db.SaveChangesAsync();
    }

    private static int ReadAmount(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value : fallback;
}

public sealed record S4CheckoutRequest(string PlanCode, Guid? CompanyId);
public sealed record S4VerificationRequest(string TargetType, Guid TargetId,
    string VerificationType, string EvidenceReference);
public sealed record S4ReviewRequest(string Status, string? Notes);

public static class Sprint4Api
{
    private static Guid UserId(ClaimsPrincipal p) =>
        Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier) ??
                      p.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    private static async Task<bool> IsAdmin(JobDropDbContext db, Guid userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user == null) return false;
        return user.Id != Guid.Empty &&
            await db.UserRoles.AnyAsync(ur => ur.UserId == userId &&
                db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.PlatformAdmin));
    }

    private static async Task<bool> IsCompanyMember(JobDropDbContext db, Guid companyId, Guid userId) =>
        await db.CompanyMembers.AnyAsync(x => x.CompanyId == companyId && x.UserId == userId);

    private static async Task<bool> CanManageCompany(
        JobDropDbContext db, Guid companyId, Guid userId)
    {
        var member = await db.CompanyMembers.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.CompanyId == companyId && x.UserId == userId);

        return member != null &&
            (member.Role == CompanyMemberRole.Owner ||
             member.Role == CompanyMemberRole.Admin);
    }

    private static async Task<bool> CanSearchTalent(
        JobDropDbContext db, Guid companyId, Guid userId)
    {
        var member = await db.CompanyMembers.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.CompanyId == companyId && x.UserId == userId);

        return member != null &&
            (member.Role == CompanyMemberRole.Owner ||
             member.Role == CompanyMemberRole.Admin ||
             member.Role == CompanyMemberRole.Recruiter);
    }

    private static async Task<bool> IsPremium(JobDropDbContext db, Guid userId, Guid? companyId)
    {
        var now = DateTime.UtcNow;
        return await db.Set<S4Subscription>().AnyAsync(x =>
            x.Status == "Active" && x.ExpiresAtUtc > now &&
            (companyId.HasValue
                ? x.CompanyId == companyId
                : x.UserId == userId && x.Audience == "Candidate"));
    }

    public static void MapSprint4(this WebApplication app)
    {
        app.MapGet("/api/sprint4/plans", async (JobDropDbContext db) =>
            Results.Ok(await db.Set<S4Plan>().Where(x => x.IsActive)
                .Select(x => new { x.Code, x.Name, x.Audience, x.AmountCents, x.Currency, x.DurationDays })
                .ToListAsync()));

        var subscriptions = app.MapGroup("/api/sprint4/subscriptions").RequireAuthorization();

        subscriptions.MapGet("/me", async (ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var uid = UserId(principal);
            var rows = await db.Set<S4Subscription>()
                .Where(x => x.UserId == uid || (x.CompanyId != null &&
                    db.CompanyMembers.Any(m => m.CompanyId == x.CompanyId && m.UserId == uid)))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new { x.Id, x.Audience, x.PlanCode, x.Status, x.StartedAtUtc, x.ExpiresAtUtc })
                .ToListAsync();
            return Results.Ok(rows);
        });

        subscriptions.MapPost("/checkout", async (
            S4CheckoutRequest request, ClaimsPrincipal principal,
            JobDropDbContext db, IHttpClientFactory clients, IConfiguration config) =>
        {
            var uid = UserId(principal);
            if (uid == Guid.Empty) return Results.Unauthorized();

            var plan = await db.Set<S4Plan>().FirstOrDefaultAsync(x =>
                x.Code == request.PlanCode && x.IsActive);
            if (plan == null) return Results.NotFound(new { error = "Plan not found." });

            Guid? companyId = null;
            if (plan.Audience == "Employer")
            {
                if (!request.CompanyId.HasValue ||
                    !await CanManageCompany(db, request.CompanyId.Value, uid))
                    return Results.Forbid();
                companyId = request.CompanyId.Value;
            }
            else if (plan.Audience != "Candidate")
                return Results.BadRequest(new { error = "Invalid plan audience." });

            if (plan.Audience == "Candidate" &&
                !await db.CandidateProfiles.AnyAsync(x => x.UserId == uid))
                return Results.BadRequest(new { error = "Candidate profile required." });

            var secret = Environment.GetEnvironmentVariable("PAYMOB_SECRET_KEY");
            var publicKey = Environment.GetEnvironmentVariable("PAYMOB_PUBLIC_KEY");
            var currency = Environment.GetEnvironmentVariable("PAYMOB_CURRENCY") ?? "USD";
            var notificationUrl = Environment.GetEnvironmentVariable("PAYMOB_NOTIFICATION_URL");
            var redirectionUrl = Environment.GetEnvironmentVariable("PAYMOB_REDIRECTION_URL");
            var methodsText = Environment.GetEnvironmentVariable("PAYMOB_INTEGRATION_IDS");

            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(publicKey) ||
                string.IsNullOrWhiteSpace(notificationUrl) || string.IsNullOrWhiteSpace(redirectionUrl) ||
                string.IsNullOrWhiteSpace(methodsText))
                return Results.Problem("Paymob environment configuration is incomplete.", statusCode: 503);

            if (!Uri.TryCreate(notificationUrl, UriKind.Absolute, out var notifyUri) ||
                notifyUri.Scheme != Uri.UriSchemeHttps)
                return Results.BadRequest(new { error = "PAYMOB_NOTIFICATION_URL must be a public HTTPS URL." });

            if (!Uri.TryCreate(redirectionUrl, UriKind.Absolute, out var redirectUri) ||
                redirectUri.Scheme != Uri.UriSchemeHttps)
                return Results.BadRequest(new { error = "PAYMOB_REDIRECTION_URL must be an HTTPS URL." });

            var methods = methodsText.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var n) ? n : 0).Where(x => x > 0).ToArray();

            if (methods.Length == 0)
                return Results.BadRequest(new { error = "No valid Paymob integration IDs configured." });

            if (!string.Equals(currency, plan.Currency, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Configured currency does not match the plan currency." });

            var subscription = new S4Subscription
            {
                UserId = uid, CompanyId = companyId, Audience = plan.Audience,
                PlanCode = plan.Code, Status = "Pending",
                StartedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow
            };

            var reference = "JD4-" + Guid.NewGuid().ToString("N");
            var payment = new S4Payment
            {
                SubscriptionId = subscription.Id, UserId = uid,
                MerchantReference = reference, AmountCents = plan.AmountCents,
                Currency = currency, Status = "Pending"
            };

            db.Set<S4Subscription>().Add(subscription);
            db.Set<S4Payment>().Add(payment);
            await db.SaveChangesAsync();

            var billing = await db.Users
                .Where(x => x.Id == uid)
                .Select(x => new
                {
                    x.Email,
                    x.DisplayName,
                    x.PhoneNumber
                })
                .FirstOrDefaultAsync();

            if (billing == null || string.IsNullOrWhiteSpace(billing.Email))
                return Results.BadRequest(new
                {
                    error = "A valid account email is required for checkout."
                });

            var email = billing.Email;

            var displayParts = (billing.DisplayName ?? "JobDrop User")
                .Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            var firstName = displayParts.Length > 0 ? displayParts[0] : "JobDrop";
            var lastName = displayParts.Length > 1 ? displayParts[1] : "User";

            var phoneNumber = string.IsNullOrWhiteSpace(billing.PhoneNumber)
                ? "NA"
                : billing.PhoneNumber;

            var body = new
            {
                amount = plan.AmountCents,
                currency,
                payment_methods = methods,
                special_reference = reference,
                notification_url = notificationUrl,
                redirection_url = redirectionUrl,
                billing_data = new
                {
                    email,
                    first_name = firstName,
                    last_name = lastName,
                    phone_number = phoneNumber,
                    apartment = "NA",
                    floor = "NA",
                    street = "NA",
                    building = "NA",
                    shipping_method = "NA",
                    postal_code = "NA",
                    city = "NA",
                    country = "EG"
                }
            };

            try
            {
                var client = clients.CreateClient();
                client.BaseAddress = new Uri("https://accept.paymob.com");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Token", secret);

                var response = await client.PostAsJsonAsync("/v1/intention/", body);
                var raw = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    payment.Status = "ProviderError";
                    await db.SaveChangesAsync();
                    return Results.Problem("Paymob rejected the checkout request. Check merchant configuration.",
                        statusCode: 502);
                }

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("client_secret", out var cs))
                    return Results.Problem("Paymob response did not contain a checkout client secret.", statusCode: 502);

                var clientSecret = cs.GetString();
                if (string.IsNullOrWhiteSpace(clientSecret))
                    return Results.Problem("Paymob returned an empty checkout client secret.", statusCode: 502);

                payment.CheckoutUrl =
                    "https://accept.paymob.com/unifiedcheckout/?publicKey=" +
                    Uri.EscapeDataString(publicKey) + "&clientSecret=" +
                    Uri.EscapeDataString(clientSecret);

                await db.SaveChangesAsync();
                return Results.Ok(new { payment.Id, payment.MerchantReference, payment.CheckoutUrl,
                    payment.AmountCents, payment.Currency, subscription.Status });
            }
            catch
            {
                payment.Status = "ProviderError";
                await db.SaveChangesAsync();
                return Results.Problem("Could not create Paymob checkout.", statusCode: 502);
            }
        });

        app.MapPost("/api/sprint4/payments/paymob/webhook", async (
            HttpRequest request, JobDropDbContext db) =>
        {
            var hmacSecret = Environment.GetEnvironmentVariable("PAYMOB_HMAC_SECRET");
            if (string.IsNullOrWhiteSpace(hmacSecret))
                return Results.Problem("Paymob webhook is not configured.", statusCode: 503);

            if (request.ContentLength.HasValue &&
                request.ContentLength.Value > 65536)
                return Results.StatusCode(413);

            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int bytesRead;

            while ((bytesRead = await request.Body.ReadAsync(
                chunk.AsMemory(0, chunk.Length))) > 0)
            {
                if (buffer.Length + bytesRead > 65536)
                    return Results.StatusCode(413);

                await buffer.WriteAsync(chunk.AsMemory(0, bytesRead));
            }

            var raw = Encoding.UTF8.GetString(buffer.ToArray());

            JsonDocument doc;
            try { doc = JsonDocument.Parse(raw); }
            catch { return Results.BadRequest(); }

            using (doc)
            {
                var root = doc.RootElement;
                var obj = root.TryGetProperty("obj", out var nested) ? nested : root;
                var suppliedHmac = request.Query["hmac"].ToString();

                string Get(JsonElement element, string name) =>
                    element.TryGetProperty(name, out var value) ? value.ToString() : "";

                string Nested(JsonElement element, string parent, string child)
                {
                    if (!element.TryGetProperty(parent, out var p) ||
                        !p.TryGetProperty(child, out var c)) return "";
                    return c.ToString();
                }

                var values = new[]
                {
                    Get(obj,"amount_cents"), Get(obj,"created_at"), Get(obj,"currency"),
                    Get(obj,"error_occured").ToLowerInvariant(),
                    Get(obj,"has_parent_transaction").ToLowerInvariant(),
                    Get(obj,"id"), Get(obj,"integration_id"),
                    Get(obj,"is_3d_secure").ToLowerInvariant(),
                    Get(obj,"is_auth").ToLowerInvariant(),
                    Get(obj,"is_capture").ToLowerInvariant(),
                    Get(obj,"is_refunded").ToLowerInvariant(),
                    Get(obj,"is_standalone_payment").ToLowerInvariant(),
                    Get(obj,"is_voided").ToLowerInvariant(),
                    Nested(obj,"order","id"), Get(obj,"owner"),
                    Get(obj,"pending").ToLowerInvariant(),
                    Nested(obj,"source_data","pan"),
                    Nested(obj,"source_data","sub_type"),
                    Nested(obj,"source_data","type"),
                    Get(obj,"success").ToLowerInvariant()
                };

                var message = string.Concat(values);
                using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
                var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();

                if (suppliedHmac.Length != expected.Length)
                    return Results.Unauthorized();

                var a = Encoding.UTF8.GetBytes(expected);
                var b = Encoding.UTF8.GetBytes(suppliedHmac.ToLowerInvariant());
                if (!CryptographicOperations.FixedTimeEquals(a, b))
                    return Results.Unauthorized();

                var reference = Nested(obj, "order", "merchant_order_id");

                if (string.IsNullOrWhiteSpace(reference))
                    reference = Get(obj, "special_reference");

                if (string.IsNullOrWhiteSpace(reference))
                    reference = Get(obj, "merchant_order_id");

                if (string.IsNullOrWhiteSpace(reference))
                    return Results.BadRequest(new
                    {
                        error = "Missing merchant reference."
                    });

                var payment = await db.Set<S4Payment>()
                    .FirstOrDefaultAsync(x => x.MerchantReference == reference);
                if (payment == null) return Results.NotFound();

                var transactionId = Get(obj, "id");

                if (string.IsNullOrWhiteSpace(transactionId))
                    return Results.BadRequest(new
                    {
                        error = "Paymob transaction ID is missing."
                    });

                var eventKey = "paymob:" + transactionId;

                if (await db.Set<S4WebhookEvent>().AnyAsync(x =>
                    x.EventKey == eventKey))
                    return Results.Ok(new
                    {
                        received = true,
                        duplicate = true
                    });

                var success = Get(obj, "success").Equals("true", StringComparison.OrdinalIgnoreCase);
                var pending = Get(obj, "pending").Equals("true", StringComparison.OrdinalIgnoreCase);
                var captured = Get(obj, "is_capture").Equals("true", StringComparison.OrdinalIgnoreCase);
                var refunded = Get(obj, "is_refunded").Equals("true", StringComparison.OrdinalIgnoreCase);
                var voided = Get(obj, "is_voided").Equals("true", StringComparison.OrdinalIgnoreCase);
                var errored = Get(obj, "error_occured").Equals("true", StringComparison.OrdinalIgnoreCase);

                var amountOk = int.TryParse(Get(obj, "amount_cents"), out var amount) &&
                    amount == payment.AmountCents;
                var currencyOk = string.Equals(Get(obj, "currency"), payment.Currency,
                    StringComparison.OrdinalIgnoreCase);

                var allowedIds = (Environment.GetEnvironmentVariable("PAYMOB_INTEGRATION_IDS") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var integrationOk = allowedIds.Contains(Get(obj, "integration_id"));

                await using var transaction =
                    await db.Database.BeginTransactionAsync();

                try
                {
                    var reusedTransaction = await db.Set<S4Payment>()
                        .AnyAsync(x =>
                            x.Id != payment.Id &&
                            x.ProviderTransactionId == transactionId);

                    if (reusedTransaction)
                        return Results.Conflict(new
                        {
                            error = "This provider transaction is already linked to another payment."
                        });

                    db.Set<S4WebhookEvent>().Add(new S4WebhookEvent
                    {
                        EventKey = eventKey
                    });

                    payment.ProviderTransactionId = transactionId;

                    if (success && !pending && captured &&
                        !refunded && !voided && !errored &&
                        amountOk && currencyOk && integrationOk)
                    {
                        if (payment.Status == "Paid")
                            return Results.Conflict(new
                            {
                                error = "Payment has already been processed."
                            });

                        var sub = await db.Set<S4Subscription>()
                            .FirstOrDefaultAsync(x =>
                                x.Id == payment.SubscriptionId);

                        if (sub == null)
                            return Results.NotFound();

                        var plan = await db.Set<S4Plan>()
                            .FirstOrDefaultAsync(x =>
                                x.Code == sub.PlanCode);

                        if (plan == null)
                            return Results.NotFound();

                        var now = DateTime.UtcNow;

                        sub.Status = "Active";
                        sub.StartedAtUtc = now;
                        sub.ExpiresAtUtc = now.AddDays(plan.DurationDays);
                        sub.UpdatedAtUtc = now;

                        payment.Status = "Paid";
                        payment.PaidAtUtc = now;
                    }
                    else
                    {
                        payment.Status = "Rejected";
                    }

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Results.Ok(new
                    {
                        received = true,
                        payment.Status
                    });
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync();
                    db.ChangeTracker.Clear();

                    var duplicate = await db.Set<S4WebhookEvent>()
                        .AnyAsync(x => x.EventKey == eventKey);

                    if (duplicate)
                        return Results.Ok(new
                        {
                            received = true,
                            duplicate = true
                        });

                    throw;
                }
            }
        }).AllowAnonymous();

        var talent = app.MapGroup("/api/sprint4/talent-hunt").RequireAuthorization();

        talent.MapPost("/search", async (
            HttpRequest request, ClaimsPrincipal principal, JobDropDbContext db,
            string? skills, string? country, string? city) =>
        {
            var uid = UserId(principal);
            if (!Guid.TryParse(request.Query["companyId"], out var companyId))
                return Results.BadRequest(new { error = "companyId query parameter is required." });

            if (!await CanSearchTalent(db, companyId, uid))
                return Results.Forbid();

            if (!await IsPremium(db, uid, companyId))
                return Results.StatusCode(403);

            var today = DateTime.UtcNow.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var searches = await db.Set<S4AuditLog>().CountAsync(x =>
                x.TargetType == "Company" &&
                x.TargetId == companyId.ToString() &&
                x.Action == "TalentSearch" &&
                x.CreatedAtUtc >= monthStart);

            var limit = int.TryParse(Environment.GetEnvironmentVariable("SPRINT4_MONTHLY_TALENT_SEARCH_LIMIT"),
                out var configuredLimit) ? Math.Clamp(configuredLimit, 1, 10000) : 100;

            if (searches >= limit)
                return Results.Problem("Monthly talent search quota reached.", statusCode: 429);

            var query = db.CandidateProfiles
                .Where(x => x.IsProfileVisible)
                .Include(x => x.Skills)
                .Include(x => x.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(country))
                query = query.Where(x => x.ResidenceCountry == country);
            if (!string.IsNullOrWhiteSpace(city))
                query = query.Where(x => x.ResidenceCity == city);

            var requiredSkills = (skills ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            var candidates = await query.Take(500).ToListAsync();

            var results = candidates
                .Select(x => new
                {
                    x.Id,
                    x.Headline,
                    x.Summary,
                    x.ResidenceCountry,
                    x.ResidenceCity,
                    Skills = x.Skills.Select(s => s.Name).ToArray(),
                    MatchCount = x.Skills.Count(s => requiredSkills.Any(r =>
                        s.Name.Equals(r, StringComparison.OrdinalIgnoreCase)))
                })
                .Where(x => requiredSkills.Length == 0 || x.MatchCount > 0)
                .OrderByDescending(x => x.MatchCount)
                .Take(50)
                .ToList();

            db.Set<S4AuditLog>().Add(new S4AuditLog
            {
                ActorUserId = uid, Action = "TalentSearch",
                TargetType = "Company", TargetId = companyId.ToString()
            });
            await db.SaveChangesAsync();

            return Results.Ok(new { results, quota = new { used = searches + 1, limit } });
        });

        var verification = app.MapGroup("/api/sprint4/verifications").RequireAuthorization();

        verification.MapPost("/", async (
            S4VerificationRequest request, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var uid = UserId(principal);
            if (uid == Guid.Empty) return Results.Unauthorized();

            var targetType = request.TargetType.Trim();
            var verificationType = request.VerificationType.Trim();

            if (!new[] { "Candidate", "Company" }.Contains(targetType))
                return Results.BadRequest(new { error = "TargetType must be Candidate or Company." });

            if (!new[] { "Education", "Professional", "Company" }.Contains(verificationType))
                return Results.BadRequest(new { error = "Unsupported verification type." });

            if (string.IsNullOrWhiteSpace(request.EvidenceReference) ||
                request.EvidenceReference.Length > 1000)
                return Results.BadRequest(new { error = "EvidenceReference is required and must be <= 1000 characters." });

            if (targetType == "Candidate")
            {
                if (verificationType == "Company" ||
                    !await db.CandidateProfiles.AnyAsync(x =>
                        x.Id == request.TargetId && x.UserId == uid))
                    return Results.Forbid();

                if (!Guid.TryParse(request.EvidenceReference.Trim(), out var evidenceDocumentId) ||
                    !await db.CandidateDocuments.AnyAsync(x =>
                        x.Id == evidenceDocumentId &&
                        x.CandidateProfileId == request.TargetId))
                {
                    return Results.BadRequest(new
                    {
                        error = "EvidenceReference must be the ID of a private document uploaded to this candidate profile."
                    });
                }
            }
            else
            {
                if (verificationType != "Company" ||
                    !await CanManageCompany(db, request.TargetId, uid))
                    return Results.Forbid();
            }

            var pending = await db.Set<S4Verification>().AnyAsync(x =>
                x.TargetType == targetType && x.TargetId == request.TargetId &&
                x.VerificationType == verificationType && x.Status == "Pending");

            if (pending)
                return Results.Conflict(new { error = "A pending request already exists." });

            var item = new S4Verification
            {
                SubmittedByUserId = uid,
                TargetType = targetType,
                TargetId = request.TargetId,
                VerificationType = verificationType,
                EvidenceReference = request.EvidenceReference.Trim()
            };

            db.Set<S4Verification>().Add(item);
            db.Set<S4AuditLog>().Add(new S4AuditLog
            {
                ActorUserId = uid, Action = "VerificationSubmitted",
                TargetType = targetType, TargetId = request.TargetId.ToString()
            });
            await db.SaveChangesAsync();

            return Results.Created($"/api/sprint4/verifications/{item.Id}",
                new { item.Id, item.Status, item.CreatedAtUtc });
        });

        verification.MapGet("/mine", async (ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var uid = UserId(principal);
            var rows = await db.Set<S4Verification>()
                .Where(x => x.SubmittedByUserId == uid)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new
                {
                    x.Id, x.TargetType, x.TargetId, x.VerificationType,
                    x.Status, x.AdminNotes, x.CreatedAtUtc, x.ReviewedAtUtc
                }).ToListAsync();
            return Results.Ok(rows);
        });

        var admin = app.MapGroup("/api/sprint4/admin").RequireAuthorization();

        admin.MapGet("/verifications", async (
            ClaimsPrincipal principal, JobDropDbContext db, string? status) =>
        {
            if (!await IsAdmin(db, UserId(principal))) return Results.Forbid();

            var query = db.Set<S4Verification>().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(x => x.Status == status);

            return Results.Ok(await query.OrderBy(x => x.CreatedAtUtc)
                .Select(x => new
                {
                    x.Id, x.SubmittedByUserId, x.TargetType, x.TargetId,
                    x.VerificationType, x.EvidenceReference, x.Status,
                    x.AdminNotes, x.CreatedAtUtc
                }).ToListAsync());
        });

        admin.MapPut("/verifications/{id:guid}", async (
            Guid id, S4ReviewRequest request, ClaimsPrincipal principal, JobDropDbContext db) =>
        {
            var uid = UserId(principal);
            if (!await IsAdmin(db, uid)) return Results.Forbid();

            if (request.Status is not ("Approved" or "Rejected"))
                return Results.BadRequest(new { error = "Status must be Approved or Rejected." });

            var item = await db.Set<S4Verification>().FirstOrDefaultAsync(x => x.Id == id);
            if (item == null) return Results.NotFound();
            if (item.Status != "Pending")
                return Results.Conflict(new { error = "Verification has already been reviewed." });

            item.Status = request.Status;
            item.AdminNotes = request.Notes?.Trim();
            item.ReviewedByUserId = uid;
            item.ReviewedAtUtc = DateTime.UtcNow;

            if (item.TargetType == "Company" && item.VerificationType == "Company")
            {
                var company = await db.Companies.FindAsync(item.TargetId);
                if (company != null) company.IsVerified = request.Status == "Approved";
            }

            db.Set<S4AuditLog>().Add(new S4AuditLog
            {
                ActorUserId = uid, Action = "VerificationReviewed",
                TargetType = item.TargetType, TargetId = item.TargetId.ToString()
            });

            await db.SaveChangesAsync();
            return Results.Ok(new { item.Id, item.Status, item.ReviewedAtUtc });
        });

        subscriptions.MapGet("/entitlements", async (
            ClaimsPrincipal principal, JobDropDbContext db, Guid? companyId) =>
        {
            var uid = UserId(principal);
            if (companyId.HasValue && !await IsCompanyMember(db, companyId.Value, uid))
                return Results.Forbid();

            var active = await IsPremium(db, uid, companyId);
            return Results.Ok(new
            {
                premium = active,
                audience = companyId.HasValue ? "Employer" : "Candidate",
                features = active
                    ? new[] { "PremiumProfile", "TalentHunting", "AdvancedFilters" }
                    : new[] { "BasicProfile", "BasicSearch" }
            });
        });
    }
}
