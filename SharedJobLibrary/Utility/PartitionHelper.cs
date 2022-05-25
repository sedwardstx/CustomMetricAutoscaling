using System;
using System.Text;

namespace SharedJobLibrary.Utility
{
    public class PartitionHelper
	{
		public static Int64 GetInt64PartitionKey(string key)
		{
			Int64 hashCode = 0;
			if (!string.IsNullOrEmpty(key))
			{
				//Unicode Encode Covering all characterset
				byte[] byteContents = Encoding.Unicode.GetBytes(key);
				System.Security.Cryptography.SHA256 hash =
					new System.Security.Cryptography.SHA256CryptoServiceProvider();
				byte[] hashText = hash.ComputeHash(byteContents);
				//32Byte hashText separate
				//hashCodeStart = 0~7  8Byte
				//hashCodeMedium = 8~23  8Byte
				//hashCodeEnd = 24~31  8Byte
				//and Fold
				Int64 hashCodeStart = BitConverter.ToInt64(hashText, 0);
				Int64 hashCodeMedium = BitConverter.ToInt64(hashText, 8);
				Int64 hashCodeEnd = BitConverter.ToInt64(hashText, 24);
				hashCode = hashCodeStart ^ hashCodeMedium ^ hashCodeEnd;
			}
			return (hashCode);
		}

		//public static Task<string> GetPartitionUrl(long partitionKey, out ServicePartitionInformation info)
		//{
		//	CancellationTokenSource src = new CancellationTokenSource();

		//	var resolver = ServicePartitionResolver.GetDefault();

		//	var partKey = new ServicePartitionKey(partitionKey);

		//	var partition = resolver.ResolveAsync(new Uri
		//		("fabric:/Vending/CustomerManager"), partKey, src.Token).Result;

		//	var pEndpoint = partition.GetEndpoint();

		//	var primaryEndpoint = partition.Endpoints.FirstOrDefault(p => p.Role ==
		//		System.Fabric.ServiceEndpointRole.StatefulPrimary);

		//	info = partition.Info;
		//	if (primaryEndpoint != null)
		//	{
		//		JObject addresses = JObject.Parse(primaryEndpoint.Address);

		//		var p = addresses["Endpoints"].First();

		//		string primaryReplicaAddress = p.First().Value<string>();

		//		return Task.FromResult(primaryReplicaAddress);
		//	}
		//	else
		//		return Task.FromResult(":(");
		//}
	}
}
