using ClassLibrary.Interfaces;
using System.Text.Json;

public class Consumer<T> : IConsumer<T>
{
    public void Consume(object t)
    {
        T casted = JsonSerializer.Deserialize<T>((JsonElement)t);
        return;
    }
}