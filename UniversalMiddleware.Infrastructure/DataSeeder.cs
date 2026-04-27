using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.Infrastructure;

public static class DataSeeder {
    public static async Task SeedTemplatesAsync(AppDbContext context) {
        var templates = new[] {
            new { Name = "Shopify", Json = "{\"id\": 123, \"total_price\": \"10.00\", \"customer\": {\"first_name\": \"John\"}}" },
            new { Name = "WooCommerce", Json = "{\"id\": 456, \"total\": \"15.00\", \"billing\": {\"first_name\": \"Jane\"}}" },
            new { Name = "Magento", Json = "{\"entity_id\": 789, \"base_grand_total\": 20.00, \"customer_firstname\": \"Bob\"}" },
            new { Name = "Amazon SP-API", Json = "{\"AmazonOrderId\": \"111-2222222-3333333\", \"OrderTotal\": {\"Amount\": \"25.00\"}}" },
            new { Name = "eBay", Json = "{\"orderId\": \"12-34567-89012\", \"total\": {\"value\": \"30.00\"}}" },
            new { Name = "Stripe", Json = "{\"id\": \"cs_test_123\", \"amount_total\": 3500, \"customer_details\": {\"name\": \"Alice\"}}" },
            new { Name = "BigCommerce", Json = "{\"id\": 101, \"total_inc_tax\": \"40.00\", \"billing_address\": {\"first_name\": \"Tom\"}}" },
            new { Name = "Wix eCommerce", Json = "{\"orderId\": \"abc-123\", \"totals\": {\"total\": 45.00}, \"buyerInfo\": {\"firstName\": \"Sam\"}}" },
            new { Name = "Etsy", Json = "{\"receipt_id\": 202, \"grandtotal\": 50.00, \"name\": \"Chris\"}" },
            new { Name = "Square", Json = "{\"order_id\": \"xyz-789\", \"total_money\": {\"amount\": 5500}}" },
            new { Name = "HubSpot", Json = "{\"properties\": {\"dealname\": \"New Deal\", \"amount\": \"60.00\"}}" },
            new { Name = "Salesforce", Json = "{\"Name\": \"New Opp\", \"Amount\": 65.00}" },
            new { Name = "Zoho CRM", Json = "{\"data\": [{\"Subject\": \"Sale\", \"Grand_Total\": 70.00}]}" },
            new { Name = "Pipedrive", Json = "{\"title\": \"Deal 1\", \"value\": 75.00}" },
            new { Name = "Microsoft Dynamics 365", Json = "{\"name\": \"Opp 1\", \"estimatedvalue\": 80.00}" },
            new { Name = "Monday.com", Json = "{\"item_name\": \"New Pulse\", \"column_values\": {\"numbers\": 85.00}}" },
            new { Name = "Zendesk Sell", Json = "{\"data\": {\"name\": \"Deal 2\", \"value\": 90.00}}" },
            new { Name = "Freshsales", Json = "{\"deal\": {\"name\": \"Deal 3\", \"amount\": 95.00}}" },
            new { Name = "ClickUp", Json = "{\"name\": \"Task 1\", \"custom_fields\": [{\"value\": 100.00}]}" },
            new { Name = "GoHighLevel", Json = "{\"contact_id\": \"123\", \"opportunity\": {\"title\": \"Deal\", \"monetaryValue\": 105.00}}" }
        };

        foreach (var t in templates) {
            if (!await context.SchemaTemplates.AnyAsync(x => x.PlatformName == t.Name)) {
                context.SchemaTemplates.Add(new SchemaTemplate {
                    PlatformName = t.Name,
                    SchemaJson = t.Json
                });
            }
        }
        
        await context.SaveChangesAsync();
    }
}
