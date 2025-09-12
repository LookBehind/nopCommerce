using Nop.Web.Areas.Admin.Models.Common;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Company.Company.Areas.Admin.Models
{
    /// <summary>
    /// Represents a company address model
    /// </summary>
    public partial record CompanyAddressModel : BaseNopEntityModel
    {
        #region Ctor

        public CompanyAddressModel()
        {
            Address = new AddressModel();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the company identifier
        /// </summary>
        public int CompanyId { get; set; }

        /// <summary>
        /// Gets or sets the address identifier
        /// </summary>
        public int AddressId { get; set; }

        /// <summary>
        /// Gets or sets the address
        /// </summary>
        public AddressModel Address { get; set; }

        #endregion
    }
}
