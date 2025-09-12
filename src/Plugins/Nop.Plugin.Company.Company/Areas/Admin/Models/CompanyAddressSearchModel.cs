using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    /// <summary>
    /// Represents a company address search model
    /// </summary>
    public partial record CompanyAddressSearchModel : BaseSearchModel
    {
        #region Properties

        /// <summary>
        /// Gets or sets the company identifier
        /// </summary>
        public int CompanyId { get; set; }

        #endregion
    }
}
