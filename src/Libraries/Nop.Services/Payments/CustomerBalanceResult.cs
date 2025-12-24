using System;
using Nop.Core.Domain.Companies;

namespace Nop.Services.Payments;

public class CustomerBalanceResult
{
    public decimal TotalAllowance { get; set; }
    public decimal RemainingAllowance { get; set; }
    public AmountLimitType RefreshCadence { get; set; }
    public TimeSpan RefreshedAfter { get; set; }
}