using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Companies;
using Nop.Core.Domain.Discounts;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Companies;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.ExportImport;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Nop.Plugin.Company.Company.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Areas.Admin.Models.Catalog;
using Nop.Plugin.Company.Company.Areas.Admin.Models;
using Nop.Web.Areas.Admin.Models.Customers;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Areas.Admin.Controllers;
using Nop.Web.Areas.Admin.Factories;
using Nop.Plugin.Company.Company.Services;


namespace Nop.Plugin.Company.Company.Areas.Admin.Controllers
{
    [Area("Admin")]
    public partial class CompanyController : BaseAdminController
    {
        #region Fields

        private readonly IAclService _aclService;
        private readonly Nop.Plugin.Company.Company.Areas.Admin.Factories.ICompanyModelFactory _companyModelFactory;
        private readonly ICompanyService _companyService;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly ICustomerService _customerService;
        private readonly IDiscountService _discountService;
        private readonly IExportManager _exportManager;
        private readonly IImportManager _importManager;
        private readonly ILocalizationService _localizationService;
        private readonly ILocalizedEntityService _localizedEntityService;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        private readonly IPictureService _pictureService;
        private readonly IProductService _productService;
        private readonly IStaticCacheManager _staticCacheManager;
        private readonly IStoreMappingService _storeMappingService;
        private readonly IStoreService _storeService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IWorkContext _workContext;
        private readonly ICustomerModelFactory _customerModelFactory;
        private readonly IAddressAttributeParser _addressAttributeParser;
        private readonly IAddressService _addressService;
        private readonly IVendorService _vendorService;
        private readonly ICompanyAddressService _companyAddressService;

        #endregion

        #region Ctor

        public CompanyController(IAclService aclService,
            Nop.Plugin.Company.Company.Areas.Admin.Factories.ICompanyModelFactory companyModelFactory,
            ICompanyService companyService,
            ICustomerActivityService customerActivityService,
            ICustomerService customerService,
            IDiscountService discountService,
            IExportManager exportManager,
            IImportManager importManager,
            ILocalizationService localizationService,
            ILocalizedEntityService localizedEntityService,
            INotificationService notificationService,
            IPermissionService permissionService,
            IPictureService pictureService,
            IProductService productService,
            IStaticCacheManager staticCacheManager,
            IStoreMappingService storeMappingService,
            IStoreService storeService,
            IUrlRecordService urlRecordService,
            IWorkContext workContext,
            ICustomerModelFactory customerModelFactory,
            IAddressAttributeParser addressAttributeParser,
            IAddressService addressService,
            IVendorService vendorService,
            ICompanyAddressService companyAddressService)
        {
            _aclService = aclService;
            _companyModelFactory = companyModelFactory;
            _companyService = companyService;
            _customerActivityService = customerActivityService;
            _customerService = customerService;
            _discountService = discountService;
            _exportManager = exportManager;
            _importManager = importManager;
            _localizationService = localizationService;
            _localizedEntityService = localizedEntityService;
            _notificationService = notificationService;
            _permissionService = permissionService;
            _pictureService = pictureService;
            _productService = productService;
            _staticCacheManager = staticCacheManager;
            _storeMappingService = storeMappingService;
            _storeService = storeService;
            _urlRecordService = urlRecordService;
            _workContext = workContext;
            _customerModelFactory = customerModelFactory;
            _addressAttributeParser = addressAttributeParser;
            _addressService = addressService;
            _vendorService = vendorService;
            _companyAddressService = companyAddressService;
        }

        #endregion

        #region Utilities

        protected virtual async Task UpdateLocalesAsync(Core.Domain.Companies.Company company, CompanyModel model)
        {
            foreach (var localized in model.Locales)
            {
                await _localizedEntityService.SaveLocalizedValueAsync(company,
                    x => x.Name,
                    localized.Name,
                    localized.LanguageId);
            }
        }

        #endregion

        #region List

        public virtual IActionResult Index()
        {
            return RedirectToAction("List");
        }

        public virtual async Task<IActionResult> List()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //prepare model
            var model = await _companyModelFactory.PrepareCompanySearchModelAsync(new CompanySearchModel());

            return View("~/Plugins/Company.Company/Views/List.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> List(CompanySearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return await AccessDeniedDataTablesJson();

            //prepare model
            var model = await _companyModelFactory.PrepareCompanyListModelAsync(searchModel);

            return Json(model);
        }

        #endregion

        #region Create / Edit / Delete

        public virtual async Task<IActionResult> Create()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //prepare model
            var model = await _companyModelFactory.PrepareCompanyModelAsync(new CompanyModel(), null);

            return View("~/Plugins/Company.Company/Views/Create.cshtml", model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        public virtual async Task<IActionResult> Create(CompanyModel model, bool continueEditing)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            if (ModelState.IsValid)
            {
                var company = model.ToEntity<Core.Domain.Companies.Company>();
                await _companyService.InsertCompanyAsync(company);

                //locales
                await UpdateLocalesAsync(company, model);

                await _companyService.UpdateCompanyAsync(company);

                await _staticCacheManager.ClearAsync();

                //activity log
                await _customerActivityService.InsertActivityAsync("AddNewCompany",
                    string.Format(await _localizationService.GetResourceAsync("ActivityLog.AddNewCompany"), company.Name), company);

                _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Company.Companies.Added"));

                if (!continueEditing)
                    return RedirectToAction("List");

                return RedirectToAction("Edit", new { id = company.Id });
            }

            //prepare model
            model = await _companyModelFactory.PrepareCompanyModelAsync(model, null, true);

            //if we got this far, something failed, redisplay form
            return View("~/Plugins/Company.Company/Views/Create.cshtml", model);
        }

        public virtual async Task<IActionResult> Edit(int id)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //try to get a Company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(id);
            if (company == null)
                return RedirectToAction("List");

            //prepare model
            var model = await _companyModelFactory.PrepareCompanyModelAsync(null, company);

            return View("~/Plugins/Company.Company/Views/Edit.cshtml", model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        public virtual async Task<IActionResult> Edit(CompanyModel model, bool continueEditing)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //try to get a Company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(model.Id);
            if (company == null)
                return RedirectToAction("List");

            if (ModelState.IsValid)
            {
                company = model.ToEntity(company);
                await _companyService.UpdateCompanyAsync(company);

                await _staticCacheManager.ClearAsync();

                //locales
                await UpdateLocalesAsync(company, model);

                //activity log
                await _customerActivityService.InsertActivityAsync("EditCompany",
                    string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditCompany"), company.Name), company);

                _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Company.Companies.Updated"));

                if (!continueEditing)
                    return RedirectToAction("List");

                return RedirectToAction("Edit", new { id = company.Id });
            }

            //prepare model
            model = await _companyModelFactory.PrepareCompanyModelAsync(model, company, true);

            //if we got this far, something failed, redisplay form
            return View("~/Plugins/Company.Company/Views/Edit.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> Delete(int id)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //try to get a Company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(id);
            if (company == null)
                return RedirectToAction("List");

            var companyCustomers = await _companyService.GetCompanyCustomersByCompanyIdAsync(id);
            foreach (var companyCustomer in companyCustomers)
            {
                var customer = await _customerService.GetCustomerByIdAsync(companyCustomer.CustomerId);
                var addresses = await _customerService.GetAddressesByCustomerIdAsync(companyCustomer.CustomerId);
                foreach (var address in addresses)
                {
                    await _customerService.RemoveCustomerAddressAsync(customer, address);
                    customer.ShippingAddressId = null;
                    customer.BillingAddressId = null;
                    await _customerService.UpdateCustomerAsync(customer);
                    //now delete the address record
                    await _addressService.DeleteAddressAsync(address);
                    await _companyService.DeleteCompanyCustomerAsync(companyCustomer);
                }
            }
            await _companyService.DeleteCompanyAsync(company);

            await _staticCacheManager.ClearAsync();

            //activity log
            await _customerActivityService.InsertActivityAsync("DeleteCompany",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.DeleteCompany"), company.Name), company);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Company.Companies.Deleted"));

            return RedirectToAction("List");
        }

        #endregion

        #region Customers

        [HttpPost]
        public virtual async Task<IActionResult> CustomerList(CompanyCustomerSearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return await AccessDeniedDataTablesJson();

            //try to get a Company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(searchModel.CompanyId)
                ?? throw new ArgumentException("No Company found with the specified id");

            //prepare model
            var model = await _companyModelFactory.PrepareCompanyCustomerListModelAsync(searchModel, company);

            return Json(model);
        }

        public virtual async Task<IActionResult> CustomerUpdate(CompanyCustomerModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //try to get a product Company with the specified id
            var companyCustomer = await _companyService.GetCompanyCustomersByIdAsync(model.Id)
                ?? throw new ArgumentException("No product Company mapping found with the specified id");

            //fill entity from product
            companyCustomer = model.ToEntity(companyCustomer);
            await _companyService.UpdateCompanyCustomerAsync(companyCustomer);

            return new NullJsonResult();
        }

        public virtual async Task<IActionResult> CustomerDelete(int id)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //try to get a product Company with the specified id
            var companyCustomer = await _companyService.GetCompanyCustomersByIdAsync(id)
                ?? throw new ArgumentException("No Customer Company mapping found with the specified id", nameof(id));

            var customer = await _customerService.GetCustomerByIdAsync(companyCustomer.CustomerId);
            var addresses = await _customerService.GetAddressesByCustomerIdAsync(companyCustomer.CustomerId);
            foreach (var address in addresses)
            {
                await _customerService.RemoveCustomerAddressAsync(customer, address);
                customer.ShippingAddressId = null;
                customer.BillingAddressId = null;
                await _customerService.UpdateCustomerAsync(customer);
            }

            await _companyService.DeleteCompanyCustomerAsync(companyCustomer);

            return new NullJsonResult();
        }

        public virtual async Task<IActionResult> CustomerAddPopup(int companyId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            var searchModel = new AddCustomerToCompanySearchModel();
            searchModel.CompanyId = companyId;
            //prepare model
            var model = await _companyModelFactory.PrepareAddCustomerToCompanySearchModelAsync(searchModel);

            return View("~/Plugins/Company.Company/Views/CustomerAddPopup.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> CustomerAddPopupList(AddCustomerToCompanySearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return await AccessDeniedDataTablesJson();

            //prepare model
            var model = await _companyModelFactory.PrepareAddCustomerToCompanyListModelAsync(searchModel);

            return Json(model);
        }

        [HttpPost]
        [FormValueRequired("save")]
        public virtual async Task<IActionResult> CustomerAddPopup(AddCustomerToCompanyModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //get selected products
            var selectedCustomers = await _customerService.GetCustomersByIdsAsync(model.SelectedCustomerIds.ToArray());
            if (selectedCustomers.Any())
            {
                var existingCompanyCustomers = await _companyService.GetCompanyCustomersByCompanyIdAsync(model.CompanyId, showHidden: true);
                foreach (var customer in selectedCustomers)
                {
                    //whether product Company with such parameters already exists
                    if (_companyService.FindCompanyCustomer(existingCompanyCustomers, customer.Id, model.CompanyId) != null)
                        continue;

                    var addressId = 0;
                    //foreach (var existingCompanyCustomer in existingCompanyCustomers)
                    //{
                    if (existingCompanyCustomers.Any())
                    {
                        var addresses = await _customerService.GetAddressesByCustomerIdAsync(existingCompanyCustomers.FirstOrDefault().CustomerId);
                        var customerAddresses = await _customerService.GetCustomerAddressesByCustomerIdAsync(existingCompanyCustomers.FirstOrDefault().CustomerId);
                        foreach (var address in addresses)
                        {
                            if (_customerService.FindCustomerAddressMapping(customerAddresses, customer.Id, address.Id) != null)
                            {
                                addressId = address.Id;
                                continue;
                            }

                            await _customerService.InsertCustomerAddressAsync(customer, address);
                            addressId = address.Id;
                        }
                    }
                    //}
                    await _companyService.InsertCompanyCustomerAsync(new CompanyCustomer { CompanyId = model.CompanyId, CustomerId = customer.Id });
                    if (addressId > 0)
                    {
                        customer.ShippingAddressId = addressId;
                        customer.BillingAddressId = addressId;
                        await _customerService.UpdateCustomerAsync(customer);
                    }
                    else if (existingCompanyCustomers.Any())
                    {
                        var newAddressId = 0;
                        foreach (var existingCompanyCustomer in existingCompanyCustomers)
                        {
                            var addressMappings = await _customerService.GetCustomerAddressesByCustomerIdAsync(existingCompanyCustomer.CustomerId);
                            if (addressMappings.Any())
                                newAddressId = addressMappings.FirstOrDefault().AddressId;
                        }
                        if (newAddressId > 0)
                        {
                            customer.ShippingAddressId = newAddressId;
                            customer.BillingAddressId = newAddressId;
                            await _customerService.UpdateCustomerAsync(customer);
                        }
                    }
                }
            }

            ViewBag.RefreshPage = true;

            return View("~/Plugins/Company.Company/Views/CustomerAddPopup.cshtml", new AddCustomerToCompanySearchModel());
        }

        #endregion

        #region vendors

        [HttpPost]
        [FormValueRequired("save")]
        public virtual async Task<IActionResult> VendorAddPopup(AddVendorToCompanyModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //get selected products
            var selectedVendors = await _vendorService.GetVendorsByIdsAsync(model.SelectedVendorIds.ToArray());
            foreach (var selectedVendor in selectedVendors)
            {
                var existingCompanyVendor = (await _companyService.GetCompanyVendorsByCompanyIdAsync(model.CompanyId, showHidden: true))
                    .Where(v => v.VendorId == selectedVendor.Id).FirstOrDefault();
                if (existingCompanyVendor == null)
                {
                    await _companyService.InsertCompanyVendorAsync(new CompanyVendor { CompanyId = model.CompanyId, VendorId = selectedVendor.Id });
                }
            }
            ViewBag.RefreshPage = true;

            return View("~/Plugins/Company.Company/Views/VendorAddPopup.cshtml", new AddVendorToCompanySearchModel());
        }

        public virtual async Task<IActionResult> VendorAddPopup(int companyId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            var searchModel = new AddVendorToCompanySearchModel();
            searchModel.CompanyId = companyId;
            //prepare model
            var model = await _companyModelFactory.PrepareAddVendorToCompanySearchModelAsync(searchModel);

            return View("~/Plugins/Company.Company/Views/VendorAddPopup.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> VendorList(CompanyVendorSearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return await AccessDeniedDataTablesJson();

            //try to get a Company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(searchModel.CompanyId)
                ?? throw new ArgumentException("No Company found with the specified id");

            //prepare model PrepareCompanyVendorListModelAsync
            var model = await _companyModelFactory.PrepareCompanyVendorListModelAsync(searchModel, company);

            return Json(model);
        }

        public virtual async Task<IActionResult> VendorDelete(int id)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //try to get a product Company with the specified id
            var companyVendor = await _companyService.GetCompanyVendorByIdAsync(id)
                ?? throw new ArgumentException("No Customer Company mapping found with the specified id", nameof(id));

            await _companyService.DeleteCompanyVendorAsync(companyVendor);

            return new NullJsonResult();
        }

        public virtual async Task<IActionResult> VendorUpdate(CompanyVendorModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return AccessDeniedView();

            //try to get a product Company with the specified id
            var companyVendor = await _companyService.GetCompanyVendorByIdAsync(model.Id)
                ?? throw new ArgumentException("No product Company mapping found with the specified id");

            //fill entity from product
            companyVendor = model.ToEntity(companyVendor);
            await _companyService.UpdateCompanyVendorAsync(companyVendor);

            return new NullJsonResult();
        }

        [HttpPost]
        public virtual async Task<IActionResult> VendorAddPopupList(AddVendorToCompanySearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCategories))
                return await AccessDeniedDataTablesJson();

            //prepare model
            var model = await _companyModelFactory.PrepareAddVendorToCompanyListModelAsync(searchModel);

            return Json(model);
        }

        #endregion

        #region Addresses

        [HttpPost]
        public virtual async Task<IActionResult> AddressesSelect(CompanyAddressSearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return await AccessDeniedDataTablesJson();

            //try to get a company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(searchModel.CompanyId)
                ?? throw new ArgumentException("No company found with the specified id");

            //prepare model
            var model = await _companyModelFactory.PrepareCompanyAddressListModelAsync(searchModel, company);

            return Json(model);
        }

        public virtual async Task<IActionResult> AddressCreate(int companyId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return AccessDeniedView();

            //try to get a company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(companyId);
            if (company == null)
                return RedirectToAction("List");

            //prepare model
            var model = await _companyModelFactory.PrepareCompanyAddressModelAsync(new CompanyAddressModel(), company, null);

            return View("~/Plugins/Company.Company/Views/AddressCreate.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> AddressCreate(CompanyAddressModel model, IFormCollection form)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return AccessDeniedView();

            var companyId = model.CompanyId;
            //try to get a company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(model.CompanyId);
            if (company == null)
                return RedirectToAction("List");

            //custom address attributes
            var customAttributes = await _addressAttributeParser.ParseCustomAddressAttributesAsync(form);
            var customAttributeWarnings = await _addressAttributeParser.GetAttributeWarningsAsync(customAttributes);
            foreach (var error in customAttributeWarnings)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (ModelState.IsValid)
            {
                var address = model.Address.ToEntity<Address>();
                address.CustomAttributes = customAttributes;
                address.CreatedOnUtc = DateTime.UtcNow;

                //some validation
                if (address.CountryId == 0)
                    address.CountryId = null;
                if (address.StateProvinceId == 0)
                    address.StateProvinceId = null;

                await _addressService.InsertAddressAsync(address);
                
                //create company address mapping
                var companyAddress = new Domain.CompanyAddress
                {
                    CompanyId = companyId,
                    AddressId = address.Id
                };
                await _companyAddressService.InsertCompanyAddressAsync(companyAddress);
                
                _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Companies.Company.Addresses.Added"));
                return RedirectToAction("Edit", new { id = companyId });
            }

            //if we got this far, something failed, redisplay form
            return View("~/Plugins/Company.Company/Views/AddressCreate.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> AddressDelete(int id, int companyId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return AccessDeniedView();

            //try to get a company address with the specified id
            var companyAddress = await _companyAddressService.GetCompanyAddressByIdAsync(id);
            if (companyAddress == null || companyAddress.CompanyId != companyId)
                return new NullJsonResult();

            //get the address
            var address = await _addressService.GetAddressByIdAsync(companyAddress.AddressId);
            if (address == null)
                return new NullJsonResult();

            //delete company address mapping
            await _companyAddressService.DeleteCompanyAddressAsync(companyAddress);

            //delete the address record
            await _addressService.DeleteAddressAsync(address);

            return new NullJsonResult();
        }

        public virtual async Task<IActionResult> AddressEdit(int companyId, int addressId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return AccessDeniedView();

            //try to get a company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(companyId);
            if (company == null)
                return RedirectToAction("List");

            //try to get an address with the specified id
            var address = await _addressService.GetAddressByIdAsync(addressId);
            if (address == null)
                return RedirectToAction("Edit", new { id = companyId });

            //prepare model
            var model = await _companyModelFactory.PrepareCompanyAddressModelAsync(new CompanyAddressModel(), company, address);

            return View("~/Plugins/Company.Company/Views/AddressEdit.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> AddressEdit(CompanyAddressModel model, IFormCollection form)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return AccessDeniedView();

            var companyId = model.CompanyId;
            //try to get a company with the specified id
            var company = await _companyService.GetCompanyByIdAsync(model.CompanyId);
            if (company == null)
                return RedirectToAction("List");

            //try to get an address with the specified id
            var address = await _addressService.GetAddressByIdAsync(model.AddressId);
            if (address == null)
                return RedirectToAction("Edit", new { id = companyId });

            //custom address attributes
            var customAttributes = await _addressAttributeParser.ParseCustomAddressAttributesAsync(form);
            var customAttributeWarnings = await _addressAttributeParser.GetAttributeWarningsAsync(customAttributes);
            foreach (var error in customAttributeWarnings)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (ModelState.IsValid)
            {
                address = model.Address.ToEntity(address);
                address.CustomAttributes = customAttributes;

                //some validation
                if (address.CountryId == 0)
                    address.CountryId = null;
                if (address.StateProvinceId == 0)
                    address.StateProvinceId = null;

                await _addressService.UpdateAddressAsync(address);

                _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Companies.Company.Addresses.Updated"));
                return RedirectToAction("Edit", new { id = companyId });
            }

            //if we got this far, something failed, redisplay form
            model = await _companyModelFactory.PrepareCompanyAddressModelAsync(model, company, address, true);
            return View("~/Plugins/Company.Company/Views/AddressEdit.cshtml", model);
        }

        #endregion

        #region Address validation / fix

        /// <summary>
        /// Builds a content-based comparison key for an address so customer addresses can be matched
        /// against the company's canonical addresses regardless of their Id. Mirrors the fields copied
        /// by IAddressService.CloneAddress (the same fields used when company addresses are cloned to a
        /// customer at registration), so a correctly cloned address yields an identical key.
        /// </summary>
        private static string GetAddressKey(Nop.Core.Domain.Common.Address a)
        {
            string s(string v) => (v ?? string.Empty).Trim().ToLowerInvariant();
            return string.Join("|",
                s(a.FirstName), s(a.LastName), s(a.Email), s(a.Company),
                a.CountryId ?? 0, a.StateProvinceId ?? 0,
                s(a.County), s(a.City), s(a.Address1), s(a.Address2),
                s(a.ZipPostalCode), s(a.PhoneNumber), s(a.FaxNumber),
                s(a.CustomAttributes));
        }

        /// <summary>
        /// Human-readable one-line summary of an address for display in the validation results table.
        /// </summary>
        private static string GetAddressSummary(Nop.Core.Domain.Common.Address a)
        {
            var parts = new List<string>();
            var name = $"{a.FirstName} {a.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(name))
                parts.Add(name);
            if (!string.IsNullOrWhiteSpace(a.Address1))
                parts.Add(a.Address1);
            var cityZip = $"{a.City} {a.ZipPostalCode}".Trim();
            if (!string.IsNullOrWhiteSpace(cityZip))
                parts.Add(cityZip);
            if (!string.IsNullOrWhiteSpace(a.Email))
                parts.Add(a.Email);
            var summary = string.Join(", ", parts);
            return string.IsNullOrWhiteSpace(summary) ? $"(empty address #{a.Id})" : summary;
        }

        /// <summary>
        /// Builds the company's canonical set of addresses keyed by content (one entry per unique
        /// address). Used both to detect mismatches and as the source for cloning missing addresses
        /// back onto a customer.
        /// </summary>
        private async Task<Dictionary<string, Nop.Core.Domain.Common.Address>> GetCompanyCanonicalAddressesAsync(int companyId)
        {
            var companyAddresses = await _companyAddressService.GetCompanyAddressesByCompanyIdAsync(companyId);
            var canonical = new Dictionary<string, Nop.Core.Domain.Common.Address>();
            foreach (var ca in companyAddresses)
            {
                var addr = await _addressService.GetAddressByIdAsync(ca.AddressId);
                if (addr == null)
                    continue;
                var key = GetAddressKey(addr);
                if (!canonical.ContainsKey(key))
                    canonical[key] = addr;
            }

            return canonical;
        }

        /// <summary>
        /// Scans every company-associated customer and reports those whose address list contains one or
        /// more addresses that are NOT among the company's addresses (i.e. they don't have exactly the
        /// same set of addresses as the company). Read-only — makes no changes.
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> ValidateAddresses(int companyId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return Json(new { success = false, message = "Access denied." });

            var company = await _companyService.GetCompanyByIdAsync(companyId);
            if (company == null)
                return Json(new { success = false, message = "Company not found." });

            var canonical = await GetCompanyCanonicalAddressesAsync(company.Id);
            if (canonical.Count == 0)
                return Json(new { success = false, message = "This company has no addresses defined. Add at least one company address before validating — otherwise every customer address would be treated as invalid." });

            var companyCustomers = await _companyService.GetCompanyCustomersByCompanyIdAsync(company.Id, showHidden: true);
            var flagged = new List<object>();
            var totalCustomers = 0;

            foreach (var cc in companyCustomers)
            {
                var customer = await _customerService.GetCustomerByIdAsync(cc.CustomerId);
                if (customer == null)
                    continue;
                totalCustomers++;

                var addresses = await _customerService.GetAddressesByCustomerIdAsync(customer.Id);
                var presentKeys = new HashSet<string>(addresses.Select(GetAddressKey));

                var extras = addresses.Where(a => !canonical.ContainsKey(GetAddressKey(a))).ToList();
                var missing = canonical.Where(kv => !presentKeys.Contains(kv.Key)).Select(kv => kv.Value).ToList();

                if (extras.Any() || missing.Any())
                {
                    flagged.Add(new
                    {
                        customerId = customer.Id,
                        name = await _customerService.GetCustomerFullNameAsync(customer),
                        email = customer.Email,
                        extras = extras.Select(a => new { id = a.Id, summary = GetAddressSummary(a) }).ToList(),
                        missing = missing.Select(a => new { summary = GetAddressSummary(a) }).ToList()
                    });
                }
            }

            return Json(new
            {
                success = true,
                companyAddressCount = canonical.Count,
                totalCustomers,
                flaggedCount = flagged.Count,
                flagged
            });
        }

        /// <summary>
        /// Brings the supplied (already-validated and displayed) customers into exact agreement with the
        /// company's addresses: it unlinks every customer-address mapping whose address is NOT one of the
        /// company's addresses, and clones any missing company addresses onto the customer (the same way
        /// they are copied at registration). Both sets are recomputed server-side (the client list is not
        /// trusted). Billing/shipping pointers that ended up cleared are repointed to a company address.
        /// Extra address rows themselves are left intact — only their CustomerAddressMapping links are removed.
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> FixAddresses(int companyId, int[] customerIds)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageCustomers))
                return Json(new { success = false, message = "Access denied." });

            var company = await _companyService.GetCompanyByIdAsync(companyId);
            if (company == null)
                return Json(new { success = false, message = "Company not found." });

            if (customerIds == null || customerIds.Length == 0)
                return Json(new { success = false, message = "No customers were selected to fix." });

            var canonical = await GetCompanyCanonicalAddressesAsync(company.Id);
            if (canonical.Count == 0)
                return Json(new { success = false, message = "This company has no addresses defined. Nothing to validate against." });

            //only ever touch customers that actually belong to this company
            var validCustomerIds = (await _companyService.GetCompanyCustomersByCompanyIdAsync(company.Id, showHidden: true))
                .Select(cc => cc.CustomerId).ToHashSet();

            var fixedCustomers = 0;
            var removedMappings = 0;
            var addedAddresses = 0;

            foreach (var customerId in customerIds.Distinct())
            {
                if (!validCustomerIds.Contains(customerId))
                    continue;

                var customer = await _customerService.GetCustomerByIdAsync(customerId);
                if (customer == null)
                    continue;

                var addresses = await _customerService.GetAddressesByCustomerIdAsync(customer.Id);
                var presentKeys = new HashSet<string>(addresses.Select(GetAddressKey));

                var extras = addresses.Where(a => !canonical.ContainsKey(GetAddressKey(a))).ToList();
                var missing = canonical.Where(kv => !presentKeys.Contains(kv.Key)).Select(kv => kv.Value).ToList();

                if (!extras.Any() && !missing.Any())
                    continue;

                //remove extras (RemoveCustomerAddressAsync also nulls Billing/ShippingAddressId in memory if matched)
                foreach (var extra in extras)
                {
                    await _customerService.RemoveCustomerAddressAsync(customer, extra);
                    removedMappings++;
                }

                //backfill missing company addresses by cloning them onto the customer (same as registration)
                foreach (var companyAddress in missing)
                {
                    var clone = _addressService.CloneAddress(companyAddress);
                    clone.CreatedOnUtc = DateTime.UtcNow;
                    await _addressService.InsertAddressAsync(clone);
                    await _customerService.InsertCustomerAddressAsync(customer, clone);
                    addedAddresses++;
                }

                //ensure billing/shipping point at one of the company addresses now mapped to the customer
                var companyMapped = (await _customerService.GetAddressesByCustomerIdAsync(customer.Id))
                    .Where(a => canonical.ContainsKey(GetAddressKey(a))).ToList();
                var fallbackId = companyMapped.Select(a => (int?)a.Id).FirstOrDefault();
                if ((customer.BillingAddressId == null || customer.BillingAddressId == 0) && fallbackId.HasValue)
                    customer.BillingAddressId = fallbackId;
                if ((customer.ShippingAddressId == null || customer.ShippingAddressId == 0) && fallbackId.HasValue)
                    customer.ShippingAddressId = fallbackId;

                await _customerService.UpdateCustomerAsync(customer);
                fixedCustomers++;
            }

            return Json(new
            {
                success = true,
                message = $"Fixed {fixedCustomers} customer(s); removed {removedMappings} extra link(s), added {addedAddresses} missing address(es).",
                fixedCustomers,
                removedMappings,
                addedAddresses
            });
        }

        #endregion
    }
}
