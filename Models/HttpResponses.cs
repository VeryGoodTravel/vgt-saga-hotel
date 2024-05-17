using Newtonsoft.Json;

namespace vgt_saga_hotel.Models;

public class RoomHttp
{
    [JsonProperty("room_id")]
    public string RoomId { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("price_per_person")]
    public double Price { get; set; }
}

public class HotelHttp
{
    [JsonProperty("hotel_id")]
    public string HotelId { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("city")]
    public string City { get; set; }
    
    [JsonProperty("country")]
    public string Country { get; set; }
    
    [JsonProperty("rooms")]
    public List<RoomHttp> Rooms { get; set; }
}

/// <summary>
/// Describes travel location, both origins and destinations.
/// </summary>
public class TravelLocation
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("label")]
    public string Label { get; set; }

    [JsonProperty("locations", NullValueHandling = NullValueHandling.Ignore)]
    public TravelLocation[] Locations { get; set; }
        
    public static TravelLocation GetExample()
    {
        return new TravelLocation
        {
            Id = "1",
            Label = "Germany",
            Locations = new TravelLocation[]
            {
                new TravelLocation
                {
                    Id = "2",
                    Label = "Berlin",
                },
                new TravelLocation
                {
                    Id = "5",
                    Label = "Munich",
                }
            }
        };
    }
}

public class HotelsResponse
{
    [JsonProperty("hotels")]
    public List<HotelHttp> Hotels { get; set; }
}

public class HotelResponse : HotelHttp
{
    
}

public class LocationsResponse
{
    // Combinations of Country and City
    // Id and Label should be the same
    [JsonProperty("locations")]
    public List<TravelLocation> Locations { get; set; }
}