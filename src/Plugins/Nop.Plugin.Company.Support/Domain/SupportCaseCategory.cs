namespace Nop.Plugin.Company.Support.Domain
{
    /// <summary>
    /// A customer-chosen support case category. VendorQuality is the one category that
    /// requires (and stores) a real VendorId - see SupportCase.VendorId.
    /// </summary>
    public enum SupportCaseCategory
    {
        Technical = 1,
        VendorQuality = 2,
        NewVendorRequest = 3,
        DeliveryInquiry = 4,
        BillingInquiry = 5
    }
}
