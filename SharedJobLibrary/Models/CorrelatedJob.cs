using System;
using System.Runtime.Serialization;

namespace SharedJobLibrary.Models
{
	[DataContract]
	public class xCorrelatedJob
	{
		[DataMember]
		public Guid CorrelationId { get; set; }

		[DataMember]
		public JobDetail JobDetail { get; set; }
	}
}
