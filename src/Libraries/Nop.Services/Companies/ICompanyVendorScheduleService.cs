using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Companies;

namespace Nop.Services.Companies
{
    /// <summary>
    /// Company vendor schedule service interface — the weekly recurring working-day
    /// pattern and per-date day-off overrides for a (company, vendor) pair
    /// </summary>
    public partial interface ICompanyVendorScheduleService
    {
        /// <summary>
        /// Gets the configured working days for a company-vendor pair. An empty result
        /// means the vendor is available every day (the default).
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="vendorId">Vendor identifier</param>
        /// <returns>Working days</returns>
        Task<IList<CompanyVendorWorkingDay>> GetWorkingDaysAsync(int companyId, int vendorId);

        /// <summary>
        /// Replaces the working-day pattern for a company-vendor pair. Passing an empty
        /// list clears the pattern, meaning the vendor becomes available every day.
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="vendorId">Vendor identifier</param>
        /// <param name="daysOfWeek">The days of week the vendor works</param>
        Task SetWorkingDaysAsync(int companyId, int vendorId, IList<DayOfWeek> daysOfWeek);

        /// <summary>
        /// Gets the day-off record for a specific date, if the vendor is currently off that date
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="vendorId">Vendor identifier</param>
        /// <param name="date">Company-local calendar date</param>
        /// <returns>The day-off record, or null if the vendor is not off that date</returns>
        Task<CompanyVendorDayOff> GetDayOffAsync(int companyId, int vendorId, DateTime date);

        /// <summary>
        /// Gets all day-off records (including restored ones) for a company-vendor pair
        /// within a date range, for calendar rendering
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="vendorId">Vendor identifier</param>
        /// <param name="fromDate">Range start (inclusive)</param>
        /// <param name="toDate">Range end (inclusive)</param>
        /// <returns>Day-off records</returns>
        Task<IList<CompanyVendorDayOff>> GetDaysOffAsync(int companyId, int vendorId, DateTime fromDate, DateTime toDate);

        /// <summary>
        /// Marks a specific calendar date off for a company-vendor pair
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="vendorId">Vendor identifier</param>
        /// <param name="date">Company-local calendar date</param>
        Task MarkDayOffAsync(int companyId, int vendorId, DateTime date);

        /// <summary>
        /// Restores a previously marked-off day for a company-vendor pair
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="vendorId">Vendor identifier</param>
        /// <param name="date">Company-local calendar date</param>
        Task RestoreDayAsync(int companyId, int vendorId, DateTime date);

        /// <summary>
        /// Determines whether a vendor is available for a company on a given date,
        /// combining the day-off override with the weekly working-day pattern
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="vendorId">Vendor identifier</param>
        /// <param name="date">Company-local calendar date</param>
        /// <returns>True if the vendor is available on that date</returns>
        Task<bool> IsVendorAvailableAsync(int companyId, int vendorId, DateTime date);
    }
}
