using Microsoft.ServiceFabric.Services.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharedJobLibrary.Utility
{
	public static class SharedServiceResolver
	{
		public static async Task<string> ResolveParitionedServiceEndpoint(string applicationName, string serviceName, long partitionKey, CancellationToken cancellationToken)
		{
			try
			{
				ServicePartitionResolver resolver = ServicePartitionResolver.GetDefault();
				var serviceUri = new Uri($"{applicationName}/{serviceName}");
				ResolvedServicePartition partition =
					await resolver.ResolveAsync(serviceUri, new ServicePartitionKey(partitionKey), cancellationToken);

				var resolvedEndpoints = partition.Endpoints;
				var primary = resolvedEndpoints.FirstOrDefault();

				// returns a nested keyvalue pair, so need to cajole the address out of it
				var jObj = JObject.Parse(primary.Address);
				var addresses = jObj.Cast<KeyValuePair<string, JToken>>().FirstOrDefault();
				var addr1 = addresses.Value.FirstOrDefault();
				return addr1.First.Value<string>();
			}
			catch (Exception)
			{
				throw;
			}
		}

		public static async Task<string> ResolveSingletonServiceEndpoint(string applicationName, string serviceName, CancellationToken cancellationToken)
		{
			try
			{
				ServicePartitionResolver resolver = ServicePartitionResolver.GetDefault();
				var serviceUri = new Uri($"{applicationName}/{serviceName}");
				ResolvedServicePartition partition =
					await resolver.ResolveAsync(serviceUri, null, cancellationToken);

				var resolvedEndpoints = partition.Endpoints;
				var primary = resolvedEndpoints.FirstOrDefault();

				// returns a nested keyvalue pair, so need to cajole the address out of it
				var jObj = JObject.Parse(primary.Address);
				var addresses = jObj.Cast<KeyValuePair<string, JToken>>().FirstOrDefault();
				var addr1 = addresses.Value.FirstOrDefault();
				return addr1.First.Value<string>();
			}
			catch (Exception)
			{
				throw;
			}
		}
	}
}
