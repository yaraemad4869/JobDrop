# JobDrop Sprint 1 API

## Run
1. Install .NET 10 SDK and SQL Server / LocalDB.
2. Set `JOBDROP_JWT_KEY` to a strong random secret outside Development.
3. Update `ConnectionStrings:DefaultConnection` in appsettings.json.
4. Run `dotnet run`.

The application applies EF Core migrations on startup and seeds:
- Roles: JobSeeker, CompanyOwner, CompanyMember, PlatformAdmin
- Institutions: Cairo University, Ain Shams University, Alexandria University, ITI, AUC, GUC
- Approved operating-area records from the Sprint 1 scope

Set `JOBDROP_ADMIN_EMAIL` and `JOBDROP_ADMIN_PASSWORD` before first startup to create the initial platform administrator.
The script creates an EF Core InitialCreate migration and applies migrations on startup.

Before production, configure a real email provider, private object storage, strict CORS, HTTPS,
a secrets manager, malware scanning, content-signature validation, and retention policies.

## Important implementation notes
- Email verification and password-reset tokens are returned only as development bootstrap values;
  connect an email delivery provider before production.
- Company invitation tokens are returned by the endpoint for development integration; email them securely
  and do not expose them in production responses.
- Operating-area rows are seeded without fabricated boundary polygons or coordinates.
  Add verified boundary geometry/data before enabling precise point-in-polygon job validation.
- This Sprint 1 API does not implement job posting, applications, quotas, queues, matching, billing or Hunting;
  those belong to later sprints.
- The document upload endpoint has extension/size checks. Production should add malware scanning,
  content-signature validation, storage encryption and retention policies.
