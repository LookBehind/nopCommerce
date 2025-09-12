using System.Collections.Generic;
using Nop.Web.Areas.Admin.Models.Customers;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    public partial record CompanyModel : BaseNopEntityModel, ILocalizedModel<CompanyLocalizedModel>
    {
        public CompanyModel()
        {
            CompanyCustomerSearchModel = new CompanyCustomerSearchModel();
            Locales = new List<CompanyLocalizedModel>();
            CompanyAddressSearchModel = new CompanyAddressSearchModel();
            CompanyVendorSearchModel = new CompanyVendorSearchModel();
        }
        [NopResourceDisplayName("Admin.Companies.Company.Fields.Email")]
        public string Email { get; set; }

        [NopResourceDisplayName("Admin.Companies.Company.Fields.Name")]
        public string Name { get; set; }

        [NopResourceDisplayName("Admin.Companies.Company.Fields.AmountLimit")]
        public decimal AmountLimit { get; set; }

        public CompanyCustomerSearchModel CompanyCustomerSearchModel { get; set; }

        public IList<CompanyLocalizedModel> Locales { get; set; }

        public CompanyAddressSearchModel CompanyAddressSearchModel { get; set; }

        public CompanyVendorSearchModel CompanyVendorSearchModel { get; set; }

        public bool CustomerExist { get; set; }
    }

    public partial record CompanyLocalizedModel : ILocalizedLocaleModel
    {
        public int LanguageId { get; set; }

        [NopResourceDisplayName("Admin.Companies.Company.Fields.Name")]
        public string Name { get; set; }

        [NopResourceDisplayName("Admin.Companies.Company.Fields.Email")]
        public string Email { get; set; }
    }
}
