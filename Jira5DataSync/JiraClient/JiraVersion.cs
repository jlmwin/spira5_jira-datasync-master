using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Represents a JIRA version
    /// </summary>
    public class JiraVersion : BaseEntity
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("releaseDate", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? ReleaseDate { get; set; }

        [JsonProperty("archived")]
        public bool Archived { get; set; }

        [JsonProperty("released")]
        public bool Released { get; set; }

        [JsonProperty("overdue", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Overdue { get; set; }

        [JsonProperty("project")]
        public string Project { get; set; }
    }
}
