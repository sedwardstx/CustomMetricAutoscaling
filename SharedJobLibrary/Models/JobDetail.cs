using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SharedJobLibrary.Models
{
    [DataContract]
	public class JobDetail : ICloneable
	{
		[DataMember]
		public Guid JobId { get; set; }

		[DataMember]
		public JobResourceType JobType { get; set; }

		[DataMember]
		public IEnumerable<int> InputValues { get; set; }

		[DataMember]
		public int Delay { get; set; }

		[DataMember]
		public int ImageSize { get; set; }

		[DataMember]
		public DateTime? ExpiresAfter { get; set; }

		public object Clone()
		{
			return this.MemberwiseClone();
		}
	}
}
