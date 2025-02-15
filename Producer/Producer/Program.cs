using ClassLibrary.Interfaces;
using ClassLibrary.Contract;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text;
using System.ComponentModel.DataAnnotations;

class Program
{
    private static Logger _logger = new Logger("Producer.txt");
    private static readonly object _lock = new object();
    private static readonly Dictionary<string, int> sequenceNumbers = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> retryNumbers = new Dictionary<string, int>();
    private static readonly HttpClient _httpClient = new HttpClient();
    private static string _brokerUrl = "http://localhost:5157/messagebroker";

    static async Task Main()
    {
        string dependencyPath = @"E:\work\Csharp Bootcamp\Project\SG-Producer-Consumer-MessageBroker\samples\Dlls\AutoFixture.dll";
        string dllDirectory = @"E:\work\Csharp Bootcamp\Project\SG-Producer-Consumer-MessageBroker\samples\Dlls";

        Assembly.LoadFrom(dependencyPath);
        var dllFiles = Directory.GetFiles(dllDirectory, "*.dll");

        List<Task> producerTasks = new List<Task>();
        foreach (var dllPath in dllFiles)
        {
            Assembly assembly = Assembly.LoadFrom(dllPath);

            var producerType = assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IProducer<>)))
                .FirstOrDefault();

            string producerId = producerType.Name;
            string consumerId = assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
                .FirstOrDefault().Name;

            object producerInstance = Activator.CreateInstance(producerType);
            int RetryNumber = 0;
            int RateLimit = 0;
            ReadAttributes(producerType, ref RetryNumber, ref RateLimit);

            sequenceNumbers[producerId] = 0;
            retryNumbers[producerId] = 0;

            for (int i = 0; i < RateLimit; i++)
            {
                producerTasks.Add(Task.Run(async () =>
                {
                    while (true)
                    {
                        int sequenceNumber = 0;
                        lock (_lock)
                        {
                            sequenceNumbers[producerId]++;
                            sequenceNumber = sequenceNumbers[producerId];
                        }
                        object producedData = InvokeProduceMethod(producerInstance);
                        await SendMessage(producerId, consumerId, producedData, sequenceNumber, RetryNumber);
                        Thread.Sleep(2000);
                    }
                }));
            }

        }
        await Task.WhenAll(producerTasks);
    }

    static void ReadAttributes(Type type, ref int RetryNumber, ref int RateLimit)
    {
        foreach (var attr in type.GetCustomAttributes(false))
        {
            if (attr.GetType().Name == "RetryNumberAttribute")
            {
                PropertyInfo prop = attr.GetType().GetProperty("RetryNumber");
                RetryNumber = (int)prop?.GetValue(attr);
            }
            else if (attr.GetType().Name == "RateLimitAttribute")
            {
                PropertyInfo prop = attr.GetType().GetProperty("RateLimit");
                RateLimit = (int)prop?.GetValue(attr);
            }
        }
    }

    static object InvokeProduceMethod(object instance)
    {
        MethodInfo produceMethod = instance.GetType().GetMethod("Produce");
        if (produceMethod == null)
        {
            Console.WriteLine("Produce method not found.");
            return null;
        }
        object result = produceMethod.Invoke(instance, null);
        return result;
    }

    static async Task SendMessage(string producerId, string consumerId, object message, int sequenceId, int retryNumber)
    {
        MessagePayload msgData = new MessagePayload()
        {
            producerId = producerId,
            consumerId = consumerId,
            sequenceId = sequenceId,
            message = message
        };

        string json = JsonSerializer.Serialize(msgData);
        var content = new StringContent(json, Encoding.UTF8, "application/json");


        while (retryNumbers[producerId] < retryNumber)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync($"{_brokerUrl}/Send", content);
                if (response.IsSuccessStatusCode)
                {
                    retryNumbers[producerId] = 0;
                    return;
                }
            }
            catch
            {
                lock (_lock)
                {
                    if (retryNumbers[producerId] >= retryNumber)
                        break;
                    _logger.Log(LogLevel.Warning, $"Producer {producerId} Sequence Number {sequenceId}-> Broker Unreachable. Retrying {retryNumbers[producerId] + 1}/{retryNumber}...");
                    retryNumbers[producerId]++;
                }
                await Task.Delay(2000);
            }
        }
        while (true)
        {
            _logger.Log(LogLevel.Error, $"Producer {producerId} -> Broker Unreachable.");
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_brokerUrl}/Check");
                if (response.IsSuccessStatusCode)
                {
                    _logger.Log(LogLevel.Info, $"Producer {producerId} Sequence Number {sequenceId}-> Broker available again. Resuming.");
                    await _httpClient.PostAsync($"{_brokerUrl}/Send", content);
                    retryNumbers[producerId] = 0;
                    return;
                }
            }
            catch { }
            await Task.Delay(5000);
        }
    }
}