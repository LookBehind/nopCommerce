using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Catalog
{
    /// <summary>
    /// Represents one selectable option (e.g. "Milk", "Gluten-Free") of the real
    /// "Ingredients" specification attribute, for the product edit page's
    /// Ingredients tab (a plain checkbox list, not the generic multi-attribute
    /// picker under the Specification attributes tab - see
    /// ProductModelFactory.PrepareProductIngredientsModelAsync).
    /// </summary>
    public partial record ProductIngredientOptionModel : BaseNopModel
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public bool IsAllergen { get; set; }

        public bool Checked { get; set; }
    }
}
