using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Customers;

namespace Nop.Plugin.Company.AiChat.Services
{
    public class AiChatProductCandidate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal PriceValue { get; set; }
        public string Price { get; set; }
        public string ImageUrl { get; set; }
        public string VendorName { get; set; }
        public string CategoryName { get; set; }
        public IList<string> IngredientLabels { get; set; } = new List<string>();
    }

    public partial interface IAiChatCatalogService
    {
        /// <summary>
        /// Real, store-scoped product search backing the assistant's "search_products" tool
        /// call. Never returns a product that carries one of the customer's own saved allergy
        /// labels (Company.Company's CustomerPreferencesService) - the model doesn't need to
        /// filter allergens itself, whatever comes back is already safe to suggest.
        /// </summary>
        Task<IList<AiChatProductCandidate>> SearchAsync(Customer customer, int storeId, string query, int maxResults = 6);

        /// <summary>
        /// The customer's saved allergy labels (Company.Company's CustomerPreferencesService),
        /// for informational use in the assistant's system prompt.
        /// </summary>
        Task<IList<string>> GetCustomerAllergiesAsync(Customer customer, int storeId);
    }
}
