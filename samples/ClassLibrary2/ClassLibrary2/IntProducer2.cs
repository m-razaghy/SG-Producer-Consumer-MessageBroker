using ClassLibrary.Attributes;
using ClassLibrary.Interfaces;

[RetryNumber(3)]
[RateLimit(5)]
public class IntProducer2 : IProducer<int>
{
    Logger _logger = new Logger("Producer.txt");
    Producer<int> _producer = new Producer<int>();
    public int Produce()
    {
        int sample = _producer.Produce();
        _logger.Log(LogLevel.Info, $"{nameof(IntProducer2)} data {sample} is produced.");
        return sample;
    }
}