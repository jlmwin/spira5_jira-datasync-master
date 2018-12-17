using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    public class JiraAvatarUrls
    {
        [JsonProperty("16x16")]
        public string Size16 { get; set; }

        [JsonProperty("48x48")]
        public string Size48 { get; set; }
    }
}
