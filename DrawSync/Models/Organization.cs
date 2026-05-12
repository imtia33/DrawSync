using Newtonsoft.Json;

namespace DrawSync.Models
{
    public class Organization
    {
        [JsonProperty("$id")]
        public string? Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = null!;

        [JsonProperty("plan")]
        public string Plan { get; set; } = "free";

        [JsonProperty("$createdAt")]
        public string CreatedAt { get; set; } = null!;

        [JsonProperty("$updatedAt")]
        public string UpdatedAt { get; set; } = null!;

        [JsonProperty("createdBy")]
        public string CreatedBy { get; set; } = null!;
    }
}
