using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Companies;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Services.Helpers;

namespace Nop.Services.Companies
{
    public partial class CompanyService : ICompanyService
    {
        #region Fields

        private readonly IRepository<Company> _companyRepository;
        private readonly IRepository<CompanyCustomer> _companyCustomerRepository;
        private readonly IRepository<CompanyVendor> _companyVendorRepository;
        private readonly IRepository<Customer> _customerRepository;
        private readonly IRepository<Vendor> _vendorRepository;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;
        private readonly IDateTimeHelper _dateTimeHelper;

        #endregion

        #region Ctor

        public CompanyService(
            IRepository<Customer> customerRepository,
            IRepository<Company> companyRepository,
            IRepository<CompanyCustomer> companyCustomerRepository,
            IStoreContext storeContext,
            IWorkContext workContext,
            IRepository<CompanyVendor> companyVendorRepository,
            IRepository<Vendor> vendorRepository,
            IDateTimeHelper dateTimeHelper)
        {
            _customerRepository = customerRepository;
            _companyRepository = companyRepository;
            _companyCustomerRepository = companyCustomerRepository;
            _storeContext = storeContext;
            _workContext = workContext;
            _companyVendorRepository = companyVendorRepository;
            _vendorRepository = vendorRepository;
            _dateTimeHelper = dateTimeHelper;
        }

        #endregion

        #region Methods

        #region Companies

        public virtual async Task<IPagedList<Company>> GetAllCompaniesAsync(string name = null, string email = null,
            int pageIndex = 0, int pageSize = int.MaxValue, bool getOnlyTotalCount = false)
        {
            var companies = await _companyRepository.GetAllPagedAsync(query =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                    query = query.Where(c => c.Name.Contains(name));

                if (!string.IsNullOrWhiteSpace(email))
                    query = query.Where(c => c.Email.Contains(email));

                query = query.OrderByDescending(c => c.Id);

                return query;
            }, pageIndex, pageSize, getOnlyTotalCount);

            return companies;
        }

        public virtual async Task DeleteCompanyAsync(Company company)
        {
            if (company == null)
                throw new ArgumentNullException(nameof(company));

            await _companyRepository.DeleteAsync(company);
        }

        public virtual async Task<Company> GetCompanyByIdAsync(int companyId)
        {
            return await _companyRepository.GetByIdAsync(companyId);
        }

        public virtual async Task InsertCompanyAsync(Company company)
        {
            company.TimeZone = company.TimeZone ?? (await _dateTimeHelper.GetCurrentTimeZoneAsync()).DisplayName;
            await _companyRepository.InsertAsync(company);
        }

        public virtual async Task UpdateCompanyAsync(Company company)
        {
            await _companyRepository.UpdateAsync(company);
        }

        #endregion

        #region Company Vendor

        public virtual async Task UpdateCompanyVendorAsync(CompanyVendor companyVendor)
        {
            await _companyVendorRepository.UpdateAsync(companyVendor);

        }

        public virtual CompanyVendor FindCompanyVendor(IList<CompanyVendor> source, int vendorId, int companyId)
        {
            foreach (var companyVendor in source)
                if (companyVendor.VendorId == vendorId && companyVendor.CompanyId == companyId)
                    return companyVendor;

            return null;
        }

        public virtual async Task<IPagedList<CompanyVendor>> GetCompanyVendorsByCompanyIdAsync(int companyId,
            int pageIndex = 0, int pageSize = int.MaxValue, bool showHidden = false)
        {
            if (companyId == 0)
                return new PagedList<CompanyVendor>(new List<CompanyVendor>(), pageIndex, pageSize);

            var query = from pc in _companyVendorRepository.Table
                        join p in _vendorRepository.Table on pc.VendorId equals p.Id
                        where pc.CompanyId == companyId && !p.Deleted
                        orderby pc.Id
                        select pc;

            return await query.ToPagedListAsync(pageIndex, pageSize);
        }

        public virtual async Task<IList<CompanyVendor>> GetCompanyVendorsByVendorIdAsync(int vendorId)
        {
            return await GetCompanyVendorsByCustomerIdAsync(vendorId);
        }

        protected virtual async Task<IList<CompanyVendor>> GetCompanyVendorsByCustomerIdAsync(int vendorId)
        {
            if (vendorId == 0)
                return new List<CompanyVendor>();

            return await _companyVendorRepository.GetAllAsync(query =>
            {
                return query
                    .Where(pc => pc.VendorId == vendorId)
                    .OrderBy(pc => pc.Id);

            });
        }

        public virtual async Task InsertCompanyVendorAsync(CompanyVendor companyVendor)
        {
            await _companyVendorRepository.InsertAsync(companyVendor);
        }

        public virtual async Task<CompanyVendor> GetCompanyVendorByIdAsync(int companyVendorId)
        {
            return await _companyVendorRepository.GetByIdAsync(companyVendorId, cache => default);
        }

        public virtual async Task DeleteCompanyVendorAsync(CompanyVendor companyVendor)
        {
            await _companyVendorRepository.DeleteAsync(companyVendor);
        }

        public virtual async Task<IList<CompanyVendor>> GetCompanyVendorsByCompanyAsync(int companyId)
        {
            if (companyId == 0)
                return new List<CompanyVendor>();

            return await _companyVendorRepository.GetAllAsync(async query =>
            {
                return query
                    .Where(pc => pc.CompanyId == companyId)
                    .OrderBy(pc => pc.Id);

            });
        }

        #endregion

        #region Company Customer

        public virtual CompanyCustomer FindCompanyCustomer(IList<CompanyCustomer> source, int customerId, int companyId)
        {
            foreach (var companyCustomer in source)
                if (companyCustomer.CustomerId == customerId && companyCustomer.CompanyId == companyId)
                    return companyCustomer;

            return null;
        }

        public virtual async Task DeleteCompanyCustomerAsync(CompanyCustomer companyCustomer)
        {
            await _companyCustomerRepository.DeleteAsync(companyCustomer);
        }

        public virtual async Task<IPagedList<CompanyCustomer>> GetCompanyCustomersByCompanyIdAsync(int companyId,
            int pageIndex = 0, int pageSize = int.MaxValue, bool showHidden = false)
        {
            if (companyId == 0)
                return new PagedList<CompanyCustomer>(new List<CompanyCustomer>(), pageIndex, pageSize);

            var query = from pc in _companyCustomerRepository.Table
                        join p in _customerRepository.Table on pc.CustomerId equals p.Id
                        where pc.CompanyId == companyId && !p.Deleted
                        orderby pc.Id
                        select pc;


            return await query.ToPagedListAsync(pageIndex, pageSize);
        }

        public virtual async Task<IList<CompanyCustomer>> GetCompanyCustomersByCustomerIdAsync(int customerId, bool showHidden = false)
        {
            return await GetCompanyCustomersByCustomerIdAsync(customerId, (await _storeContext.GetCurrentStoreAsync()).Id, showHidden);
        }

        public virtual async Task<CompanyCustomer> GetCompanyCustomersByIdAsync(int companyCustomersId)
        {
            return await _companyCustomerRepository.GetByIdAsync(companyCustomersId, cache => default);
        }

        public virtual async Task InsertCompanyCustomerAsync(CompanyCustomer companyCustomer)
        {
            await _companyCustomerRepository.InsertAsync(companyCustomer);
        }

        public virtual async Task UpdateCompanyCustomerAsync(CompanyCustomer companyCustomer)
        {
            await _companyCustomerRepository.UpdateAsync(companyCustomer);

        }
        public virtual async Task<Company> GetCompanyByCustomerIdAsync(int customerId)
        {
            if (customerId == 0)
                return null;

            var customer = await _workContext.GetCurrentCustomerAsync();

            var companies = _companyRepository.Table;

            companies = from company in companies
                        join dcm in _companyCustomerRepository.Table on company.Id equals dcm.CompanyId
                        where dcm.CustomerId == customerId
                        select company;
            return companies.FirstOrDefault();
        }


        protected virtual async Task<IList<CompanyCustomer>> GetCompanyCustomersByCustomerIdAsync(int customerId, int storeId,
           bool showHidden = false)
        {
            if (customerId == 0)
                return new List<CompanyCustomer>();

            return await _companyCustomerRepository.GetAllAsync(async query =>
            {
                var customer = await _workContext.GetCurrentCustomerAsync();
                return query
                    .Where(pc => pc.CustomerId == customerId)
                    .OrderBy(pc => pc.Id);

            });
        }
        #endregion

        #endregion
    }
}