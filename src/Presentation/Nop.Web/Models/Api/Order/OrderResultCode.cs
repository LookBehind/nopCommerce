namespace Nop.Web.Models.Api.Order
{
    /// <summary>
    /// Result codes returned by the order API (order-confirmation).
    /// Keep in sync with the mobile app's orderErrorCodes.js.
    /// </summary>
    public enum OrderResultCode
    {
        /// <summary>No specific error.</summary>
        None = 0,

        /// <summary>Scheduled delivery time has passed or is otherwise invalid.</summary>
        ScheduleNotAllowed = 1000,

        /// <summary>Customer has no valid billing/shipping (delivery) address.</summary>
        InvalidDeliveryAddress = 1001
    }
}
