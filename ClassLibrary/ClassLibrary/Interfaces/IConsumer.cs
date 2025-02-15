namespace ClassLibrary.Interfaces
{
    public interface IConsumer<T>
    {
        public void Consume(object t);
    }
}
