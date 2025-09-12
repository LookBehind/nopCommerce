using AutoMapper;
using Nop.Core.Domain.Companies;
using Nop.Core.Infrastructure.Mapper;
using Nop.Plugin.Company.Company.Areas.Admin.Models;
using Nop.Plugin.Company.Company.Domain;

namespace Nop.Plugin.Company.Company.Infrastructure.Mapper
{
    /// <summary>
    /// AutoMapper configuration for Company plugin models
    /// </summary>
    public class CompanyMapperConfiguration : Profile, IOrderedMapperProfile
    {
        #region Ctor

        public CompanyMapperConfiguration()
        {
            CreateMap<Core.Domain.Companies.Company, CompanyModel>();
            CreateMap<CompanyModel, Core.Domain.Companies.Company>();

            CreateMap<CompanyCustomer, CompanyCustomerModel>()
                .ForMember(model => model.CustomerFullName, options => options.Ignore());
            CreateMap<CompanyCustomerModel, CompanyCustomer>()
                .ForMember(entity => entity.CompanyId, options => options.Ignore())
                .ForMember(entity => entity.CustomerId, options => options.Ignore());

            CreateMap<CompanyVendor, CompanyVendorModel>()
                .ForMember(model => model.VendorName, options => options.Ignore());
            CreateMap<CompanyVendorModel, CompanyVendor>()
                .ForMember(entity => entity.CompanyId, options => options.Ignore())
                .ForMember(entity => entity.VendorId, options => options.Ignore());

            CreateMap<CompanyAddress, CompanyAddressModel>()
                .ForMember(model => model.Address, options => options.Ignore());
            CreateMap<CompanyAddressModel, CompanyAddress>()
                .ForMember(entity => entity.CompanyId, options => options.Ignore())
                .ForMember(entity => entity.AddressId, options => options.Ignore());
        }

        #endregion

        #region Properties

        /// <summary>
        /// Order of this mapper implementation
        /// </summary>
        public int Order => 1;

        #endregion
    }
}
