using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Events;
using Nop.Services.Localization;
using Nop.Services.Logging;

namespace Nop.Services.Catalog
{
    public class ProductChangeLogger : IProductChangeLogger
    {
        private readonly ICustomerActivityService _customerActivityService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;

        public ProductChangeLogger(ICustomerActivityService customerActivityService,
            IEventPublisher eventPublisher,
            ILocalizationService localizationService,
            IWorkContext workContext)
        {
            _customerActivityService = customerActivityService;
            _eventPublisher = eventPublisher;
            _localizationService = localizationService;
            _workContext = workContext;
        }

        public async Task LogEditAsync(Product product, IReadOnlyDictionary<string, string> before, string source)
        {
            var comment = string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditProduct"), product.Name);
            if (source != "admin")
                comment += $" via {source}";

            var changes = ProductChangeTracker.Describe(before, product);
            if (!string.IsNullOrEmpty(changes))
                comment += $" | {changes}";

            await _customerActivityService.InsertActivityAsync("EditProduct", comment, product);

            if (string.IsNullOrEmpty(changes))
                return;

            var customer = await _workContext.GetCurrentCustomerAsync();
            await _eventPublisher.PublishAsync(new ProductChangedEvent(product, changes, source, customer?.Id, customer?.Email));
        }
    }
}
