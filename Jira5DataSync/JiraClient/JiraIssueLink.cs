using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Represents a link to a JIRA issue
    /// </summary>
    public class JiraIssueLink : BaseEntity
    {
        [JsonProperty("key", NullValueHandling = NullValueHandling.Ignore)]
        public string Key { get; set; }
    }
}
