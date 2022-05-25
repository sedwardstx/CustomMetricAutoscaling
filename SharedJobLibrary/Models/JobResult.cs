using System;
using System.Runtime.Serialization;

namespace SharedJobLibrary.Models
{


	[DataContract]
	public class xJobResult
	{
		[DataMember]
		public Guid JobId { get; set; }

		[DataMember]
		public DateTime StartedOn { get; set; }

		[DataMember]
		public DateTime CompletedOn { get; set; }

		[DataMember]
		public TaskState JobState { get; set; }

		[DataMember]
		public JobResourceType JobType { get; set; }

		[DataMember]
		public double Result { get; set; }
	}
}
