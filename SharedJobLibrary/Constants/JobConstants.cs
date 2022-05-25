using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedJobLibrary.Constants
{
	public static class JobConstants
	{
		public const string JobDictionary = "JobDictionary";
		public const string CorrelatedJobDictionary = "CorrelatedJobDictionary";
		public const string ResultsDictionary = "ResultsDictionary";
		public const string JobRecord = "JobRecord";
	}

	public static class RestEndpointServiceNameConstants
	{
		public const string JobGateway = "JobGateway";
		public const string JobService = "JobService";
	}
}
