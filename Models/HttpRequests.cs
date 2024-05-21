using Newtonsoft.Json;

namespace vgt_saga_hotel.Models;

/// <summary>
/// Describes travel date range.
/// </summary>
public class TravelDateRange : Dictionary<string, string>
{
    [JsonProperty("end")]
    public string End
    {
        get => this["end"];
        set => this["end"] = value;
    }

    [JsonProperty("start")]
    public string Start
    {
        get => this["start"];
        set => this["start"] = value;
    }

    public  DateTime EndDt()
    {
        return DateTime.Parse(End).ToUniversalTime();
    }
    
    public  DateTime StartDt()
    {
        return DateTime.Parse(Start).ToUniversalTime();
    }

    public static TravelDateRange GetExample()
    {
        return new TravelDateRange
        {
            Start = "01-05-2024",
            End = "01-01-2025"
        };
    }
}

public class HotelsRequest
{
    [JsonProperty("dates")]
    public TravelDateRange Dates { get; set; }
    
    // If null, all cities are considered
    [JsonProperty("cities")]
    public List<string>? Cities { get; set; }
    
    [JsonProperty("participants")]
    public Dictionary<int, int> Participants { get; set; }
}

public class HotelRequest
{
    [JsonProperty("hotel_id")]
    public string HotelId { get; set; }
    
    [JsonProperty("room_id")]
    public string RoomId { get; set; }
    
    [JsonProperty("participants")]
    public Dictionary<int, int> Participants { get; set; }
    
    [JsonProperty("dates")]
    public TravelDateRange Dates { get; set; }
}