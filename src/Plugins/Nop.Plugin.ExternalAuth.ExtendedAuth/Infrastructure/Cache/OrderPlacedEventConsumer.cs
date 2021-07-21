﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure;
using Nop.Services.Customers;
using Nop.Services.Events;
using Telegram.Bot;
using System.Security.Claims;
using Nop.Services.Authentication.External;
using System.Linq;
using Nop.Core.Configuration;
using Nop.Services.Orders;
using System.Threading.Tasks;

namespace Nop.Plugin.ExternalAuth.ExtendedAuth.Infrastructure.Cache
{
    struct VendorToChatMap
    {
        public int VendorId { get; set; }
        public int ChatGroupId { get; set; }
    }

    class OrderPlacedEventConsumer
        : IConsumer<OrderPlacedEvent>
    {
        private readonly Lazy<ITelegramBotClient> _telegramBotClient;
        private readonly ICustomerService _customerService;
        private readonly IExternalAuthenticationService _externalAuthenticationService;
        private readonly AppSettings _config;
        private readonly IOrderService _orderService;

        static private readonly List<VendorToChatMap> _vendorToChat = new List<VendorToChatMap>{
            new VendorToChatMap{ VendorId = 0, ChatGroupId = -580079767 },  // All Vendors
        };

        public OrderPlacedEventConsumer(Lazy<ITelegramBotClient> telegramBotClient,
            ICustomerService customerService, 
            IExternalAuthenticationService externalAuthenticationService,
            AppSettings config,
            IOrderService orderService)
        {
            this._telegramBotClient = telegramBotClient;
            this._customerService = customerService;
            this._externalAuthenticationService = externalAuthenticationService;
            this._config = config;
            this._orderService = orderService;
        }

        public async Task HandleEventAsync(OrderPlacedEvent eventMessage)
        {
            if (!_config.ExtendedAuthSettings.TelegramBotEnabled)
                return;

            var chatGroupsToNotify =
                await _vendorToChat.WhereAwait(async x => (x.VendorId == 0) || (await _orderService.GetOrderItemsAsync(eventMessage.Order.Id, vendorId: x.VendorId)).Any())
                    .Select(x => x.ChatGroupId)
                    .ToListAsync();

            if (!chatGroupsToNotify.Any())
                return;

            var customer = await _customerService.GetCustomerByIdAsync(eventMessage.Order.CustomerId);
            var externalRecord = (await _externalAuthenticationService.GetCustomerExternalAuthenticationRecordsAsync(customer)).FirstOrDefault();
            var externalDisplayIdentifier =
                externalRecord?.ExternalDisplayIdentifier ??
                await _customerService.GetCustomerFullNameAsync(customer);

            if(string.IsNullOrEmpty(externalDisplayIdentifier))
                externalDisplayIdentifier = "Unknown";

            foreach(var chatGroupId in chatGroupsToNotify) {
                var chat = await _telegramBotClient.Value.GetChatAsync(chatGroupId);
                await _telegramBotClient.Value.SendTextMessageAsync(chat, $"New order from {externalDisplayIdentifier}");
            }
        }
    }
}
