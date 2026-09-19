using Nop.Web.Framework.Models;

namespace Nop.Web.Models.Api.Catalog
{
    /// <summary>
    /// Represents a product specification attribute value model
    /// </summary>
    public record ProductSpecificationAttributeValueApiModel : BaseNopModel
    {
        #region Properties

        /// <summary>
        /// Gets or sets the attribute type id
        /// </summary>
        public int AttributeTypeId { get; set; }

        public bool AllowFiltering { get; set; }

        /// <summary>
        /// Gets or sets the specification attribute option identifier (only set for
        /// <see cref="Nop.Core.Domain.Catalog.SpecificationAttributeType.Option"/> values) -
        /// lets mobile clients filter search by this exact option via
        /// SearchProductByFilters.SpecificationAttributeOptionId.
        /// </summary>
        public int SpecificationAttributeOptionId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this option is a common allergen
        /// (only meaningful for <see cref="Nop.Core.Domain.Catalog.SpecificationAttributeType.Option"/>
        /// values, e.g. an "Ingredients" spec attribute option).
        /// </summary>
        public bool IsAllergen { get; set; }

        /// <summary>
        /// Gets or sets the value raw. This value is already HTML encoded
        /// </summary>
        public string ValueRaw { get; set; }

        /// <summary>
        /// Gets or sets the option color (if specified). Used to display color squares
        /// </summary>
        public string ColorSquaresRgb { get; set; }

        #endregion
    }
}
