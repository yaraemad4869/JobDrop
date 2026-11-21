using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is missing.");
var jwtKey = Environment.GetEnvironmentVariable("JOBDROP_JWT_KEY")
    ?? builder.Configuration["Jwt:SigningKey"]
    ?? (builder.Environment.IsDevelopment()
        ? "DEV_ONLY_CHANGE_THIS_KEY_TO_A_RANDOM_SECRET_AT_LEAST_64_CHARACTERS_LONG_123456789"
        : throw new InvalidOperationException("Set JOBDROP_JWT_KEY in production."));

builder.Services.AddDbContext<JobDropDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddIdentityCore<AppUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 10;
    o.Password.RequireDigit = true;
    o.Password.RequireUppercase = true;
    o.Password.RequireLowercase = true;
    o.Password.RequireNonAlphanumeric = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<JobDropDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "JobDrop",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "JobDropClients",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddPolicy("Api", p =>
    p.AllowAnyHeader().AllowAnyMethod().WithOrigins(
        builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])));

builder.Services.AddHostedService<Sprint2QueueWorker>();

var app = builder.Build();
app.UseCors("Api");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "JobDrop API", sprint = 2, status = "ok" }));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var storageRoot = Path.GetFullPath(builder.Configuration["Storage:Root"] ?? "App_Data/PrivateDocuments");
Directory.CreateDirectory(storageRoot);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobDropDbContext>();
    await db.Database.EnsureCreatedAsync();
    await Sprint2Schema.InitializeAsync(db);
    await Sprint3Schema.InitializeAsync(db);
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

string CreateJwt(AppUser user, IList<string> roles)
{
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };
    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        builder.Configuration["Jwt:Issuer"] ?? "JobDrop",
        builder.Configuration["Jwt:Audience"] ?? "JobDropClients",
        claims,
        expires: DateTime.UtcNow.AddMinutes(
            int.TryParse(builder.Configuration["Jwt:ExpiresMinutes"], out var m) ? m : 60),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

Guid UserId(ClaimsPrincipal principal) =>
    Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
        ? id : Guid.Empty;

var auth = app.MapGroup("/api/auth");
auth.MapPost("/register/candidate", async (
    CandidateRegisterRequest request, UserManager<AppUser> users, JobDropDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { error = "Email and password are required." });
    var user = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName?.Trim() };
    var result = await users.CreateAsync(user, request.Password);
    if (!result.Succeeded) return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    await users.AddToRoleAsync(user, Roles.JobSeeker);
    db.CandidateProfiles.Add(new CandidateProfile { UserId = user.Id });
    await db.SaveChangesAsync();
    var token = await users.GenerateEmailConfirmationTokenAsync(user);
    return Results.Created($"/api/candidates/me", new {
        userId = user.Id, email = user.Email, emailVerificationToken = token,
        note = "Development bootstrap returns the verification token. Connect an email provider before production."
    });
});

auth.MapPost("/register/company", async (
    CompanyRegisterRequest request, UserManager<AppUser> users, JobDropDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) ||
        string.IsNullOrWhiteSpace(request.CompanyName))
        return Results.BadRequest(new { error = "Email, password and company name are required." });
    var user = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName?.Trim() };
    var result = await users.CreateAsync(user, request.Password);
    if (!result.Succeeded) return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    await users.AddToRoleAsync(user, Roles.CompanyOwner);
    var company = new Company
    {
        LegalName = request.CompanyName.Trim(), DisplayName = request.CompanyName.Trim(),
        Industry = request.Industry, Website = request.Website, CreatedAtUtc = DateTime.UtcNow
    };
    db.Companies.Add(company);
    db.CompanyMembers.Add(new CompanyMember { Company = company, UserId = user.Id, Role = CompanyMemberRole.Owner, JoinedAtUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();
    var token = await users.GenerateEmailConfirmationTokenAsync(user);
    return Results.Created($"/api/companies/{company.Id}", new {
        userId = user.Id, companyId = company.Id, emailVerificationToken = token,
        note = "Development bootstrap returns the verification token. Connect an email provider before production."
    });
});

auth.MapPost("/login", async (LoginRequest request, UserManager<AppUser> users) =>
{
    var user = await users.FindByEmailAsync(request.Email);
    if (user is null || !user.IsActive || !await users.CheckPasswordAsync(user, request.Password))
        return Results.Unauthorized();
    if (!user.EmailConfirmed) return Results.Json(new { error = "Email is not verified." }, statusCode: 403);
    var roles = await users.GetRolesAsync(user);
    return Results.Ok(new { accessToken = CreateJwt(user, roles), tokenType = "Bearer", expiresInMinutes = 60, userId = user.Id, roles });
});

auth.MapPost("/verify-email", async (VerifyEmailRequest request, UserManager<AppUser> users) =>
{
    var user = await users.FindByEmailAsync(request.Email);
    if (user is null) return Results.BadRequest(new { error = "Invalid verification request." });
    var result = await users.ConfirmEmailAsync(user, request.Token);
    return result.Succeeded ? Results.Ok(new { message = "Email verified." }) :
        Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
});

auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, UserManager<AppUser> users, IWebHostEnvironment env) =>
{
    var user = await users.FindByEmailAsync(request.Email);
    if (user is null) return Results.Ok(new { message = "If the account exists, reset instructions will be sent." });
    var token = await users.GeneratePasswordResetTokenAsync(user);
    // Replace this development-only token response with an email delivery integration.
    return Results.Ok(new {
        message = "If the account exists, reset instructions will be sent.",
        developmentToken = env.IsDevelopment() ? token : null
    });
});

auth.MapPost("/reset-password", async (ResetPasswordRequest request, UserManager<AppUser> users) =>
{
    var user = await users.FindByEmailAsync(request.Email);
    if (user is null) return Results.BadRequest(new { error = "Invalid reset request." });
    var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
    return result.Succeeded ? Results.Ok(new { message = "Password reset." }) :
        Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
});

auth.MapPost("/change-password", async (ChangePasswordRequest request, ClaimsPrincipal principal, UserManager<AppUser> users) =>
{
    var user = await users.FindByIdAsync(UserId(principal).ToString());
    if (user is null) return Results.Unauthorized();
    var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
    return result.Succeeded ? Results.Ok(new { message = "Password changed." }) :
        Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
}).RequireAuthorization();

auth.MapPost("/deactivate", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
{
    var user = await users.FindByIdAsync(UserId(principal).ToString());
    if (user is null) return Results.Unauthorized();
    user.IsActive = false;
    await users.UpdateAsync(user);
    return Results.Ok(new { message = "Account deactivated." });
}).RequireAuthorization();

auth.MapPost("/deletion-request", async (ClaimsPrincipal principal, JobDropDbContext db) =>
{
    var id = UserId(principal);
    if (id == Guid.Empty) return Results.Unauthorized();
    var existing = await db.AccountDeletionRequests.AnyAsync(x => x.UserId == id && x.CompletedAtUtc == null);
    if (!existing) db.AccountDeletionRequests.Add(new AccountDeletionRequest { UserId = id, RequestedAtUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();
    return Results.Accepted(value: new { message = "Account deletion request recorded for administrative processing." });
}).RequireAuthorization();

var candidates = app.MapGroup("/api/candidates").RequireAuthorization();
candidates.MapGet("/me", async (ClaimsPrincipal principal, JobDropDbContext db) =>
{
    var id = UserId(principal);
    var profile = await db.CandidateProfiles
        .Include(x => x.WorkExperiences).Include(x => x.Educations)
        .Include(x => x.Certifications).Include(x => x.Skills)
        .Include(x => x.Documents).Include(x => x.SavedLocations)
        .FirstOrDefaultAsync(x => x.UserId == id);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
});

candidates.MapPut("/me", async (CandidateProfileRequest request, ClaimsPrincipal principal, JobDropDbContext db, UserManager<AppUser> users) =>
{
    var id = UserId(principal);
    var user = await users.FindByIdAsync(id.ToString());
    if (user is null) return Results.Unauthorized();
    if (!await users.IsInRoleAsync(user, Roles.JobSeeker))
        return Results.Forbid();
    var p = await db.CandidateProfiles.FirstOrDefaultAsync(x => x.UserId == id);
    if (p is null) { p = new CandidateProfile { UserId = id }; db.CandidateProfiles.Add(p); }
    p.Headline = request.Headline; p.Summary = request.Summary; p.ResidenceCountry = request.ResidenceCountry;
    p.ResidenceCity = request.ResidenceCity; p.Phone = request.Phone; p.ProfilePhotoUrl = request.ProfilePhotoUrl;
    p.Languages = request.Languages; p.PreferredWorkArrangement = request.PreferredWorkArrangement;
    p.IsProfileVisible = request.IsProfileVisible; p.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(p);
});

candidates.MapPut("/me/experience", async (
    List<WorkExperienceRequest> items,
    ClaimsPrincipal principal,
    JobDropDbContext db) =>
{
    var id = UserId(principal);

    var profileId = await db.CandidateProfiles
        .Where(x => x.UserId == id)
        .Select(x => (Guid?)x.Id)
        .FirstOrDefaultAsync();

    if (profileId is null)
        return Results.NotFound(new { error = "Create candidate profile first." });

    await using var transaction = await db.Database.BeginTransactionAsync();

    await db.WorkExperiences
        .Where(x => x.CandidateProfileId == profileId.Value)
        .ExecuteDeleteAsync();

    var entities = items.Select(x => new WorkExperience
    {
        CandidateProfileId = profileId.Value,
        JobTitle = x.JobTitle,
        EmployerName = x.EmployerName,
        Description = x.Description,
        StartDate = x.StartDate,
        EndDate = x.EndDate
    }).ToList();

    if (entities.Count > 0)
        db.WorkExperiences.AddRange(entities);

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(entities);
});

candidates.MapPut("/me/education", async (
    List<EducationRequest> items,
    ClaimsPrincipal principal,
    JobDropDbContext db) =>
{
    var id = UserId(principal);

    var profileId = await db.CandidateProfiles
        .Where(x => x.UserId == id)
        .Select(x => (Guid?)x.Id)
        .FirstOrDefaultAsync();

    if (profileId is null)
        return Results.NotFound(new { error = "Create candidate profile first." });

    await using var transaction = await db.Database.BeginTransactionAsync();

    await db.Educations
        .Where(x => x.CandidateProfileId == profileId.Value)
        .ExecuteDeleteAsync();

    var entities = items.Select(x => new EducationRecord
    {
        CandidateProfileId = profileId.Value,
        InstitutionId = x.InstitutionId,
        Campus = x.Campus,
        Faculty = x.Faculty,
        Program = x.Program,
        FieldOfStudy = x.FieldOfStudy,
        Qualification = x.Qualification,
        Status = x.Status,
        GraduationYear = x.GraduationYear,
        EvidenceDocumentId = x.EvidenceDocumentId
    }).ToList();

    if (entities.Count > 0)
        db.Educations.AddRange(entities);

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(entities);
});

candidates.MapPut("/me/skills", async (
    List<string> items,
    ClaimsPrincipal principal,
    JobDropDbContext db) =>
{
    var id = UserId(principal);

    var profileId = await db.CandidateProfiles
        .Where(x => x.UserId == id)
        .Select(x => (Guid?)x.Id)
        .FirstOrDefaultAsync();

    if (profileId is null)
        return Results.NotFound();

    var names = items
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    await using var transaction = await db.Database.BeginTransactionAsync();

    await db.CandidateSkills
        .Where(x => x.CandidateProfileId == profileId.Value)
        .ExecuteDeleteAsync();

    var entities = names.Select(name => new CandidateSkill
    {
        CandidateProfileId = profileId.Value,
        Name = name
    }).ToList();

    if (entities.Count > 0)
        db.CandidateSkills.AddRange(entities);

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(entities);
});

candidates.MapPut("/me/certifications", async (
    List<CertificationRequest> items,
    ClaimsPrincipal principal,
    JobDropDbContext db) =>
{
    var id = UserId(principal);

    var profileId = await db.CandidateProfiles
        .Where(x => x.UserId == id)
        .Select(x => (Guid?)x.Id)
        .FirstOrDefaultAsync();

    if (profileId is null)
        return Results.NotFound();

    await using var transaction = await db.Database.BeginTransactionAsync();

    await db.Certifications
        .Where(x => x.CandidateProfileId == profileId.Value)
        .ExecuteDeleteAsync();

    var entities = items.Select(x => new CandidateCertification
    {
        CandidateProfileId = profileId.Value,
        Name = x.Name,
        Issuer = x.Issuer,
        CredentialId = x.CredentialId,
        IssuedAt = x.IssuedAt,
        ExpiresAt = x.ExpiresAt,
        CredentialUrl = x.CredentialUrl
    }).ToList();

    if (entities.Count > 0)
        db.Certifications.AddRange(entities);

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(entities);
});

candidates.MapPut("/me/saved-locations", async (
    List<SavedLocationRequest> items,
    ClaimsPrincipal principal,
    JobDropDbContext db) =>
{
    var id = UserId(principal);

    var profileId = await db.CandidateProfiles
        .Where(x => x.UserId == id)
        .Select(x => (Guid?)x.Id)
        .FirstOrDefaultAsync();

    if (profileId is null)
        return Results.NotFound();

    await using var transaction = await db.Database.BeginTransactionAsync();

    await db.SavedLocations
        .Where(x => x.CandidateProfileId == profileId.Value)
        .ExecuteDeleteAsync();

    var entities = items.Select(x => new SavedLocation
    {
        CandidateProfileId = profileId.Value,
        Label = x.Label,
        Country = x.Country,
        CityOrDistrict = x.CityOrDistrict,
        Latitude = x.Latitude,
        Longitude = x.Longitude,
        RadiusKm = x.RadiusKm
    }).ToList();

    if (entities.Count > 0)
        db.SavedLocations.AddRange(entities);

    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(entities);
});

candidates.MapPost("/me/documents", async (HttpRequest request, ClaimsPrincipal principal, JobDropDbContext db, IConfiguration config) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data is required." });
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "A file is required." });
    var maxBytes = (long)(int.TryParse(config["Storage:MaxFileSizeMb"], out var mb) ? mb : 10) * 1024 * 1024;
    if (file.Length > maxBytes) return Results.BadRequest(new { error = "File is too large." });
    var allowed = new[] { ".pdf", ".doc", ".docx", ".png", ".jpg", ".jpeg" };
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowed.Contains(ext)) return Results.BadRequest(new { error = "Unsupported file type." });
    var id = UserId(principal);
    var profile = await db.CandidateProfiles.FirstOrDefaultAsync(x => x.UserId == id);
    if (profile is null) return Results.BadRequest(new { error = "Create candidate profile first." });
    var documentId = Guid.NewGuid();
    var safeName = documentId.ToString("N") + ext;
    var path = Path.Combine(storageRoot, safeName);
    await using (var stream = File.Create(path)) await file.CopyToAsync(stream);
    var doc = new CandidateDocument {
        Id = documentId, CandidateProfileId = profile.Id, OriginalFileName = Path.GetFileName(file.FileName),
        StorageName = safeName, ContentType = file.ContentType, SizeBytes = file.Length,
        Category = form["category"].ToString(), UploadedAtUtc = DateTime.UtcNow
    };
    db.CandidateDocuments.Add(doc);
    await db.SaveChangesAsync();
    return Results.Created($"/api/candidates/me/documents/{doc.Id}", new { doc.Id, doc.OriginalFileName, doc.ContentType, doc.SizeBytes });
});

candidates.MapGet("/me/documents/{documentId:guid}", async (Guid documentId, ClaimsPrincipal principal, JobDropDbContext db) =>
{
    var id = UserId(principal);
    var doc = await db.CandidateDocuments.Include(x => x.CandidateProfile)
        .FirstOrDefaultAsync(x => x.Id == documentId && x.CandidateProfile.UserId == id);
    if (doc is null) return Results.NotFound();
    var path = Path.Combine(storageRoot, doc.StorageName);
    if (!File.Exists(path)) return Results.NotFound();
    return Results.File(path, doc.ContentType, doc.OriginalFileName);
});

var companies = app.MapGroup("/api/companies").RequireAuthorization();
companies.MapGet("/mine", async (ClaimsPrincipal principal, JobDropDbContext db) =>
{
    var id = UserId(principal);
    var result = await db.CompanyMembers.Where(x => x.UserId == id)
        .Include(x => x.Company).Select(x => new { x.CompanyId, x.Role, company = x.Company }).ToListAsync();
    return Results.Ok(result);
});

companies.MapPut("/{companyId:guid}", async (Guid companyId, CompanyUpdateRequest request, ClaimsPrincipal principal, JobDropDbContext db) =>
{
    var id = UserId(principal);
    var member = await db.CompanyMembers.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == id);
    if (member is null || member.Role is not (CompanyMemberRole.Owner or CompanyMemberRole.Admin)) return Results.Forbid();
    var c = await db.Companies.FindAsync(companyId);
    if (c is null) return Results.NotFound();
    c.DisplayName = request.DisplayName; c.Industry = request.Industry; c.Size = request.Size;
    c.Description = request.Description; c.Website = request.Website; c.LogoUrl = request.LogoUrl;
    c.ContactEmail = request.ContactEmail; c.ContactPhone = request.ContactPhone;
    c.OperatingAddress = request.OperatingAddress; c.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(c);
});

companies.MapPost("/{companyId:guid}/invitations", async (Guid companyId, InviteCompanyMemberRequest request, ClaimsPrincipal principal, JobDropDbContext db) =>
{
    var id = UserId(principal);
    var member = await db.CompanyMembers.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == id);
    if (member is null || member.Role is not (CompanyMemberRole.Owner or CompanyMemberRole.Admin)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(request.Email)) return Results.BadRequest(new { error = "Email is required." });
    var invite = new CompanyInvitation {
        CompanyId = companyId, Email = request.Email.Trim(), Role = request.Role,
        Token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        InvitedByUserId = id, CreatedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
    };
    db.CompanyInvitations.Add(invite);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/invitations/{invite.Id}", new {
        invite.Id, invite.Email, invite.Role, invite.ExpiresAtUtc, invite.Token,
        note = "Deliver this token by email; do not expose it in production API responses."
    });
});

companies.MapPost("/invitations/accept", async (AcceptInvitationRequest request, ClaimsPrincipal principal, JobDropDbContext db) =>
{
    var id = UserId(principal);
    var invite = await db.CompanyInvitations.FirstOrDefaultAsync(x => x.Token == request.Token && x.AcceptedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow);
    if (invite is null) return Results.BadRequest(new { error = "Invitation is invalid or expired." });
    var user = await db.Users.FindAsync(id);
    if (user?.Email is null || !string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();
    if (!await db.CompanyMembers.AnyAsync(x => x.CompanyId == invite.CompanyId && x.UserId == id))
        db.CompanyMembers.Add(new CompanyMember { CompanyId = invite.CompanyId, UserId = id, Role = invite.Role, JoinedAtUtc = DateTime.UtcNow });
    invite.AcceptedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Invitation accepted." });
});

var reference = app.MapGroup("/api/reference");
reference.MapGet("/institutions", async (JobDropDbContext db) =>
    Results.Ok(await db.Institutions.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync()));
reference.MapGet("/geography/operating-areas", async (JobDropDbContext db) =>
    Results.Ok(await db.OperatingAreas.Where(x => x.IsActive).OrderBy(x => x.Country).ThenBy(x => x.Name).ToListAsync()));

var admin = app.MapGroup("/api/admin").RequireAuthorization().RequireAuthorization(p => p.RequireRole(Roles.PlatformAdmin));
admin.MapPost("/institutions", async (InstitutionRequest request, JobDropDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Name is required." });
    var entity = new AcademicInstitution { Name = request.Name.Trim(), Type = request.Type, IsActive = true };
    db.Institutions.Add(entity); await db.SaveChangesAsync();
    return Results.Created($"/api/reference/institutions/{entity.Id}", entity);
});
admin.MapPut("/geography/operating-areas/{id:guid}/activation", async (Guid id, ActivationRequest request, JobDropDbContext db) =>
{
    var area = await db.OperatingAreas.FindAsync(id);
    if (area is null) return Results.NotFound();
    area.IsActive = request.IsActive; area.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(area);
});
admin.MapPost("/users/{id:guid}/deactivate", async (Guid id, UserManager<AppUser> users) =>
{
    var user = await users.FindByIdAsync(id.ToString());
    if (user is null) return Results.NotFound();
    user.IsActive = false; await users.UpdateAsync(user);
    return Results.Ok(new { message = "User deactivated." });
});

// JOBDROP_SPRINT2_REGISTERED
app.MapSprint2();
// JOBDROP_SPRINT3_REGISTERED
app.MapSprint3();

app.Run();

public static class Roles
{
    public const string JobSeeker = "JobSeeker";
    public const string CompanyOwner = "CompanyOwner";
    public const string CompanyMember = "CompanyMember";
    public const string PlatformAdmin = "PlatformAdmin";
}
public enum CompanyMemberRole { Owner, Admin, Recruiter, Viewer }
public enum EducationStatus { Student, Graduate }
public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class CandidateProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    [JsonIgnore] public AppUser User { get; set; } = null!;
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public string? Phone { get; set; }
    public string? ResidenceCountry { get; set; }
    public string? ResidenceCity { get; set; }
    public string? PreferredWorkArrangement { get; set; }
    public string? Languages { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public bool IsProfileVisible { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<WorkExperience> WorkExperiences { get; set; } = new List<WorkExperience>();
    public ICollection<EducationRecord> Educations { get; set; } = new List<EducationRecord>();
    public ICollection<CandidateSkill> Skills { get; set; } = new List<CandidateSkill>();
    public ICollection<CandidateCertification> Certifications { get; set; } = new List<CandidateCertification>();
    public ICollection<CandidateDocument> Documents { get; set; } = new List<CandidateDocument>();
    public ICollection<SavedLocation> SavedLocations { get; set; } = new List<SavedLocation>();
}
public sealed class WorkExperience
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CandidateProfileId { get; set; }
    public string JobTitle { get; set; } = ""; public string? EmployerName { get; set; }
    public string? Description { get; set; } public DateOnly? StartDate { get; set; } public DateOnly? EndDate { get; set; }
}
public sealed class EducationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CandidateProfileId { get; set; }
    public Guid? InstitutionId { get; set; } public AcademicInstitution? Institution { get; set; }
    public string? Campus { get; set; } public string? Faculty { get; set; } public string? Program { get; set; }
    public string? FieldOfStudy { get; set; } public string? Qualification { get; set; }
    public EducationStatus Status { get; set; } public int? GraduationYear { get; set; } public Guid? EvidenceDocumentId { get; set; }
}
public sealed class CandidateSkill
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CandidateProfileId { get; set; } public string Name { get; set; } = "";
}
public sealed class CandidateCertification
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CandidateProfileId { get; set; }
    public string Name { get; set; } = ""; public string? Issuer { get; set; } public string? CredentialId { get; set; }
    public DateOnly? IssuedAt { get; set; } public DateOnly? ExpiresAt { get; set; } public string? CredentialUrl { get; set; }
}
public sealed class CandidateDocument
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CandidateProfileId { get; set; }
    [JsonIgnore] public CandidateProfile CandidateProfile { get; set; } = null!;
    public string OriginalFileName { get; set; } = ""; public string StorageName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream"; public long SizeBytes { get; set; }
    public string? Category { get; set; } public DateTime UploadedAtUtc { get; set; }
}
public sealed class SavedLocation
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CandidateProfileId { get; set; }
    public string Label { get; set; } = ""; public string Country { get; set; } = "";
    public string CityOrDistrict { get; set; } = ""; public double? Latitude { get; set; }
    public double? Longitude { get; set; } public int? RadiusKm { get; set; }
}
public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string LegalName { get; set; } = "";
    public string DisplayName { get; set; } = ""; public string? Industry { get; set; }
    public string? Size { get; set; } public string? Description { get; set; }
    public string? Website { get; set; } public string? LogoUrl { get; set; }
    public string? ContactEmail { get; set; } public string? ContactPhone { get; set; }
    public string? OperatingAddress { get; set; } public bool IsVerified { get; set; }
    public string SubscriptionState { get; set; } = "Free";
    public DateTime CreatedAtUtc { get; set; } public DateTime? UpdatedAtUtc { get; set; }
}
public sealed class CompanyMember
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!; public Guid UserId { get; set; }
    [JsonIgnore] public AppUser User { get; set; } = null!; public CompanyMemberRole Role { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}
public sealed class CompanyInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid CompanyId { get; set; }
    public string Email { get; set; } = ""; public CompanyMemberRole Role { get; set; }
    public string Token { get; set; } = ""; public Guid InvitedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}
public sealed class AcademicInstitution
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = "";
    public string? Type { get; set; } public bool IsActive { get; set; } = true;
}
public sealed class OperatingArea
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Country { get; set; } = "";
    public string Name { get; set; } = ""; public string? CityOrDistrict { get; set; }
    public string? TimeZoneId { get; set; } public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; } public string BoundarySource { get; set; } = "NotConfigured";
    public bool IsActive { get; set; } = true; public DateTime? UpdatedAtUtc { get; set; }
}
public sealed class AccountDeletionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; }
    public DateTime RequestedAtUtc { get; set; } public DateTime? CompletedAtUtc { get; set; }
}
public sealed class JobDropDbContext(DbContextOptions<JobDropDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
    public DbSet<WorkExperience> WorkExperiences => Set<WorkExperience>();
    public DbSet<EducationRecord> Educations => Set<EducationRecord>();
    public DbSet<CandidateSkill> CandidateSkills => Set<CandidateSkill>();
    public DbSet<CandidateCertification> Certifications => Set<CandidateCertification>();
    public DbSet<CandidateDocument> CandidateDocuments => Set<CandidateDocument>();
    public DbSet<SavedLocation> SavedLocations => Set<SavedLocation>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<AcademicInstitution> Institutions => Set<AcademicInstitution>();
    public DbSet<OperatingArea> OperatingAreas => Set<OperatingArea>();
    public DbSet<AccountDeletionRequest> AccountDeletionRequests => Set<AccountDeletionRequest>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        Sprint2Schema.Configure(b);
        Sprint3Schema.Configure(b);
        b.Entity<CandidateProfile>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<CandidateProfile>().HasOne(x => x.User).WithOne().HasForeignKey<CandidateProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CompanyMember>().HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique();
        b.Entity<CompanyMember>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CompanyInvitation>().HasIndex(x => x.Token).IsUnique();
        b.Entity<CompanyInvitation>().HasIndex(x => new { x.CompanyId, x.Email, x.AcceptedAtUtc });
        b.Entity<CandidateSkill>().HasIndex(x => new { x.CandidateProfileId, x.Name });
        b.Entity<OperatingArea>().HasIndex(x => new { x.Country, x.Name }).IsUnique();
        b.Entity<AcademicInstitution>().HasIndex(x => x.Name).IsUnique();
        b.Entity<CandidateDocument>().HasIndex(x => x.CandidateProfileId);
        b.Entity<AccountDeletionRequest>().HasIndex(x => new { x.UserId, x.CompletedAtUtc });
        b.Entity<Company>().Property(x => x.LegalName).HasMaxLength(200);
        b.Entity<Company>().Property(x => x.DisplayName).HasMaxLength(200);
        b.Entity<OperatingArea>().Property(x => x.BoundarySource).HasMaxLength(100);
    }
}
public sealed class JobDropDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<JobDropDbContext>
{
    public JobDropDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false).AddEnvironmentVariables().Build();
        var options = new DbContextOptionsBuilder<JobDropDbContext>()
            .UseSqlServer(config.GetConnectionString("DefaultConnection")).Options;
        return new JobDropDbContext(options);
    }
}
public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { Roles.JobSeeker, Roles.CompanyOwner, Roles.CompanyMember, Roles.PlatformAdmin })
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
        var db = services.GetRequiredService<JobDropDbContext>();
        var adminEmail = Environment.GetEnvironmentVariable("JOBDROP_ADMIN_EMAIL");
        var adminPassword = Environment.GetEnvironmentVariable("JOBDROP_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var userManager = services.GetRequiredService<UserManager<AppUser>>();
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser is null)
            {
                adminUser = new AppUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true, DisplayName = "Platform Administrator" };
                var created = await userManager.CreateAsync(adminUser, adminPassword);
                if (!created.Succeeded)
                    throw new InvalidOperationException("Could not create bootstrap admin: " + string.Join("; ", created.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(adminUser, Roles.PlatformAdmin);
            }
        }
        var institutions = new[] { "Cairo University", "Ain Shams University", "Alexandria University", "ITI", "AUC", "GUC" };
        foreach (var name in institutions)
            if (!await db.Institutions.AnyAsync(x => x.Name == name))
                db.Institutions.Add(new AcademicInstitution { Name = name, IsActive = true });
        var areas = new (string Country, string Name, string? City, string Zone)[]
        {
            ("United Arab Emirates", "Dubai", "Dubai", "Asia/Dubai"),
            ("Saudi Arabia", "Jeddah", "Jeddah", "Asia/Riyadh"),
            ("Saudi Arabia", "Dammam", "Dammam", "Asia/Riyadh"),
            ("Saudi Arabia", "Al Khobar", "Al Khobar", "Asia/Riyadh"),
            ("Qatar", "Qatar Nationwide", null, "Asia/Qatar"),
            ("Bahrain", "Bahrain Nationwide", null, "Asia/Bahrain"),
            ("Egypt", "Dokki", "Dokki", "Africa/Cairo"),
            ("Egypt", "Agouza", "Agouza", "Africa/Cairo"),
            ("Egypt", "Fifth Settlement", "Fifth Settlement", "Africa/Cairo"),
            ("Egypt", "Smart Village", "Smart Village", "Africa/Cairo")
        };
        foreach (var a in areas)
            if (!await db.OperatingAreas.AnyAsync(x => x.Country == a.Country && x.Name == a.Name))
                db.OperatingAreas.Add(new OperatingArea {
                    Country = a.Country, Name = a.Name, CityOrDistrict = a.City,
                    TimeZoneId = a.Zone, BoundarySource = "NotConfigured", IsActive = true
                });
        await db.SaveChangesAsync();
    }
}
public record CandidateRegisterRequest(string Email, string Password, string? DisplayName);
public record CompanyRegisterRequest(string Email, string Password, string CompanyName, string? DisplayName, string? Industry, string? Website);
public record LoginRequest(string Email, string Password);
public record VerifyEmailRequest(string Email, string Token);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record CandidateProfileRequest(string? Headline, string? Summary, string? Phone, string? ResidenceCountry,
    string? ResidenceCity, string? PreferredWorkArrangement, string? Languages, string? ProfilePhotoUrl, bool IsProfileVisible);
public record WorkExperienceRequest(string JobTitle, string? EmployerName, string? Description, DateOnly? StartDate, DateOnly? EndDate);
public record EducationRequest(Guid? InstitutionId, string? Campus, string? Faculty, string? Program,
    string? FieldOfStudy, string? Qualification, EducationStatus Status, int? GraduationYear, Guid? EvidenceDocumentId);
public record CertificationRequest(string Name, string? Issuer, string? CredentialId, DateOnly? IssuedAt, DateOnly? ExpiresAt, string? CredentialUrl);
public record SavedLocationRequest(string Label, string Country, string CityOrDistrict, double? Latitude, double? Longitude, int? RadiusKm);
public record CompanyUpdateRequest(string DisplayName, string? Industry, string? Size, string? Description,
    string? Website, string? LogoUrl, string? ContactEmail, string? ContactPhone, string? OperatingAddress);
public record InviteCompanyMemberRequest(string Email, CompanyMemberRole Role);
public record AcceptInvitationRequest(string Token);
public record InstitutionRequest(string Name, string? Type);
public record ActivationRequest(bool IsActive);

