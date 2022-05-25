using System;
using System.Runtime.Serialization;

namespace SharedJobLibrary.Models
{
	[DataContract]
	public class JobRecord : ICloneable
	{
		[DataMember]
		public TaskState JobState { get; set; }

		[DataMember]
		public Guid xCorrelationId { get; set; }

		[DataMember]
		public JobDetail JobDetail { get; set; }

		[DataMember]
		public DateTime CreatedOn { get; set; }

		[DataMember]
		public DateTime ExpiresOn { get; set; }

		[DataMember]
		public DateTime StartedOn { get; set; }

		[DataMember]
		public DateTime CompletedOn { get; set; }

		[DataMember]
		public double Result { get; set; }

		public object Clone()
		{
			var clone = (JobRecord)this.MemberwiseClone();
			clone.JobDetail = (JobDetail)this.JobDetail.Clone();
			return clone;
		}
	}
}
