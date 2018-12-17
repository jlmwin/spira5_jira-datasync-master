using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Represents the result from uploading an attachment
    /// </summary>
    public class JiraAttachment
    {
        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("created")]
        public DateTime Created { get; set; }

        [JsonProperty("author")]
        public User Author { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("content")]
        public string ContentUrl { get; set; }
    }
}
