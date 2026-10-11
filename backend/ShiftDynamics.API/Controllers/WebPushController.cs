using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Application.Interfaces;
using ShiftDynamics.API.Application.Services;
using ShiftDynamics.API.Common;
using ShiftDynamics.API.Domain.Entities;
using ShiftDynamics.API.Infrastructure.Data;

namespace ShiftDynamics.API.Controllers;

[ApiController]
[Authorize]
[Route("api/web-push")]
public class WebPushController : ControllerBase
{
    private readonly ShiftDynamicsDbContext _db;
    private readonly FirebaseWebPushSender _sender;
    private readonly INotificationService _notifications;
    public WebPushController(ShiftDynamicsDbContext db, FirebaseWebPushSender sender, INotificationService notifications)
    { _db = db; _sender = sender; _notifications = notifications; }

    public record Registration(
        [Required, StringLength(4096, MinimumLength = 20)] string Token,
        [Required, StringLength(500)] string Origin,
        [Required, StringLength(300)] string BasePath);
    public record TokenRequest([Required, StringLength(4096, MinimumLength = 20)] string Token);
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var id = User.RequireUserId();
        return Ok(new {
            configured = _sender.Configured,
            enabledBrowsers = await _db.WebPushSubscriptions.CountAsync(s => s.UserId == id && s.Enabled, ct)
        });
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Register(Registration request, CancellationToken ct)
    {
        var id = User.RequireUserId();
        if (!await _db.Users.AnyAsync(u => u.Id == id && u.Status == AccountStatus.Active, ct)) return Forbid();
        if (!Uri.TryCreate(request.Origin, UriKind.Absolute, out var origin) ||
            origin.GetLeftPart(UriPartial.Authority) != request.Origin ||
            (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback)))
            return BadRequest(new { message = "Web push requires HTTPS or localhost." });
        if (Request.Headers.Origin.ToString() != request.Origin)
            return BadRequest(new { message = "Browser origin did not match the subscription." });
        if (!Regex.IsMatch(request.BasePath, @"^/(?:[A-Za-z0-9_-]+/)*$"))
            return BadRequest(new { message = "Invalid frontend base path." });
        if (request.Token.Any(char.IsWhiteSpace)) return BadRequest(new { message = "Invalid browser token." });
        var hash = Hash(request.Token);
        var row = await _db.WebPushSubscriptions.SingleOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (row is not null && row.Enabled && row.UserId != id)
            return Conflict(new { message = "Push notifications are enabled for another account in this browser. Disable them there or use a separate browser profile." });
        if (row is null)
        {
            row = new WebPushSubscription { Id = Guid.NewGuid(), TokenHash = hash };
            _db.WebPushSubscriptions.Add(row);
        }
        if (!row.Enabled || row.UserId != id) row.ActivatedAt = DateTime.UtcNow;
        row.UserId = id; row.Token = request.Token;
        row.Origin = request.Origin; row.BasePath = request.BasePath;
        row.Enabled = true; row.UpdatedAt = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Browser registration changed concurrently. Try Enable push again." });
        }
        return Ok(new { enabled = true, configured = _sender.Configured });
    }

    [HttpPost("subscriptions/disable")]
    public async Task<IActionResult> Disable(TokenRequest request, CancellationToken ct)
    {
        var id = User.RequireUserId(); var hash = Hash(request.Token);
        var row = await _db.WebPushSubscriptions.SingleOrDefaultAsync(s => s.TokenHash == hash && s.UserId == id, ct);
        if (row is not null) { row.Enabled = false; row.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); }
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var id = User.RequireUserId();
        if (!_sender.Configured) return BadRequest(new { message = "Configure the backend Firebase service-account file first." });
        if (!await _db.WebPushSubscriptions.AnyAsync(s => s.UserId == id && s.Enabled, ct))
            return BadRequest(new { message = "Enable push for this browser first." });
        var since = DateTime.UtcNow.AddMinutes(-1);
        if (await _db.Notifications.AnyAsync(n => n.UserId == id && n.Type == "web-push-test" && n.CreatedAt >= since, ct))
            return StatusCode(429, new { message = "Wait one minute before sending another test." });
        await _notifications.CreateAsync(id, "web-push-test", "ShyftDynamics push test",
            "Your browser push notification setup has been tested. Check your browser or system notification area.");
        return Accepted(new { message = "Test queued. Switch to another tab to see the background notification." });
    }
}