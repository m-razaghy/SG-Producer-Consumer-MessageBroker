using ClassLibrary;
using ClassLibrary.Attributes;
using ClassLibrary.Interfaces;
using System.Text.Json;

[RetryNumber(3)]
[RateLimit(2)]
public class IntConsumer1 : IConsumer<int>
{
    Logger _logger = new Logger("Consumer.txt");
    Consumer<int> _consumer = new Consumer<int>();
    public void Consume(object t)
    {
        _consumer.Consume(t);
        int result = JsonSerializer.Deserialize<int>((JsonElement)t);
        _logger.Log(LogLevel.Info, $"{nameof(IntConsumer1)} data {result} is consumed.");
    }
}