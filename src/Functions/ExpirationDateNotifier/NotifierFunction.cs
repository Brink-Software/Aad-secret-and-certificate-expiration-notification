using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpirationDateNotifier.Configuration;
using ExpirationDateNotifier.Entities;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace ExpirationDateNotifier
{
    public class NotifierFunction
    {
        private readonly IGraphApiReader _graphApiReader;
        private readonly IOptions<GraphServiceCredentials> _graphServiceConfiguration;
        private readonly IOptions<EventGridConfiguration> _eventGridConfiguration;

        public NotifierFunction(IGraphApiReader graphApiReader, IOptions<GraphServiceCredentials> graphServiceConfiguration, IOptions<EventGridConfiguration> eventGridConfiguration)
        {
            _graphApiReader = graphApiReader;
            _graphServiceConfiguration = graphServiceConfiguration;
            _eventGridConfiguration = eventGridConfiguration;
        }

        [FunctionName("ExpirationDateNotifier")]
        public async Task Run(
            [TimerTrigger("%TimerSchedule%")]TimerInfo myTimer,
            [EventGrid(TopicEndpointUri = "EventGridTopicUriSetting", TopicKeySetting = "EventGridTopicKeySetting")] IAsyncCollector<EventGridEvent> outputEvents,
            ILogger log,
            [DurableClient] IDurableEntityClient entityClient)
        {
            // Get expiring secrets and certificates (subjects) from Azure AD
            var expiringSubjectsInAad = await _graphApiReader.ReadExpiringSubjectsAsync();

            // Get known soon-to-be-expired registrations
            var entities = await entityClient.ListEntitiesAsync(new EntityQuery
            {
                EntityName = nameof(SubjectEntity),
                FetchState = true
            }, CancellationToken.None);
            var knownExpiringSubjects = entities.Entities.Where(e => e.State != null).ToList();

            await RemoveKnownRegistrationsNoLongerInAad(log, entityClient, knownExpiringSubjects, expiringSubjectsInAad);

            var knownExpiringSubjectIds = knownExpiringSubjects.Select(entity => entity.EntityId.EntityKey);
            foreach (var subject in expiringSubjectsInAad.Where(subjectInAad =>
                 !knownExpiringSubjectIds.Contains(subjectInAad.Id)))
            {
                await entityClient.SignalEntityAsync<ISubjectEntity>(subject.Id, subjectEntity =>
                {
                    subjectEntity.CreateOrUpdate(subject);
                    log.LogInformation($"{subject} {subject.Id} done");
                });

                await outputEvents.AddAsync(subject.ODataType == "microsoft.graph.passwordCredential" ? 
                    CreateExpiringSecretEvent(subject) : 
                    CreateExpiringCertificateEvent(subject));
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
            return new EventGridEvent
            {
                Id = Guid.NewGuid().ToString(),
                DataVersion = "1.0",
                EventTime = DateTime.UtcNow,
                EventType = _eventGridConfiguration.Value.ExpiringSecretEventType,
                Subject = $"{_graphServiceConfiguration.Value.TenantId}/{secret.AppRegistration.AppId}",
                // Workaround for event grid not being able to serialize anonymous objects: https://github.com/Azure/azure-sdk-for-net/issues/4199
                Data = JObject.FromObject(new
                {
                    secret.AppRegistration,
                    secret.StartDateTime,
                    secret.EndDateTime,
                    secret.DaysLeft,
                    Description = secret.DisplayName,
                    ValueHint = secret.Context
                })
            };
        }

        private EventGridEvent CreateExpiringCertificateEvent(Subject certificate)
        {
            return new EventGridEvent
            {
                Id = Guid.NewGuid().ToString(),
                DataVersion = "1.0",
                EventTime = DateTime.UtcNow,
                EventType = _eventGridConfiguration.Value.ExpiringCertificateEventType,
                Subject = $"{_graphServiceConfiguration.Value.TenantId}/{certificate.AppRegistration.AppId}",
                // Workaround for event grid not being able to serialize anonymous objects: https://github.com/Azure/azure-sdk-for-net/issues/4199
                Data = JObject.FromObject(new
                {
                    certificate.AppRegistration,
                    certificate.DaysLeft,
                    certificate.DisplayName,
                    certificate.StartDateTime,
                    certificate.EndDateTime,
                    Thumbprint = certificate.Context
                })
            };
        }
    }
}