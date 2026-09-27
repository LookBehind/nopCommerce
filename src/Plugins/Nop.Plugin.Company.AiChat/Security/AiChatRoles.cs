namespace Nop.Plugin.Company.AiChat.Security
{
    /// <summary>
    /// The feature-flag gate for the whole AI Assistant feature: a plain CustomerRole (seeded on
    /// install, assigned per-customer via the existing core Admin &gt; Customers &gt; edit &gt;
    /// "Customer roles" checkboxes - no new admin UI needed) rather than a bespoke feature-flag
    /// table. A customer sees the feature only while they hold this role.
    /// </summary>
    public static class AiChatRoles
    {
        public const string BetaTesterSystemName = "AiChatBetaTester";
        public const string BetaTesterName = "AI Chat Beta Tester";
    }
}
