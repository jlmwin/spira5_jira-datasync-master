using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// The base JIRA client entity
    /// </summary>
    public class BaseEntity
    {
        [JsonIgnore]
        public int? Id { get; set; }

        /// <summary>
        /// Jira expects all IDs to be serialized as strings
        /// </summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string IdString
        {
            get
            {
                if (this.Id.HasValue)
                {
                    return this.Id.ToString();
                }
                else
                {
                    return null;
                }
            }
            set
            {
                int intValue;
                if (Int32.TryParse(value, out intValue))
                {
                    this.Id = intValue;
                }
                else
                {
                    this.Id = null;
                }
            }
        }
    }
}
