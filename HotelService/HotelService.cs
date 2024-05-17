using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NEventStore;
using NEventStore.Serialization.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Npgsql;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using vgt_saga_hotel.Models;
using vgt_saga_serialization;

namespace vgt_saga_hotel.HotelService;

/// <summary>
/// Saga Payment service;
/// handles all payments in the transaction.
/// </summary>
public class HotelService : IDisposable
{
    private readonly HotelQueueHandler _queues;
    private readonly Logger _logger;
    private readonly IConfiguration _config;
    private readonly Utils _jsonUtils;
    private readonly IStoreEvents _eventStore;
    
    private readonly Channel<Message> _payments;
    private readonly Channel<Message> _publish;
    private readonly HotelHandler _hotelHandler;

    private readonly HotelDbContext _writeDb;
    private readonly HotelDbContext _readDb;
    
    /// <summary>
    /// Allows tasks cancellation from the outside of the class
    /// </summary>
    public CancellationToken Token { get; } = new();

    /// <summary>
    /// Constructor of the HotelService class.
    /// Initializes HotelService object.
    /// Creates, initializes and opens connections to the database and rabbitmq
    /// based on configuration data present and handled by specified handling objects.
    /// Throws propagated exceptions if the configuration data is nowhere to be found.
    /// </summary>
    /// <param name="config"> Configuration with the connection params </param>
    /// <param name="lf"> Logger factory to use by the event store </param>
    /// <exception cref="ArgumentException"> Which variable is missing in the configuration </exception>
    /// <exception cref="BrokerUnreachableException"> Couldn't establish connection with RabbitMQ </exception>
    public HotelService(IConfiguration config, ILoggerFactory lf)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _config = config;

        _jsonUtils = new Utils(_logger);
        _payments = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });

        var connStr = SecretUtils.GetConnectionString(_config, "DB_NAME_HOTEL", _logger);
        var options = new DbContextOptions<HotelDbContext>();
        _writeDb = new HotelDbContext(options, connStr);
        _readDb = new HotelDbContext(options, connStr);

        if (!_readDb.Hotels.Any())
        {
            CreateData().Wait(); 
        }
        
        _publish = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });
        
        _hotelHandler = new HotelHandler(_payments, _publish, _writeDb, _readDb, _logger);

        _queues = new HotelQueueHandler(_config, _logger);
        
        _queues.AddRepliesConsumer(SagaOrdersEventHandler);
    }

    private async Task CreateData()
    {
        using StreamReader reader = new("./hotels.json");
        var json = await reader.ReadToEndAsync(Token);
        List<Hotel> hotels = JsonConvert.DeserializeObject<List<Hotel>>(json) ?? [];
        var rnd = new Random();
        foreach (var hotel in hotels)
        {
            List<RoomDb> dbRooms = [];
            dbRooms.AddRange(hotel.Rooms.Select(room => new RoomDb
            {
                Amount = rnd.Next(1, 5),
                Name = room.Name,
                MinPeople = room.People.Min,
                MaxPeople = room.People.Max,
                MaxAdults = room.Adults.Max,
                MinAdults = room.Adults.Min,
                MaxChildren = room.Children.Max,
                MinChildren = room.Children.Min,
                Max10yo = rnd.Next(0, room.Children.Max),
                MaxLesserChildren = rnd.Next(0, room.Children.Max / 2)
            }));
            _writeDb.Rooms.AddRange(dbRooms);
            _writeDb.Hotels.Add(new HotelDb
            {
                Name = hotel.Name,
                Country = hotel.Country,
                City = hotel.City,
                AirportCode = hotel.Airport.Code,
                AirportName = hotel.Airport.Name,
                Rooms = dbRooms,
            });
        }
        await _writeDb.SaveChangesAsync(Token);
    }
    
    private void Initialize()
    {
        JObject o1 = JObject.Parse(File.ReadAllText(@"c:\videogames.json"));

        // read JSON directly from a file
        using (StreamReader file = File.OpenText(@"c:\videogames.json"))
        using (JsonTextReader reader = new JsonTextReader(file))
        {
            JObject o2 = (JObject) JToken.ReadFrom(reader);
        }
    }

    /// <summary>
    /// Publishes made messages to the right queues
    /// </summary>
    private async Task RabbitPublisher()
    {
        while (await _publish.Reader.WaitToReadAsync(Token))
        {
            var message = await _publish.Reader.ReadAsync(Token);

            _queues.PublishToOrchestrator( _jsonUtils.Serialize(message));
        }
    }

    /// <summary>
    /// Event Handler that hooks to the event of the queue consumer.
    /// Handles incoming replies from the RabbitMQ and routes them to the appropriate tasks.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="ea"></param>
    private void SagaOrdersEventHandler(object? sender, BasicDeliverEventArgs ea)
    {
        _logger.Debug("Received response | Tag: {tag}", ea.DeliveryTag);
        var body = ea.Body.ToArray();

        var reply = _jsonUtils.Deserialize(body);

        if (reply == null) return;

        var message = reply.Value;

        // send message reply to the appropriate task
        var result = _payments.Writer.TryWrite(message);
        
        if (result) _logger.Debug("Replied routed successfuly to {type} handler", message.MessageType.ToString());
        else _logger.Warn("Something went wrong in routing to {type} handler", message.MessageType.ToString());

        _queues.PublishTagResponse(ea, result);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _logger.Debug("Disposing");
        _queues.Dispose();
    }
}