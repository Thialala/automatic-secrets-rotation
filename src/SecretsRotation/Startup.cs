[assembly: FunctionsStartup(typeof(SecretsRotation.Startup))]
namespace SecretsRotation;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
    }
}
