using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Newtonsoft.Json;
using NLog;
using NLog.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using vgt_saga_hotel;
using vgt_saga_hotel.HotelService;
using vgt_saga_hotel.Models;
using ILogger = NLog.ILogger;

var builder = WebApplication.CreateBuilder(args);
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
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

// await using var scope = app.Services.CreateAsyncScope();
// {
//     await using var db = scope.ServiceProvider.GetService<HotelDbContext>();
//     {
//         logger.Info("CAN CONNECT {v}" ,db.Database.CanConnect());
//         //db.Database.EnsureDeleted();
//         await db.Database.MigrateAsync();
//         //db.Database.EnsureCreated();
//     }
// }


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


app.MapPost("/hotels", ([FromBody]HotelsRequest request) =>
    {
        using var scope = app.Services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetService<HotelDbContext>();

        logger.Info("Cities: {room}",  request.Cities);

        var dbRooms = from rooms in db.Rooms
            where rooms.MaxAdults >= request.Participants[4]
                  && rooms.MinAdults <= request.Participants[4]
                  && rooms.MaxChildren >= request.Participants[3]
                  && rooms.MinChildren <= request.Participants[3]
                  && rooms.Max10yo >= request.Participants[2]
                  && rooms.MaxLesserChildren >= request.Participants[1]
                  && (request.Cities.Any(p => p.Equals(rooms.Hotel.City)))
                  && (from m in db.Bookings 
                      where m.BookFrom > request.Dates.EndDt()
                            && m.BookTo > request.Dates.StartDt()
                            && m.Room == rooms select m).Count() < rooms.Amount
            select rooms;
            

        var Dbhotels = new Dictionary<HotelDb, List<RoomDb>>();

        var results = dbRooms.Include(p => p.Hotel).AsSplitQuery().AsNoTracking().Distinct().ToList();
        logger.Info("Found rooms: {room}",  results.Count);
        
        var hotelIds = results.Select(p => p.Hotel.HotelDbId).Distinct().ToList();
        
        logger.Info("hotel ids: {room}",  hotelIds.Count);
        
        var hotels = new List<HotelHttp>();
        foreach (var hotelId in hotelIds)
        {
            List<RoomHttp> rooms = [];
            rooms.AddRange(results.Where(p => p.Hotel.HotelDbId == hotelId).Select(room => new RoomHttp { RoomId = room.RoomDbId.ToString(), Name = room.Name, Price = room.Price }).ToList());
            var hot = results.First(p => p.Hotel.HotelDbId == hotelId).Hotel;
            hotels.Add(new HotelHttp
            {
                HotelId = hotelId.ToString(),
                Name = hot.Name,
                City = hot.City,
                Country = hot.Country,
                Rooms = rooms
            });
        }
        
        logger.Info("final hotels: {room}",  hotels.Count);

        return hotels;
    })
    .WithName("Hotels")
    .WithOpenApi();

app.MapPost("/hotel", handler: ([FromBody]HotelRequest request) =>
    {
        using var scope = app.Services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetService<HotelDbContext>();

        var id = int.Parse(request.HotelId);
        
        var dbHotel = db.Hotels.Include(p => p.Rooms).FirstOrDefault(p => id == p.HotelDbId);

        if (dbHotel == null) return null;

        List<RoomHttp> rooms = [];
        rooms.AddRange(dbHotel.Rooms.Select(room => new RoomHttp { RoomId = room.RoomDbId.ToString(), Name = room.Name, Price = room.Price }));

        HotelHttp hotel = new()
        {
            HotelId = dbHotel.HotelDbId.ToString(),
            Name = dbHotel.Name,
            City = dbHotel.City,
            Country = dbHotel.Country,
            Rooms = rooms
        };
        
        return hotel;
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

        foreach (var location in locations.Distinct())
        {
            var cities = location.Value.Select(city => new TravelLocation { Id = city, Label = city, }).ToArray();
            travels.Add(new TravelLocation
            {
                Id = location.Key,
                Label = location.Key,
                Locations = cities.DistinctBy(p => p.Id).ToArray()
            });
        }
        
        
        return JsonConvert.SerializeObject(travels.Distinct());
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

