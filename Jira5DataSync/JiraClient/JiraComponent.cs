using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Represents a JIRA component
    /// </summary>
    public class JiraComponent : BaseEntity
    {
        public JiraComponent()
        {
        }

        public JiraComponent(string id)
        {
            int idAsInt;
            if (Int32.TryParse(id, out idAsInt))
            {
                this.Id = idAsInt;
            }
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
