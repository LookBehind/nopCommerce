using System;
using Nop.Core.Domain.Customers;

namespace Nop.Services.Payments;

public class CustomerBalanceRequest
{
    public DateTime OrderDateUtc { get; set; }
    public Customer Customer { get; set; }
}