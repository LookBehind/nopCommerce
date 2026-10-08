namespace Nop.Plugin.Company.Support
{
    /// <summary>
    /// Message template system names owned by this plugin (SupportCase is a plugin entity,
    /// so these can't live in core's MessageTemplateSystemNames).
    /// </summary>
    public static class SupportMessageTemplateSystemNames
    {
        /// <summary>
        /// Notifies the store owner that a new support case was submitted.
        /// </summary>
        public const string NewSupportCaseStoreOwnerNotification = "SupportCase.NewCase.StoreOwnerNotification";

        /// <summary>
        /// Notifies the store owner that a customer replied on their own support case.
        /// </summary>
        public const string SupportCaseCustomerRepliedStoreOwnerNotification = "SupportCase.CustomerReplied.StoreOwnerNotification";
    }
}
