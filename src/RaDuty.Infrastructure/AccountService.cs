using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RaDuty.Application;
using RaDuty.Domain;

namespace RaDuty.Infrastructure;

public sealed class AccountService(
    RaDutyDbContext db,
    UserManager<ApplicationAccount> accountManager,
    IPasswordHasher<ApplicationAccount> passwordHasher,
    IOptions<IdentityOptions> identityOptions,
    IConfiguration configuration,
    ICurrentUserService currentUserService) : IAccountService
{
    private const int MaximumPasswordLength = 128;
    private const string TemporaryPasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%*-_";
    private static readonly ApplicationAccount DummyAccount = new() { Id = Guid.Empty, UserName = "invalid" };
    private static readonly string DummyHash = new PasswordHasher<ApplicationAccount>()
        .HashPassword(DummyAccount, "This-password-is-never-used-2026!");
    private static readonly HashSet<string> BlockedPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "passwordpassword", "password123456", "qwertyqwerty123", "williampennuniversity",
        "williampenn2026", "eltsedormpassword", "eltsehallpassword"
    };

    public async Task<AuthenticatedAccountDto> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!IsAllowedEmail(email) || string.IsNullOrEmpty(request.Password) || request.Password.Length > MaximumPasswordLength)
        {
            passwordHasher.VerifyHashedPassword(DummyAccount, DummyHash, request.Password ?? string.Empty);
            throw InvalidCredentials();
        }

        var normalizedEmail = accountManager.NormalizeEmail(email);
        var account = await db.Users.Include(x => x.User).ThenInclude(x => x.HallMemberships)
            .ThenInclude(x => x.ResidenceHall)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        if (account is null)
        {
            passwordHasher.VerifyHashedPassword(DummyAccount, DummyHash, request.Password);
            throw InvalidCredentials();
        }

        var now = DateTimeOffset.UtcNow;
        if (account.LockoutEnabled && account.LockoutEnd > now)
            throw new AppException(429, "SIGN_IN_TEMPORARILY_LOCKED", "Sign-in is temporarily unavailable. Wait 15 minutes and try again.");

        var verification = passwordHasher.VerifyHashedPassword(account, account.PasswordHash ?? string.Empty, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            account.AccessFailedCount++;
            if (account.LockoutEnabled && account.AccessFailedCount >= identityOptions.Value.Lockout.MaxFailedAccessAttempts)
            {
                account.LockoutEnd = now.Add(identityOptions.Value.Lockout.DefaultLockoutTimeSpan);
                account.AccessFailedCount = 0;
            }
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidCredentials();
        }

        EnsureActive(account);
        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
        account.LastLoginAt = now;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            account.PasswordHash = passwordHasher.HashPassword(account, request.Password);
        await db.SaveChangesAsync(cancellationToken);
        return ToAuthenticated(account);
    }

    public async Task<AuthenticatedAccountDto> BootstrapAdminAsync(BootstrapAdminRequest request,
        string? bootstrapToken, CancellationToken cancellationToken)
    {
        var expectedToken = configuration["Authentication:BootstrapToken"];
        if (string.IsNullOrWhiteSpace(expectedToken) || !FixedTimeEquals(expectedToken, bootstrapToken)
            || await db.Users.AnyAsync(cancellationToken))
            throw new AppException(404, "BOOTSTRAP_UNAVAILABLE", "Initial account setup is not available.");

        ValidatePassword(request.Password);
        var email = ValidateAndNormalizeEmail(request.Email);
        var firstName = ValidateName(request.FirstName, "First name");
        var lastName = ValidateName(request.LastName, "Last name");
        var hall = await db.ResidenceHalls.SingleOrDefaultAsync(x => x.IsActive, cancellationToken)
            ?? throw new AppException(409, "NO_ACTIVE_HALL", "Create the Eltse Hall database record before bootstrapping an administrator.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.StaffUsers.Include(x => x.HallMemberships)
            .SingleOrDefaultAsync(x => x.SchoolEmail == email, cancellationToken);
        if (user is null)
        {
            user = new User { SchoolEmail = email, FirstName = firstName, LastName = lastName, Role = HallRole.Admin };
            db.StaffUsers.Add(user);
        }
        else
        {
            user.FirstName = firstName;
            user.LastName = lastName;
            user.Role = HallRole.Admin;
            user.IsActive = true;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var membership = user.HallMemberships.SingleOrDefault(x => x.ResidenceHallId == hall.Id);
        if (membership is null)
        {
            membership = new HallMembership { ResidenceHall = hall, User = user, HallRole = HallRole.Admin };
            user.HallMemberships.Add(membership);
        }
        membership.HallRole = HallRole.Admin;
        membership.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);

        var account = NewAccount(user, false);
        var result = await accountManager.CreateAsync(account, request.Password);
        ThrowIfFailed(result);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = user.Id,
            Action = "INITIAL_ADMIN_BOOTSTRAPPED",
            EntityType = "User",
            EntityId = user.Id.ToString(),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { user.SchoolEmail, Role = HallRole.Admin })
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToAuthenticated(account, user, membership);
    }

    public async Task<AuthenticatedAccountDto> ChangePasswordAsync(Guid userId, ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePassword(request.NewPassword);
        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
            throw new AppException(400, "PASSWORD_UNCHANGED", "Choose a new password that is different from the current password.");
        var account = await db.Users.Include(x => x.User).ThenInclude(x => x.HallMemberships)
            .ThenInclude(x => x.ResidenceHall).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new AppException(401, "ACCOUNT_NOT_FOUND", "Your account is no longer available.");
        EnsureActive(account);
        var result = await accountManager.ChangePasswordAsync(account, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(x => x.Code == "PasswordMismatch"))
                throw new AppException(400, "CURRENT_PASSWORD_INCORRECT", "The current password is incorrect.");
            ThrowIfFailed(result);
        }
        account.MustChangePassword = false;
        account.PasswordChangedAt = DateTimeOffset.UtcNow;
        await accountManager.UpdateAsync(account);
        var membership = ActiveMembership(account.User);
        db.AuditLogs.Add(Audit(userId, "PASSWORD_CHANGED", account.User));
        await db.SaveChangesAsync(cancellationToken);
        return ToAuthenticated(account, account.User, membership);
    }

    public async Task<ProvisionedAccountDto> CreateAsync(CreateStaffAccountRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await currentUserService.GetAsync(cancellationToken);
        EnsureCanManageRole(actor, request.Role);
        var email = ValidateAndNormalizeEmail(request.Email);
        var firstName = ValidateName(request.FirstName, "First name");
        var lastName = ValidateName(request.LastName, "Last name");
        ValidateContact(request.RoomNumber, request.PhoneNumber);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == accountManager.NormalizeEmail(email), cancellationToken))
            throw new AppException(409, "EMAIL_ALREADY_EXISTS", "An account already exists for that school email.");

        var user = await db.StaffUsers.Include(x => x.HallMemberships)
            .SingleOrDefaultAsync(x => x.SchoolEmail == email, cancellationToken);
        HallMembership membership;
        if (user is null)
        {
            user = new User
            {
                SchoolEmail = email,
                FirstName = firstName,
                LastName = lastName,
                RoomNumber = Clean(request.RoomNumber),
                PhoneNumber = Clean(request.PhoneNumber),
                Role = request.Role
            };
            membership = new HallMembership
            {
                ResidenceHallId = actor.ResidenceHallId,
                User = user,
                HallRole = request.Role
            };
            user.HallMemberships.Add(membership);
        }
        else
        {
            membership = user.HallMemberships.SingleOrDefault(x => x.ResidenceHallId == actor.ResidenceHallId)
                ?? throw new AppException(409, "EMAIL_ALREADY_EXISTS", "That school email belongs to a person in another hall.");
            user.FirstName = firstName;
            user.LastName = lastName;
            user.RoomNumber = Clean(request.RoomNumber);
            user.PhoneNumber = Clean(request.PhoneNumber);
            user.Role = request.Role;
            user.IsActive = true;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            membership.HallRole = request.Role;
            membership.IsActive = true;
        }
        var temporaryPassword = GenerateTemporaryPassword();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Entry(user).State == EntityState.Detached) db.StaffUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        var account = NewAccount(user, true);
        var result = await accountManager.CreateAsync(account, temporaryPassword);
        ThrowIfFailed(result);
        db.AuditLogs.Add(Audit(actor.Id, "ACCOUNT_CREATED", user, new { user.SchoolEmail, user.Role }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ProvisionedAccountDto(new ResidentAssistantDto(user.Id, user.FirstName, user.LastName,
            user.SchoolEmail, user.RoomNumber, user.PhoneNumber, membership.HallRole, true), temporaryPassword);
    }

    public async Task<TemporaryPasswordDto> ResetPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await currentUserService.GetAsync(cancellationToken);
        var account = await db.Users.Include(x => x.User).ThenInclude(x => x.HallMemberships)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new AppException(404, "ACCOUNT_NOT_FOUND", "Login account not found.");
        var membership = account.User.HallMemberships.SingleOrDefault(x => x.ResidenceHallId == actor.ResidenceHallId)
            ?? throw new AppException(404, "ACCOUNT_NOT_FOUND", "Login account not found in this hall.");
        if (actor.Role == HallRole.HallDirector && membership.HallRole != HallRole.ResidentAssistant)
            throw new AppException(403, "PASSWORD_RESET_NOT_ALLOWED", "Hall Directors can reset Resident Assistant passwords only.");
        if (actor.Id == userId)
            throw new AppException(400, "USE_CHANGE_PASSWORD", "Use Change password to update your own password.");

        var temporaryPassword = GenerateTemporaryPassword();
        var token = await accountManager.GeneratePasswordResetTokenAsync(account);
        var result = await accountManager.ResetPasswordAsync(account, token, temporaryPassword);
        ThrowIfFailed(result);
        account.MustChangePassword = true;
        account.PasswordChangedAt = DateTimeOffset.UtcNow;
        await accountManager.UpdateAsync(account);
        db.AuditLogs.Add(Audit(actor.Id, "PASSWORD_RESET", account.User));
        await db.SaveChangesAsync(cancellationToken);
        return new TemporaryPasswordDto(temporaryPassword);
    }

    private ApplicationAccount NewAccount(User user, bool mustChangePassword) => new()
    {
        Id = user.Id,
        UserId = user.Id,
        UserName = user.SchoolEmail,
        Email = user.SchoolEmail,
        EmailConfirmed = true,
        LockoutEnabled = true,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        MustChangePassword = mustChangePassword,
        PasswordChangedAt = DateTimeOffset.UtcNow
    };

    private static AuthenticatedAccountDto ToAuthenticated(ApplicationAccount account)
    {
        var membership = ActiveMembership(account.User);
        return ToAuthenticated(account, account.User, membership);
    }

    private static AuthenticatedAccountDto ToAuthenticated(ApplicationAccount account, User user,
        HallMembership membership) => new(user.Id, user.SchoolEmail, user.FirstName, user.LastName,
        membership.HallRole, account.SecurityStamp ?? string.Empty, account.MustChangePassword);

    private static HallMembership ActiveMembership(User user) => user.HallMemberships
        .SingleOrDefault(x => x.IsActive && x.ResidenceHall.IsActive)
        ?? throw new AppException(403, "NO_ACTIVE_MEMBERSHIP", "Your residence-life account is inactive.");

    private static void EnsureActive(ApplicationAccount account)
    {
        if (!account.User.IsActive) throw new AppException(403, "USER_INACTIVE", "Your residence-life account is inactive.");
        ActiveMembership(account.User);
    }

    private bool IsAllowedEmail(string email) => AuthorizationRules.IsAllowedSchoolEmail(email,
        configuration["Authentication:AllowedEmailDomain"]);

    private string ValidateAndNormalizeEmail(string? value)
    {
        var email = NormalizeEmail(value);
        if (!IsAllowedEmail(email))
            throw new AppException(400, "EMAIL_DOMAIN_NOT_ALLOWED", "Use a valid @wmpenn.edu email address.");
        return email;
    }

    private static string NormalizeEmail(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string ValidateName(string? value, string label)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > 80)
            throw new AppException(400, "INVALID_NAME", $"{label} is required and must be 80 characters or fewer.");
        return clean;
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < 15 || password.Length > MaximumPasswordLength)
            throw new AppException(400, "WEAK_PASSWORD", "Use a password or passphrase between 15 and 128 characters.");
        if (BlockedPasswords.Contains(password) || password.Contains("wmpenn", StringComparison.OrdinalIgnoreCase)
            || password.Contains("eltsehall", StringComparison.OrdinalIgnoreCase))
            throw new AppException(400, "WEAK_PASSWORD", "Choose a less predictable password or passphrase.");
    }

    private static void ValidateContact(string? room, string? phone)
    {
        if (room?.Length > 30) throw new AppException(400, "INVALID_ROOM_NUMBER", "Room number must be 30 characters or fewer.");
        if (phone?.Length > 30 || phone is not null && phone.Any(c => !char.IsDigit(c) && c is not '+' and not '-' and not '(' and not ')' and not ' '))
            throw new AppException(400, "INVALID_PHONE_NUMBER", "Enter a valid phone number with 30 characters or fewer.");
    }

    private static void EnsureCanManageRole(CurrentUserDto actor, HallRole requestedRole)
    {
        if (!Enum.IsDefined(requestedRole)) throw new AppException(400, "INVALID_ROLE", "Choose a valid hall role.");
        if (actor.Role == HallRole.HallDirector && requestedRole != HallRole.ResidentAssistant)
            throw new AppException(403, "ROLE_ASSIGNMENT_NOT_ALLOWED", "Hall Directors can create Resident Assistant accounts only.");
        if (actor.Role == HallRole.ResidentAssistant)
            throw new AppException(403, "ACCOUNT_MANAGEMENT_NOT_ALLOWED", "You cannot create login accounts.");
    }

    private static string GenerateTemporaryPassword()
    {
        Span<byte> random = stackalloc byte[24];
        RandomNumberGenerator.Fill(random);
        var chars = new char[random.Length];
        for (var i = 0; i < random.Length; i++) chars[i] = TemporaryPasswordAlphabet[random[i] % TemporaryPasswordAlphabet.Length];
        return new string(chars);
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static AppException InvalidCredentials() =>
        new(401, "INVALID_CREDENTIALS", "The email or password is incorrect.");

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded) return;
        var duplicate = result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName");
        if (duplicate) throw new AppException(409, "EMAIL_ALREADY_EXISTS", "An account already exists for that school email.");
        var password = result.Errors.FirstOrDefault(x => x.Code.StartsWith("Password", StringComparison.Ordinal));
        if (password is not null) throw new AppException(400, "WEAK_PASSWORD", password.Description);
        throw new AppException(400, "ACCOUNT_OPERATION_FAILED", "The account operation could not be completed.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditLog Audit(Guid actor, string action, User user, object? values = null) => new()
    {
        ActorUserId = actor,
        Action = action,
        EntityType = "User",
        EntityId = user.Id.ToString(),
        NewValuesJson = values is null ? null : System.Text.Json.JsonSerializer.Serialize(values)
    };
}
