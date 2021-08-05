using Nop.Core.Domain.Customers;
using Nop.Web.Framework.Models;

namespace Nop.Web.Models.Common
{
    public partial record HeaderLinksModel : BaseNopModel
    {
        public bool IsAuthenticated { get; set; }
        public bool IsRegistrationEnabled { get; set; }
        public string CustomerName { get; set; }
        
        public bool ShoppingCartEnabled { get; set; }
        public decimal ShoppingCartItems { get; set; }
        
        public bool WishlistEnabled { get; set; }
        public decimal WishlistItems { get; set; }

        public bool AllowPrivateMessages { get; set; }
        public string UnreadPrivateMessages { get; set; }
        public string AlertMessage { get; set; }
        public UserRegistrationType RegistrationType { get; set; }
    }
}