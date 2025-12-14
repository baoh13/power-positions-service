using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services;

namespace power_position_tracker
{
    public static class ServiceExtensions
    {
        public static IHostBuilder RegisterServices(this IHostBuilder hostBuilder)
        {
            return hostBuilder.ConfigureServices((ctx, svc) => svc.RegisterServices(ctx));
        }

        public static IServiceCollection RegisterServices(this IServiceCollection svc, HostBuilderContext ctx)
        {
            svc.Configure<PowerPositionSettings>(ctx.Configuration.GetSection("PowerPositionSettings"));
            
            // Register external PowerService dependency
            svc.AddSingleton<IPowerService, PowerService>();
            
            // Register application services
            svc.AddSingleton<Services.Interfaces.ILocalTimeProvider, Services.LocalTimeProvider>();
            svc.AddSingleton<Services.Interfaces.IPowerTradeProvider, Services.PowerTradeProvider>();
            svc.AddSingleton<Services.Interfaces.IPositionAggregator, Services.PositionAggregator>();
            svc.AddSingleton<Services.Interfaces.IReportWriter, Services.ReportWriter>();
            svc.AddSingleton<Services.Interfaces.IExecutionAuditLogger, Services.ExecutionAuditLogger>();
            svc.AddSingleton<Services.Interfaces.IDeadLetterQueue, Services.DeadLetterQueue>();

            svc.AddHostedService<Services.PowerPositionWorker>();

            return svc;
        }
    }
}
