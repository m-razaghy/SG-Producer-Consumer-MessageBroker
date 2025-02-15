using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using ClassLibrary.Contract;

namespace MessageBroker.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MessageBrokerController : ControllerBase
    {
        private static Logger _logger = new Logger("MessageBroker.txt");
        private static readonly string _messageFilePath = "messages.json";
        private static readonly string _backupFilePath = "messages_backup.json";
        private static readonly string _sequenceFilePath = "sequence_tracker.json";
        private static readonly string _sequenceBackupFilePath = "sequence_tracker_backup.json";
        private static readonly object _lock = new object();

        private static readonly Dictionary<string, PriorityQueue<MessagePayload, int>> _messageQueues = LoadMessages();
        private static Dictionary<string, int> _sequenceTracker = LoadSequenceTracker();

        private static Dictionary<string, PriorityQueue<MessagePayload, int>> LoadMessages()
        {
            string jsonFileToLoad = System.IO.File.Exists(_messageFilePath) && System.IO.File.Exists(_backupFilePath)
                ? _backupFilePath
                : _messageFilePath;

            if (!System.IO.File.Exists(jsonFileToLoad))
                return new Dictionary<string, PriorityQueue<MessagePayload, int>>();

            string json = System.IO.File.ReadAllText(jsonFileToLoad);
            var messages = string.IsNullOrWhiteSpace(json)
                ? new List<MessagePayload>()
                : JsonSerializer.Deserialize<List<MessagePayload>>(json);

            var queues = new Dictionary<string, PriorityQueue<MessagePayload, int>>();
            foreach (var message in messages)
            {
                string key = $"{message.producerId}-{message.consumerId}";
                if (!queues.ContainsKey(key))
                    queues[key] = new PriorityQueue<MessagePayload, int>();

                queues[key].Enqueue(message, message.sequenceId);
            }

            return queues;
        }


        private static void SaveMessages()
        {
            try
            {
                List<MessagePayload> messages;
                lock (_lock)
                {
                    messages = _messageQueues
                        .SelectMany(pair => pair.Value.UnorderedItems.Select(item => item.Element))
                        .OrderBy(m => m.sequenceId)
                        .ToList();
                }

                Task.Run(() =>
                {
                    try
                    {
                        lock (_lock)
                        {
                            if (System.IO.File.Exists(_messageFilePath))
                                System.IO.File.Copy(_messageFilePath, _backupFilePath, true);
                            System.IO.File.WriteAllText(_messageFilePath, JsonSerializer.Serialize(messages));

                            if (System.IO.File.Exists(_backupFilePath))
                                System.IO.File.Delete(_backupFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Error, $"Failed to save messages: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error extracting messages: {ex.Message}");
            }
        }

        private static Dictionary<string, int> LoadSequenceTracker()
        {
            string jsonFileToLoad = System.IO.File.Exists(_sequenceFilePath) && System.IO.File.Exists(_sequenceBackupFilePath)
                ? _sequenceBackupFilePath
                : _sequenceFilePath;

            if (!System.IO.File.Exists(jsonFileToLoad))
                return new Dictionary<string, int>();

            try
            {
                string json = System.IO.File.ReadAllText(jsonFileToLoad);
                return string.IsNullOrWhiteSpace(json)
                    ? new Dictionary<string, int>()
                    : JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Failed to load sequence tracker from {jsonFileToLoad}: {ex.Message}");
                return new Dictionary<string, int>();
            }
        }


        private static void SaveSequenceTracker()
        {
            try
            {
                Dictionary<string, int> sequenceCopy;
                lock (_lock)
                {
                    sequenceCopy = new Dictionary<string, int>(_sequenceTracker);
                }

                Task.Run(() =>
                {
                    try
                    {
                        lock (_lock)
                        {
                            if (System.IO.File.Exists(_sequenceFilePath))
                                System.IO.File.Copy(_sequenceFilePath, _sequenceBackupFilePath, true);

                            System.IO.File.WriteAllText(_sequenceFilePath, JsonSerializer.Serialize(sequenceCopy));

                            if (System.IO.File.Exists(_sequenceBackupFilePath))
                                System.IO.File.Delete(_sequenceBackupFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Error, $"Failed to save sequence tracker: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error extracting sequence tracker: {ex.Message}");
            }
        }


        [HttpGet("Receive")]
        public ActionResult<MessagePayload> Receive(string producerId, string consumerId)
        {
            string key = $"{producerId}-{consumerId}";

            lock (_lock)
            {
                if (!_messageQueues.ContainsKey(key) || _messageQueues[key].Count == 0)
                {
                    _logger.Log(LogLevel.Warning, $"No messages available for {producerId} -> {consumerId}.");
                    return NoContent();
                }

                int expectedSequence;
                if (!_sequenceTracker.ContainsKey(key))
                    _sequenceTracker[key] = 1;
                expectedSequence = _sequenceTracker[key];

                MessagePayload nextMessage = _messageQueues[key].Peek();

                if (nextMessage.sequenceId == expectedSequence)
                {
                    _logger.Log(LogLevel.Info, $"Message {nextMessage.sequenceId} sent to {consumerId}: {nextMessage.message}");

                    _sequenceTracker[key]++;

                    Task.Run(SaveSequenceTracker);
                    return Ok(nextMessage);
                }
            }

            _logger.Log(LogLevel.Warning, $"Waiting for message {producerId} -> {consumerId} (Expected: {_sequenceTracker[key]}).");
            return NoContent();
        }


        [HttpGet("Check")]
        public ActionResult Check()
        {
            return Ok();
        }

        [HttpPost("Send")]
        public ActionResult Send([FromBody] MessagePayload newMessage)
        {
            string key = $"{newMessage.producerId}-{newMessage.consumerId}";

            bool isDuplicate = false;

            lock (_lock)
            {
                if (!_messageQueues.ContainsKey(key))
                    _messageQueues[key] = new PriorityQueue<MessagePayload, int>();

                if (_messageQueues[key].UnorderedItems.Any(m => m.Element.sequenceId == newMessage.sequenceId))
                {
                    isDuplicate = true;
                }
                else
                {
                    _messageQueues[key].Enqueue(newMessage, newMessage.sequenceId);
                }
            }

            if (isDuplicate)
            {
                _logger.Log(LogLevel.Warning, $"Duplicate message ignored (Seq {newMessage.sequenceId}).");
                return Ok($"Duplicate message ignored (ProducerId: {newMessage.producerId} Sequence Number {newMessage.sequenceId}).");
            }

            Task.Run(SaveMessages);

            _logger.Log(LogLevel.Info, $"Message stored (ProducerId: {newMessage.producerId} Sequence Number {newMessage.sequenceId}): {newMessage.message}");
            return Ok("Message stored successfully.");
        }


        [HttpPost("Ack")]
        public ActionResult Acknowledge([FromBody] AckPayload ackPayload)
        {
            string key = $"{ackPayload.producerId}-{ackPayload.consumerId}";

            if (!_messageQueues.ContainsKey(key) || _messageQueues[key].Count == 0)
            {
                _logger.Log(LogLevel.Warning, $"No messages found for {ackPayload.producerId} -> {ackPayload.consumerId}.");
                return Ok($"No messages found.");
            }

            bool removed = false;
            lock (_lock)
            {
                var messages = _messageQueues[key].UnorderedItems.Select(m => m.Element).ToList();
                if (messages.Any(m => m.sequenceId == ackPayload.sequenceId))
                {
                    messages.RemoveAll(m => m.sequenceId == ackPayload.sequenceId);
                    var newQueue = new PriorityQueue<MessagePayload, int>();
                    foreach (var message in messages)
                        newQueue.Enqueue(message, message.sequenceId);
                    _messageQueues[key] = newQueue;
                    removed = true;
                }
            }

            if (removed)
            {
                Task.Run(SaveMessages);
                _logger.Log(LogLevel.Info, $"Message (ConsumerId {ackPayload.consumerId} Sequence Number {ackPayload.sequenceId}) acknowledged and removed.");
                return Ok($"Message {ackPayload.sequenceId} acknowledged.");
            }
            else
            {
                _logger.Log(LogLevel.Warning, $"Message with ConsumerId {ackPayload.consumerId} and Sequence ID {ackPayload.sequenceId} not found.");
                return Ok($"Message with Sequence ID {ackPayload.sequenceId} not found.");
            }
        }

    }
}
