using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nop.Core.Configuration;

namespace Nop.Plugin.Notifications.Manager.Services;

public class TelegramMiniAppAuthService : ITelegramMiniAppAuthService
{
    private static readonly TimeSpan BoardTokenLifetime = TimeSpan.FromDays(3);

    private readonly AppSettings _appSettings;

    public TelegramMiniAppAuthService(AppSettings appSettings)
    {
        _appSettings = appSettings;
    }

    private class BoardTokenPayload
    {
        public int VendorId { get; set; }
        public int StoreId { get; set; }
        public long ExpiresAtUnixSeconds { get; set; }
    }

    public string MintBoardToken(int vendorId, int storeId)
    {
        var payload = new BoardTokenPayload
        {
            VendorId = vendorId,
            StoreId = storeId,
            ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.Add(BoardTokenLifetime).ToUnixTimeSeconds()
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var payloadPart = Base64UrlEncode(payloadBytes);
        var signaturePart = Base64UrlEncode(SignBoardTokenPayload(payloadBytes));

        return $"{payloadPart}.{signaturePart}";
    }

    public bool TryValidateBoardToken(string token, out int vendorId, out int storeId)
    {
        vendorId = 0;
        storeId = 0;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 2)
            return false;

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            providedSignature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = SignBoardTokenPayload(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            return false;

        BoardTokenPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<BoardTokenPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload == null)
            return false;

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload.ExpiresAtUnixSeconds)
            return false;

        vendorId = payload.VendorId;
        storeId = payload.StoreId;
        return true;
    }

    // Telegram's documented algorithm:
    //   secret_key = HMAC_SHA256(data: bot_token, key: "WebAppData")
    //   expected_hash = hex(HMAC_SHA256(data: data_check_string, key: secret_key))
    // data_check_string = every initData field except "hash", sorted by key, joined "key=value" with '\n'.
    public bool TryValidateInitData(string initData, out long telegramUserId)
    {
        telegramUserId = 0;

        if (string.IsNullOrWhiteSpace(initData))
            return false;

        var parsed = ParseQueryString(initData);
        if (!parsed.TryGetValue("hash", out var providedHash) || string.IsNullOrEmpty(providedHash))
            return false;

        var dataCheckString = string.Join('\n', parsed
            .Where(kv => kv.Key != "hash")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        var botToken = _appSettings.ExtendedAuthSettings.TelegramBotSecret;
        using var secretKeyHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = secretKeyHmac.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        using var dataHmac = new HMACSHA256(secretKey);
        var computedHash = dataHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
        var computedHashHex = Convert.ToHexString(computedHash).ToLowerInvariant();

        if (!string.Equals(computedHashHex, providedHash, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!parsed.TryGetValue("user", out var userJson) || string.IsNullOrEmpty(userJson))
            return false;

        using var userDoc = JsonDocument.Parse(userJson);
        if (!userDoc.RootElement.TryGetProperty("id", out var idElement))
            return false;

        telegramUserId = idElement.GetInt64();
        return true;
    }

    private byte[] SignBoardTokenPayload(byte[] payloadBytes)
    {
        var secret = _appSettings.ExtendedAuthSettings.TelegramMiniAppSigningSecret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(payloadBytes);
    }

    // Telegram's initData is a plain "a=1&b=2" query string, not URL-prefixed - decode
    // each value once with WebUtility (no System.Web.HttpUtility dependency needed).
    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0)
                continue;

            var key = WebUtility.UrlDecode(pair[..idx]);
            var value = WebUtility.UrlDecode(pair[(idx + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
