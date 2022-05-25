using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SharedJobLibrary.Constants;
using SharedJobLibrary.Models;
using SharedJobLibrary.Utility;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace JobGateway.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class JobController : ControllerBase
    {
        private readonly ILogger<JobController> _logger;
        private readonly StatelessServiceContext _context;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly FabricClient _fabricClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        // Set up some properties and metrics:
        Dictionary<string, string> _traceProperties;

        public JobController(ILogger<JobController> logger, StatelessServiceContext context, FabricClient fabricClient, TelemetryClient telemetryClient, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _cancellationTokenSource = new CancellationTokenSource();
            _fabricClient = fabricClient;
            _telemetryClient = telemetryClient;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        // GET api/<JobController>/5ADE4821-9DA0-4AC4-A5AA-F0266E87FECE
        [HttpGet("{jobId}")]
        public async Task<IActionResult> Get(Guid jobId, CancellationToken cancellationToken)
        {
            var partitionKey = PartitionHelper.GetInt64PartitionKey(jobId.ToString());
            var address = await SharedServiceResolver.ResolveParitionedServiceEndpoint(_context.CodePackageActivationContext.ApplicationName, RestEndpointServiceNameConstants.JobService, partitionKey, cancellationToken);

            var httpClient = _httpClientFactory.CreateClient();
            var jobServiceUri = new Uri(address + "/api/Job/" + jobId.ToString());
            using HttpResponseMessage response = await httpClient.GetAsync(jobServiceUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using HttpContent content = response.Content;
                string contentString = await content.ReadAsStringAsync(cancellationToken);

                var jobRecord = JsonConvert.DeserializeObject<JobRecord>(contentString);
                if(jobRecord != null)
                {
                    return Ok(jobRecord);
                } else
                {
                    return NotFound(jobRecord);
                }
            }

            return BadRequest(response.StatusCode);
        }

        // POST api/<JobController>
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] JobDetail jobDetail, CancellationToken cancellationToken)
        {
            if (jobDetail.JobId == Guid.Empty)
            {
                _logger.LogError("Invalid JobId, cannot be equal to Guid.Empty");
                throw new ArgumentException("Invalid JobId, cannot be equal to Guid.Empty");
            }

            _logger.BeginScope("Creating new Job actor for: {jobid} ", jobDetail.JobId);

            try
            {
                var partitionKey = PartitionHelper.GetInt64PartitionKey(jobDetail.JobId.ToString());
                var address = await SharedServiceResolver.ResolveParitionedServiceEndpoint(_context.CodePackageActivationContext.ApplicationName, RestEndpointServiceNameConstants.JobService, partitionKey, cancellationToken);

                var httpClient = _httpClientFactory.CreateClient();
                var jobServiceUri = new Uri(address + "/api/Job");

                var payload = new StringContent(JsonConvert.SerializeObject(jobDetail), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await httpClient.PostAsync(jobServiceUri, payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var correlationId = await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger.LogInformation("Job: {1}, job created with correlationId {2}", jobDetail.JobId, correlationId);

                    _traceProperties = new Dictionary<string, string>{
                        {"traceType", "Job"},
                        {"JobId", jobDetail.JobId.ToString() },
                        {"JobState", "New" },
                        {"correlationId", correlationId}, 
                        {"message", $"Job: {jobDetail.JobId} created"}
                    };
                    _telemetryClient.TrackEvent("JobTrace", _traceProperties);

                    return Ok($"Job Created, JobId: {jobDetail.JobId}, CorrelationId: {correlationId}");
                }
                {
                    var errorMessage = String.Format("Error - StatusCode does not indicate success: {0}: Unable to create Job for: {1}", response.StatusCode, jobDetail.JobId);
                    _traceProperties = new Dictionary<string, string>{
                        {"traceType", "Error"},
                        {"JobId", jobDetail.JobId.ToString() },
                        {"JobState", "New" },
                        {"message", $"Job: {jobDetail.JobId} error: {errorMessage}"}
                    };
                    _telemetryClient.TrackEvent("JobTrace", _traceProperties);
                    return BadRequest(errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception - {exceptionMessage}: Unable to create Job for: {jobid}", ex.Message, jobDetail.JobId);
                throw;
            }
        }

    }
}
