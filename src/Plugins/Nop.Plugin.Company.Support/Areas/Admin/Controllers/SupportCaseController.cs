using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Company.Support.Areas.Admin.Factories;
using Nop.Plugin.Company.Support.Areas.Admin.Models;
using Nop.Plugin.Company.Support.Domain;
using Nop.Plugin.Company.Support.Security;
using Nop.Plugin.Company.Support.Services;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Controllers;

namespace Nop.Plugin.Company.Support.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SupportCaseController : BaseAdminController
    {
        private readonly ISupportCaseModelFactory _supportCaseModelFactory;
        private readonly ISupportCaseService _supportCaseService;
        private readonly IPermissionService _permissionService;
        private readonly INotificationService _notificationService;
        private readonly IWorkContext _workContext;

        public SupportCaseController(
            ISupportCaseModelFactory supportCaseModelFactory,
            ISupportCaseService supportCaseService,
            IPermissionService permissionService,
            INotificationService notificationService,
            IWorkContext workContext)
        {
            _supportCaseModelFactory = supportCaseModelFactory;
            _supportCaseService = supportCaseService;
            _permissionService = permissionService;
            _notificationService = notificationService;
            _workContext = workContext;
        }

        public virtual IActionResult Index()
        {
            return RedirectToAction("List");
        }

        public virtual async Task<IActionResult> List()
        {
            if (!await _permissionService.AuthorizeAsync(SupportPermissionProvider.ManageSupportCases))
                return AccessDeniedView();

            var model = await _supportCaseModelFactory.PrepareSupportCaseSearchModelAsync(new SupportCaseSearchModel());

            return View("~/Plugins/Company.Support/Areas/Admin/Views/SupportCase/List.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> List(SupportCaseSearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(SupportPermissionProvider.ManageSupportCases))
                return await AccessDeniedDataTablesJson();

            var model = await _supportCaseModelFactory.PrepareSupportCaseListModelAsync(searchModel);

            return Json(model);
        }

        public virtual async Task<IActionResult> Edit(int id)
        {
            if (!await _permissionService.AuthorizeAsync(SupportPermissionProvider.ManageSupportCases))
                return AccessDeniedView();

            var supportCase = await _supportCaseService.GetSupportCaseByIdAsync(id);
            if (supportCase == null)
                return RedirectToAction("List");

            var model = await _supportCaseModelFactory.PrepareSupportCaseModelAsync(new SupportCaseModel(), supportCase);

            return View("~/Plugins/Company.Support/Areas/Admin/Views/SupportCase/Edit.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> Edit(SupportCaseModel model)
        {
            if (!await _permissionService.AuthorizeAsync(SupportPermissionProvider.ManageSupportCases))
                return AccessDeniedView();

            var supportCase = await _supportCaseService.GetSupportCaseByIdAsync(model.Id);
            if (supportCase == null)
                return RedirectToAction("List");

            if (ModelState.IsValid)
            {
                var customer = await _workContext.GetCurrentCustomerAsync();
                await _supportCaseService.ChangeStatusAsync(supportCase.Id, (SupportCaseStatus)model.StatusId, customer.Id);

                _notificationService.SuccessNotification("Status updated successfully.");

                return RedirectToAction("Edit", new { id = supportCase.Id });
            }

            model = await _supportCaseModelFactory.PrepareSupportCaseModelAsync(model, supportCase);
            return View("~/Plugins/Company.Support/Areas/Admin/Views/SupportCase/Edit.cshtml", model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> AddMessage(int id, string newMessageBody)
        {
            if (!await _permissionService.AuthorizeAsync(SupportPermissionProvider.ManageSupportCases))
                return AccessDeniedView();

            var supportCase = await _supportCaseService.GetSupportCaseByIdAsync(id);
            if (supportCase == null)
                return RedirectToAction("List");

            if (!string.IsNullOrWhiteSpace(newMessageBody))
            {
                var customer = await _workContext.GetCurrentCustomerAsync();
                await _supportCaseService.AddMessageAsync(supportCase.Id, customer.Id, isStaff: true, newMessageBody.Trim());
            }

            return RedirectToAction("Edit", new { id = supportCase.Id });
        }

        [HttpPost]
        public virtual async Task<IActionResult> AssignToMe(int id)
        {
            if (!await _permissionService.AuthorizeAsync(SupportPermissionProvider.ManageSupportCases))
                return AccessDeniedView();

            var customer = await _workContext.GetCurrentCustomerAsync();
            var assigned = await _supportCaseService.AssignToCustomerAsync(id, customer.Id);

            if (assigned)
                _notificationService.SuccessNotification("This case has been assigned to you.");
            else
                _notificationService.ErrorNotification("This case is already assigned.");

            return RedirectToAction("Edit", new { id });
        }
    }
}
