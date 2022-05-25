using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Newtonsoft.Json;
using SharedJobLibrary.Constants;
using SharedJobLibrary.Models;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace JobService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobController : ControllerBase
    {
        // AI Tracing
        private readonly TelemetryClient _telemetryClient;
        private Dictionary<string, string> _traceProperties;

        // JobController
        private readonly ILogger<JobController> _logger;
        private readonly StatefulServiceContext _context;
        private readonly IReliableStateManager _stateManager;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly FabricClient _fabricClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        //dictionaries        
        private IReliableDictionary<Guid, JobRecord> _jobDictionary;

        // SB config settings
        private readonly string _senderConnectionString;
        private readonly string _jobQueueName;

        // SB client
        private readonly IQueueClient _messageSender;

        public JobController(ILogger<JobController> logger, StatefulServiceContext context, IReliableStateManager stateManager, FabricClient fabricClient, TelemetryClient telemetryClient, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _stateManager = stateManager;
            _cancellationTokenSource = new CancellationTokenSource();
            _fabricClient = fabricClient;
            _telemetryClient = telemetryClient;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;

            _jobDictionary = _stateManager.GetOrAddAsync<IReliableDictionary<Guid, JobRecord>>(JobConstants.JobDictionary).Result;

            _senderConnectionString = _configuration.GetSection("JobServiceSettings").GetValue<string>("ServiceBusConnectionString");
            _jobQueueName = _configuration.GetSection("JobServiceSettings").GetValue<string>("JobQueueName");

            //var settings = context.CodePackageActivationContext.GetConfigurationPackageObject("config").Settings;
            //_senderConnectionString = settings.Sections["JobServiceSettings"].Parameters["ServiceBusConnectionString"].Value;
            //_jobQueueName = settings.Sections["JobServiceSettings"].Parameters["JobQueueName"].Value;

            // initialize clients
            _messageSender = new QueueClient(connectionString: _senderConnectionString, entityPath: _jobQueueName);
        }
        // GET: api/<JobController>
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET api/<JobController>/5ADE4821-9DA0-4AC4-A5AA-F0266E87FECE
        [HttpGet("{jobId}")]
        public async Task<JobRecord> Get(Guid jobId, CancellationToken cancellationToken)
        {
            // update the JobState
            using (var tx = _stateManager.CreateTransaction())
            {
                var fetchedJob = await _jobDictionary.TryGetValueAsync(tx, jobId, TimeSpan.FromSeconds(5), cancellationToken);
                if (fetchedJob.HasValue)
                {
                    return fetchedJob.Value;
                }
                else
                {
                    return null;
                }
            }
        }

        // POST api/<JobController>
        [HttpPost]
        public async Task Post([FromBody] JobDetail job, CancellationToken cancellationToken)
        {
            if (job.ExpiresAfter == null)
            {
                job.ExpiresAfter = DateTime.UtcNow.Add(TimeSpan.FromMinutes(15));
            }

            // set JobState to New
            var jobRecord = new JobRecord
            {
                JobState = TaskState.New,
                CreatedOn = DateTime.UtcNow,
                ExpiresOn = job.ExpiresAfter.HasValue && job.ExpiresAfter.Value != DateTime.MinValue ? job.ExpiresAfter.Value : DateTime.UtcNow.Add(TimeSpan.FromMinutes(15)),
                JobDetail = job
            };

            try
            {
                // Save jobs with initial state
                using (var tx = _stateManager.CreateTransaction())
                {
                    var savedJob = await _jobDictionary.AddOrUpdateAsync(tx, jobRecord.JobDetail.JobId, jobRecord, (key, value) => jobRecord, TimeSpan.FromSeconds(5), cancellationToken);

                    await tx.CommitAsync();
                }

                // Create a new brokered message to send to the queue 
                var queueMessage = new Message(Encoding.UTF8.GetBytes(jobRecord.JobDetail.JobId.ToString()))
                {
                    ContentType = "application/json",
                    CorrelationId = jobRecord.JobDetail.JobId.ToString()
                };
                queueMessage.UserProperties.Add("job", JsonConvert.SerializeObject(jobRecord));

                // Send the correlated message to the queue 
                await _messageSender.SendAsync(queueMessage);

                #region tracing

                var inputs = String.Join(", ", jobRecord.JobDetail.InputValues.Select(a => a.ToString()).Cast<string>().ToArray());
                _traceProperties = new Dictionary<string, string>{
                    {"traceType", "JobCreation"},
                    {"jobId", jobRecord.JobDetail.JobId.ToString()},
                    {"inputs", inputs },
                    {"message", $"Job queued successfully for JobId: {jobRecord.JobDetail.JobId}"}
                };
                _telemetryClient.TrackEvent("JobTrace", _traceProperties);
                #endregion
                
                // update the JobState
                using (var tx = _stateManager.CreateTransaction())
                {
                    jobRecord.JobState = TaskState.Queued;
                    await _jobDictionary.AddOrUpdateAsync(tx, jobRecord.JobDetail.JobId, jobRecord, (key, value) => jobRecord, TimeSpan.FromSeconds(5), cancellationToken);
                    //var fetchedJob = await _jobDictionary.TryGetValueAsync(tx, jobRecord.JobDetail.JobId, TimeSpan.FromSeconds(5), cancellationToken);
                    //if (fetchedJob.HasValue)
                    //{
                    //    var queuedJob = (JobRecord)fetchedJob.Value.Clone();
                    //    queuedJob.JobState = TaskState.Queued;
                    //    await _jobDictionary.AddOrUpdateAsync(tx, queuedJob.JobDetail.JobId, queuedJob, (key, value) => queuedJob, TimeSpan.FromSeconds(5), cancellationToken);
                    //}
                    await tx.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                throw;
            }
        }

        // PUT api/<JobController>/5ADE4821-9DA0-4AC4-A5AA-F0266E87FECE
        [HttpPut("{jobId}")]
        public async Task Put(Guid jobId, [FromBody] TaskState taskState, CancellationToken cancellationToken)
        {
            using (var tx = _stateManager.CreateTransaction())
            {
                var fetchedJob = await _jobDictionary.TryGetValueAsync(tx, jobId, TimeSpan.FromSeconds(5), cancellationToken);
                if(fetchedJob.HasValue)
                {
                    var newRecord = (JobRecord)fetchedJob.Value.Clone();
                    newRecord.JobState = taskState;
                    await _jobDictionary.AddOrUpdateAsync(tx, jobId, newRecord, (key, value) => newRecord, TimeSpan.FromSeconds(5), cancellationToken);
                }
                await tx.CommitAsync();
            }
        }

        // DELETE api/<JobController>/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
