using System;
using Nop.Core.Domain.Companies;

namespace Nop.Services.Payments;

public class CustomerBalanceResult
{
    public decimal TotalAllowance { get; set; }
    public decimal RemainingAllowance { get; set; }
    public AmountLimitType RefreshCadence { get; set; }
    public TimeSpan RefreshedAfter { get; set; }

    /// <summary>
    /// How much the customer should have spent by now if spending evenly across the
    /// current allowance period, based on how many days have already elapsed in it.
    /// Extracted from Nop.Plugin.Payments.CheckMoneyOrder's BalanceViewComponent, which
    /// computed this same formula inline for the storefront header widget - centralized
    /// here so the mobile API can expose the identical number instead of a second,
    /// possibly-drifting copy of the formula.
    /// </summary>
    public decimal GetRecommendedSpendingUntilNow()
    {
        var daysInPeriod = RefreshCadence switch
        {
            AmountLimitType.Daily => 1,
            AmountLimitType.Weekly => 7,
            AmountLimitType.Monthly => DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month),
            _ => throw new ArgumentOutOfRangeException(nameof(RefreshCadence))
        };

        var recommendedAverageSpending = TotalAllowance / daysInPeriod;
        var daysRemaining = RefreshedAfter.Days;
        var daysPassed = daysInPeriod - daysRemaining;

        return daysPassed * recommendedAverageSpending;
    }
}