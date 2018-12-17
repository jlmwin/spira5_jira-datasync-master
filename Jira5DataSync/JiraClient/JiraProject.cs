using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    public class JiraProject : BaseEntity
    {
        public JiraProject()
        {
        }

        public JiraProject(string key)
        {
            this.Key = key;
        }

        public JiraProject(int id)
        {
            this.Id = id;
        }

        [JsonProperty("key", NullValueHandling = NullValueHandling.Ignore)]
        public string Key { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("avatarUrls", NullValueHandling = NullValueHandling.Ignore)]
        public JiraAvatarUrls AvatarUrls { get; set; }

        [JsonProperty("issuetypes", NullValueHandling = NullValueHandling.Ignore)]
        public List<IssueType> IssueTypes { get; set; }
    }
}
