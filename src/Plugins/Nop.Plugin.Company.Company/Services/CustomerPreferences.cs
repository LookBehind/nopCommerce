using System.Collections.Generic;

namespace Nop.Plugin.Company.Company.Services
{
    /// <summary>
    /// A customer's allergy/undesired-ingredient/avoided-vendor preferences.
    /// Allergy and undesired values are free-text against the mock's 9-item pickable
    /// ingredient taxonomy (Milk, Eggs, Fish, Shellfish, Tree Nuts, Peanuts, Wheat, Soybeans,
    /// Sesame) - stored as plain strings, not validated against an enum, since no
    /// specification-attribute-backed ingredient schema exists yet (see the mobile-v2 plan's
    /// open ambiguity #2 on the Gluten/Gluten-Free taxonomy gap - not resolved here).
    /// </summary>
    public class CustomerPreferences
    {
        public IList<string> Allergies { get; set; } = new List<string>();
        public IList<string> Undesired { get; set; } = new List<string>();
        public IList<int> AvoidedVendorIds { get; set; } = new List<int>();
    }
}
