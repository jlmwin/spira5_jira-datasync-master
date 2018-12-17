using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Data object that represents a JIRA Issue
    /// </summary>
    public class JiraIssue : BaseEntity
    {
        private string m_KeyString;

        [JsonProperty("expand", NullValueHandling = NullValueHandling.Ignore)]
        public string Expand { get; set; }

        #region Special key solution
        [JsonProperty("key", NullValueHandling = NullValueHandling.Ignore)]
        public string ProxyKey
        {
            get
            {
                if (Key == null)
                {
                    return null;
                }
                else
                {
                    return Key.ToString();
                }
            }
            set
            {
                m_KeyString = value;
            }
        }

        [JsonIgnore]
        public IssueKey Key
        {
            get
            {
                if (String.IsNullOrEmpty(m_KeyString))
                {
                    return null;
                }
                return IssueKey.Parse(m_KeyString);
            }
        }
        #endregion Special key solution

        [JsonProperty("fields")]
        public Fields Fields
        {
            get
            {
                if (this.fields == null)
                {
                    this.fields = new Fields();
                }
                return this.fields;
            }
            set
            {
                this.fields = value;
            }
        }
        private Fields fields;

        /// <summary>
        /// Contains all the JIRA custom fields, manually serialized
        /// </summary>
        [JsonIgnore]
        public List<JiraCustomFieldValue> CustomFieldValues
        {
            get
            {
                return this.customFieldValues;
            }
        }
        protected List<JiraCustomFieldValue> customFieldValues = new List<JiraCustomFieldValue>();
    }

    /// <summary>
    /// A class representing a JIRA issue key [PROJECT KEY]-[ISSUE ID]
    /// </summary>
    public class IssueKey
    {
        public string ProjectKey { get; set; }

        public int IssueId { get; set; }

        public IssueKey() { }
        public IssueKey(string projectKey, int issueId)
        {
            ProjectKey = projectKey;
            IssueId = issueId;
        }

        public static IssueKey Parse(string issueKeyString)
        {
            if (issueKeyString == null)
            {
                throw new ArgumentNullException("IssueKeyString is null!");
            }

            string[] split = issueKeyString.Split('-');

            if (split.Length != 2)
            {
                throw new ArgumentException("The string entered is not a JIRA key!");
            }

            int issueId = 0;
            if (!int.TryParse(split[1], out issueId))
            {
                throw new ArgumentException("The string entered could not be parsed, issue id is non-integer!");
            }

            return new IssueKey(split[0], issueId);
        }

        public override string ToString()
        {
            return string.Format("{0}-{1}", ProjectKey, IssueId);
        }
    }

    /// <summary>
    /// Contains JIRA fields
    /// </summary>
    public class Fields
    {
        [JsonProperty("summary", NullValueHandling = NullValueHandling.Ignore)]
        public string Summary { get; set; }

        [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
        public Comment Comment { get; set; }

        [JsonProperty("project", NullValueHandling = NullValueHandling.Ignore)]
        public JiraProject Project { get; set; }

        [JsonProperty("security", NullValueHandling = NullValueHandling.Ignore)]
        public SecurityLevel Security { get; set; }

        [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
        public Status Status { get; set; }

        [JsonProperty("assignee", NullValueHandling = NullValueHandling.Ignore)]
        public User Assignee { get; set; }

        [JsonProperty("created", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime Created { get; set; }

        [JsonProperty("reporter", NullValueHandling = NullValueHandling.Ignore)]
        public User Reporter { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("environment", NullValueHandling = NullValueHandling.Ignore)]
        public string Environment { get; set; }

        [JsonProperty("issuetype", NullValueHandling = NullValueHandling.Ignore)]
        public IssueType IssueType { get; set; }

        [JsonProperty("duedate", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? DueDate { get; set; }

        [JsonProperty("resolutiondate", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? ResolutionDate { get; set; }

        [JsonProperty("priority", NullValueHandling = NullValueHandling.Ignore)]
        public Priority Priority { get; set; }

        [JsonProperty("resolution", NullValueHandling = NullValueHandling.Ignore)]
        public Resolution Resolution { get; set; }

        [JsonProperty("versions", NullValueHandling = NullValueHandling.Ignore)]
        public List<JiraVersion> Versions { get; set; }

        [JsonProperty("fixVersions", NullValueHandling = NullValueHandling.Ignore)]
        public List<JiraVersion> FixVersions { get; set; }

        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)]
        public List<JiraComponent> Components { get; set; }

        [JsonProperty("attachment", NullValueHandling = NullValueHandling.Ignore)]
        public List<JiraAttachment> Attachments { get; set; }
    }

    /// <summary>
    /// Contains the JIRA issue comments
    /// </summary>
    public class Comment
    {
        [JsonProperty("startAt")]
        public int StartAt { get; set; }

        [JsonProperty("maxResults")]
        public int MaxResults { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("comments")]
        public List<JiraComment> Comments { get; set; }
    }

    public class SecurityLevel : BaseEntity
    {
        public SecurityLevel()
        {
        }

        public SecurityLevel(string id)
        {
            int idAsInt;
            if (Int32.TryParse(id, out idAsInt))
            {
                this.Id = idAsInt;
            }
        }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }

    public class Status : BaseEntity
    {
        public Status()
        {
        }

        public Status(string id)
        {
            int idAsInt;
            if (Int32.TryParse(id, out idAsInt))
            {
                this.Id = idAsInt;
            }
        }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("iconUrl", NullValueHandling = NullValueHandling.Ignore)]
        public string IconUrl { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }

    public class Resolution : BaseEntity
    {
        public Resolution()
        {
        }

        public Resolution(string id)
        {
            int idAsInt;
            if (Int32.TryParse(id, out idAsInt))
            {
                this.Id = idAsInt;
            }
        }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("iconUrl", NullValueHandling = NullValueHandling.Ignore)]
        public string IconUrl { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }

    public class IssueType : BaseEntity
    {
        public IssueType()
        {
        }

        public IssueType(string id)
        {
            int idAsInt;
            if (Int32.TryParse(id, out idAsInt))
            {
                this.Id = idAsInt;
            }
        }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("iconUrl", NullValueHandling = NullValueHandling.Ignore)]
        public string IconUrl { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("subtask", NullValueHandling = NullValueHandling.Ignore)]
        public bool SubTask { get; set; }

        [JsonProperty("fields", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Fields { get; set; }
    }

    public class Priority : BaseEntity
    {
        public Priority()
        {
        }

        public Priority(string id)
        {
            int idAsInt;
            if (Int32.TryParse(id, out idAsInt))
            {
                this.Id = idAsInt;
            }
        }

        [JsonProperty("iconUrl", NullValueHandling = NullValueHandling.Ignore)]
        public string IconUrl { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }
    }

    public class User : BaseEntity
    {
        public User()
        {
        }

        public User(string name)
        {
            this.Name = name;
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("emailAddress", NullValueHandling = NullValueHandling.Ignore)]
        public string EmailAddress { get; set; }

        [JsonProperty("avatarUrls", NullValueHandling = NullValueHandling.Ignore)]
        public AvatarUrls AvatarUrls { get; set; }

        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; }
    }

    public class AvatarUrls
    {
        [JsonProperty("16x16")]
        public string Size16 { get; set; }

        [JsonProperty("48x48")]
        public string Size48 { get; set; }
    }
}
