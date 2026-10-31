using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.SchemaRegistry.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>Registers exact schema client factories without activating a registry.</summary>
    public static class SchemaRegistryClientServiceCollectionExtensions
    {
        /// <summary>Adds the idempotent Schema Registry client factory. Register telemetry with AddOpcUa.</summary>
        public static IServiceCollection AddSchemaRegistryClient(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton(static provider =>
                new SchemaRegistryClientFactory(provider.GetRequiredService<Opc.Ua.ITelemetryContext>()));
            return services;
        }
    }
}
