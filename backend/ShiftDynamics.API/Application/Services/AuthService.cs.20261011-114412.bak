using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Common;
using ShiftDynamics.API.Domain.Entities;
using ShiftDynamics.API.Application.DTOs.Auth;
using ShiftDynamics.API.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Mail;

namespace ShiftDynamics.API.Application.Services;

public class AuthService : IAuthService
{
    private readonly ShiftDynamicsDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public AuthService(
        ShiftDynamicsDbContext db,
        ITokenService tokenService,
        ILogger<AuthService> logger, IConfiguration configuration, IHostEnvironment environment)
    {
        _db = db;
        _tokenService = tokenService;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<AuthResponse> RegisterCustomerAsync(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = request.Phone.Trim();
        var fullName = request.FullName.Trim();
        var names = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (email.Length > 191 || names.Length == 0 || names[0].Length > 100 || (names.Length > 1 && names[1].Length > 100))
            throw new ValidationException("Email must be at most 191 characters and each name at most 100 characters.");

        if (await _db.Users.AnyAsync(u => u.Email == email))
            throw new ConflictException("An account with this email already exists.");

        if (await _db.Users.AnyAsync(u => u.Phone == phone))
            throw new ConflictException("An account with this phone number already exists.");

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = names[0],
            LastName = names.Length > 1 ? names[1] : string.Empty,
            Email = email,
            Phone = phone,
            Address = request.Address?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            Phone = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = SystemRole.Customer,
            Status = AccountStatus.Active,
            CustomerId = customer.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(customer);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (token, expires) = _tokenService.CreateAccessToken(user);
        return MapAuthResponse(user, token, expires);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid email or password.");

        if (user.Status == AccountStatus.Inactive)
            throw new ForbiddenException("This account is inactive.");

        if (user.Status == AccountStatus.Pending)
            throw new ForbiddenException("This account is pending approval.");

        if (user.Status == AccountStatus.Rejected)
            throw new ForbiddenException("This account registration was rejected.");

        var (token, expires) = _tokenService.CreateAccessToken(user);
        return MapAuthResponse(user, token, expires);
    }

    public async Task RequestPasswordResetAsync(string email)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Always return success to avoid email enumeration
        if (user is null)
        {
            _logger.LogInformation("Password reset requested for unknown email {Email}", email);
            return;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = HashToken(rawToken);

        var reset = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };

        _db.PasswordResetTokens.Add(reset);
        await _db.SaveChangesAsync();

        var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://127.0.0.1:5500";
        var link = $"{frontendUrl.TrimEnd('/')}/reset-password.html?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(rawToken)}";
        var host = _configuration["Smtp:Host"];
        var from = _configuration["Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            if (_environment.IsDevelopment()) _logger.LogInformation("Development password reset link: {ResetLink}", link);
            else _logger.LogError("Password reset email could not be sent: configure Smtp:Host and Smtp:From.");
            return;
        }
        try
        {
            using var client = new SmtpClient(host, _configuration.GetValue<int?>("Smtp:Port") ?? 587)
            { EnableSsl = _configuration.GetValue<bool?>("Smtp:EnableSsl") ?? true };
            var username = _configuration["Smtp:Username"];
            if (!string.IsNullOrWhiteSpace(username)) client.Credentials = new NetworkCredential(username, _configuration["Smtp:Password"]);
            using var mail = new MailMessage(from, email, "Reset your Shift Dynamics password",
                $"Use this link to reset your password within one hour:\n{link}\nIf you did not request this, ignore this email.");
            await client.SendMailAsync(mail);
        }
        catch (Exception error)
        {
            // The public endpoint must give the same response for known and unknown accounts.
            _logger.LogError(error, "Password reset email delivery failed for user {UserId}.", user.Id);
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashToken(request.Token);

        var reset = await _db.PasswordResetTokens
            .Include(t => t.User)
            .Where(t => t.TokenHash == tokenHash && t.User.Email == email)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (reset is null || reset.UsedAt is not null || reset.ExpiresAt < DateTime.UtcNow)
            throw new ValidationException("Invalid or expired password reset token.");

        reset.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        reset.User.UpdatedAt = DateTime.UtcNow;
        reset.UsedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static AuthResponse MapAuthResponse(User user, string token, DateTime expires) => new()
    {
        UserId = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Phone = user.Phone,
        Role = user.Role.ToString(),
        Status = user.Status.ToString(),
        CustomerId = user.CustomerId,
        AccessToken = token,
        ExpiresAt = expires
    };
}