using AutoFixture;
using ClassLibrary.Attributes;
using ClassLibrary.Interfaces;

[RetryNumber(3)]
[RateLimit(5)]
public class Producer<T> : IProducer<T>
{
    private readonly IFixture _fixture = new Fixture();
    public T Produce()
    {
        return _fixture.Create<T>();
    }
}
