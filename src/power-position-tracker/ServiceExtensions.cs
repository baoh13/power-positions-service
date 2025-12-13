using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
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

        // todo: implement logging
        public static IHostBuilder ConfigureLogging(this IHostBuilder builder, string appName, bool skipLogging = false)
        {
            const string consoleTemplate = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj} {Properties:j} {NewLine}{Exception}";

            return builder.ConfigureLogging((ctx, logging) =>
            {
                var env = ctx.HostingEnvironment.EnvironmentName;
                var serilog = new LoggerConfiguration();

                // serilog write to console
                //serilog.WriteTo.Console(outputTemplate: consoleTemplate).Enrich.FromLogContext().MinimumLevel.Information();

                //logging.AddSerilog(serilog.CreateLogger(), dispose: true);
            });
        }
    }
}
