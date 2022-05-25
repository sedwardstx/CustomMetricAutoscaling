using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Data;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using SharedJobLibrary.Models;
using Microsoft.ServiceFabric.Data.Collections;
using SharedJobLibrary.Constants;

namespace JobService
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    internal sealed class JobService : StatefulService
    {
        // AI Tracing
        private readonly TelemetryConfiguration _configuration;
        private readonly TelemetryClient _telemetryClient;
        private Dictionary<string, string> _traceProperties;

        // config settings
        private readonly string _listenerConnectionString;
        private readonly string _completedJobsQueueName;

        // clients
        private readonly ServiceBusReceiver _sbreceiver;
        private readonly FabricClient _fabricClient;

        //dictionaries
        private IReliableDictionary<Guid, JobRecord> _jobDictionary;

        public JobService(StatefulServiceContext context)
            : base(context)
        {
            // configuration
            var settings = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("config").Settings;
            var instrumentationKey = settings.Sections["JobServiceSettings"].Parameters["InstrumentationKey"].Value;
            _listenerConnectionString = settings.Sections["JobServiceSettings"].Parameters["ServiceBusConnectionString"].Value;
            _completedJobsQueueName = settings.Sections["JobServiceSettings"].Parameters["CompletedJobQueueName"].Value;

            // sb receiver
            var client = new ServiceBusClient(_listenerConnectionString);
            var serviceBusReceiverOptions = new ServiceBusReceiverOptions()
            {
                PrefetchCount = 1,
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.None
            };
            _sbreceiver = client.CreateReceiver(_completedJobsQueueName, serviceBusReceiverOptions);

            var clientSettings = new FabricClientSettings()
            {
                HealthOperationTimeout = TimeSpan.FromSeconds(120),
                HealthReportSendInterval = TimeSpan.FromSeconds(0),
                HealthReportRetrySendInterval = TimeSpan.FromSeconds(40),
            };
            _fabricClient = new FabricClient(clientSettings);

            // setup AI Telemetry and Live Metrics
            _configuration = TelemetryConfiguration.CreateDefault();
            _configuration.InstrumentationKey = instrumentationKey;
            _telemetryClient = new TelemetryClient(_configuration);
        }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new ServiceReplicaListener[]
            {
                new ServiceReplicaListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        // Get AI configuration
						var gatewaySettings = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings.Sections["JobServiceSettings"];
                        var appInsightsConnectionString = gatewaySettings.Parameters["ApplicationInsightsConnectionString"].Value;

                        return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatefulServiceContext>(serviceContext)
                                            .AddSingleton<IReliableStateManager>(this.StateManager)
                                            .AddSingleton<FabricClient>(_fabricClient)
                                            .AddSingleton<TelemetryClient>(_telemetryClient)
                                            .AddApplicationInsightsTelemetry(appInsightsConnectionString)
                                            .AddHttpClient()
                                            )
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                                    .UseUrls(url)
                                    .Build();
                    }))
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            _jobDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, JobRecord>>(JobConstants.JobDictionary);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var message = await _sbreceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(3));
                    if (message != null)
                    {
                        object jobProperty;
                        if (message.ApplicationProperties.TryGetValue("jobResult", out jobProperty))
                        {
                            var jobResult = JsonConvert.DeserializeObject<JobRecord>((string)jobProperty);

                            await UpdateJobAsync(jobResult, message, cancellationToken);
                            var dir = _traceProperties = new Dictionary<string, string>
                            {
                                {"traceType", "Job"},
                                {"JobId", jobResult.JobDetail.JobId.ToString() },
                                {"JobState", jobResult.JobState.ToString() },
                                {"JobType", jobResult.JobDetail.JobType.ToString() },
                                {"StartedOn", jobResult.StartedOn.ToString() },
                                {"CompletedOn", jobResult.CompletedOn.ToString() },
                                {"Result", jobResult.Result.ToString() }
                            };
                            _telemetryClient.TrackEvent("JobTrace", _traceProperties);

                        }
                        await _sbreceiver.CompleteMessageAsync(message, cancellationToken);
                    }
                }
                catch (ServiceBusException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (Exception ex)
                {
                    _telemetryClient.TrackException(ex);
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        private async Task UpdateJobAsync(JobRecord jobResult, ServiceBusReceivedMessage message, CancellationToken cancellationToken)
        {
            // update JobState and Correlated Result
            using (var tx = this.StateManager.CreateTransaction())
            {
                var jobRecord = await _jobDictionary.TryGetValueAsync(tx, jobResult.JobDetail.JobId);
                if (jobRecord.HasValue)
                {
                    var completedJob = (JobRecord)jobRecord.Value.Clone();
                    completedJob.JobState = jobResult.JobState;
                    completedJob.StartedOn = jobResult.StartedOn;
                    completedJob.CompletedOn = jobResult.CompletedOn;
                    completedJob.Result = jobResult.Result;

                    await _jobDictionary.AddOrUpdateAsync(tx, completedJob.JobDetail.JobId, completedJob, (key, newValue) => completedJob, TimeSpan.FromSeconds(5), cancellationToken);
                    await tx.CommitAsync();

                    // complete the SB message
                    await _sbreceiver.CompleteMessageAsync(message);

                    #region tracing
                    _traceProperties = new Dictionary<string, string>{
                        {"traceType", "JobFinalize"},
                        {"jobId", completedJob.JobDetail.JobId.ToString()},
                        {"totalTime", $"{completedJob.CompletedOn.Subtract(completedJob.CreatedOn).TotalSeconds}" },
                        {"waitTime", $"{completedJob.StartedOn.Subtract(completedJob.CreatedOn).TotalSeconds}" },
                        {"executionTime", $"{completedJob.CompletedOn.Subtract(completedJob.StartedOn).TotalSeconds}" },
                        {"jobType", $"{completedJob.JobDetail.JobType}" },
                        {"result", $"{completedJob.Result}" },
                        {"message",$"RunAsync JOB COMPLETED for JobId: {completedJob.JobDetail.JobId}"}
                    };
                    _telemetryClient.TrackEvent("JobTrace", _traceProperties);
                    #endregion
                }
            }
        }
    }
}
