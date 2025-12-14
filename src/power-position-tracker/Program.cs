using Microsoft.Extensions.Hosting;
using power_position_tracker;

var host = Host.CreateDefaultBuilder(args)
    .RegisterServices()
    .Build();

host.Run();