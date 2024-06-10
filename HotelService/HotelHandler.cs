using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NLog;
using vgt_saga_hotel.Models;
using vgt_saga_serialization;
using vgt_saga_serialization.MessageBodies;
using HotelRequest = vgt_saga_serialization.MessageBodies.HotelRequest;

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
                SagaState.HotelTimedRollback => Task.Run(() => TempRollback(message), Token),
                _ => null
            };
        }
    }

    private async Task TempBookHotel(Message message)
    {
        if (message.MessageType != MessageType.HotelRequest || message.Body == null) return;
        var requestBody = (HotelRequest)message.Body;
        
        await _dbWriteLock.WaitAsync(Token);
        await using var transaction = await _writeDb.Database.BeginTransactionAsync(Token);

        var room = await _writeDb.Rooms
            .Include(p=>p.Hotel)
            .FirstOrDefaultAsync(p => p.Name.Equals(requestBody.RoomType) 
                                      && p.Hotel.Name.Equals(requestBody.HotelName), Token);
        var hotel = await _writeDb.Hotels
            .FirstOrDefaultAsync(p => p.Name.Equals(requestBody.HotelName), Token);
        if (room == null || hotel == null)
        {
            await transaction.RollbackAsync(Token);

            message.MessageId += 1;
            message.MessageType = MessageType.PaymentRequest;
            message.State = SagaState.HotelTimedFail;
            message.Body = new PaymentRequest();
            
            await Publish.Writer.WriteAsync(message, Token);
            _dbWriteLock.Release();
            _concurencySemaphore.Release();
            return;
        }
        
        var booked = _writeDb.Bookings
            .Include(p => p.Room)
            .Include(p => p.Hotel)
            .Where(p => p.Room == room
                   && p.Hotel == hotel
                   && p.BookFrom < requestBody.BookTo
                   && p.BookTo > requestBody.BookFrom);
        var count = booked.Count();

        if (count < room.Amount)
        {
            _writeDb.Bookings.Add(new Booking
            {
                Hotel = hotel,
                Room = room,
                TransactionId = message.TransactionId,
                Temporary = 1,
                TemporaryDt = DateTime.Now,
                BookFrom = requestBody.BookFrom.Value,
                BookTo = requestBody.BookTo.Value
            });
            await transaction.CommitAsync(Token);
            await _readDb.SaveChangesAsync(Token);

            message.MessageId += 1;
            message.MessageType = MessageType.PaymentRequest;
            message.State = SagaState.HotelTimedAccept;
            message.Body = new PaymentRequest();
            message.CreationDate = DateTime.Now;
            
            await Publish.Writer.WriteAsync(message, Token);
            _dbWriteLock.Release();
            _concurencySemaphore.Release();
            return;
        }
        var temporary =
            booked.Where(p => p.Temporary == 1 
                              && DateTime.Now - p.TemporaryDt > TimeSpan.FromMinutes(1));
        if (count - temporary.Count() >= room.Amount)
        {
            await transaction.RollbackAsync(Token);

            message.MessageId += 1;
            message.MessageType = MessageType.PaymentRequest;
            message.State = SagaState.HotelTimedFail;
            message.Body = new PaymentRequest();
            message.CreationDate = DateTime.Now;
            
            await Publish.Writer.WriteAsync(message, Token);
            _dbWriteLock.Release();
            _concurencySemaphore.Release();
            return;
        }

        await temporary.ExecuteDeleteAsync(Token);
        _writeDb.Bookings.Add(new Booking
        {
            Hotel = hotel,
            Room = room,
            TransactionId = message.TransactionId,
            Temporary = 1,
            TemporaryDt = DateTime.Now,
            BookFrom = requestBody.BookFrom.Value,
            BookTo = requestBody.BookTo.Value
        });
        await transaction.CommitAsync(Token);
        await _readDb.SaveChangesAsync(Token);
        
        
        message.MessageId += 1;
        message.MessageType = MessageType.PaymentRequest;
        message.State = SagaState.HotelTimedAccept;
        message.Body = new PaymentRequest();
        message.CreationDate = DateTime.Now;
            
        await Publish.Writer.WriteAsync(message, Token);
        _dbWriteLock.Release();
        _concurencySemaphore.Release();
    }
    
    private async Task TempRollback(Message message)
    {
        await _dbReadLock.WaitAsync(Token);
        await using var transaction = await _readDb.Database.BeginTransactionAsync(Token);

        var booked = _readDb.Bookings
            .Where(p => p.TransactionId == message.TransactionId);

        if (booked.Any())
        {
            await booked.ExecuteDeleteAsync(Token);
        }
        await transaction.CommitAsync(Token);
        await _readDb.SaveChangesAsync(Token);
       
        
        message.MessageType = MessageType.OrderReply;
        message.MessageId += 1;
        message.State = SagaState.HotelTimedRollback;
        message.Body = new HotelReply();
        message.CreationDate = DateTime.Now;
        
        await Publish.Writer.WriteAsync(message, Token);
        _dbReadLock.Release();
        _concurencySemaphore.Release();
    }
    
    private async Task BookHotel(Message message)
    {
        _logger.Debug("Running BookHotel");
        await _dbReadLock.WaitAsync(Token);
        await using var transaction = await _readDb.Database.BeginTransactionAsync(Token);

        _logger.Debug("Started transaction");
        
        var booking = await _readDb.Bookings
            .FirstOrDefaultAsync(p => p.TransactionId == message.TransactionId);

        _logger.Debug("Received booking from db {d}", JsonConvert.SerializeObject(booking));
        
        if (booking != null)
        {
            _logger.Debug("Changing temporary status");
            booking.Temporary = 0;
            await transaction.CommitAsync(Token);
            await _readDb.SaveChangesAsync(Token);
            
                
            _logger.Debug("Creating positive response");
            message.MessageType = MessageType.OrderReply;
            message.MessageId += 1;
            message.State = SagaState.HotelFullAccept;
            message.Body = new HotelReply();
            message.CreationDate = DateTime.Now;
        
            _logger.Debug("Routing positive response {m}", JsonConvert.SerializeObject(message));
            await Publish.Writer.WriteAsync(message, Token);
            _dbReadLock.Release();
            _concurencySemaphore.Release();
        }
        
        _logger.Debug("Transaction rollback");
        await transaction.RollbackAsync(Token);
        
        _logger.Debug("Creating Fail request");
        message.MessageType = MessageType.OrderReply;
        message.MessageId += 1;
        message.State = SagaState.HotelFullFail;
        message.Body = new HotelReply();
        message.CreationDate = DateTime.Now;
        
        _logger.Debug("Routing positive response {m}", JsonConvert.SerializeObject(message));
        await Publish.Writer.WriteAsync(message, Token);
        _dbReadLock.Release();
        _concurencySemaphore.Release();
    }
}