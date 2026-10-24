using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.API.Helpers;
using UniversalMiddleware.Domain;
using UniversalMiddleware.Infrastructure;

namespace UniversalMiddleware.API.Controllers;

public record GenerateInvoiceRequest(string? PlanId, decimal Amount, string? Currency, string PaymentMethod, bool AddWhiteLabel = false);

/// <summary>SuperAdmin overrides at approval, mainly for custom Agency Platinum deals. All optional.</summary>
public record ApproveInvoiceOverrides(decimal? Amount, int? MonthlyTaskQuota, int? MonthlyAiQuota, int? MaxSubTenants);

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

    /// <summary>Public plan catalog. ?audience=agency returns agency plans and the white-label add-on.</summary>
    [AllowAnonymous]
    [HttpGet("plans")]
    public IActionResult GetPlans([FromQuery] string? audience)
    {
        if (string.Equals(audience, "agency", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                Plans = AgencyPlans.All.Select(p => new
                {
                    p.Id, p.Name, p.PriceJod, p.PriceUsdRef, p.MaxClients, p.Tasks, p.AiTasks,
                    p.WhiteLabelIncluded, p.CustomPricing, p.Extras
                }),
                WhiteLabelAddOn = new { PriceJod = AgencyPlans.WhiteLabelAddOnJod, PriceUsdRef = AgencyPlans.WhiteLabelAddOnUsdRef }
            });
        }

        return Ok(Plans.All.Select(p => new { p.Id, p.Name, p.PriceJod, p.PriceUsdRef, p.Tasks, p.AiTasks, p.Extras }));
    }

    /// <summary>The caller's invoices, newest first.</summary>
    [HttpGet("my-invoices")]
    public async Task<IActionResult> MyInvoices()
    {
        var user = await CurrentUser();
        if (user == null) return Unauthorized();

        var account = BillableAccount(user);
        if (account == null) return Ok(Array.Empty<object>());

        var (accountId, accountType) = account.Value;
        var accountName = await AccountName(accountId, accountType);
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
            i.WhiteLabelAddOn,
            i.Amount,
            i.Currency,
            i.PaymentMethod,
            PlanName = PlanName(i.PlanId),
            Instructions = GetPaymentInstructions(i.PaymentMethod.ToLowerInvariant(), i.ReferenceCode),
            TransferNote = TransferNote(i, accountName),
            Status = i.Status switch { "Confirmed" => "Approved", "Rejected" => "Rejected", _ => "Pending" },
            i.CreatedAt,
            i.PaymentReportedAt,
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
        var addWhiteLabel = false;

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
            // Agencies buy an agency plan (+ optional white-label add-on); the server sets the price.
            var plan = AgencyPlans.Find(request.PlanId);
            if (plan == null || plan.PriceJod == 0m)
                return BadRequest(new { Error = "planId must be one of: agency_bronze, agency_silver, agency_gold, agency_platinum." });

            planId = plan.Id;
            addWhiteLabel = request.AddWhiteLabel && !plan.WhiteLabelIncluded;
            amount = plan.PriceJod + (addWhiteLabel ? AgencyPlans.WhiteLabelAddOnJod : 0m);
            currency = "JOD";
        }

        // Format: INV-YYYY-XXXXXX
        var referenceCode = $"INV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var invoice = new PaymentInvoice
        {
            AccountId = accountId,
            AccountType = accountType,
            PlanId = planId,
            WhiteLabelAddOn = addWhiteLabel,
            Amount = amount,
            Currency = currency,
            PaymentMethod = method,
            ReferenceCode = referenceCode,
            Status = "Unpaid"
        };

        _context.PaymentInvoices.Add(invoice);
        await _context.SaveChangesAsync();

        var name = await AccountName(accountId, accountType);
        TelegramAlert.Fire($"💰 Invoice Generated\n{accountType}: {name}\nPlan: {planId ?? "-"}{(addWhiteLabel ? " + white-label" : "")}\nAmount: {amount} {currency}\nMethod: {method}\nRef: {referenceCode}");

        return Ok(new
        {
            InvoiceId = invoice.Id,
            invoice.ReferenceCode,
            invoice.PlanId,
            invoice.WhiteLabelAddOn,
            invoice.Amount,
            invoice.Currency,
            invoice.PaymentMethod,
            PlanName = PlanName(invoice.PlanId),
            Instructions = GetPaymentInstructions(method, referenceCode),
            TransferNote = TransferNote(invoice, name)
        });
    }

    /// <summary>Customer says they've paid; alerts the admin. Doesn't activate anything by itself.</summary>
    [HttpPost("invoices/{invoiceId:guid}/report-payment")]
    public async Task<IActionResult> ReportPayment(Guid invoiceId)
    {
        var user = await CurrentUser();
        if (user == null) return Unauthorized();
        var account = BillableAccount(user);
        if (account == null) return NotFound(new { Error = "Invoice not found." });

        var (accountId, accountType) = account.Value;
        var invoice = await _context.PaymentInvoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.AccountId == accountId && i.AccountType == accountType);
        if (invoice == null) return NotFound(new { Error = "Invoice not found." });
        if (invoice.Status != "Unpaid") return BadRequest(new { Error = "This invoice is already closed." });

        var first = invoice.PaymentReportedAt == null;
        invoice.PaymentReportedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (first)
        {
            var name = await AccountName(accountId, accountType);
            TelegramAlert.Fire($"✅ Payment reported\n{accountType}: {name}\nRef: {invoice.ReferenceCode}\nAmount: {invoice.Amount} {invoice.Currency}\nMethod: {invoice.PaymentMethod}\nCheck the bank and approve.");
        }

        return Ok(new { invoice.ReferenceCode, invoice.PaymentReportedAt, Message = "Thanks! We'll activate your plan once the payment is confirmed." });
    }

    /// <summary>SuperAdmin: approve an invoice, apply its plan, activate the account for 30 days.</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("approve-invoice/{invoiceId:guid}")]
    public async Task<IActionResult> ApproveInvoice(Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveInvoiceOverrides? overrides)
    {
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId);

        var invoice = await _context.PaymentInvoices.FindAsync(invoiceId);
        if (invoice == null) return NotFound(new { Error = "Invoice not found." });
        if (invoice.Status == "Confirmed") return BadRequest(new { Error = "Invoice is already confirmed." });
        if (invoice.Status == "Rejected") return BadRequest(new { Error = "Invoice was rejected; generate a new one." });

        var now = DateTime.UtcNow;
        if (overrides?.Amount is > 0m) invoice.Amount = overrides.Amount.Value; // record the negotiated price
        invoice.Status = "Confirmed";
        invoice.PaidAt = now;
        invoice.ConfirmedByAdminId = adminId;

        string applied;
        if (invoice.AccountType == "Agency")
        {
            var agency = await _context.Agencies.FindAsync(invoice.AccountId);
            if (agency == null) return NotFound(new { Error = "Agency not found." });

            var wasTrialAgency = agency.BillingStatus is "Trial" or "TrialExpired";
            var plan = AgencyPlans.Find(invoice.PlanId);
            if (plan != null) AgencyPlans.Apply(agency, plan);
            if (invoice.WhiteLabelAddOn) agency.WhiteLabel = true;
            else if (plan != null && !plan.WhiteLabelIncluded) agency.WhiteLabel = false; // add-on not renewed

            // Custom deal limits (Platinum)
            if (overrides?.MonthlyTaskQuota is > 0) agency.MonthlyTaskQuota = overrides.MonthlyTaskQuota.Value;
            if (overrides?.MonthlyAiQuota is >= 0) agency.MonthlyAiQuota = overrides.MonthlyAiQuota.Value;
            if (overrides?.MaxSubTenants is > 0) agency.MaxSubTenants = overrides.MaxSubTenants.Value;

            agency.BillingStatus = "Active";
            agency.NextRenewalDate = wasTrialAgency ? now.AddDays(30) : NextRenewal(agency.NextRenewalDate, now);
            agency.TasksUsedThisMonth = 0;
            agency.AiTasksUsedThisMonth = 0;

            // Clients follow the agency's renewal date; their own counters reset too
            var clients = await _context.Tenants.Where(t => t.AgencyId == agency.Id).ToListAsync();
            foreach (var c in clients)
            {
                c.NextRenewalDate = agency.NextRenewalDate;
                c.TasksUsedThisMonth = 0;
                c.AiTasksUsedThisMonth = 0;
                c.SubscriptionTier = agency.SubscriptionTier;
            }

            applied = plan != null ? $"agency plan {plan.Name}{(agency.WhiteLabel ? " (white-label)" : "")}" : "agency renewal";
        }
        else
        {
            var tenant = await _context.Tenants.FindAsync(invoice.AccountId);
            if (tenant == null) return NotFound(new { Error = "Tenant not found." });

            var wasTrialTenant = tenant.BillingStatus is "Trial" or "TrialExpired";
            var plan = Plans.Find(invoice.PlanId);
            if (plan != null && plan.Id != "free") Plans.Apply(tenant, plan);

            tenant.BillingStatus = "Active";
            tenant.NextRenewalDate = wasTrialTenant ? now.AddDays(30) : NextRenewal(tenant.NextRenewalDate, now);
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

    private async Task<string> AccountName(Guid accountId, string accountType) =>
        accountType == "Agency"
            ? await _context.Agencies.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Name).FirstOrDefaultAsync() ?? "Agency"
            : await _context.Tenants.AsNoTracking().Where(t => t.Id == accountId).Select(t => t.Name).FirstOrDefaultAsync() ?? "Workspace";

    private static string? PlanName(string? planId) =>
        Plans.Find(planId)?.Name ?? AgencyPlans.Find(planId)?.Name;

    /// <summary>One line the customer pastes into the bank/CliQ description; everything we need to match the payment.</summary>
    private static string TransferNote(PaymentInvoice i, string accountName)
    {
        var plan = PlanName(i.PlanId) ?? "Plan";
        var addOn = i.WhiteLabelAddOn ? " + White-label" : "";
        var name = accountName.Length > 40 ? accountName[..40] : accountName;
        return $"OmniNode | {i.ReferenceCode} | {name} | {plan}{addOn} | {i.Amount:0.###} {i.Currency}";
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
            Note = "Copy the transfer message and paste it into the CliQ transfer description."
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
