using System.Collections.Generic;
using Nop.Core.Domain.Orders;

namespace Nop.Core.Domain.Messages
{
    /// <summary>
    /// A container for tokens that are added.
    /// </summary>
    /// <typeparam name="U">Type</typeparam>
    public class MessageForOrderIsBeingSentEvent
    {
        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="message">Message</param>
        /// <param name="order">order</param>
        public MessageForOrderIsBeingSentEvent(MessageTemplate message, Order order)
        {
            Message = message;
            Order = order;
        }

        /// <summary>
        /// Message
        /// </summary>
        public MessageTemplate Message { get; }

        /// <summary>
        /// Order
        /// </summary>
        public Order Order { get; }
    }
}