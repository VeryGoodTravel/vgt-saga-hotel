using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NLog;
using NLog.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using vgt_saga_hotel;
using vgt_saga_hotel.HotelService;
using vgt_saga_hotel.Models;
using ILogger = NLog.ILogger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
try
{
    builder.Configuration.AddJsonFile("appsettings.json", false, true).AddEnvironmentVariables().Build();
}
catch (InvalidDataException e)
{
    Console.WriteLine(e);
    Environment.Exit(0);
}


try
{
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
    var options = new NLogProviderOptions
    {
        AutoShutdown = true
    };
    options.Configure(builder.Configuration.GetSection("NLog"));
    builder.Logging.AddNLog(options);
}
catch (InvalidDataException e)
{
    Console.WriteLine(e);
    Environment.Exit(0);
}

var logger = LogManager.GetCurrentClassLogger();
var dboptions = new DbContextOptionsBuilder<HotelDbContext>();
dboptions.UseNpgsql(SecretUtils.GetConnectionString(builder.Configuration, "DB_NAME_HOTEL", logger));
builder.Services.AddDbContext<HotelDbContext>(options => options.UseNpgsql(SecretUtils.GetConnectionString(builder.Configuration, "DB_NAME_HOTEL", logger)));

var app = builder.Build();

var lf = app.Services.GetRequiredService<ILoggerFactory>();
logger.Info("Hello word");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

await using var scope = app.Services.CreateAsyncScope();
{
    await using var db = scope.ServiceProvider.GetService<HotelDbContext>();
    {
        logger.Info("CAN CONNECT {v}" ,db.Database.CanConnect());
        db.Database.EnsureDeleted();
        await db.Database.MigrateAsync();
    }
}


app.UseHttpsRedirection();


HotelService? hotelService = null;

try
{
    hotelService = new HotelService(app.Configuration, lf);
}
catch (BrokerUnreachableException)
{
    GracefulExit(app, logger, [hotelService]);
}
catch (ArgumentException)
{
    GracefulExit(app, logger, [hotelService]);
}


app.MapGet("/hotels", (HotelsRequest request) =>
    {
        using var scope = app.Services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetService<HotelDbContext>();


        var dbRooms = from rooms in db.Rooms
            where rooms.MaxAdults >= request.Participants[4]
                  && rooms.MinAdults <= request.Participants[4]
                  && rooms.MaxChildren >= request.Participants[3]
                  && rooms.MinChildren <= request.Participants[3]
                  && rooms.Max10yo >= request.Participants[2]
                  && rooms.MaxLesserChildren >= request.Participants[1]
                  && request.Cities.Contains(rooms.Hotel.City)
                  && request.Cities.Contains(rooms.Hotel.City)
            join booking in db.Bookings on rooms equals booking.Room 
            where booking.BookFrom > request.Dates.EndDt()
                && booking.BookTo > request.Dates.StartDt()
            group booking by rooms into g
            where g.Count() < g.Key.Amount
            select g.Key;

        var Dbhotels = new Dictionary<HotelDb, List<RoomDb>>();

        foreach (var room in dbRooms)
        {
            if (Dbhotels.TryGetValue(room.Hotel, out var roms))
            {
                roms.Add(room);
            }
            else
            {
                Dbhotels[room.Hotel] = [room];
            }
            
        }

        var hotels = new List<HotelHttp>();
        foreach (var roomDb in Dbhotels)
        {
            List<RoomHttp> rooms = [];
            rooms.AddRange(roomDb.Value.Select(room => new RoomHttp { RoomId = room.RoomDbId.ToString(), Name = room.Name, Price = room.Price }));
            hotels.Add(new HotelHttp
            {
                HotelId = roomDb.Key.HotelDbId.ToString(),
                Name = roomDb.Key.Name,
                City = roomDb.Key.City,
                Country = roomDb.Key.Country,
                Rooms = rooms
            });
        }

        return JsonConvert.SerializeObject(hotels);
    })
    .WithName("Hotels")
    .WithOpenApi();

app.MapGet("/hotel", (HotelRequest request) =>
    {
        using var scope = app.Services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetService<HotelDbContext>();

        var id = int.Parse(request.HotelId);
        
        var dbHotel = db.Hotels.Include(p => p.Rooms).FirstOrDefault(p => id == p.HotelDbId);

        if (dbHotel == null) return "";

        List<RoomHttp> rooms = [];
        rooms.AddRange(dbHotel.Rooms.Select(room => new RoomHttp { RoomId = room.RoomDbId.ToString(), Name = room.Name, Price = room.Price }));

        var hotel = new HotelHttp
        {
            HotelId = dbHotel.HotelDbId.ToString(),
            Name = dbHotel.Name,
            City = dbHotel.City,
            Country = dbHotel.Country,
            Rooms = rooms
        };
        return JsonConvert.SerializeObject(hotel);
    })
    .WithName("Hotel")
    .WithOpenApi();

app.MapGet("/locations", () =>
    {
        using var scope = app.Services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetService<HotelDbContext>();

        var dbHotel = from hotels in db.Hotels
            select new { country = hotels.Country, city = hotels.City };
        var locations = new Dictionary<string, List<string>>();

        foreach (var location in dbHotel)
        {
            if (locations.TryGetValue(location.country, out var roms))
            {
                roms.Add(location.city);
            }
            else
            {
                locations[location.country] = [location.city];
            }

        }

        var travels = new List<TravelLocation>();

        foreach (var location in locations)
        {
            var cities = location.Value.Select(city => new TravelLocation { Id = city, Label = city, }).ToArray();
            travels.Add(new TravelLocation
            {
                Id = location.Key,
                Label = location.Key,
                Locations = cities
            });
        }
        
        
        return JsonConvert.SerializeObject(travels);
    })
    .WithName("Locations")
    .WithOpenApi();

app.Run();

return;

// dispose objects and close connections
void GracefulExit(WebApplication wA, ILogger log, List<IDisposable?> toDispose)
{
    foreach (var obj in toDispose)
    {
        obj?.Dispose();
    }

    try
    {
        wA.Lifetime.StopApplication();
        AwaitAppStop(wA).Wait();
    }
    catch (ObjectDisposedException)
    {
        log.Info("App already disposed off");
    }

    LogManager.Shutdown();
    Environment.Exit(0);
    throw new Exception("Kill the rest of the app");
}

async Task AwaitAppStop(WebApplication wA)
{
    await wA.StopAsync();
    await wA.DisposeAsync();
}

namespace vgt_saga_hotel
{
    internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
    {
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }
}

