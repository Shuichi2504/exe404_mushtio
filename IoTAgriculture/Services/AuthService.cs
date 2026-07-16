using System.Security.Cryptography;
using IoTAgriculture.Data;
using IoTAgriculture.DTOs.Auth;
using IoTAgriculture.Models;
using IoTAgriculture.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Services
{
    public class AuthService : IAuthService
    {
        private const int SessionDays = 30;
        private const int UserRole = 0;
        private const int AdminRole = 1;
        private readonly IoTDbContext _db;
        private readonly IEmailSender _emailSender;

        public AuthService(IoTDbContext db, IEmailSender emailSender)
        {
            _db = db;
            _emailSender = emailSender;
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto)
        {
            var phone = NormalizePhone(dto.PhoneNumber);
            var email = NormalizeEmail(dto.Email);
            if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Phone number and email are required");
            }

            if (await _db.Users.AnyAsync(u => u.PhoneNumber == phone))
            {
                throw new InvalidOperationException("Phone number already exists");
            }

            if (await _db.Users.AnyAsync(u => u.Email == email))
            {
                throw new InvalidOperationException("Email already exists");
            }

            if (!await HasVerifiedCodeAsync(email, "register"))
            {
                throw new InvalidOperationException("Email verification is required");
            }

            var password = HashPassword(dto.Password);
            var user = new AppUser
            {
                UserId = Guid.NewGuid(),
                FullName = dto.FullName.Trim(),
                PhoneNumber = phone,
                Email = email,
                EmailVerified = true,
                Address = dto.Address.Trim(),
                DateOfBirth = dto.DateOfBirth.Date,
                Role = UserRole,
                PasswordHash = password.Hash,
                PasswordSalt = password.Salt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await MarkCodesUsedAsync(email, "register");
            await _db.SaveChangesAsync();
            await LogActivityAsync(user.UserId, "register", "Tao tai khoan moi", "info");
            return await CreateSessionAsync(user);
        }

        public async Task RequestEmailCodeAsync(EmailCodeRequestDto dto)
        {
            var email = NormalizeEmail(dto.Email);
            var purpose = NormalizePurpose(dto.Purpose);
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Email is required");
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (purpose == "register" && user != null)
            {
                throw new InvalidOperationException("Email already exists");
            }

            if (purpose == "reset-password" && user == null)
            {
                throw new InvalidOperationException("Email does not exist");
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var now = DateTime.UtcNow;
            _db.EmailVerificationCodes.Add(new EmailVerificationCode
            {
                VerificationId = Guid.NewGuid(),
                Email = email,
                Code = code,
                Purpose = purpose,
                UserId = user?.UserId,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(10)
            });

            await _db.SaveChangesAsync();
            await _emailSender.SendVerificationCodeAsync(email, code, purpose);
        }

        public async Task<bool> VerifyEmailCodeAsync(VerifyEmailCodeRequestDto dto)
        {
            var code = await FindCodeAsync(
                NormalizeEmail(dto.Email),
                NormalizePurpose(dto.Purpose),
                dto.Code);
            if (code == null)
            {
                return false;
            }

            code.VerifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordRequestDto dto)
        {
            var email = NormalizeEmail(dto.Email);
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || !await HasCodeAsync(email, "reset-password", dto.Code))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            {
                throw new InvalidOperationException("New password must have at least 6 characters");
            }

            var password = HashPassword(dto.NewPassword);
            user.PasswordHash = password.Hash;
            user.PasswordSalt = password.Salt;
            user.UpdatedAt = DateTime.UtcNow;
            await MarkCodesUsedAsync(email, "reset-password");
            await LogActivityAsync(user.UserId, "password_reset", "Dat lai mat khau bang email", "warning");
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto dto)
        {
            var identifier = NormalizeIdentifier(dto);
            var user = identifier.Contains('@')
                ? await _db.Users.FirstOrDefaultAsync(u => u.Email == identifier)
                : await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == identifier);
            if (user == null || !VerifyPassword(dto.Password, user.PasswordHash, user.PasswordSalt))
            {
                return null;
            }

            var response = await CreateSessionAsync(user);
            await LogActivityAsync(user.UserId, "login", "Dang nhap vao he thong", "info");
            return response;
        }

        public async Task<UserProfileDto?> GetProfileAsync(string token)
        {
            var session = await FindSessionAsync(token);
            return session?.User == null ? null : ToProfile(session.User);
        }

        public async Task<AccountSummaryDto?> GetAccountSummaryAsync(string token)
        {
            var session = await FindSessionAsync(token);
            if (session?.User == null)
            {
                return null;
            }

            var userId = session.UserId;
            var activeSessionCount = await _db.UserSessions
                .CountAsync(s => s.UserId == userId && s.ExpiresAt > DateTime.UtcNow);
            var assignedDeviceCount = await _db.UserDevices.CountAsync(x => x.UserId == userId);
            var permissions = session.User.Role == AdminRole
                ? new List<string> { "Quan ly nguoi dung", "Gan thiet bi", "Xem toan bo thiet bi", "Dieu khien thiet bi" }
                : new List<string> { "Xem thiet bi duoc gan", "Xem du lieu cam bien", "Dieu khien may bom duoc gan", "Cap nhat thong tin ca nhan" };

            return new AccountSummaryDto
            {
                Profile = ToProfile(session.User),
                ActiveSessionCount = activeSessionCount,
                AssignedDeviceCount = assignedDeviceCount,
                Permissions = permissions
            };
        }

        public async Task<List<UserActivityDto>> GetActivitiesAsync(string token, int limit = 50)
        {
            var session = await FindSessionAsync(token);
            if (session?.User == null)
            {
                return [];
            }

            var safeLimit = Math.Clamp(limit, 1, 100);
            var items = await _db.UserActivities
                .Where(x => x.UserId == session.UserId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(safeLimit)
                .ToListAsync();
            return items.Select(ToActivityDto).ToList();
        }

        public async Task<UserProfileDto?> UpdateProfileAsync(string token, UpdateProfileRequestDto dto)
        {
            var session = await FindSessionAsync(token);
            if (session?.User == null)
            {
                return null;
            }

            var fullName = dto.FullName.Trim();
            var phone = NormalizePhone(dto.PhoneNumber);
            var email = NormalizeEmail(dto.Email);
            var address = dto.Address.Trim();
            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(phone) ||
                string.IsNullOrWhiteSpace(address))
            {
                throw new InvalidOperationException("Profile fields are required");
            }

            if (await _db.Users.AnyAsync(u => u.UserId != session.UserId && u.PhoneNumber == phone))
            {
                throw new InvalidOperationException("Phone number already exists");
            }

            if (!string.IsNullOrWhiteSpace(email) &&
                await _db.Users.AnyAsync(u => u.UserId != session.UserId && u.Email == email))
            {
                throw new InvalidOperationException("Email already exists");
            }

            session.User.FullName = fullName;
            session.User.PhoneNumber = phone;
            session.User.Email = email;
            session.User.Address = address;
            session.User.DateOfBirth = dto.DateOfBirth.Date;
            session.User.UpdatedAt = DateTime.UtcNow;
            await LogActivityAsync(session.UserId, "profile_update", "Cap nhat thong tin tai khoan", "info");
            await _db.SaveChangesAsync();
            return ToProfile(session.User);
        }

        public async Task<bool> ChangePasswordAsync(string token, ChangePasswordRequestDto dto)
        {
            var session = await FindSessionAsync(token);
            if (session?.User == null)
            {
                return false;
            }

            if (!VerifyPassword(dto.CurrentPassword, session.User.PasswordHash, session.User.PasswordSalt))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            {
                throw new InvalidOperationException("New password must have at least 6 characters");
            }

            var password = HashPassword(dto.NewPassword);
            session.User.PasswordHash = password.Hash;
            session.User.PasswordSalt = password.Salt;
            session.User.UpdatedAt = DateTime.UtcNow;
            await LogActivityAsync(session.UserId, "password_change", "Doi mat khau dang nhap", "warning");
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task LogoutAsync(string token)
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Token == token);
            if (session == null)
            {
                return;
            }

            await LogActivityAsync(session.UserId, "logout", "Dang xuat khoi he thong", "info");
            _db.UserSessions.Remove(session);
            await _db.SaveChangesAsync();
        }

        private async Task<AuthResponseDto> CreateSessionAsync(AppUser user)
        {
            var session = new UserSession
            {
                SessionId = Guid.NewGuid(),
                UserId = user.UserId,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(SessionDays)
            };

            _db.UserSessions.Add(session);
            await _db.SaveChangesAsync();

            return new AuthResponseDto
            {
                Token = session.Token,
                ExpiresAt = session.ExpiresAt,
                User = ToProfile(user)
            };
        }

        private async Task<UserSession?> FindSessionAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var session = await _db.UserSessions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Token == token);

            if (session == null)
            {
                return null;
            }

            if (session.ExpiresAt <= DateTime.UtcNow)
            {
                _db.UserSessions.Remove(session);
                await _db.SaveChangesAsync();
                return null;
            }

            return session;
        }

        private static UserProfileDto ToProfile(AppUser user)
        {
            return new UserProfileDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email,
                Address = user.Address,
                DateOfBirth = user.DateOfBirth,
                Role = user.Role == AdminRole ? "admin" : "user"
            };
        }

        private static UserActivityDto ToActivityDto(UserActivity activity)
        {
            var local = activity.CreatedAt.ToLocalTime();
            return new UserActivityDto
            {
                UserActivityId = activity.UserActivityId,
                Action = activity.Action,
                Description = activity.Description,
                Severity = activity.Severity,
                CreatedAt = activity.CreatedAt,
                CreatedLocal = $"{local:yyyy-MM-dd HH:mm:ss}"
            };
        }

        private async Task LogActivityAsync(Guid userId, string action, string description, string severity)
        {
            _db.UserActivities.Add(new UserActivity
            {
                UserActivityId = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                Description = description,
                Severity = severity,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        private static string NormalizePhone(string phone)
        {
            return phone.Trim();
        }

        private static string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        private static string NormalizePurpose(string purpose)
        {
            var normalized = purpose.Trim().ToLowerInvariant();
            return normalized == "reset" || normalized == "forgot-password"
                ? "reset-password"
                : normalized;
        }

        private static string NormalizeIdentifier(LoginRequestDto dto)
        {
            var raw = !string.IsNullOrWhiteSpace(dto.Identifier)
                ? dto.Identifier
                : !string.IsNullOrWhiteSpace(dto.Email)
                    ? dto.Email
                    : dto.PhoneNumber;

            raw = raw.Trim();
            return raw.Contains('@') ? NormalizeEmail(raw) : NormalizePhone(raw);
        }

        private async Task<bool> HasVerifiedCodeAsync(string email, string purpose)
        {
            return await _db.EmailVerificationCodes.AnyAsync(x =>
                x.Email == email &&
                x.Purpose == purpose &&
                x.VerifiedAt != null &&
                x.UsedAt == null &&
                x.ExpiresAt > DateTime.UtcNow);
        }

        private async Task<bool> HasCodeAsync(string email, string purpose, string code)
        {
            return await FindCodeAsync(email, purpose, code) != null;
        }

        private async Task<EmailVerificationCode?> FindCodeAsync(string email, string purpose, string code)
        {
            var normalizedCode = code.Trim();
            return await _db.EmailVerificationCodes
                .Where(x =>
                x.Email == email &&
                x.Purpose == purpose &&
                x.Code == normalizedCode &&
                x.UsedAt == null &&
                x.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        private async Task MarkCodesUsedAsync(string email, string purpose)
        {
            var codes = await _db.EmailVerificationCodes
                .Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null)
                .ToListAsync();
            foreach (var code in codes)
            {
                code.UsedAt = DateTime.UtcNow;
            }
        }

        private static PasswordParts HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                100000,
                HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);
            return new PasswordParts(Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }

        private static bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(storedSalt))
                {
                    return VerifyPasswordParts(password, storedHash, storedSalt);
                }

                var parts = storedHash.Split('.');
                return parts.Length == 2 && VerifyPasswordParts(password, parts[1], parts[0]);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool VerifyPasswordParts(string password, string storedHash, string storedSalt)
        {
            var salt = Convert.FromBase64String(storedSalt);
            var expectedHash = Convert.FromBase64String(storedHash);
            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                100000,
                HashAlgorithmName.SHA256);
            var actualHash = pbkdf2.GetBytes(32);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        private sealed record PasswordParts(string Hash, string Salt);
    }
}
