using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Represents the issue creation meta-data (which projects, issue types and fields are available)
    /// </summary>
    public class JiraCreateMetaData
    {
        [JsonProperty("projects")]
        public List<JiraProject> Projects { get; set; }
    }
}
