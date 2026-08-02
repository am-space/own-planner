using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace OwnPlanner.E2E.Tests.Infrastructure;

internal sealed class E2eStaticFilesStartupFilter(string frontendDistributionPath) : IStartupFilter
{
	public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
	{
		if (!Directory.Exists(frontendDistributionPath))
		{
			throw new DirectoryNotFoundException(
				$"The built frontend was not found at '{frontendDistributionPath}'. Run the frontend build before E2E tests.");
		}

		var fileProvider = new PhysicalFileProvider(frontendDistributionPath);
		return application =>
		{
			application.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
			application.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
			next(application);
		};
	}
}
