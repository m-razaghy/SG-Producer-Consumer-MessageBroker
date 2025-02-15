using ClassLibrary.Interfaces;
using ClassLibrary.Contract;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text;

class Program
{
    private static Logger _logger = new Logger("Consumer.txt");
    private static readonly object _lock = new object();
    private static readonly Dictionary<string, int> retryNumbers = new Dictionary<string, int>();
    private static readonly HttpClient _httpClient = new HttpClient();
    private static string _brokerUrl = "http://localhost:5157/messagebroker";

    static async Task Main()
    {
        string dependencyPath = @"E:\work\Csharp Bootcamp\Project\SG-Producer-Consumer-MessageBroker\samples\Dlls\AutoFixture.dll";
        string dllDirectory = @"E:\work\Csharp Bootcamp\Project\SG-Producer-Consumer-MessageBroker\samples\Dlls";

        Assembly.LoadFrom(dependencyPath);
        var dllFiles = Directory.GetFiles(dllDirectory, "*.dll");

        List<Task> consumerTasks = new List<Task>();
        foreach (var dllPath in dllFiles)
        {
            Assembly assembly = Assembly.LoadFrom(dllPath);
            var consumerType = assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
                .FirstOrDefault();
            string consumerId = consumerType.Name;

            var producerId = assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IProducer<>)))
                .FirstOrDefault().Name;

            object consumerInstance = Activator.CreateInstance(consumerType);
            int RetryNumber = 0;
            int RateLimit = 0;
            ReadAttributes(consumerType, ref RetryNumber, ref RateLimit);

            retryNumbers[consumerId] = 0;

            for (int i = 0; i < RateLimit; i++)
            {
                consumerTasks.Add(Task.Run(async () =>
                {
                    while (true)
                    {
                        await ReceiveAndProcessMessage(consumerId, producerId, consumerInstance, RetryNumber);
                        await Task.Delay(1000);
                    }
                }));
            }

        }
        await Task.WhenAll(consumerTasks.ToArray());
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

    static async Task ReceiveAndProcessMessage(string consumerId, string producerId, object consumerInstance, int retryNumber)
    {
        while (retryNumbers[consumerId] < retryNumber)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_brokerUrl}/Receive?consumerId={consumerId}&producerId={producerId}");
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    _logger.Log(LogLevel.Warning, $"Consumer {consumerId} -> No messages available.");
                    return;
                }
                else if (!response.IsSuccessStatusCode)
                {
                    lock (_lock)
                    {
                        _logger.Log(LogLevel.Warning, $"Consumer {consumerId} -> Broker Unreachable. Retrying {retryNumbers[consumerId] + 1}/{retryNumber}...");
                        retryNumbers[consumerId]++;
                    }
                    await Task.Delay(2000);
                    continue;
                }
                retryNumbers[consumerId] = 0;
                string responseData = await response.Content.ReadAsStringAsync();
                var messageData = JsonSerializer.Deserialize<MessagePayload>(responseData);

                var ackPayload = new AckPayload()
                {
                    consumerId = consumerId,
                    producerId = producerId,
                    sequenceId = messageData.sequenceId
                };
                string jsonAck = JsonSerializer.Serialize(ackPayload);
                var ackContent = new StringContent(jsonAck, Encoding.UTF8, "application/json");

                HttpResponseMessage ackResponse = await _httpClient.PostAsync($"{_brokerUrl}/Ack", ackContent);
                if (ackResponse.IsSuccessStatusCode)
                {
                    _logger.Log(LogLevel.Info, $"Consumer {consumerId} SequenceNumber {ackPayload.sequenceId} -> ACK sent for Sequence {messageData.sequenceId}");
                    retryNumbers[consumerId] = 0;
                }
                else
                {
                    _logger.Log(LogLevel.Warning, $"Consumer {consumerId} SequenceNumber {ackPayload.sequenceId} -> Failed to ACK Sequence {messageData.sequenceId}");
                    lock (_lock)
                    {
                        retryNumbers[consumerId]++;
                    }
                    await Task.Delay(2000);
                    continue;
                }

                InvokeConsumeMethod(consumerInstance, messageData.message);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    if (retryNumbers[consumerId] >= retryNumber)
                        break;
                    _logger.Log(LogLevel.Warning, $"Consumer {consumerId} -> Broker Unreachable. Retrying {retryNumbers[consumerId] + 1}/{retryNumber}...");
                    retryNumbers[consumerId]++;
                }
                await Task.Delay(2000);
            }
        }
        _logger.Log(LogLevel.Error, $"Consumer {consumerId} -> Maximum retries ({retryNumber}) reached. Entering wait mode.");
        while (true)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{_brokerUrl}/Check");
                if (response.IsSuccessStatusCode)
                {
                    _logger.Log(LogLevel.Info, $"Consumer {consumerId} -> Broker available again. Resuming.");
                    retryNumbers[consumerId] = 0;
                    return;
                }

            }
            catch
            {
                _logger.Log(LogLevel.Error, $"Consumer {consumerId} -> Broker Unreachable.");
            }
            await Task.Delay(5000);
        }
    }


    static void InvokeConsumeMethod(object instance, object data)
    {
        MethodInfo consumeMethod = instance.GetType().GetMethod("Consume");
        if (consumeMethod == null)
        {
            Console.WriteLine("Consume method not found.");
            return;
        }
        object result = consumeMethod.Invoke(instance, new object[] { data });
    }
}