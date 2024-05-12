using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NLog;
using vgt_saga_hotel.Models;
using vgt_saga_serialization;
using vgt_saga_serialization.MessageBodies;

namespace vgt_saga_hotel.HotelService;

/// <summary>
/// Handles saga hotel requests
/// Creates the appropriate saga messages
/// Handles the data in messages
/// </summary>
public class HotelHandler
{
    /// <summary>
    /// Requests from the orchestrator
    /// </summary>
    public Channel<Message> Requests { get; }
    
    /// <summary>
    /// Messages that need to be sent out to the queues
    /// </summary>
    public Channel<Message> Publish { get; }
    
    private Logger _logger;

    private readonly HotelDbContext _writeDb;
    private readonly HotelDbContext _readDb;
    
    /// <summary>
    /// Task of the requests handler
    /// </summary>
    public Task RequestsTask { get; set; }

    /// <summary>
    /// Token allowing tasks cancellation from the outside of the class
    /// </summary>
    public CancellationToken Token { get; } = new();
    
    private SemaphoreSlim _concurencySemaphore = new SemaphoreSlim(6, 6);
    
    private SemaphoreSlim _dbReadLock = new SemaphoreSlim(1, 1);
    private SemaphoreSlim _dbWriteLock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Default constructor of the hotel handler class
    /// that handles data and prepares messages concerning saga hotel avaibility end booking
    /// </summary>
    /// <param name="requests"> Queue with the requests from the orchestrator </param>
    /// <param name="publish"> Queue with messages that need to be published to RabbitMQ </param>
    /// <param name="log"> logger to log to </param>
    public HotelHandler(Channel<Message> requests, Channel<Message> publish, HotelDbContext writeDb, HotelDbContext readDb, Logger log)
    {
        _logger = log;
        Requests = requests;
        Publish = publish;
        _writeDb = writeDb;
        _readDb = readDb;

        _logger.Debug("Starting tasks handling the messages");
        RequestsTask = Task.Run(HandleHotels);
        _logger.Debug("Tasks handling the messages started");
    }

    private async Task HandleHotels()
    {
        while (await Requests.Reader.WaitToReadAsync(Token))
        {
            var message = await Requests.Reader.ReadAsync(Token);

            await _concurencySemaphore.WaitAsync(Token);

            _ = message.State switch
            {
                SagaState.Begin => Task.Run(() => TempBookHotel(message), Token),
                SagaState.PaymentAccept => Task.Run(() => BookHotel(message), Token),
                SagaState.HotelFullRollback => Task.Run(() => FullRollback(message), Token),
                SagaState.HotelTimedRollback => Task.Run(() => TempRollback(message), Token),
                _ => null
            };
        }
    }

    private async Task TempBookHotel(Message message)
    {
        if (message.MessageType != MessageType.HotelRequest || message.Body == null) return;
        var requestBody = (HotelRequest)message.Body;
        
        
        await _dbReadLock.WaitAsync(Token);
        var available = _readDb.Bookings.Include(p => p.Room).Any(p => p.Room.Name.Equals(requestBody.RoomType) && true);
        var rnd = new Random();
        await Task.Delay(rnd.Next(0, 100), Token);
        var result = rnd.Next(0, 1) switch
        {
            1 => SagaState.PaymentAccept,
            _ => SagaState.PaymentFailed
        };
        
        message.MessageType = MessageType.PaymentReply;
        message.MessageId += 1;
        message.State = result;
        message.Body = new PaymentReply();
        message.CreationDate = DateTime.Now;
        
        await Publish.Writer.WriteAsync(message, Token);
        
        _concurencySemaphore.Release();
    }
    
    private async Task TempRollback(Message message)
    {
        var rnd = new Random();
        await Task.Delay(rnd.Next(0, 100), Token);
        var result = rnd.Next(0, 1) switch
        {
            1 => SagaState.PaymentAccept,
            _ => SagaState.PaymentFailed
        };
        
        message.MessageType = MessageType.PaymentReply;
        message.MessageId += 1;
        message.State = result;
        message.Body = new PaymentReply();
        message.CreationDate = DateTime.Now;
        
        await Publish.Writer.WriteAsync(message, Token);
        
        _concurencySemaphore.Release();
    }
    
    private async Task BookHotel(Message message)
    {
        var rnd = new Random();
        await Task.Delay(rnd.Next(0, 100), Token);
        var result = rnd.Next(0, 1) switch
        {
            1 => SagaState.PaymentAccept,
            _ => SagaState.PaymentFailed
        };
        
        message.MessageType = MessageType.PaymentReply;
        message.MessageId += 1;
        message.State = result;
        message.Body = new PaymentReply();
        message.CreationDate = DateTime.Now;
        
        await Publish.Writer.WriteAsync(message, Token);
        
        _concurencySemaphore.Release();
    }
    
    private async Task FullRollback(Message message)
    {
        var rnd = new Random();
        await Task.Delay(rnd.Next(0, 100), Token);
        var result = rnd.Next(0, 1) switch
        {
            1 => SagaState.PaymentAccept,
            _ => SagaState.PaymentFailed
        };
        
        message.MessageType = MessageType.PaymentReply;
        message.MessageId += 1;
        message.State = result;
        message.Body = new PaymentReply();
        message.CreationDate = DateTime.Now;
        
        await Publish.Writer.WriteAsync(message, Token);
        
        _concurencySemaphore.Release();
    }
}