using System;
using System.Collections.Generic;
using System.Fabric;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.Azure.ServiceBus;
using System.Text;
using Newtonsoft.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.ServiceBus.Management;
using System.Linq;
using System.Fabric.Query;
using System.Net.Http;
using SharedJobLibrary.Utility;
using SharedJobLibrary.Constants;
using SharedJobLibrary.Models;
using SharedJobLibrary.Services.TrendAnalysis;

namespace JobRunner
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class JobRunner : Microsoft.ServiceFabric.Services.Runtime.StatelessService
    {
        private readonly TelemetryConfiguration _configuration;
        private readonly TelemetryClient _telemetryClient;
        private Dictionary<string, string> _traceProperties;
        private readonly HttpClient _httpClient;
        private readonly RNGCryptoServiceProvider _rand = new RNGCryptoServiceProvider();
        private SimpleTrends _simpleTrends = new SimpleTrends();
        private string _listenerConnectionString;
        private string _senderConnectionString;
        private string _loadManagerSettingsConnectionString;
        private string _jobQueueName;
        private string _completedJobQueueName;
        private string _metricJobQueueHeight;

        private readonly FabricClient _fabricClient;
        private readonly IQueueClient _messageSender;
        private readonly ManagementClient _managementClient;
        private readonly ServiceBusReceiver _sbreceiver;
        //private readonly ActorProxyFactory _actorProxyFactory;

        private enum Pressure
        {
            Stable,
            Upward,
            Downward
        }

        public JobRunner(StatelessServiceContext context)
            : base(context)
        {
            // set up HttpClient instance
            _httpClient = new HttpClient
            {
                Timeout = new TimeSpan(0, 0, 30)
            };

            // Fabric Client
            _fabricClient = new FabricClient();

            // force reload of configuration/settings
            ICodePackageActivationContext activationContext = FabricRuntime.GetActivationContext();
            activationContext.ConfigurationPackageModifiedEvent += (sender, eventArgs) =>
            {
                this.UpdateConfigurationAsync(CancellationToken.None).Wait();
            };

            // configuration
            var settings = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("config").Settings;
            var instrumentationKey = settings.Sections["JobRunnerSettings"].Parameters["InstrumentationKey"].Value;
            //var apiKey = settings.Sections["JobRunnerSettings"].Parameters["LiveTelemetryApiKey"].Value;
            _listenerConnectionString = settings.Sections["JobRunnerSettings"].Parameters["JobRunnerListenConnectionString"].Value;
            _senderConnectionString = settings.Sections["JobRunnerSettings"].Parameters["JobRunnerSendConnectionString"].Value;
            _loadManagerSettingsConnectionString = settings.Sections["JobRunnerSettings"].Parameters["LoadManagerConnectionString"].Value;
            _jobQueueName = settings.Sections["JobRunnerSettings"].Parameters["JobQueueName"].Value;
            _completedJobQueueName = settings.Sections["JobRunnerSettings"].Parameters["CompletedJobQueueName"].Value;
            _metricJobQueueHeight = settings.Sections["JobRunnerSettings"].Parameters["MetricNewJobHeight"].Value;

            // clients
            _managementClient = new ManagementClient(_loadManagerSettingsConnectionString);
            _messageSender = new QueueClient(connectionString: _senderConnectionString, entityPath: _completedJobQueueName);
            var client = new ServiceBusClient(_listenerConnectionString);
            var serviceBusReceiverOptions = new ServiceBusReceiverOptions()
            {
                PrefetchCount = 1,
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.None
            };
            _sbreceiver = client.CreateReceiver(_jobQueueName, serviceBusReceiverOptions);

            // setup AI Telemetry and Live Metrics
            _configuration = TelemetryConfiguration.CreateDefault();
            _configuration.InstrumentationKey = instrumentationKey;
            _telemetryClient = new TelemetryClient(_configuration);
        }

        /// <summary>
        /// close SB clients
        /// </summary>
        protected override void OnAbort()
        {
            _sbreceiver.CloseAsync().RunSynchronously();
            _messageSender.CloseAsync().RunSynchronously();
            _managementClient.CloseAsync().RunSynchronously();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="none"></param>
        /// <returns></returns>
        private Task UpdateConfigurationAsync(CancellationToken none)
        {
            var settings = this.Context.CodePackageActivationContext.GetConfigurationPackageObject("config").Settings;
            _listenerConnectionString = settings.Sections["JobRunnerSettings"].Parameters["JobRunnerListenConnectionString"].Value;
            _senderConnectionString = settings.Sections["JobRunnerSettings"].Parameters["JobRunnerSendConnectionString"].Value;
            _loadManagerSettingsConnectionString = settings.Sections["JobRunnerSettings"].Parameters["LoadManagerConnectionString"].Value;
            _jobQueueName = settings.Sections["JobRunnerSettings"].Parameters["JobQueueName"].Value;
            _completedJobQueueName = settings.Sections["JobRunnerSettings"].Parameters["CompletedJobQueueName"].Value;
            _metricJobQueueHeight = settings.Sections["JobRunnerSettings"].Parameters["MetricNewJobHeight"].Value;

            return Task.FromResult(true);
        }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[0];
        }


        /// <summary>
        /// This is the main entry point for your service instance.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await CalculateAndReportLoad();

                // Process next message from SB queue
                try
                {
                    var message = await _sbreceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(3));
                    if (message != null)
                    {
                        object jobProperty;
                        if (message.ApplicationProperties.TryGetValue("job", out jobProperty))
                        {
                            var job = JsonConvert.DeserializeObject<JobRecord>((string)jobProperty);
                            var startTime = DateTime.UtcNow;

                            var partitionKey = PartitionHelper.GetInt64PartitionKey(job.JobDetail.JobId.ToString());
                            var address = await SharedServiceResolver.ResolveParitionedServiceEndpoint(this.Context.CodePackageActivationContext.ApplicationName, RestEndpointServiceNameConstants.JobService, partitionKey, cancellationToken);

                            // abandon job processing if the job has already expired
                            if (DateTime.UtcNow.CompareTo(job.JobDetail.ExpiresAfter) > 0)
                            {
                                // update job state
                                var jobServiceUri = new Uri(address + "/api/Job/" + job.JobDetail.JobId.ToString());
                                var payload = new StringContent(JsonConvert.SerializeObject(TaskState.ExecutionAborted), Encoding.UTF8, "application/json");
                                using HttpResponseMessage response = await _httpClient.PutAsync(jobServiceUri, payload, cancellationToken);
                                #region tracing
                                _traceProperties = new Dictionary<string, string>
                                {
                                        {"traceType", "JobExecution"},
                                        {"jobId", job.JobDetail.JobId.ToString()},
                                        {"message",$"JobRunner: Aborting JobId: {job.JobDetail.JobId}, Job Expired at {job.JobDetail.ExpiresAfter}."}
                                };
                                if (!response.IsSuccessStatusCode)
                                {
                                    _traceProperties.Add("errorMessage", $"JobRunner: unable to update TaskState for JobId: {job.JobDetail.JobId}");
                                }
                                _telemetryClient.TrackEvent("JobTrace", _traceProperties);
                                #endregion
                            }
                            else
                            {
                                // Update job state and process the job
                                var jobServiceUri = new Uri(address + "/api/Job/" + job.JobDetail.JobId.ToString());
                                var payload = new StringContent(JsonConvert.SerializeObject(TaskState.Executing), Encoding.UTF8, "application/json");
                                using HttpResponseMessage response = await _httpClient.PutAsync(jobServiceUri, payload, cancellationToken);
                                #region tracing
                                _traceProperties = new Dictionary<string, string>
                                {
                                    {"traceType", "JobExecution"},
                                    {"jobId", job.JobDetail.JobId.ToString()},
                                    {"message",$"JobRunner: TaskState updated to Executing for JobId: {job.JobDetail.JobId}"}
                                };
                                if (!response.IsSuccessStatusCode)
                                {
                                    _traceProperties.Add("errorMessage", $"JobRunner: unable to update TaskState for JobId: {job.JobDetail.JobId}");
                                }
                                _telemetryClient.TrackEvent("JobTrace", _traceProperties); 
                                #endregion

                                try
                                {
                                    // do the work for this job.  Currently just two simple types of job but this could easil be abstracted to use an interface and an executor
                                    job.StartedOn = DateTime.UtcNow;

                                    switch (job.JobDetail.JobType)
                                    {
                                        case JobResourceType.CpuBound:
                                            var mb = new Mandelbrot();
                                            job.Result = await mb.Compute(job.JobDetail.ImageSize, cancellationToken);

                                            break;

                                        default:
                                            foreach (var val in job.JobDetail.InputValues)
                                            {
                                                job.Result += val;
                                                await Task.Delay(TimeSpan.FromSeconds(job.JobDetail.Delay), cancellationToken);
                                            }
                                            break;
                                    }

                                    job.JobState = TaskState.ExecutionComplete;
                                    job.CompletedOn = DateTime.UtcNow;

                                    // Create a new brokered message to send to the queue (ActorId is in the Message Body for identification)
                                    var queueMessage = new Message(Encoding.UTF8.GetBytes(job.JobDetail.JobId.ToString()))
                                    {
                                        To = job.JobDetail.JobId.ToString(),
                                        ContentType = "application/json",
                                        CorrelationId = job.JobDetail.JobId.ToString()
                                    };
                                    queueMessage.UserProperties.Add("JobId", job.JobDetail.JobId.ToString());
                                    queueMessage.UserProperties.Add("jobResult", JsonConvert.SerializeObject(job));

                                    // Send the message to the queues 
                                    await _messageSender.SendAsync(queueMessage);

                                    _traceProperties = new Dictionary<string, string>
                                    {
                                        {"traceType", "JobExecution"},
                                        {"jobId", job.JobDetail.JobId.ToString()},
                                        {"message",$"JobRunner: RunAsync for JobId: {job.JobDetail.JobId}"}
                                    };
                                    _telemetryClient.TrackEvent("JobTrace", _traceProperties);
                                }
                                catch (Exception ex)
                                {
                                    _telemetryClient.TrackException(ex);
                                    throw;
                                }
                            }

                            // Complete the message so that it is not received again.  
                            await _sbreceiver.CompleteMessageAsync(message, cancellationToken);
                        }
                    }
                    else
                    {
                        _traceProperties = new Dictionary<string, string>
                        {
                            {"traceType", "Verbose"},
                            {"message",$"JobRunner: NO MESSAGES in queue"}
                        };
                        _telemetryClient.TrackEvent("JobTrace", _traceProperties);
                    }
                }
                catch (Azure.Messaging.ServiceBus.ServiceBusException sbex)
                {
                    _telemetryClient.TrackException(sbex);
                }
                catch (Exception ex)
                {
                    _telemetryClient.TrackException(ex);
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        private async Task CalculateAndReportLoad()
        {
            Pressure progress = Pressure.Stable;
            int jobHeightMetric;

            // Calculate jobHeight metric based on queue length trend and processing capacity, and report jobHeightMetric
            try
            {
                var nodesList = await _fabricClient.QueryManager.GetNodeListAsync();
                var partitionList = await _fabricClient.QueryManager.GetPartitionListAsync(this.Context.ServiceName);

                var jobQueueInfo = await _managementClient.GetQueueRuntimeInfoAsync(_jobQueueName);
                var jobQueueMessageCount = jobQueueInfo.MessageCount;
                var partitionListCount = partitionList.Count;
                var runnerNodeCount = nodesList.Count(nl => nl.NodeType.Contains(this.Context.NodeContext.NodeType)) == 0
                    ? nodesList.Count(nl => nl.NodeType.Contains(this.Context.NodeContext.NodeType))
                    : 1;
                var pressureMetric =
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 14) ? 50 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 13) ? 40 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 12) ? 30 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 11) ? 20 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 10) ? 10 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 9) ? 9 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 8) ? 8 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 7) ? 7 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 6) ? 6 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 5) ? 5 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 4) ? 4 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 3) ? 3 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 2) ? 2 :
                    (jobQueueMessageCount) > (partitionListCount * runnerNodeCount * 1) ? 1 : 0;

                _simpleTrends.UpdateSeries(jobQueueMessageCount);
                var pressureTrend = _simpleTrends.CalculateTrend();

                progress = pressureTrend < 0 ? Pressure.Downward :
                    pressureTrend > 0 ? Pressure.Upward : Pressure.Stable;

                switch (progress)
                {
                    case Pressure.Downward:
                        switch (pressureMetric)
                        {
                            case 50:
                            case 40:
                                jobHeightMetric = 50; // large backlog, continue scaling to working down the pressure
                                break;
                            case 30:
                            case 20:
                                jobHeightMetric = 45; // big backlog, reduce scaling to allow existing partitions to continue working down the pressure
                                break;
                            case 10:
                            case 9:
                            case 8:
                                jobHeightMetric = 40; // holding and allowing current partitions to keep working down the pressure
                                break;
                            case 7:
                            case 6:
                                jobHeightMetric = 35; // holding and allowing current partitions to keep working down the pressure
                                break;
                            case 5:
                            case 4:
                            case 3:
                                jobHeightMetric = 25; // processing all requests with 50% or less capacity, can start scaling down
                                break;
                            case 2:
                            case 1:
                            default:
                                jobHeightMetric = 20; // basically no pressure, scale down slow
                                break;
                        }
                        break;
                    case Pressure.Upward:
                        switch (pressureMetric)
                        {
                            case 50:
                                jobHeightMetric = 250; // request immediate scale up
                                break;
                            case 40:
                                jobHeightMetric = 200; // request immediate scale up
                                break;
                            case 30:
                                jobHeightMetric = 180; // request immediate scale up
                                break;
                            case 20:
                                jobHeightMetric = 125; // request immediate scale up
                                break;
                            case 10:
                                jobHeightMetric = 100; // request immediate scale up
                                break;
                            case 9:
                                jobHeightMetric = 90; // request immediate scale up
                                break;
                            case 8:
                                jobHeightMetric = 80; // request immediate scale up
                                break;
                            case 7:
                                jobHeightMetric = 70; // request immediate scale up
                                break;
                            case 6:
                                jobHeightMetric = 60; // request immediate scale up
                                break;
                            case 5:
                                jobHeightMetric = 50; // request immediate scale up
                                break;
                            case 4:
                                jobHeightMetric = 45; // request immediate scale up
                                break;
                            case 3:
                                jobHeightMetric = 40; // request immediate scale up
                                break;
                            case 2:
                                jobHeightMetric = 35; // start scaling
                                break;
                            case 1:
                            default:
                                jobHeightMetric = 25; // holding
                                break;
                        }
                        break;
                    default: //stable
                        switch (pressureMetric)
                        {
                            case 50:
                            case 40:
                            case 30:
                                jobHeightMetric = 50; // big backlog, keep scaling to allow new partitions to help work down the pressure
                                break;
                            case 20:
                            case 10:
                                jobHeightMetric = 10; // big backlog, reduce scaling and allow existing partitions to continue working down the pressure
                                break;
                            case 9:
                            case 8:
                            case 7:
                            case 6:
                            case 5:
                            case 4:
                                jobHeightMetric = 4; // reduce scaling more but allow existing partitions to continue working down the pressure
                                break;
                            case 3:
                                jobHeightMetric = 3; // holding with just enough capacity to process incoming messages
                                break;
                            case 2:
                            case 1:
                            default:
                                jobHeightMetric = 1; // probably have more capacity than we need, scale down
                                break;
                        }
                        break;
                }

                var dir = _traceProperties = new Dictionary<string, string>
                {
                    {"traceType", "Pressure"},
                    {"scalingTrend",$"JobRunner: RunAsync - PressureChange: {String.Format("{0:00.0}", pressureTrend)}, Direction: {progress}"},
                    {"PressureChange", String.Format("{0:00.0}", pressureTrend)},
                    {"Direction", $"{progress}" },
                    {"MessageCount", jobQueueInfo.MessageCount.ToString()},
                    {"RunnerNodeCount", runnerNodeCount.ToString()},
                    {"PartitionCountReady", partitionList.Count(pc => pc.PartitionStatus.Equals(ServicePartitionStatus.Ready)).ToString()},
                    {"PressureMetric", pressureMetric.ToString()},
                    {"JobHeightMetric", jobHeightMetric.ToString()},
                    {"EstimatedJobHeight", (partitionList.Count(pc => pc.PartitionStatus.Equals(ServicePartitionStatus.Ready)) * jobHeightMetric / runnerNodeCount).ToString()},
                };
                _telemetryClient.TrackEvent("JobTrace", _traceProperties);

                this.Partition.ReportLoad(new List<LoadMetric>
                    {
                        new LoadMetric(_metricJobQueueHeight, jobHeightMetric)
                    });
            }
            catch (FabricTransientException ftex)
            {
                _telemetryClient.TrackException(ftex);
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                throw;
            }
        }
    }
}
