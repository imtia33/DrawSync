using Newtonsoft.Json;

namespace DrawSync.Models
{
    public class Invoice
    {
        [JsonProperty("$id")]
        public string? Id { get; set; }

        [JsonProperty("organizationId")]
        public string OrganizationId { get; set; } = null!;

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("currency")]
        public string Currency { get; set; } = "USD";

        [JsonProperty("status")]
        public string Status { get; set; } = "paid"; // paid, pending, failed

        [JsonProperty("period")]
        public string Period { get; set; } = null!; // "2024-05"

        [JsonProperty("$createdAt")]
        public string CreatedAt { get; set; } = null!;

        [JsonProperty("$updatedAt")]
        public string UpdatedAt { get; set; } = null!;
    }
}
