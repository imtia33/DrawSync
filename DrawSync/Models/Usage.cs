using Newtonsoft.Json;

namespace DrawSync.Models
{
    public class Usage
    {
        [JsonProperty("$id")]
        public string? Id { get; set; }

        [JsonProperty("organizationId")]
        public string OrganizationId { get; set; } = null!;

        [JsonProperty("drawingsCount")]
        public int DrawingsCount { get; set; }

        [JsonProperty("collaborators")]
        public int Collaborators { get; set; }

        [JsonProperty("renewDate")]
        public string RenewDate { get; set; } = null!; // Next billing cycle date

        [JsonProperty("$createdAt")]
        public string CreatedAt { get; set; } = null!;

        [JsonProperty("$updatedAt")]
        public string UpdatedAt { get; set; } = null!;
    }
}
