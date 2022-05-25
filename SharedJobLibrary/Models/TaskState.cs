
using System.Runtime.Serialization;

namespace SharedJobLibrary.Models
{
	
	public enum TaskState
	{
		[EnumMember(Value = "New")]
		New,
		[EnumMember(Value = "Queued")]
		Queued,
		[EnumMember(Value = "Cancelled")]
		Cancelled,
		[EnumMember(Value = "Executing")]
		Executing,
		[EnumMember(Value = "ExecutionAborted")]
		ExecutionAborted,
		[EnumMember(Value = "ExecutionComplete")]
		ExecutionComplete,
		[EnumMember(Value = "Finalized")]
		Finalized
	}
}
