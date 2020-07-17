using ExpirationDateNotifier.Configuration;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(ExpirationDateNotifier.Startup))]

namespace ExpirationDateNotifier
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddOptions();

            builder.Services.AddOptions<NotificationConfiguration>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.GetSection(nameof(NotificationConfiguration)).Bind(settings);
                });

            builder.Services.AddOptions<GraphServiceCredentials>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.GetSection(nameof(GraphServiceCredentials)).Bind(settings);
                });

            builder.Services.AddSingleton<IGraphApiReader, GraphApiReader>();
        }
    }
}