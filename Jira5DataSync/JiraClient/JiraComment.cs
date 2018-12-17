using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Represents a JIRA comment
    /// </summary>
    public class JiraComment : BaseEntity
    {
        [JsonProperty("body", NullValueHandling = NullValueHandling.Ignore)]
        public string Body { get; set; }

        [JsonProperty("author", NullValueHandling = NullValueHandling.Ignore)]
        public User Author { get; set; }

        [JsonProperty("updateAuthor", NullValueHandling = NullValueHandling.Ignore)]
        public User UpdateAuthor { get; set; }

        [JsonProperty("created", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? Created { get; set; }

        [JsonProperty("updated", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? Updated { get; set; }
    }
}
