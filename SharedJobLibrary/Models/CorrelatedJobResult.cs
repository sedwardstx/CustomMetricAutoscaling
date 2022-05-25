using System;
using System.Runtime.Serialization;

namespace SharedJobLibrary.Models
{
    [DataContract]
	public class xCorrelatedJobResult
	{
		[DataMember]
		public Guid CorrelationId { get; set; }

		[DataMember]
		public xJobResult Result { get; set; }
	}
}
