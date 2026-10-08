using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Core.Events;
using Nop.Plugin.Company.Support.Domain;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Stores;

namespace Nop.Plugin.Company.Support.Services
{
    public class SupportCaseNotificationService : ISupportCaseNotificationService
    {
        private readonly IMessageTemplateService _messageTemplateService;
        private readonly IMessageTokenProvider _messageTokenProvider;
        private readonly ISupportCaseTokenProvider _supportCaseTokenProvider;
        private readonly IWorkflowMessageService _workflowMessageService;
        private readonly ILocalizationService _localizationService;
        private readonly IEmailAccountService _emailAccountService;
        private readonly EmailAccountSettings _emailAccountSettings;
        private readonly IStoreService _storeService;
        private readonly IStoreContext _storeContext;
        private readonly IEventPublisher _eventPublisher;

        public SupportCaseNotificationService(
            IMessageTemplateService messageTemplateService,
            IMessageTokenProvider messageTokenProvider,
            ISupportCaseTokenProvider supportCaseTokenProvider,
            IWorkflowMessageService workflowMessageService,
            ILocalizationService localizationService,
            IEmailAccountService emailAccountService,
            EmailAccountSettings emailAccountSettings,
            IStoreService storeService,
            IStoreContext storeContext,
            IEventPublisher eventPublisher)
        {
            _messageTemplateService = messageTemplateService;
            _messageTokenProvider = messageTokenProvider;
            _supportCaseTokenProvider = supportCaseTokenProvider;
            _workflowMessageService = workflowMessageService;
            _localizationService = localizationService;
            _emailAccountService = emailAccountService;
            _emailAccountSettings = emailAccountSettings;
            _storeService = storeService;
            _storeContext = storeContext;
            _eventPublisher = eventPublisher;
        }

        public Task<IList<int>> SendNewSupportCaseStoreOwnerNotificationAsync(SupportCase supportCase, int languageId) =>
            SendStoreOwnerNotificationAsync(SupportMessageTemplateSystemNames.NewSupportCaseStoreOwnerNotification, supportCase, null, languageId);

        public Task<IList<int>> SendSupportCaseCustomerRepliedStoreOwnerNotificationAsync(SupportCase supportCase, string messageBody, int languageId) =>
            SendStoreOwnerNotificationAsync(SupportMessageTemplateSystemNames.SupportCaseCustomerRepliedStoreOwnerNotification, supportCase,
                new List<Token> { new Token("SupportCase.NewMessageText", messageBody) }, languageId);

        private async Task<IList<int>> SendStoreOwnerNotificationAsync(string messageTemplateName, SupportCase supportCase,
            IList<Token> extraTokens, int languageId)
        {
            var store = await _storeService.GetStoreByIdAsync(supportCase.StoreId) ?? await _storeContext.GetCurrentStoreAsync();

            var messageTemplates = (await _messageTemplateService.GetMessageTemplatesByNameAsync(messageTemplateName, store.Id))
                ?.Where(mt => mt.IsActive).ToList();

            if (messageTemplates == null || !messageTemplates.Any())
                return new List<int>();

            //tokens shared by every active template
            var commonTokens = new List<Token>();
            await _supportCaseTokenProvider.AddSupportCaseTokensAsync(commonTokens, supportCase);
            await _messageTokenProvider.AddCustomerTokensAsync(commonTokens, supportCase.CustomerId);
            if (extraTokens != null)
            {
                foreach (var extraToken in extraTokens)
                    commonTokens.Add(extraToken);
            }

            var result = new List<int>();
            foreach (var messageTemplate in messageTemplates)
            {
                //email account - mirrors WorkflowMessageService.GetEmailAccountOfMessageTemplateAsync
                var emailAccountId = await _localizationService.GetLocalizedAsync(messageTemplate, mt => mt.EmailAccountId, languageId);
                if (emailAccountId == 0)
                    emailAccountId = messageTemplate.EmailAccountId;

                var emailAccount = (await _emailAccountService.GetEmailAccountByIdAsync(emailAccountId) ??
                    await _emailAccountService.GetEmailAccountByIdAsync(_emailAccountSettings.DefaultEmailAccountId)) ??
                    (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();

                if (emailAccount == null)
                    continue;

                var tokens = new List<Token>(commonTokens);
                await _messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount);

                //event notification
                await _eventPublisher.MessageTokensAddedAsync(messageTemplate, tokens);

                var toEmail = emailAccount.Email;
                var toName = emailAccount.DisplayName;

                var queuedEmailId = await _workflowMessageService.SendNotificationAsync(messageTemplate, emailAccount, languageId, tokens,
                    toEmail, toName, storeId: store.Id);
                result.Add(queuedEmailId);
            }

            return result;
        }
    }
}
