using System;

using Newtonsoft.Json;

namespace DrawSync.Models
{
    public class User
    {
        [JsonProperty("$id")]
        public string? Id { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; } = null!;
        
        [JsonProperty("email")]
        public string Email { get; set; } = null!;
        
        [JsonProperty("$createdAt")]
        public string CreatedAt { get; set; } = null!;

        [JsonProperty("$updatedAt")]
        public string UpdatedAt { get; set; } = null!;
    }
}
