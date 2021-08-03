using System;

namespace Nop.Web.Models.Common
{
    public class CustomerSpendingsDetailsModel
    {
        public DateTime FromDate { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; }
    }
}
