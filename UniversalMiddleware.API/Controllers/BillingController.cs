using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BillingController : ControllerBase
{
    private readonly AppDbContext _context;

    public BillingController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Generates an invoice and payment instructions (CliQ, Wire, Cash) with a unique reference code.
    /// </summary>
    [HttpPost("generate-invoice")]
    public async Task<IActionResult> GenerateInvoice([FromBody] GenerateInvoiceRequest request)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { Error = "User not found." });

        Guid accountId;
        string accountType;

        if (user.Role == UserRole.AgencyOwner && user.AgencyId.HasValue)
        {
            accountId = user.AgencyId.Value;
            accountType = "Agency";
        }
        else if (user.TenantId.HasValue)
        {
            accountId = user.TenantId.Value;
            accountType = "Tenant";
        }
        else
        {
            return BadRequest(new { Error = "No billable organization mapped to this user." });
        }

        // Format: INV-YYYY-RANDOM (e.g., INV-2026-AB12)
        var referenceCode = $"INV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var invoice = new PaymentInvoice
        {
            AccountId = accountId,
            AccountType = accountType,
            Amount = request.Amount,
            Currency = request.Currency ?? "JOD",
            PaymentMethod = request.PaymentMethod,
            ReferenceCode = referenceCode,
            Status = "Unpaid"
        };

        _context.PaymentInvoices.Add(invoice);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            InvoiceId = invoice.Id,
            invoice.ReferenceCode,
            invoice.Amount,
            invoice.Currency,
            invoice.PaymentMethod,
            Instructions = GetPaymentInstructions(request.PaymentMethod, referenceCode)
        });
    }

    /// <summary>
    /// SuperAdmin only: Approves an invoice, activates the account, and extends the renewal date.
    /// </summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("approve-invoice/{invoiceId}")]
    public async Task<IActionResult> ApproveInvoice(Guid invoiceId)
    {
        var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(adminIdClaim, out var adminId);

        var invoice = await _context.PaymentInvoices.FindAsync(invoiceId);
        if (invoice == null)
            return NotFound(new { Error = "Invoice not found." });

        if (invoice.Status == "Confirmed")
            return BadRequest(new { Error = "Invoice is already confirmed." });

        invoice.Status = "Confirmed";
        invoice.PaidAt = DateTime.UtcNow;
        invoice.ConfirmedByAdminId = adminId;

        // Apply activation based on account type
        if (invoice.AccountType == "Agency")
        {
            var agency = await _context.Agencies.FindAsync(invoice.AccountId);
            if (agency != null)
            {
                agency.BillingStatus = "Active";
                agency.NextRenewalDate = DateTime.UtcNow.AddDays(30);
                agency.TasksUsedThisMonth = 0;
                agency.AiTasksUsedThisMonth = 0;
            }
        }
        else
        {
            var tenant = await _context.Tenants.FindAsync(invoice.AccountId);
            if (tenant != null)
            {
                tenant.BillingStatus = "Active";
                tenant.NextRenewalDate = DateTime.UtcNow.AddDays(30);
                tenant.TasksUsedThisMonth = 0;
                tenant.AiTasksUsedThisMonth = 0;
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new { Message = $"Invoice {invoice.ReferenceCode} approved. Account activated for 30 days." });
    }

    private static object GetPaymentInstructions(string method, string refCode)
    {
        return method.ToLowerInvariant() switch
        {
            "cliq" => new
            {
                Method = "CliQ",
                Alias = "OMNINODE",
                Bank = "Arab Bank",
                RequiredMemo = refCode,
                Note = "Please paste the exact ReferenceCode into the transfer description."
            },
            "bankwire" => new
            {
                Method = "Bank Wire",
                Beneficiary = "OmniNode Middleware Systems",
                IBAN = "JO00ARAB0000000000000000000000",
                SWIFT = "ARABJOAX",
                RequiredMemo = refCode
            },
            _ => new
            {
                Method = "Cash",
                Note = "Payment arranged directly with account manager. Quote reference: " + refCode
            }
        };
    }
}

public record GenerateInvoiceRequest(decimal Amount, string? Currency, string PaymentMethod);