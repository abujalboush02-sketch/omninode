using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

namespace UniversalMiddleware.API.Helpers;

public static class TelegramAlert
{
    private static readonly HttpClient _httpClient = new HttpClient();
    public static string BotToken { get; set; } = "";
    public static string ChatId { get; set; } = "";

    public static void Fire(string message)
    {
        if (string.IsNullOrEmpty(BotToken) || string.IsNullOrEmpty(ChatId))
            return;

        var url = $"https://api.telegram.org/bot{BotToken}/sendMessage?chat_id={ChatId}&text={Uri.EscapeDataString(message)}";

        // Fire and forget - discard the task so it doesn't block the executing thread
        _ = _httpClient.GetAsync(url);
    }

    public static void Initialize(IConfiguration configuration)
    {
        BotToken = configuration["Telegram:BotToken"];
        ChatId = configuration["Telegram:ChatId"];
    }
}