using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;
namespace tramsac99.Areas.User.Controllers
{
    [Area("User")]
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;

        // Why changed: keep only ONE constructor so ASP.NET Core DI can resolve HomeController correctly.
        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Route()
        {
            return View();
        }

        // Why changed: keep contact page route for the user navbar.
        public IActionResult Contact()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> PlaceSuggest([FromQuery] string keyword)
        {
            keyword = (keyword ?? string.Empty).Trim();

            if (keyword.Length < 3)
            {
                return Json(Array.Empty<object>());
            }

            var apiKey = _configuration["Map4D:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Json(Array.Empty<object>());
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Why changed: fetch Map4D autosuggest on the server and normalize the payload for the route page.
            var url = $"https://api.map4d.vn/sdk/autosuggest?text={Uri.EscapeDataString(keyword)}&key={Uri.EscapeDataString(apiKey)}";

            using var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return Json(Array.Empty<object>());
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            var suggestions = ExtractSuggestions(document.RootElement)
                .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
                .Take(8)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Address,
                    x.DisplayName,
                    x.Lat,
                    x.Lng
                })
                .ToList();

            return Json(suggestions);
        }

        private static IEnumerable<SuggestItem> ExtractSuggestions(JsonElement root)
        {
            var items = FindSuggestionArray(root);

            if (items.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var item in items.EnumerateArray())
            {
                var name = FirstString(item, "name", "title", "text");
                var displayName = FirstString(item, "displayName", "display_name", "description");
                var address = FirstString(item, "address", "formattedAddress", "fullAddress", "description");
                var id = FirstString(item, "id", "refId", "ref_id", "placeId", "place_id");

                var (lat, lng) = ExtractLatLng(item);

                var finalName = !string.IsNullOrWhiteSpace(name)
                    ? name
                    : (!string.IsNullOrWhiteSpace(displayName) ? displayName : "Địa điểm");

                var finalAddress = !string.IsNullOrWhiteSpace(address)
                    ? address
                    : (!string.IsNullOrWhiteSpace(displayName) ? displayName : finalName);

                var finalDisplayName = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : finalAddress;

                yield return new SuggestItem
                {
                    Id = id,
                    Name = finalName,
                    Address = finalAddress,
                    DisplayName = finalDisplayName,
                    Lat = lat,
                    Lng = lng
                };
            }
        }

        private static JsonElement FindSuggestionArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root;
            }

            var candidates = new[] { "result", "results", "data", "items", "suggestions", "predictions" };

            foreach (var name in candidates)
            {
                if (root.TryGetProperty(name, out var value))
                {
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        return value;
                    }

                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var nestedName in candidates)
                        {
                            if (value.TryGetProperty(nestedName, out var nestedValue) && nestedValue.ValueKind == JsonValueKind.Array)
                            {
                                return nestedValue;
                            }
                        }
                    }
                }
            }

            return root;
        }

        private static string? FirstString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            return null;
        }

        private static (double? lat, double? lng) ExtractLatLng(JsonElement element)
        {
            double? lat = null;
            double? lng = null;

            if (TryGetDouble(element, "lat", out var directLat)) lat = directLat;
            if (TryGetDouble(element, "lng", out var directLng) || TryGetDouble(element, "lon", out directLng) || TryGetDouble(element, "longitude", out directLng)) lng = directLng;

            if (lat.HasValue && lng.HasValue)
            {
                return (lat, lng);
            }

            var nestedNames = new[] { "location", "coordinate", "coordinates", "geometry", "center" };
            foreach (var nestedName in nestedNames)
            {
                if (!element.TryGetProperty(nestedName, out var nested) || nested.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!lat.HasValue && (TryGetDouble(nested, "lat", out var nestedLat) || TryGetDouble(nested, "latitude", out nestedLat)))
                {
                    lat = nestedLat;
                }

                if (!lng.HasValue && (TryGetDouble(nested, "lng", out var nestedLng) || TryGetDouble(nested, "lon", out nestedLng) || TryGetDouble(nested, "longitude", out nestedLng)))
                {
                    lng = nestedLng;
                }

                if (lat.HasValue && lng.HasValue)
                {
                    return (lat, lng);
                }
            }

            return (lat, lng);
        }

        private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0;

            if (!element.TryGetProperty(propertyName, out var raw))
            {
                return false;
            }

            if (raw.ValueKind == JsonValueKind.Number)
            {
                return raw.TryGetDouble(out value);
            }

            if (raw.ValueKind == JsonValueKind.String)
            {
                return double.TryParse(raw.GetString(), out value);
            }

            return false;
        }

        private sealed class SuggestItem
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Address { get; set; }
            public string? DisplayName { get; set; }
            public double? Lat { get; set; }
            public double? Lng { get; set; }
        }
    }
}
