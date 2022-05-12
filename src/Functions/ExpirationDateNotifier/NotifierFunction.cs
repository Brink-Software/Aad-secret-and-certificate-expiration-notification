using Azure.Messaging.EventGrid;
using ExpirationDateNotifier.Configuration;
using ExpirationDateNotifier.Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ExpirationDateNotifier
{
    public class NotifierFunction
    {
        private readonly IGraphApiReader _graphApiReader;
        private readonly IOptions<GraphServiceCredentials> _graphServiceConfiguration;
        private readonly IOptions<EventGridConfiguration> _eventGridConfiguration;
        private readonly IOptions<NotificationConfiguration> _notificationConfiguration;

        public NotifierFunction(IGraphApiReader graphApiReader,
            IOptions<GraphServiceCredentials> graphServiceConfiguration,
            IOptions<EventGridConfiguration> eventGridConfiguration,
            IOptions<NotificationConfiguration> notificationConfiguration)
        {
            _graphApiReader = graphApiReader;
            _graphServiceConfiguration = graphServiceConfiguration;
            _eventGridConfiguration = eventGridConfiguration;
            _notificationConfiguration = notificationConfiguration;
        }

        [FunctionName("ExpirationDateNotifier")]
        public async Task Run(
            [TimerTrigger("%TimerSchedule%")] TimerInfo myTimer,
            [EventGrid(TopicEndpointUri = "EventGridTopicUriSetting", TopicKeySetting = "EventGridTopicKeySetting")] IAsyncCollector<EventGridEvent> outputEvents,
            ILogger log,
            [DurableClient] IDurableEntityClient entityClient)
        {
            // Get expiring secrets and certificates (subjects) from Azure AD
            var expiringSubjectsInAad = await _graphApiReader.ReadExpiringSubjectsAsync();
            log.LogInformation("Found {Count} expiring subjects.", expiringSubjectsInAad.Count);

            // Get known soon-to-be-expired registrations
            var entities = await entityClient.ListEntitiesAsync(new EntityQuery
            {
                EntityName = nameof(SubjectEntity),
                FetchState = true
            }, CancellationToken.None);
            var knownExpiringSubjects = entities.Entities.Where(e => e.State != null).ToList();
            log.LogInformation("Found {Count} known subjects.", knownExpiringSubjects.Count);

            await RemoveKnownRegistrationsNoLongerInAad(log, entityClient, knownExpiringSubjects, expiringSubjectsInAad);

            var knownExpiringSubjectIds = knownExpiringSubjects.Select(entity => entity.EntityId.EntityKey);
            foreach (var subject in expiringSubjectsInAad.Where(subjectInAad =>
                 !knownExpiringSubjectIds.Contains(subjectInAad.Id)
                 || subjectInAad.DaysLeft % _notificationConfiguration.Value.ExpirationThresholdInDays == 0))
            {
                await outputEvents.AddAsync(subject.ODataType == "microsoft.graph.passwordCredential" ?
                    CreateExpiringSecretEvent(subject) :
                    CreateExpiringCertificateEvent(subject));

                await entityClient.SignalEntityAsync<ISubjectEntity>(subject.Id, subjectEntity =>
                {
                    subjectEntity.CreateOrUpdate(subject);
                    log.LogInformation(LogEvent.ExpiringSubjectDeleted,
                        "Notified subject of type {ODataType} with id {Id} named {SubjectDisplayName} for app with id {AppId} named {AppDisplayName}.",
                        subject.ODataType, subject.Id, subject.DisplayName, subject.AppRegistration.AppId,
                        subject.AppRegistration.DisplayName);
                });
            }
        }

        private static async Task RemoveKnownRegistrationsNoLongerInAad(ILogger log, IDurableEntityClient entityClient, IEnumerable<DurableEntityStatus> knownExpiringSubjects,
            IEnumerable<Subject> expiringSubjectsInAad)
        {
            var expiringSubjectIdsInAad = expiringSubjectsInAad.Select(subject => subject.Id);

            var registrationsNoLongerInAad = knownExpiringSubjects
                .Where(entity => !expiringSubjectIdsInAad.Contains(entity.EntityId.EntityKey));

            foreach (var registration in registrationsNoLongerInAad)
            {
                var entity = await entityClient.ReadEntityStateAsync<SubjectEntity>(registration.EntityId);

                await entityClient.SignalEntityAsync<ISubjectEntity>(registration.EntityId, appRegistrationEntity =>
                {
                    appRegistrationEntity.Delete();

                    var subject = entity.EntityState.Subject;
                    log.LogInformation(LogEvent.ExpiringSubjectDeleted,
                        "Deleted subject of type {ODataType} with id {Id} named {SubjectDisplayName} for app with id {AppId} named {AppDisplayName}.",
                        subject.ODataType, subject.Id, subject.DisplayName, subject.AppRegistration.AppId,
                        subject.AppRegistration.DisplayName);
                });
            }
        }

        private EventGridEvent CreateExpiringSecretEvent(Subject secret)
        {
            return new EventGridEvent(
                    $"{_graphServiceConfiguration.Value.TenantId}/{secret.AppRegistration.AppId}",
                    _eventGridConfiguration.Value.ExpiringSecretEventType,
                    "1.0",
                    new
                    {
                        secret.AppRegistration,
                        secret.StartDateTime,
                        secret.EndDateTime,
                        secret.DaysLeft,
                        Description = secret.DisplayName,
                        ValueHint = secret.Context
                    })
            {
                Id = Guid.NewGuid().ToString(),
                EventTime = DateTime.UtcNow,
            };
        }

        private EventGridEvent CreateExpiringCertificateEvent(Subject certificate)
        {
            return new EventGridEvent(
                    $"{_graphServiceConfiguration.Value.TenantId}/{certificate.AppRegistration.AppId}",
                    _eventGridConfiguration.Value.ExpiringCertificateEventType,
                    "1.0",
                    new
                    {
                        certificate.AppRegistration,
                        certificate.DaysLeft,
                        certificate.DisplayName,
                        certificate.StartDateTime,
                        certificate.EndDateTime,
                        Thumbprint = certificate.Context
                    }
                )
            {
                Id = Guid.NewGuid().ToString(),
                EventTime = DateTime.UtcNow,
            };
        }
    }
}