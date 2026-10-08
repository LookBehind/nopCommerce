using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Plugin.Company.Support.Domain;
using Nop.Services.Messages;
using Nop.Services.Stores;

namespace Nop.Plugin.Company.Support.Services
{
    public class SupportCaseTokenProvider : ISupportCaseTokenProvider
    {
        private readonly IStoreService _storeService;
        private readonly IStoreContext _storeContext;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly IActionContextAccessor _actionContextAccessor;

        public SupportCaseTokenProvider(
            IStoreService storeService,
            IStoreContext storeContext,
            IUrlHelperFactory urlHelperFactory,
            IActionContextAccessor actionContextAccessor)
        {
            _storeService = storeService;
            _storeContext = storeContext;
            _urlHelperFactory = urlHelperFactory;
            _actionContextAccessor = actionContextAccessor;
        }

        public async Task AddSupportCaseTokensAsync(IList<Token> tokens, SupportCase supportCase)
        {
            tokens.Add(new Token("SupportCase.Id", supportCase.Id));
            tokens.Add(new Token("SupportCase.Category", SupportCaseDisplayNames.Category[supportCase.Category]));
            tokens.Add(new Token("SupportCase.Status", SupportCaseDisplayNames.Status[supportCase.Status]));
            tokens.Add(new Token("SupportCase.Description", supportCase.Description));

            var selfUrl = await GetAdminEditUrlAsync(supportCase);
            tokens.Add(new Token("SupportCase.SelfUrl", selfUrl, true));
        }

        /// <summary>
        /// Mirrors core's MessageTokenProvider.RouteUrlAsync (store-scoped, "areaRoute" named
        /// route) - that method is protected on a core class this plugin can't inherit from,
        /// so the same store-url + admin-path composition is replicated here.
        /// </summary>
        private async Task<string> GetAdminEditUrlAsync(SupportCase supportCase)
        {
            var store = await _storeService.GetStoreByIdAsync(supportCase.StoreId) ?? await _storeContext.GetCurrentStoreAsync()
                ?? throw new Exception("No store could be loaded");

            if (string.IsNullOrEmpty(store.Url))
                throw new Exception("URL cannot be null");

            var urlHelper = _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext);
            var url = new PathString(urlHelper.RouteUrl("areaRoute", new { area = "Admin", controller = "SupportCase", action = "Edit", id = supportCase.Id }));

            var pathBase = _actionContextAccessor.ActionContext?.HttpContext?.Request?.PathBase ?? PathString.Empty;
            url.StartsWithSegments(pathBase, out url);

            return Uri.EscapeUriString(WebUtility.UrlDecode($"{store.Url.TrimEnd('/')}{url}"));
        }
    }
}
