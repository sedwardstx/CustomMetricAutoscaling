using System.Collections.Generic;
using System.Fabric;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
using System;

namespace JobGateway
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class JobGateway : StatelessService
	{
		private readonly FabricClient _fabricClient;
		private readonly TelemetryConfiguration _configuration;
		private readonly TelemetryClient _telemetryClient;

		public JobGateway(StatelessServiceContext context)
			: base(context)
		{
			var clientSettings = new FabricClientSettings()
			{
				HealthOperationTimeout = TimeSpan.FromSeconds(120),
				HealthReportSendInterval = TimeSpan.FromSeconds(0),
				HealthReportRetrySendInterval = TimeSpan.FromSeconds(40),
			};
			_fabricClient = new FabricClient(clientSettings);

			// configuration
			var settings = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("config").Settings;
			var instrumentationKey = settings.Sections["GatewaySettings"].Parameters["InstrumentationKey"].Value;
			var apiKey = settings.Sections["GatewaySettings"].Parameters["LiveTelemetryApiKey"].Value;

			// setup AI Telemetry and Live Metrics
			_configuration = TelemetryConfiguration.CreateDefault();
			_configuration.InstrumentationKey = instrumentationKey;
			QuickPulseTelemetryProcessor quickPulseProcessor = null;
			_configuration.DefaultTelemetrySink.TelemetryProcessorChainBuilder
				.Use((next) =>
				{
					quickPulseProcessor = new QuickPulseTelemetryProcessor(next);
					return quickPulseProcessor;
				})
				.Build();

			var quickPulseModule = new QuickPulseTelemetryModule
			{
				// Secure the control channel.
				AuthenticationApiKey = apiKey
			};
			quickPulseModule.Initialize(_configuration);
			quickPulseModule.RegisterTelemetryProcessor(quickPulseProcessor);

			_telemetryClient = new TelemetryClient(_configuration);
		}

		/// <summary>
		/// Optional override to create listeners (like tcp, http) for this service instance.
		/// </summary>
		/// <returns>The collection of listeners.</returns>
		protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
		{
			return new ServiceInstanceListener[]
			{
				new ServiceInstanceListener(serviceContext =>
					new KestrelCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
					{
						ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        // Get AI configuration
						var gatewaySettings = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings.Sections["GatewaySettings"];
						var appInsightsConnectionString = gatewaySettings.Parameters["ApplicationInsightsConnectionString"].Value;

						return new WebHostBuilder()
									.UseKestrel()
									.ConfigureServices(
										services => services
											.AddSingleton<StatelessServiceContext>(serviceContext)
											.AddSingleton<FabricClient>(_fabricClient)
											.AddSingleton<TelemetryClient>(_telemetryClient)
											.AddApplicationInsightsTelemetry(appInsightsConnectionString)
											.AddHttpClient()
									)
									.UseContentRoot(Directory.GetCurrentDirectory())
									.UseStartup<Startup>()
									.UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
									.UseUrls(url)
									.Build();
					}))				
			};
		}

		//protected override async Task RunAsync(CancellationToken cancellationToken)
		//{
		//	bool flagNodes = false;
		//	while (true)
		//	{
		//		// Make sure Job services are started
		//		var serviceList = await _fabricClient.QueryManager.GetServiceListAsync(new Uri(this.Context.CodePackageActivationContext.ApplicationName));
				
		//		if (flagNodes && !this.Context.NodeContext.NodeName.Equals("_sys_4"))
		//		{
		//			var healthInformation = new HealthInformation("JobGateWaySourceWatcher", "IsTheSpecialNode", HealthState.Error);
		//			var replicaHealthReport = new StatelessServiceInstanceHealthReport(this.Partition.PartitionInfo.Id, this.Context.InstanceId, healthInformation);
					
		//			_fabricClient.HealthManager.ReportHealth(replicaHealthReport);
		//		}

		//		await Task.Delay(5000, cancellationToken);
		//	}
		//}
	}
}
