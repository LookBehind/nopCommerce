using System.Threading.Tasks;
using Nop.Plugin.Company.Support.Areas.Admin.Models;
using Nop.Plugin.Company.Support.Domain;

namespace Nop.Plugin.Company.Support.Areas.Admin.Factories
{
    public partial interface ISupportCaseModelFactory
    {
        Task<SupportCaseSearchModel> PrepareSupportCaseSearchModelAsync(SupportCaseSearchModel searchModel);

        Task<SupportCaseListModel> PrepareSupportCaseListModelAsync(SupportCaseSearchModel searchModel);

        Task<SupportCaseModel> PrepareSupportCaseModelAsync(SupportCaseModel model, SupportCase supportCase);
    }
}
