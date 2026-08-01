using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core.Domain.Companies;
using Nop.Data;

namespace Nop.Services.Companies
{
    /// <summary>
    /// Company vendor schedule service
    /// </summary>
    public partial class CompanyVendorScheduleService : ICompanyVendorScheduleService
    {
        #region Fields

        private readonly IRepository<CompanyVendorWorkingDay> _workingDayRepository;
        private readonly IRepository<CompanyVendorDayOff> _dayOffRepository;

        #endregion

        #region Ctor

        public CompanyVendorScheduleService(
            IRepository<CompanyVendorWorkingDay> workingDayRepository,
            IRepository<CompanyVendorDayOff> dayOffRepository)
        {
            _workingDayRepository = workingDayRepository;
            _dayOffRepository = dayOffRepository;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the configured working days for a company-vendor pair. An empty result
        /// means the vendor is available every day (the default).
        /// </summary>
        public virtual async Task<IList<CompanyVendorWorkingDay>> GetWorkingDaysAsync(int companyId, int vendorId)
        {
            return await _workingDayRepository.GetAllAsync(query =>
            {
                return query
                    .Where(wd => wd.CompanyId == companyId && wd.VendorId == vendorId)
                    .OrderBy(wd => wd.DayOfWeekId);
            });
        }

        /// <summary>
        /// Replaces the working-day pattern for a company-vendor pair. Passing an empty
        /// list clears the pattern, meaning the vendor becomes available every day.
        /// </summary>
        public virtual async Task SetWorkingDaysAsync(int companyId, int vendorId, IList<DayOfWeek> daysOfWeek)
        {
            var existing = await GetWorkingDaysAsync(companyId, vendorId);
            if (existing.Any())
                await _workingDayRepository.DeleteAsync(existing);

            if (daysOfWeek == null || !daysOfWeek.Any())
                return;

            var newRows = daysOfWeek
                .Distinct()
                .Select(d => new CompanyVendorWorkingDay
                {
                    CompanyId = companyId,
                    VendorId = vendorId,
                    DayOfWeekId = (int)d
                })
                .ToList();

            await _workingDayRepository.InsertAsync(newRows);
        }

        /// <summary>
        /// Gets the day-off record for a specific date, if the vendor is currently off that date
        /// </summary>
        public virtual async Task<CompanyVendorDayOff> GetDayOffAsync(int companyId, int vendorId, DateTime date)
        {
            var records = await _dayOffRepository.GetAllAsync(query =>
            {
                return query.Where(d =>
                    d.CompanyId == companyId &&
                    d.VendorId == vendorId &&
                    d.Date == date.Date &&
                    d.IsOff);
            });

            return records.FirstOrDefault();
        }

        /// <summary>
        /// Gets all day-off records (including restored ones) for a company-vendor pair
        /// within a date range, for calendar rendering
        /// </summary>
        public virtual async Task<IList<CompanyVendorDayOff>> GetDaysOffAsync(int companyId, int vendorId, DateTime fromDate, DateTime toDate)
        {
            var from = fromDate.Date;
            var to = toDate.Date;

            return await _dayOffRepository.GetAllAsync(query =>
            {
                return query
                    .Where(d => d.CompanyId == companyId && d.VendorId == vendorId && d.Date >= from && d.Date <= to)
                    .OrderBy(d => d.Date);
            });
        }

        /// <summary>
        /// Marks a specific calendar date off for a company-vendor pair
        /// </summary>
        public virtual async Task MarkDayOffAsync(int companyId, int vendorId, DateTime date)
        {
            var existing = await GetExistingRecordAsync(companyId, vendorId, date);

            if (existing != null)
            {
                existing.IsOff = true;
                existing.CreatedOnUtc = DateTime.UtcNow;
                existing.RestoredOnUtc = null;
                await _dayOffRepository.UpdateAsync(existing);
                return;
            }

            await _dayOffRepository.InsertAsync(new CompanyVendorDayOff
            {
                CompanyId = companyId,
                VendorId = vendorId,
                Date = date.Date,
                IsOff = true,
                CreatedOnUtc = DateTime.UtcNow,
                RestoredOnUtc = null
            });
        }

        /// <summary>
        /// Restores a previously marked-off day for a company-vendor pair
        /// </summary>
        public virtual async Task RestoreDayAsync(int companyId, int vendorId, DateTime date)
        {
            var existing = await GetDayOffAsync(companyId, vendorId, date);
            if (existing == null)
                return;

            existing.IsOff = false;
            existing.RestoredOnUtc = DateTime.UtcNow;
            await _dayOffRepository.UpdateAsync(existing);
        }

        /// <summary>
        /// Determines whether a vendor is available for a company on a given date,
        /// combining the day-off override with the weekly working-day pattern
        /// </summary>
        public virtual async Task<bool> IsVendorAvailableAsync(int companyId, int vendorId, DateTime date)
        {
            var dayOff = await GetDayOffAsync(companyId, vendorId, date);
            if (dayOff != null)
                return false;

            var workingDays = await GetWorkingDaysAsync(companyId, vendorId);
            if (!workingDays.Any())
                return true;

            return workingDays.Any(wd => wd.DayOfWeekId == (int)date.DayOfWeek);
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Finds the existing day-off record for a specific date regardless of its
        /// current IsOff state, so marking a day off again after a restore updates the
        /// same row instead of creating a duplicate.
        /// </summary>
        protected virtual async Task<CompanyVendorDayOff> GetExistingRecordAsync(int companyId, int vendorId, DateTime date)
        {
            var records = await _dayOffRepository.GetAllAsync(query =>
            {
                return query.Where(d =>
                    d.CompanyId == companyId &&
                    d.VendorId == vendorId &&
                    d.Date == date.Date);
            });

            return records.FirstOrDefault();
        }

        #endregion
    }
}
