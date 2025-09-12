using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Plugin.Company.Company.Domain;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// Company address service interface
    /// </summary>
    public partial interface ICompanyAddressService
    {
        /// <summary>
        /// Gets all company addresses by company identifier
        /// </summary>
        /// <param name="companyId">Company identifier</param>
        /// <param name="pageIndex">Page index</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="showHidden">A value indicating whether to show hidden records</param>
        /// <returns>Company addresses</returns>
        Task<IPagedList<CompanyAddress>> GetCompanyAddressesByCompanyIdAsync(int companyId,
            int pageIndex = 0, int pageSize = int.MaxValue, bool showHidden = false);

        /// <summary>
        /// Gets a company address
        /// </summary>
        /// <param name="companyAddressId">Company address identifier</param>
        /// <returns>Company address</returns>
        Task<CompanyAddress> GetCompanyAddressByIdAsync(int companyAddressId);

        /// <summary>
        /// Inserts a company address
        /// </summary>
        /// <param name="companyAddress">Company address</param>
        Task InsertCompanyAddressAsync(CompanyAddress companyAddress);

        /// <summary>
        /// Updates the company address
        /// </summary>
        /// <param name="companyAddress">Company address</param>
        Task UpdateCompanyAddressAsync(CompanyAddress companyAddress);

        /// <summary>
        /// Deletes a company address
        /// </summary>
        /// <param name="companyAddress">Company address</param>
        Task DeleteCompanyAddressAsync(CompanyAddress companyAddress);

        /// <summary>
        /// Gets company addresses by address identifier
        /// </summary>
        /// <param name="addressId">Address identifier</param>
        /// <returns>Company addresses</returns>
        Task<IList<CompanyAddress>> GetCompanyAddressesByAddressIdAsync(int addressId);
    }
}
