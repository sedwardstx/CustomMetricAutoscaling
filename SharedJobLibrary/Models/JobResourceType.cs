using System.Runtime.Serialization;

namespace SharedJobLibrary.Models
{
    public enum JobResourceType
	{
		[EnumMember(Value = "TimeBound")]
		TimeBound,
		[EnumMember(Value = "CpuBound")]
		CpuBound
	}
}
