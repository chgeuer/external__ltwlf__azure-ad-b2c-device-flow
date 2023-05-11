[assembly: Microsoft.Azure.WebJobs.Hosting.WebJobsStartup(typeof(Ltwlf.Azure.B2C.Startup))]

namespace Ltwlf.Azure.B2C;

using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        builder.Services.AddControllers().AddNewtonsoftJson(o => o.SerializerSettings.ConfigureSnakeCaseNamingAndIgnoringNullValues());
        builder.Services.AddSingleton<PageFactory>();
        builder.Services.AddHttpClient();
        builder.Services.Configure<HttpOptions>(o => o.RoutePrefix = string.Empty);

        builder.Services.AddOptions<ConfigOptions>()
            .Configure<IConfiguration>((settings, configuration) =>
            {
                configuration.GetSection("Config").Bind(settings);
            });

        var redisConfig = builder.GetContext().Configuration.GetValue<string>("Config:Redis");
        if (!string.IsNullOrEmpty(redisConfig))
        {
            builder.Services.AddSingleton<IStorageBackend>(new RedisStorageBackend(redisConfig));
        }
        else
        {
            builder.Services.AddSingleton<IStorageBackend>(new SingleInstanceInMemoryBackend());
        }
    }
}