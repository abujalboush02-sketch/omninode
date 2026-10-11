using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public record GenerateInvoiceRequest(string? PlanId, decimal Amount, string? Currency, string PaymentMethod);

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BillingController : ControllerBase
{
    private static readonly string[] Methods = { "cliq", "bankwire", "cash" };
    private readonly AppDbContext _context;

    public BillingController(AppDbContext context)
    {
        _context = context;
    }

    private async Task<User?> CurrentUser()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var userId) ? await _context.Users.FindAsync(userId) : null;
    }

    private static (Guid AccountId, string AccountType)? BillableAccount(User user)
    {
        if (user.Role == UserRole.AgencyOwner && user.AgencyId.HasValue) return (user.AgencyId.Value, "Agency");
        if (user.TenantId.HasValue) return (user.TenantId.Value, "Tenant");
        return null;
    }

    /// <summary>Public plan catalog (single-tenant).</summary>
    [AllowAnonymous]
    [HttpGet("plans")]
    public IActionResult GetPlans() =>
        Ok(Plans.All.Select(p => new { p.Id, p.Name, p.PriceJod, p.PriceUsdRef, p.Tasks, p.AiTasks, p.Extras }));

    /// <summary>The caller's invoices, newest first.</summary>
    [HttpGet("my-invoices")]
    public async Task<IActionResult> MyInvoices()
    {
        var user = await CurrentUser();
        if (user == null) return Unauthorized();

        var account = BillableAccount(user);
        if (account == null) return Ok(Array.Empty<object>());

        var (accountId, accountType) = account.Value;
        var invoices = await _context.PaymentInvoices
            .AsNoTracking()
            .Where(i => i.AccountId == accountId && i.AccountType == accountType)
            .OrderByDescending(i => i.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Ok(invoices.Select(i => new
        {
            InvoiceId = i.Id,
            i.ReferenceCode,
            i.PlanId,
            i.Amount,
            i.Currency,
            i.PaymentMethod,
            Status = i.Status switch { "Confirmed" => "Approved", "Rejected" => "Rejected", _ => "Pending" },
            i.CreatedAt,
            ApprovedAt = i.PaidAt
        }));
    }

    /// <summary>Generates an invoice and payment instructions (CliQ, Wire, Cash) with a unique reference code.</summary>
    [HttpPost("generate-invoice")]
    public async Task<IActionResult> GenerateInvoice([FromBody] GenerateInvoiceRequest request)
    {
        var user = await CurrentUser();
        if (user == null) return Unauthorized();

        var account = BillableAccount(user);
        if (account == null)
            return BadRequest(new { Error = "No billable organization mapped to this user." });

        var method = request.PaymentMethod?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Methods.Contains(method))
            return BadRequest(new { Error = "paymentMethod must be cliq, bankwire or cash." });

        var (accountId, accountType) = account.Value;
        decimal amount;
        string currency;
        string? planId = null;

        if (accountType == "Tenant")
        {
            // Tenants buy a catalog plan; the server sets the price, never the client.
            var plan = Plans.Find(request.PlanId);
            if (plan == null || plan.Id == "free")
                return BadRequest(new { Error = "planId must be one of: bronze, silver, gold, platinum." });

            planId = plan.Id;
            amount = plan.PriceJod;
            currency = "JOD";
        }
        else
        {
            // Agency plans aren't catalogued yet; keep the old behaviour but reject nonsense amounts.
            if (request.Amount <= 0) return BadRequest(new { Error = "amount must be greater than 0." });
            amount = request.Amount;
            currency = string.IsNullOrWhiteSpace(request.Currency) ? "JOD" : request.Currency.Trim().ToUpperInvariant();
        }

        // Format: INV-YYYY-XXXXXX
        var referenceCode = $"INV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var invoice = new PaymentInvoice
        {
            AccountId = accountId,
            AccountType = accountType,
            PlanId = planId,
            Amount = amount,
            Currency = currency,
            PaymentMethod = method,
            ReferenceCode = referenceCode,
            Status = "Unpaid"
        };

        _context.PaymentInvoices.Add(invoice);
        await _context.SaveChangesAsync();

        TelegramAlert.Fire($"💰 Invoice Generated\n{accountType}: {accountId}\nPlan: {planId ?? "-"}\nAmount: {amount} {currency}\nMethod: {method}\nRef: {referenceCode}");

        return Ok(new
        {
            InvoiceId = invoice.Id,
            invoice.ReferenceCode,
            invoice.PlanId,
            invoice.Amount,
            invoice.Currency,
            invoice.PaymentMethod,
            Instructions = GetPaymentInstructions(method, referenceCode)
        });
    }

    /// <summary>SuperAdmin: approve an invoice, apply its plan, activate the account for 30 days.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("approve-invoice/{invoiceId:guid}")]
    public async Task<IActionResult> ApproveInvoice(Guid invoiceId)
    {
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId);

        var invoice = await _context.PaymentInvoices.FindAsync(invoiceId);
        if (invoice == null) return NotFound(new { Error = "Invoice not found." });
        if (invoice.Status == "Confirmed") return BadRequest(new { Error = "Invoice is already confirmed." });
        if (invoice.Status == "Rejected") return BadRequest(new { Error = "Invoice was rejected; generate a new one." });

        var now = DateTime.UtcNow;
        invoice.Status = "Confirmed";
        invoice.PaidAt = now;
        invoice.ConfirmedByAdminId = adminId;

        string applied;
        if (invoice.AccountType == "Agency")
        {
            var agency = await _context.Agencies.FindAsync(invoice.AccountId);
            if (agency == null) return NotFound(new { Error = "Agency not found." });

            agency.BillingStatus = "Active";
            agency.NextRenewalDate = NextRenewal(agency.NextRenewalDate, now);
            agency.TasksUsedThisMonth = 0;
            agency.AiTasksUsedThisMonth = 0;
            applied = "agency renewal";
        }
        else
        {
            var tenant = await _context.Tenants.FindAsync(invoice.AccountId);
            if (tenant == null) return NotFound(new { Error = "Tenant not found." });

            var plan = Plans.Find(invoice.PlanId);
            if (plan != null && plan.Id != "free") Plans.Apply(tenant, plan);

            tenant.BillingStatus = "Active";
            tenant.NextRenewalDate = NextRenewal(tenant.NextRenewalDate, now);
            tenant.TasksUsedThisMonth = 0;
            tenant.AiTasksUsedThisMonth = 0;
            applied = plan != null ? $"plan {plan.Name}" : "renewal (no plan on invoice)";
        }

        await _context.SaveChangesAsync();
        return Ok(new { Message = $"Invoice {invoice.ReferenceCode} approved: {applied}. Active for 30 days." });
    }

    /// <summary>SuperAdmin: reject an invoice (wrong amount, no payment received, etc.).</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("reject-invoice/{invoiceId:guid}")]
    public async Task<IActionResult> RejectInvoice(Guid invoiceId)
    {
        var invoice = await _context.PaymentInvoices.FindAsync(invoiceId);
        if (invoice == null) return NotFound(new { Error = "Invoice not found." });
        if (invoice.Status == "Confirmed") return BadRequest(new { Error = "Invoice is already confirmed." });

        invoice.Status = "Rejected";
        await _context.SaveChangesAsync();
        return Ok(new { Message = $"Invoice {invoice.ReferenceCode} rejected." });
    }

    /// <summary>SuperAdmin: find an invoice by the reference code the customer wrote in the transfer memo.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("invoice-by-ref/{referenceCode}")]
    public async Task<IActionResult> InvoiceByRef(string referenceCode)
    {
        var code = referenceCode.Trim().ToUpperInvariant();
        var invoice = await _context.PaymentInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.ReferenceCode == code);
        return invoice == null ? NotFound(new { Error = "Invoice not found." }) : Ok(invoice);
    }

    // Paying early extends from the current renewal date, so customers never lose days.
    private static DateTime NextRenewal(DateTime current, DateTime now) =>
        (current > now ? current : now).AddDays(30);

    private static object GetPaymentInstructions(string method, string refCode) => method switch
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
