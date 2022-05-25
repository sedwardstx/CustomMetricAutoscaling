using System.Threading.Tasks;

namespace SharedJobLibrary.Services
{
    public interface IMessagePublisher
	{
		Task Publish<T>(T obj);
		Task Publish(string raw);
	}
}
