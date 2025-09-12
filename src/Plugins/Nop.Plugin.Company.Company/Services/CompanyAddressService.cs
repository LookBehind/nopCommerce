using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Data;
using Nop.Plugin.Company.Company.Domain;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Company address service
    /// </summary>
    public partial class CompanyAddressService : ICompanyAddressService
    {
        #region Fields

        private readonly IRepository<CompanyAddress> _companyAddressRepository;
        private readonly IRepository<Address> _addressRepository;

        #endregion

        #region Ctor

        public CompanyAddressService(
            IRepository<CompanyAddress> companyAddressRepository,
            IRepository<Address> addressRepository)
        {
            _companyAddressRepository = companyAddressRepository;
            _addressRepository = addressRepository;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets all company addresses by company identifier
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="pageIndex">Page index</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Company addresses</returns>
        public virtual async Task<IPagedList<CompanyAddress>> GetCompanyAddressesByCompanyIdAsync(int companyId,
            int pageIndex = 0, int pageSize = int.MaxValue, bool showHidden = false)
        {
            if (companyId == 0)
                return new PagedList<CompanyAddress>(new List<CompanyAddress>(), pageIndex, pageSize);

            var query = from ca in _companyAddressRepository.Table
                        join a in _addressRepository.Table on ca.AddressId equals a.Id
                        where ca.CompanyId == companyId
                        orderby ca.Id
                        select ca;

            return await query.ToPagedListAsync(pageIndex, pageSize);
        }

        /// <summary>
        /// Gets a company address
        /// </summary>
        /// <param name="companyAddressId">Company address identifier</param>
        /// <returns>Company address</returns>
        public virtual async Task<CompanyAddress> GetCompanyAddressByIdAsync(int companyAddressId)
        {
            return await _companyAddressRepository.GetByIdAsync(companyAddressId, cache => default);
        }

        /// <summary>
        /// Inserts a company address
        /// </summary>
        /// <param name="companyAddress">Company address</param>
        public virtual async Task InsertCompanyAddressAsync(CompanyAddress companyAddress)
        {
            await _companyAddressRepository.InsertAsync(companyAddress);
        }

        /// <summary>
        /// Updates the company address
        /// </summary>
        /// <param name="companyAddress">Company address</param>
        public virtual async Task UpdateCompanyAddressAsync(CompanyAddress companyAddress)
        {
            await _companyAddressRepository.UpdateAsync(companyAddress);
        }

        /// <summary>
        /// Deletes a company address
        /// </summary>
        /// <param name="companyAddress">Company address</param>
        public virtual async Task DeleteCompanyAddressAsync(CompanyAddress companyAddress)
        {
            await _companyAddressRepository.DeleteAsync(companyAddress);
        }

        /// <summary>
        /// Gets company addresses by address identifier
        /// </summary>
        /// <param name="addressId">Address identifier</param>
        /// <returns>Company addresses</returns>
        public virtual async Task<IList<CompanyAddress>> GetCompanyAddressesByAddressIdAsync(int addressId)
        {
            if (addressId == 0)
                return new List<CompanyAddress>();

            return await _companyAddressRepository.GetAllAsync(query =>
            {
                return query
                    .Where(ca => ca.AddressId == addressId)
                    .OrderBy(ca => ca.Id);
            });
        }

        #endregion
    }
}
