using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    public class JiraSearchRequest
    {
        [JsonProperty("jql")]
        public string JQL { get; set; }

        [JsonProperty("startAt")]
        public int StartAt { get; set; }

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; }

        [JsonProperty("fields")]
        public List<string> Fields { get; set; }

        public JiraSearchRequest()
        {
            Fields = new List<string>();
        }
    }
}
