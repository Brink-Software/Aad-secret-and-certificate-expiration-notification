using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExpirationDateNotifier.Configuration;
using ExpirationDateNotifier.Entities;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;

namespace ExpirationDateNotifier
{
    public interface IGraphApiReader
    {
        Task<List<Subject>> ReadExpiringSubjectsAsync();
    }

    public class GraphApiReader : IGraphApiReader
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly Func<PasswordCredential, bool> _expiringSecretsSelector;
        private readonly Func<KeyCredential, bool> _expiringCertificatesSelector;

        public GraphApiReader(
            IOptions<GraphServiceCredentials> graphServiceConfiguration,
            IOptions<NotificationConfiguration> notificationConfiguration)
        {
            if (graphServiceConfiguration == null) throw new ArgumentNullException(nameof(graphServiceConfiguration));
            if (notificationConfiguration == null) throw new ArgumentNullException(nameof(notificationConfiguration));

            var confidentialClientApplication = ConfidentialClientApplicationBuilder
                .Create(graphServiceConfiguration.Value.AppId)
                .WithTenantId(graphServiceConfiguration.Value.TenantId)
                .WithClientSecret(graphServiceConfiguration.Value.ClientSecret)
                .Build();
            var authProvider = new ClientCredentialProvider(confidentialClientApplication);
            _graphServiceClient = new GraphServiceClient(authProvider);

            _expiringSecretsSelector = new Func<PasswordCredential, bool>(c =>
                c.EndDateTime <= DateTime.Now.AddDays(notificationConfiguration.Value.ExpirationThresholdInDays));

            _expiringCertificatesSelector = new Func<KeyCredential, bool>(c =>
                c.EndDateTime <= DateTime.Now.AddDays(notificationConfiguration.Value.ExpirationThresholdInDays));
        }

        public async Task<List<Subject>> ReadExpiringSubjectsAsync()
        {
            var subjects = new List<Subject>();

            var initialApplicationsCollectionPage = await _graphServiceClient.Applications
                .Request()
                .Select(app => new
                {
                    app.KeyCredentials,
                    app.AppId,
                    app.CreatedDateTime,
                    app.DisplayName,
                    app.PasswordCredentials
                })
                .GetAsync();

            var iterator = PageIterator<Application>.CreatePageIterator(_graphServiceClient, initialApplicationsCollectionPage, application =>
            {
                if (!application.PasswordCredentials.Any(_expiringSecretsSelector) &&
                    !application.KeyCredentials.Any(_expiringCertificatesSelector))
                {
                    return true;
                }

                var appRegistration = new AppRegistration
                {
                    AppId = application.AppId,
                    DisplayName = application.DisplayName,
                    CreatedDateTime = application.CreatedDateTime
                };

                subjects.AddRange(application.PasswordCredentials.Where(_expiringSecretsSelector).Select(cred => new Subject
                {
                    DisplayName = cred.DisplayName,
                    Context = cred.Hint,
                    EndDateTime = cred.EndDateTime,
                    StartDateTime = cred.StartDateTime,
                    Id = cred.KeyId.GetValueOrDefault().ToString(),
                    ODataType = cred.ODataType,
                    AppRegistration = appRegistration
                }));

                subjects.AddRange(application.KeyCredentials.Where(_expiringCertificatesSelector).Select(key => new Subject
                {
                    DisplayName = key.DisplayName,
                    Context = Convert.ToBase64String(key.CustomKeyIdentifier),
                    EndDateTime = key.EndDateTime,
                    StartDateTime = key.StartDateTime,
                    Id = key.KeyId.GetValueOrDefault().ToString(),
                    ODataType = key.ODataType,
                    AppRegistration = appRegistration
                }));

                return true;
            });

            await iterator.IterateAsync();

            return subjects;
        }
    }
}
