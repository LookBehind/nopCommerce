using System;
using System.Buffers.Binary;
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

    // Telegram restricts the `startapp` value to [A-Za-z0-9_-], max 64 characters (found the
    // hard way - a JSON+dot-separated token got rejected client-side as "start param invalid").
    // So the token is packed binary, not JSON: 8-byte payload (ushort vendorId, ushort storeId,
    // uint expiresAtUnixSeconds, all big-endian) + an 8-byte truncated HMAC-SHA256 tag, base64url
    // -encoded as one 16-byte blob (~22 chars, well under the limit, no separator needed since
    // both fields are fixed-width). Truncating the MAC to 64 bits is fine for this threat model -
    // worst case is viewing/marking one vendor's non-sensitive order list until the token expires.
    private const int PayloadLength = 8;
    private const int SignatureLength = 8;

    public string MintBoardToken(int vendorId, int storeId)
    {
        var expiresAt = (uint)DateTimeOffset.UtcNow.Add(BoardTokenLifetime).ToUnixTimeSeconds();

        var payloadBytes = new byte[PayloadLength];
        BinaryPrimitives.WriteUInt16BigEndian(payloadBytes.AsSpan(0, 2), (ushort)vendorId);
        BinaryPrimitives.WriteUInt16BigEndian(payloadBytes.AsSpan(2, 2), (ushort)storeId);
        BinaryPrimitives.WriteUInt32BigEndian(payloadBytes.AsSpan(4, 4), expiresAt);

        var signature = SignBoardTokenPayload(payloadBytes)[..SignatureLength];

        var tokenBytes = new byte[PayloadLength + SignatureLength];
        payloadBytes.CopyTo(tokenBytes, 0);
        signature.CopyTo(tokenBytes, PayloadLength);

        return Base64UrlEncode(tokenBytes);
    }

    public bool TryValidateBoardToken(string token, out int vendorId, out int storeId)
    {
        vendorId = 0;
        storeId = 0;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        byte[] tokenBytes;
        try
        {
            tokenBytes = Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return false;
        }

        if (tokenBytes.Length != PayloadLength + SignatureLength)
            return false;

        var payloadBytes = tokenBytes.AsSpan(0, PayloadLength).ToArray();
        var providedSignature = tokenBytes.AsSpan(PayloadLength, SignatureLength).ToArray();

        var expectedSignature = SignBoardTokenPayload(payloadBytes)[..SignatureLength];
        if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            return false;

        var expiresAt = BinaryPrimitives.ReadUInt32BigEndian(payloadBytes.AsSpan(4, 4));
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt)
            return false;

        vendorId = BinaryPrimitives.ReadUInt16BigEndian(payloadBytes.AsSpan(0, 2));
        storeId = BinaryPrimitives.ReadUInt16BigEndian(payloadBytes.AsSpan(2, 2));
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
