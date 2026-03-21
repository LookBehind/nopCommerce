using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Tax;
using Nop.Core.Domain.Vendors;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Events;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Vendors;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Notifications.Manager.EventConsumer
{
    public class MessageTemplateOrderTokenProvider :
        IConsumer<EntityTokensAddedEvent<Order, Vendor, Token>>,
        IConsumer<AdditionalTokensAddedEvent>
    {
        private readonly IOrderService _orderService;
        private readonly IVendorService _vendorService;
        private readonly IProductService _productService;
        private readonly ILocalizationService _localizationService;
        private readonly LocalizationSettings _localizationSettings;
        private readonly ICustomerService _customerService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly IProductAttributeFormatter _attributeFormatter;

        private readonly string[] _allowedTokens =
        {
            "%Order.ProductsHumanReadable%",
            "%Order.ScheduleDateUtc%",
            "%Order.ScheduleTime%"
        };

        public MessageTemplateOrderTokenProvider(
            IOrderService orderService,
            IVendorService vendorService,
            IProductService productService,
            ILocalizationService localizationService,
            LocalizationSettings localizationSettings,
            ICustomerService customerService,
            IDateTimeHelper dateTimeHelper,
            IProductAttributeFormatter attributeFormatter)
        {
            _orderService = orderService;
            _vendorService = vendorService;
            _productService = productService;
            _localizationService = localizationService;
            _localizationSettings = localizationSettings;
            _customerService = customerService;
            _dateTimeHelper = dateTimeHelper;
            _attributeFormatter = attributeFormatter;
        }

        public async Task HandleEventAsync(EntityTokensAddedEvent<Order, Vendor, Token> eventMessage)
        {
            eventMessage.Tokens.Add(new Token("Order.ProductsHumanReadable",
                await ProductOrdersHumanReadableAsync(eventMessage.Entity, eventMessage.AttachedParam), true));
            
            await AddScheduleDateForVendorAsync(eventMessage);
        }

        private async Task AddScheduleDateForVendorAsync(EntityTokensAddedEvent<Order, Vendor, Token> eventMessage)
        {
            var vendor = eventMessage.AttachedParam;
            var scheduleDateUtc = eventMessage.Entity.ScheduleDate;
            
            var vendorCustomer = (await _customerService.GetAllCustomersAsync(vendorId: vendor.Id))
                .Single();
            var vendorTimezone = await _dateTimeHelper.GetCustomerTimeZoneAsync(
                vendorCustomer);

            var formattedScheduleTime = _dateTimeHelper.ConvertToUserTime(scheduleDateUtc, 
                    TimeZoneInfo.Utc,
                    vendorTimezone)
                .ToString("t", DateTimeFormatInfo.InvariantInfo);

            eventMessage.Tokens.Add(new Token("Order.ScheduleDateUtc",
                scheduleDateUtc.ToString(DateTimeFormatInfo.InvariantInfo), true));
            eventMessage.Tokens.Add(new Token("Order.ScheduleTime", formattedScheduleTime, true));
        }

        public Task HandleEventAsync(AdditionalTokensAddedEvent eventMessage)
        {
            eventMessage.AddTokens(_allowedTokens);
            return Task.CompletedTask;
        }

        private async Task<string> ProductOrdersHumanReadableAsync(Order order, Vendor vendor)
        {
            var languageId = _localizationSettings.DefaultAdminLanguageId;
            var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
            var productIdsArray = orderItems.GroupBy(o => o.ProductId)
                .Select(o => o.Key).ToArray();
            var products = (await _productService.GetProductsByIdsAsync(productIdsArray)).AsEnumerable();

            if (vendor != null)
            {
                products = products.Where(product => product.VendorId == vendor.Id);
            }

            var orderItemProduct = products.Join(orderItems,
                productKey => productKey.Id,
                orderItemKey => orderItemKey.ProductId,
                (product, orderItem) => new Tuple<OrderItem, Product>(
                     orderItem, product
                ));

            var vendorCustomer = (await _customerService.GetAllCustomersAsync(vendorId: vendor.Id))
                .SingleOrDefault();

            var sb = new StringBuilder();
            foreach (var (orderItem, product) in orderItemProduct)
            {
                var productName = await _localizationService.GetLocalizedAsync(product,
                    x => x.Name,
                    languageId);

                sb.AppendLine($"{await _localizationService.GetResourceAsync("Messages.Order.Product(s).Name", languageId)}: {productName}");

                //attributes
                if (!string.IsNullOrEmpty(orderItem.AttributesXml))
                {
                    sb.AppendLine(await _attributeFormatter.FormatAttributesAsync(product, orderItem.AttributesXml,
                        vendorCustomer, "\n", false, false, true,
                        false, false));
                }

                //SKU
                var sku = await _productService.FormatSkuAsync(product, orderItem.AttributesXml);
                if (!string.IsNullOrEmpty(sku))
                {
                    sb.AppendLine(string.Format(
                        await _localizationService.GetResourceAsync("Messages.Order.Product(s).SKU", languageId),
                        sku));
                }

                sb.AppendLine($"{await _localizationService.GetResourceAsync("Messages.Order.Product(s).Quantity", languageId)}: {orderItem.Quantity}");
            }

            sb.AppendLine($"{order.CheckoutAttributeDescription ?? ""}");
            
            return sb.ToString();
        }
    }
}