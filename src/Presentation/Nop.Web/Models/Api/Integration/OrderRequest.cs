using System;
using System.Collections.Generic;

namespace Nop.Web.Models.Api.Integration
{
    public class ExternalProduct
    {
        public string Sku { get; set; }
        public string Name { get; set; }
        public string ShortDesc { get; set; }
        public int Price { get; set; }
    }
    
    public class OrderRequest
    {
        public string CustomerEmail { get; set; }
        public string? Vendor { get; set; }
        public List<ExternalProduct> Products { get; set; }
    }
}