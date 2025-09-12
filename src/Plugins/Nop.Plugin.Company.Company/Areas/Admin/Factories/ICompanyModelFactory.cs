using System.Threading.Tasks;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Companies;
using Nop.Plugin.Company.Company.Areas.Admin.Models;
using Nop.Web.Areas.Admin.Models.Customers;

namespace Nop.Plugin.Company.Company.Areas.Admin.Factories
{
    public partial interface ICompanyModelFactory
    {
        Task<CompanySearchModel> PrepareCompanySearchModelAsync(CompanySearchModel searchModel);
        Task<CompanyListModel> PrepareCompanyListModelAsync(CompanySearchModel searchModel);
        Task<CompanyModel> PrepareCompanyModelAsync(CompanyModel model, Core.Domain.Companies.Company company, bool excludeProperties = false);
        Task<CompanyCustomerListModel> PrepareCompanyCustomerListModelAsync(CompanyCustomerSearchModel searchModel, Core.Domain.Companies.Company company);
        Task<CompanyVendorListModel> PrepareCompanyVendorListModelAsync(CompanyVendorSearchModel searchModel, Core.Domain.Companies.Company company);
        Task<AddCustomerToCompanySearchModel> PrepareAddCustomerToCompanySearchModelAsync(AddCustomerToCompanySearchModel searchModel);
        Task<AddVendorToCompanySearchModel> PrepareAddVendorToCompanySearchModelAsync(AddVendorToCompanySearchModel searchModel);
        Task<AddCustomerToCompanyListModel> PrepareAddCustomerToCompanyListModelAsync(AddCustomerToCompanySearchModel searchModel);
        Task<AddVendorToCompanyListModel> PrepareAddVendorToCompanyListModelAsync(AddVendorToCompanySearchModel searchModel);
        Task<CustomerAddressListModel> PrepareCompanyCustomerAddressListModelAsync(CustomerAddressSearchModel searchModel, Core.Domain.Companies.Company company);
        Task<CustomerAddressModel> PrepareCompanyCustomerAddressModelAsync(CustomerAddressModel model,
           Core.Domain.Companies.Company company, Address address, bool excludeProperties = false);
        Task<CompanyAddressListModel> PrepareCompanyAddressListModelAsync(CompanyAddressSearchModel searchModel, Core.Domain.Companies.Company company);
        Task<CompanyAddressModel> PrepareCompanyAddressModelAsync(CompanyAddressModel model,
           Core.Domain.Companies.Company company, Address address, bool excludeProperties = false);
    }
}
