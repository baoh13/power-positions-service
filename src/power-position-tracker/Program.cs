using Microsoft.Extensions.Hosting;
using power_position_tracker;

const string APPNAME = "power-position-tracker";

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(APPNAME)
    .RegisterServices()
    .Build();

Console.WriteLine("Hello, World!");

host.Run();