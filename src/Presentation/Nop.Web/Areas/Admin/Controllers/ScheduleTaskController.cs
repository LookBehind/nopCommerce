using System;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Tasks;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Areas.Admin.Models.Tasks;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Infrastructure;
using Task = Nop.Services.Tasks.Task;

namespace Nop.Web.Areas.Admin.Controllers
{
    public partial class ScheduleTaskController : BaseAdminController
    {
        #region Fields

        private readonly ICustomerActivityService _customerActivityService;
        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        private readonly IScheduleTaskModelFactory _scheduleTaskModelFactory;
        private readonly IScheduleTaskService _scheduleTaskService;

        #endregion

        #region Ctor

        public ScheduleTaskController(ICustomerActivityService customerActivityService,
            ILocalizationService localizationService,
            INotificationService notificationService,
            IPermissionService permissionService,
            IScheduleTaskModelFactory scheduleTaskModelFactory,
            IScheduleTaskService scheduleTaskService)
        {
            _customerActivityService = customerActivityService;
            _localizationService = localizationService;
            _notificationService = notificationService;
            _permissionService = permissionService;
            _scheduleTaskModelFactory = scheduleTaskModelFactory;
            _scheduleTaskService = scheduleTaskService;
        }

        #endregion

        #region Methods

        public virtual IActionResult Index()
        {
            return RedirectToAction("List");
        }

        public virtual async Task<IActionResult> List()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageScheduleTasks))
                return AccessDeniedView();

            //prepare model
            var model = await _scheduleTaskModelFactory.PrepareScheduleTaskSearchModelAsync(new ScheduleTaskSearchModel());

            return View(model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> List(ScheduleTaskSearchModel searchModel)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageScheduleTasks))
                return await AccessDeniedDataTablesJson();

            //prepare model
            var model = await _scheduleTaskModelFactory.PrepareScheduleTaskListModelAsync(searchModel);

            return Json(model);
        }

        [HttpPost]
        public virtual async Task<IActionResult> TaskUpdate(ScheduleTaskModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageScheduleTasks))
                return AccessDeniedView();

            //try to get a schedule task with the specified id
            var scheduleTask = await _scheduleTaskService.GetTaskByIdAsync(model.Id)
                               ?? throw new ArgumentException("Schedule task cannot be loaded");

            //To prevent inject the XSS payload in Schedule tasks ('Name' field), we must disable editing this field, 
            //but since it is required, we need to get its value before updating the entity.
            if (!string.IsNullOrEmpty(scheduleTask.Name))
            {
                model.Name = scheduleTask.Name;
                ModelState.Remove(nameof(model.Name));
            }

            if (!ModelState.IsValid)
                return ErrorJson(ModelState.SerializeErrors());            

            scheduleTask = model.ToEntity(scheduleTask);

            //reconcile the Hangfire recurring job so CRON changes take effect at runtime (no restart needed on
            //the Hangfire side). Registering also validates the CRON string - Hangfire throws on a bad expression.
            var recurringJobManager = HttpContext.RequestServices.GetService<IRecurringJobManager>();
            if (recurringJobManager != null)
            {
                var taskType = scheduleTask.Type;
                try
                {
                    if (scheduleTask.Enabled && !string.IsNullOrWhiteSpace(scheduleTask.CronExpression))
                        recurringJobManager.AddOrUpdate<HangfireScheduleTaskRunner>(taskType,
                            runner => runner.RunScheduleTaskAsync(taskType), scheduleTask.CronExpression);
                    else
                        recurringJobManager.RemoveIfExists(taskType);
                }
                catch (Exception ex)
                {
                    //most likely an invalid CRON expression - report it and do not persist the change
                    return ErrorJson(string.Format(
                        await _localizationService.GetResourceAsync("Admin.System.ScheduleTasks.CronExpression.Invalid"),
                        ex.Message));
                }
            }

            await _scheduleTaskService.UpdateTaskAsync(scheduleTask);

            //activity log
            await _customerActivityService.InsertActivityAsync("EditTask",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditTask"), scheduleTask.Id), scheduleTask);

            return new NullJsonResult();
        }

        public virtual async Task<IActionResult> RunNow(int id)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageScheduleTasks))
                return AccessDeniedView();

            try
            {
                //try to get a schedule task with the specified id
                var scheduleTask = await _scheduleTaskService.GetTaskByIdAsync(id)
                                   ?? throw new ArgumentException("Schedule task cannot be loaded", nameof(id));

                //ensure that the task is enabled
                var task = new Task(scheduleTask) { Enabled = true };
                await task.ExecuteAsync(true, false);

                _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.System.ScheduleTasks.RunNow.Done"));
            }
            catch (Exception exc)
            {
                await _notificationService.ErrorNotificationAsync(exc);
            }

            return RedirectToAction("List");
        }

        #endregion
    }
}