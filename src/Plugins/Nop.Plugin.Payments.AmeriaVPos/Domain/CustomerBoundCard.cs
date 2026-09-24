using System;
using Nop.Core;

namespace Nop.Plugin.Payments.AmeriaVPos.Domain
{
    /// <summary>
    /// A customer's saved/bound card via AmeriaBank vPOS binding transactions (see the
    /// vPOS 3.1 API doc's "Binding Transactions" section). <see cref="CardHolderId"/> is
    /// OUR OWN generated identifier (a GUID), never derived from CustomerId/OrderId -
    /// AmeriaBank's MakeBindingPayment/DeactivateBinding accept no other
    /// customer-identifying field, so this value alone authorizes a charge against the
    /// card and must be unguessable.
    /// </summary>
    public partial class CustomerBoundCard : BaseEntity
    {
        public int CustomerId { get; set; }

        /// <summary>
        /// Our own generated unique identifier sent to AmeriaBank as CardHolderID - the
        /// only key MakeBindingPayment/DeactivateBinding accept to charge/remove this
        /// specific saved card.
        /// </summary>
        public string CardHolderId { get; set; }

        /// <summary>
        /// Masked card number as returned by AmeriaBank (e.g. "428895******1234")
        /// </summary>
        public string CardPanMasked { get; set; }

        public string ExpDate { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedOnUtc { get; set; }

        public DateTime? DeactivatedOnUtc { get; set; }
    }
}
