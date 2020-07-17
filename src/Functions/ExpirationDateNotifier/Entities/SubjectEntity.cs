using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace ExpirationDateNotifier.Entities
{
    public interface ISubjectEntity
    {
        void CreateOrUpdate(Subject subject);
        void Delete();
    }

    public class SubjectEntity : ISubjectEntity
    {
        public Subject Subject { get; set; }

        public void CreateOrUpdate(Subject subject)
        {
            Subject = subject;
        }

        public Task<Subject> Get()
        {
            return Task.FromResult(Subject);
        }

        public void Delete()
        {
            Entity.Current.DeleteState();
        }

        [FunctionName(nameof(SubjectEntity))]
        public static Task Run([EntityTrigger] IDurableEntityContext ctx)
            => ctx.DispatchAsync<SubjectEntity>();
    }

    [DebuggerDisplay("{DisplayName ?? \"Unknown\"} of app {AppRegistration.DisplayName} expires in {DaysLeft} days.")]
    public class Subject
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public DateTimeOffset? StartDateTime { get; set; }
        public DateTimeOffset? EndDateTime { get; set; }
        public string ODataType { get; set; }
        public string Context { get; set; }
        public AppRegistration AppRegistration { get; set; }
        public double DaysLeft => Math.Round((EndDateTime - DateTime.Now).GetValueOrDefault().TotalDays, 0, MidpointRounding.ToEven);
    }

    public class AppRegistration
    {
        public string DisplayName { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
        public string AppId { get; set; }
    }
}