using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExpirationDateNotifier.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace ExpirationDateNotifier
{
    public class ResetFunction
    {
        [FunctionName("Reset")]
        public async Task Run(
            [HttpTrigger] HttpRequest req,
            ILogger log,
            [DurableClient] IDurableEntityClient entityClient)
        {
            var entities = await entityClient.ListEntitiesAsync(new EntityQuery
            {
                EntityName = nameof(SubjectEntity),
                FetchState = true
            }, CancellationToken.None);
            var knownExpiringSubjects = entities.Entities.Where(e => e.State != null).ToList();

            await Task.WhenAll(knownExpiringSubjects.Select(async subject =>
            {
                await entityClient.SignalEntityAsync<ISubjectEntity>(subject.EntityId, subjectEntity =>
                {
                    subjectEntity.Delete();
                });
            }));
        }
    }
}