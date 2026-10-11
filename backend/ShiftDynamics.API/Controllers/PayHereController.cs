using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftDynamics.API.Application.Services;
using ShiftDynamics.API.Common;
using ShiftDynamics.API.Domain.Entities;
using ShiftDynamics.API.Infrastructure.Data;
using ValidationException = ShiftDynamics.API.Common.ValidationException;

namespace ShiftDynamics.API.Controllers;

[ApiController]
[Route("api/payhere")]
public sealed class PayHereController : ControllerBase
{
    private readonly ShiftDynamicsDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PayHereController> _logger;
    private readonly PayHereRetrievalClient _retrieval;
    public PayHereController(ShiftDynamicsDbContext db, IConfiguration config, ILogger<PayHereController> logger, PayHereRetrievalClient retrieval)
    { _db = db; _config = config; _logger = logger; _retrieval = retrieval; }

    public record CheckoutRequest(Guid InvoiceId, [Required, StringLength(500)] string Address,
        [Required, StringLength(100)] string City);

    private (string Merchant, string Secret, string Notify, string Frontend) Settings()
    {
        var merchant = _config["PayHere:MerchantId"] ?? "";
        var secret = _config["PayHere:MerchantSecret"] ?? "";
        var frontend = (_config["PayHere:FrontendUrl"] ?? "").TrimEnd('/');
        var localBackend = (_config["PayHere:LocalBackendUrl"] ?? "http://localhost:5174").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(secret) || !Regex.IsMatch(merchant, "\\A[0-9]{5,12}\\z"))
            throw new ConflictException("PayHere Sandbox Merchant ID and Merchant Secret are not configured.");
        if (!_retrieval.Configured) throw new ConflictException("Configure Sandbox App ID and App Secret to verify payments locally.");
        if (!Uri.TryCreate(localBackend, UriKind.Absolute, out var local) ||
            !(local.Scheme == "https" || local.Scheme == "http" && local.IsLoopback) || local.Query.Length > 0 || local.Fragment.Length > 0)
            throw new ConflictException("Configure PayHere:LocalBackendUrl with the local backend root address.");
        if (!Uri.TryCreate(frontend, UriKind.Absolute, out var f) ||
            !(f.Scheme == "https" || f.Scheme == "http" && f.IsLoopback) ||
            !string.IsNullOrEmpty(f.Query) || !string.IsNullOrEmpty(f.Fragment))
            throw new ConflictException("Configure PayHere:FrontendUrl with your frontend root URL.");
        return (merchant, secret, localBackend + "/api/payhere/notify", frontend);
    }

    [HttpPost("checkout")]
    [Authorize(Policy = "Customer")]
    public async Task<ActionResult<ApiResponse<object>>> Checkout([FromBody] CheckoutRequest input)
    {
        var settings = Settings();
        var customerId = User.RequireCustomerId();
        // The registered frontend is used for return URLs. Do not accept arbitrary browser redirects.
        var origin = Request.Headers.Origin.ToString();
        if (origin != new Uri(settings.Frontend).GetLeftPart(UriPartial.Authority))
            throw new ValidationException("Open the website using the configured PayHere frontend address.");
        var customer = await _db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId);
        if (string.IsNullOrWhiteSpace(input.Address) || string.IsNullOrWhiteSpace(input.City) ||
            string.IsNullOrWhiteSpace(customer.FirstName) || string.IsNullOrWhiteSpace(customer.LastName) ||
            !new EmailAddressAttribute().IsValid(customer.Email) || string.IsNullOrWhiteSpace(customer.Phone))
            throw new ValidationException("Complete your name, email and phone in Profile, and enter a billing address and city.");
        var payment = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var invoice = await _db.Invoices.FromSqlInterpolated($"SELECT * FROM dbo.invoices WITH (UPDLOCK, HOLDLOCK) WHERE Id = {input.InvoiceId}")
                .Include(i => i.WorkOrder).FirstOrDefaultAsync(i => i.WorkOrder.CustomerId == customerId)
                ?? throw new NotFoundException("Invoice not found.");
            if (invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue) || invoice.BalanceDue <= 0)
                throw new ConflictException("Only an issued invoice with an outstanding balance can be paid.");
            var since = DateTime.UtcNow.AddMinutes(-15);
            var existing = await _db.Payments.FirstOrDefaultAsync(p => p.InvoiceId == invoice.Id &&
                p.Method == PaymentMethod.Online && p.Status == PaymentStatus.Pending &&
                p.Amount == invoice.BalanceDue && p.CreatedAt >= since &&
                p.TransactionReference != null && p.TransactionReference.StartsWith("PH-SBX-ORDER-"));
            if (existing is null)
            {
                existing = new Payment { Id = Guid.NewGuid(), InvoiceId = invoice.Id,
                    Amount = invoice.BalanceDue, Method = PaymentMethod.Online, Status = PaymentStatus.Pending,
                    Notes = "PayHere Sandbox checkout awaiting authenticated payment lookup.", CreatedAt = DateTime.UtcNow, PaymentDate = DateTime.UtcNow };
                existing.TransactionReference = "PH-SBX-ORDER-" + existing.Id.ToString("N");
                _db.Payments.Add(existing);
                await _db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return existing;
        });
        var order = "SD-SBX-" + payment.Id.ToString("N");
        var invoiceNumber = await _db.Invoices.Where(i => i.Id == payment.InvoiceId).Select(i => i.InvoiceNumber).SingleAsync();
        var fields = new Dictionary<string, string> {
            ["merchant_id"] = settings.Merchant, ["order_id"] = order,
            ["amount"] = PayHereSecurity.Amount(payment.Amount), ["currency"] = "LKR",
            ["hash"] = PayHereSecurity.CheckoutHash(settings.Merchant, order, payment.Amount, settings.Secret),
            ["return_url"] = settings.Frontend + "/customer/payments.html?payhereOrder=" + order,
            ["cancel_url"] = settings.Frontend + "/customer/payments.html?payhereOrder=" + order + "&checkoutCancelled=1",
            ["notify_url"] = settings.Notify, ["items"] = "ShyftDynamics invoice " + invoiceNumber,
            ["first_name"] = customer.FirstName, ["last_name"] = customer.LastName,
            ["email"] = customer.Email, ["phone"] = customer.Phone,
            ["address"] = input.Address.Trim(), ["city"] = input.City.Trim(), ["country"] = "Sri Lanka"
        };
        return Ok(ApiResponse<object>.Ok(new { action = "https://sandbox.payhere.lk/pay/checkout", fields }, "Sandbox checkout prepared."));
    }

    [HttpGet("orders/{order}")]
    [Authorize(Policy = "Customer")]
    public async Task<ActionResult<ApiResponse<object>>> Order(string order)
    {
        if (!PayHereSecurity.TryOrder(order, out var id)) throw new NotFoundException("Payment order not found.");
        var customerId = User.RequireCustomerId();
        var p = await _db.Payments.AsNoTracking().Include(p => p.Invoice).ThenInclude(i => i.WorkOrder)
            .FirstOrDefaultAsync(p => p.Id == id && p.Invoice.WorkOrder.CustomerId == customerId && p.Method == PaymentMethod.Online &&
                p.TransactionReference != null && p.TransactionReference.StartsWith("PH-SBX-"))
            ?? throw new NotFoundException("Payment order not found.");
        var message = p.Status == PaymentStatus.Completed ? "Sandbox payment verified and recorded." :
            p.Status == PaymentStatus.Failed ? "Sandbox payment was cancelled or failed. Your invoice was not marked paid." :
            "No verified successful payment found yet. Check again after completing Sandbox checkout.";
        if (p.Notes?.Contains("REVIEW:") == true) message = "Sandbox payment needs staff review. Contact your service advisor.";
        return Ok(ApiResponse<object>.Ok(new { status = p.Status.ToString(), message, invoiceId = p.InvoiceId,
            invoiceStatus = p.Invoice.Status.ToString(), balanceDue = p.Invoice.BalanceDue }));
    }

    [HttpPost("notify")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Notify()
    {
        var f = await Request.ReadFormAsync();
        string Get(string key) => f[key].Count == 1 ? f[key].ToString() : "";
        var merchant = Get("merchant_id"); var order = Get("order_id");
        var rawAmount = Get("payhere_amount"); var currency = Get("payhere_currency");
        var status = Get("status_code"); var paymentId = Get("payment_id");
        var secret = _config["PayHere:MerchantSecret"] ?? "";
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503);
        if (merchant != _config["PayHere:MerchantId"] || currency != "LKR" ||
            status is not ("2" or "0" or "-1" or "-2" or "-3") ||
            !PayHereSecurity.TryOrder(order, out var id) || !PayHereSecurity.TryAmount(rawAmount, out var amount) ||
            !PayHereSecurity.Verify(merchant, order, rawAmount, currency, status, Get("md5sig"), secret) ||
            !Regex.IsMatch(paymentId, "\\A[0-9]{1,64}\\z"))
            return BadRequest(ApiResponse.Fail("Invalid payment notification."));
        await ApplyPaymentAsync(id, amount, status, paymentId);
        return Ok(ApiResponse.Ok("Payment notification received."));
    }

    [HttpPost("orders/{order}/verify")]
    [Authorize(Policy = "Customer")]
    public async Task<ActionResult<ApiResponse<object>>> VerifyOrder(string order)
    {
        if (!PayHereSecurity.TryOrder(order, out var id)) throw new NotFoundException("Payment order not found.");
        var customerId = User.RequireCustomerId();
        var payment = await _db.Payments.AsNoTracking().Include(p => p.Invoice).ThenInclude(i => i.WorkOrder)
            .FirstOrDefaultAsync(p => p.Id == id && p.Invoice.WorkOrder.CustomerId == customerId &&
                p.Method == PaymentMethod.Online && p.TransactionReference != null && p.TransactionReference.StartsWith("PH-SBX-"))
            ?? throw new NotFoundException("Payment order not found.");
        var records = await _retrieval.LookupAsync(order, HttpContext.RequestAborted);
        if (records.Any(p => p.Amount != payment.Amount))
            throw new ConflictException("PayHere returned an amount that differs from the stored payment order. Contact your advisor; the invoice was not changed.");
        // Handle reversals first. Never credit a received record while another record needs reconciliation.
        foreach (var record in records.OrderBy(p => p.Status == "RECEIVED" ? 1 : 0))
            await ApplyPaymentAsync(id, record.Amount,
                record.Status == "RECEIVED" ? "2" : record.Status == "CHARGEBACKED" ? "-3" : "refund", record.PaymentId);
        return await Order(order);
    }

    private async Task ApplyPaymentAsync(Guid id, decimal amount, string status, string paymentId)
    {
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync();
            // Lock the invoice before the payment consistently with checkout and manual staff receipts.
            var invoiceId = await _db.Payments.AsNoTracking().Where(p => p.Id == id)
                .Select(p => (Guid?)p.InvoiceId).FirstOrDefaultAsync()
                ?? throw new NotFoundException("Payment order not found.");
            var invoice = await _db.Invoices.FromSqlInterpolated($"SELECT * FROM dbo.invoices WITH (UPDLOCK, HOLDLOCK) WHERE Id = {invoiceId}")
                .Include(i => i.WorkOrder).SingleAsync();
            var payment = await _db.Payments.FromSqlInterpolated($"SELECT * FROM dbo.payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleAsync();
            if (payment.Method != PaymentMethod.Online || payment.TransactionReference is null ||
                !payment.TransactionReference.StartsWith("PH-SBX-", StringComparison.Ordinal) || payment.Amount != amount)
                throw new ValidationException("Payment order or amount does not match.");
            var reference = "PH-SBX-" + paymentId;
            // Prevent the same provider payment being credited to two orders, even across invoices.
            var lockName = "Shyft.PayHere." + paymentId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={lockName}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51001, 'Payment lock unavailable.', 1;");
            if (payment.Status == PaymentStatus.Completed)
            {
                if (status == "2" && payment.TransactionReference != reference)
                {
                    if (payment.Notes?.Contains("REVIEW:") != true)
                    {
                        payment.Notes = "PayHere Sandbox. REVIEW: Another successful provider payment was reported for this already credited order. Provider payment " + paymentId + ". Invoice was not credited twice.";
                        await AlertStaffAsync(invoice.Id, "Possible duplicate sandbox payment requires review");
                        await _db.SaveChangesAsync();
                    }
                    await transaction.CommitAsync(); return;
                }
                if (status is "-3" or "refund" && payment.Notes?.Contains("REVIEW:") != true)
                {
                    payment.Notes = "PayHere Sandbox. REVIEW: Provider reported a refund or chargeback; staff reconciliation required. Invoice totals were retained.";
                    await AlertStaffAsync(invoice.Id, "Sandbox reversal requires review");
                    await _db.SaveChangesAsync();
                }
                await transaction.CommitAsync(); return;
            }
            if (status == "2")
            {
                if (payment.Notes?.Contains("REVIEW:") == true)
                { await transaction.CommitAsync(); return; }
                if (await _db.Payments.AnyAsync(p => p.Id != id && p.TransactionReference == reference))
                    throw new ConflictException("Provider payment is already recorded against another order.");
                if (invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue) || amount > invoice.BalanceDue)
                {
                    if (payment.Notes?.Contains("REVIEW:") != true)
                    {
                        payment.Notes = "PayHere Sandbox. REVIEW: Verified provider payment arrived after the invoice changed. Provider payment " + paymentId + ". Staff reconciliation required.";
                        await AlertStaffAsync(invoice.Id, "Sandbox payment requires reconciliation");
                    }
                    await _db.SaveChangesAsync(); await transaction.CommitAsync(); return;
                }
                payment.TransactionReference = reference; payment.Status = PaymentStatus.Completed;
                payment.PaymentDate = DateTime.UtcNow; payment.Notes = "PayHere Sandbox: simulated payment verified by the backend. No real money collected.";
                invoice.AmountPaid += amount; invoice.BalanceDue = invoice.TotalAmount - invoice.AmountPaid;
                invoice.Status = invoice.BalanceDue <= 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
                invoice.UpdatedAt = DateTime.UtcNow;
                var users = await _db.Users.Where(u => u.CustomerId == invoice.WorkOrder.CustomerId && u.Status == AccountStatus.Active).Select(u => u.Id).ToListAsync();
                foreach (var userId in users) _db.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = userId,
                    Type = "payment", Title = "Sandbox payment recorded", Body = "A simulated PayHere payment was verified for invoice " + invoice.InvoiceNumber + ".", EntityType = "Invoice", EntityId = invoice.Id, CreatedAt = DateTime.UtcNow });
                _logger.LogInformation("Verified PayHere sandbox payment for order {OrderId}.", "SD-SBX-" + id.ToString("N"));
            }
            else if (status is "-1" or "-2")
            {
                if (payment.Notes?.Contains("REVIEW:") != true) { payment.Status = PaymentStatus.Failed; payment.Notes = "PayHere Sandbox payment cancelled or declined. Invoice unchanged."; }
            }
            else if (status is "-3" or "refund" && payment.Notes?.Contains("REVIEW:") != true)
            {
                payment.Notes = "PayHere Sandbox. REVIEW: Refund or chargeback reported before credit was recorded. Staff reconciliation required.";
                await AlertStaffAsync(invoice.Id, "Sandbox reversal requires review");
            }
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    private async Task AlertStaffAsync(Guid invoiceId, string title)
    {
        var staff = await _db.Users.Where(u => u.Status == AccountStatus.Active &&
            (u.Role == SystemRole.ServiceAdvisor || u.Role == SystemRole.Manager || u.Role == SystemRole.Admin)).Select(u => u.Id).ToListAsync();
        foreach (var id in staff) _db.Notifications.Add(new Notification { Id = Guid.NewGuid(), UserId = id,
            Type = "payment-review", Title = title, Body = "Review the PayHere Sandbox payment attempt and invoice before handover.", EntityType = "Invoice", EntityId = invoiceId, CreatedAt = DateTime.UtcNow });
    }
}
