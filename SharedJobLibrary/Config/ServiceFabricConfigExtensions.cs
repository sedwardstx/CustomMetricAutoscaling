using Microsoft.Extensions.Configuration;

namespace SharedJobLibrary.Config
{
    public static class ServiceFabricConfigExtensions
    {
        public static IConfigurationBuilder AddServiceFabricConfig(this IConfigurationBuilder builder, string packageName)
        {
            return builder.Add(new ServiceFabricConfigSource(packageName));
        }
    }
}
