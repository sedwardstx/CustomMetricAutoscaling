using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace SharedJobLibrary.Extensions
{
	public static class HttpClientExtensions
	{
		public static async Task<T> GetAsync<T>(this HttpClient httpClient, string requestUri)
		{
			return await GetAsync<T>(httpClient, new Uri(requestUri));
		}

		public static async Task<T> GetAsync<T>(this HttpClient httpClient, Uri requestUri)
		{
			var response = await httpClient.GetAsync(requestUri);
			var responseContent = await response.Content.ReadAsStringAsync();

			return response == null ? default(T) : JsonConvert.DeserializeObject<T>(responseContent);
		}
	}
}
