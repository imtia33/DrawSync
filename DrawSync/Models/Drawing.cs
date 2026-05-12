using Newtonsoft.Json;

namespace DrawSync.Models
{
    public class Drawing
    {
        [JsonProperty("$id")]
        public string? Id { get; set; }

        [JsonProperty("organizationId")]
        public string OrganizationId { get; set; } = null!;

        [JsonProperty("name")]
        public string Name { get; set; } = null!;

        [JsonProperty("$createdAt")]
        public string CreatedAt { get; set; } = null!;

        [JsonProperty("$updatedAt")]
        public string UpdatedAt { get; set; } = null!;
    }
}
