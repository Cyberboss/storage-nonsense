using System.IO.Abstractions;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

namespace StorageNonsense
{
	sealed class Program
	{
		public static Task Main(string[] args)
		{
			HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
			var serviceCollection = builder.Services;

			serviceCollection
				.AddWindowsService(
					options => options.ServiceName = "storage-nonsense")
				.AddSingleton<IFileSystem, FileSystem>()
				.AddHostedService<CleanupService>();

			LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(serviceCollection);

			var host = builder.Build();
			return host.RunAsync();
		}
	}
}
