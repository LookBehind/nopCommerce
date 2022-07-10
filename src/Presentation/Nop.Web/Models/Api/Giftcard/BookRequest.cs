using System;

namespace Nop.Web.Models.Api.Giftcard
{
    public class BookRequest
    {
        public string CustomerEmail { get; set; }
        public DateTime BookingDate { get; set; }
    }
}