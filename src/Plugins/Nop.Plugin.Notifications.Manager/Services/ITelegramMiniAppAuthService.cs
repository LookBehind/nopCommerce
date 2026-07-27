using System;

namespace Nop.Plugin.Notifications.Manager.Services;

/// <summary>
/// Mints and validates the signed, vendor-scoped "board" tokens embedded in the Telegram Mini
/// App links posted to vendor chats, and validates Telegram's own signed `initData` payload sent
/// by the Mini App on every API call. See docs/plans/2026-07-28-vendor-delivery-mini-app.md.
/// </summary>
public interface ITelegramMiniAppAuthService
{
    /// <summary>
    /// Mints a signed token scoped to one vendor+store, valid for 3 days from now. Anyone holding
    /// a message containing this link (i.e. anyone in that vendor's Telegram chat) can use it -
    /// this is the intended access model, not a placeholder for per-user auth.
    /// </summary>
    string MintBoardToken(int vendorId, int storeId);

    /// <summary>
    /// Validates a board token's signature and expiry. Returns false if tampered, malformed, or
    /// expired.
    /// </summary>
    bool TryValidateBoardToken(string token, out int vendorId, out int storeId);

    /// <summary>
    /// Validates Telegram's `initData` HMAC signature (proves the request genuinely came from a
    /// Telegram client launch, not a bare script), per the documented algorithm:
    /// secret_key = HMAC_SHA256(data: bot_token, key: "WebAppData");
    /// expected_hash = hex(HMAC_SHA256(data: data_check_string, key: secret_key)).
    /// </summary>
    bool TryValidateInitData(string initData, out long telegramUserId);
}
