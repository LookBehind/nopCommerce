namespace Nop.Core.Domain.Orders
{
    /// <summary>
    /// Represents the channel an order was placed through
    /// </summary>
    public enum OrderSource
    {
        /// <summary>
        /// Unknown / not classified (default; historical rows before this field existed)
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Storefront web checkout
        /// </summary>
        Web = 10,

        /// <summary>
        /// Mobile app
        /// </summary>
        Mobile = 20,

        /// <summary>
        /// Kerpak external integration
        /// </summary>
        Kerpak = 30
    }
}
