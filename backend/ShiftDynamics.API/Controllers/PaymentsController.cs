using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftDynamics.API.Common;
using ShiftDynamics.API.Application.Interfaces;
using ShiftDynamics.API.Application.DTOs.Payments;

namespace ShiftDynamics.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;
    public PaymentsController(IPaymentService payments) => _payments = payments;

    // Customer-supplied references cannot prove a payment. Online payments are recorded by the verified callback.
    [HttpPost]
    [Authorize(Policy = "Customer")]
    public ActionResult<ApiResponse<object>> Create([FromBody] CreatePaymentRequest request) =>
        BadRequest(ApiResponse.Fail("Use PayHere Sandbox checkout from your invoice. Payments are recorded only after a verified gateway notification."));

    [HttpGet]
    [Authorize(Policy = "Customer")]
    public async Task<ActionResult<ApiResponse<object>>> List([FromQuery] Guid? invoiceId) =>
        Ok(ApiResponse<object>.Ok(await _payments.ListForCustomerAsync(User.RequireCustomerId(), invoiceId)));
}
