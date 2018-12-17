using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using Inflectra.SpiraTest.PlugIns.Jira5DataSync.UploadHelper;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    public class JiraManager
    {
        //We're currently using v2 of the JIRA REST API
        public const string API_PATH = "/rest/api/2/";

        private string baseUrl;
        private string username;
        private string password;

        //Logging functions
        EventLog eventLog = null;
        private bool traceLogging = false;

        /// <summary>
        /// Enumaration of supported JIRA resources
        /// </summary>
        public enum JiraResource
        {
            project,
            search,
            issue,
            version,
            mypermissions,
            issueLink
        }

        /// <summary>
        /// Should we use default credentials
        /// </summary>
        public bool UseDefaultCredentials
        {
            get;
            set;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="baseUrl">The base JIRA URL</param>
        /// <param name="username">The JIRA login</param>
        /// <param name="password">The JIRA password</param>
        /// <param name="eventLog">The Windows Event Log</param>
        public JiraManager(string baseUrl, string username, string password, EventLog eventLog = null, bool traceLogging = false)
        {
            this.username = username;
            this.password = password;
            this.baseUrl = baseUrl;
            this.UseDefaultCredentials = false;
            this.eventLog = eventLog;
            this.traceLogging = traceLogging;
        }

        /// <summary>
        /// Returns the meta-data for all projects for which fields are available for creating a new issue (to avoid validation errors)
        /// </summary>
        /// <param name="projectKey">Optionally filter by project</param>
        /// <remarks>We log the meta-data with a Success Audit message if trace-loggig is enabled</remarks>
        public JiraCreateMetaData GetCreateMetaData(string projectKey = null)
        {
            string argument = "createmeta?expand=projects.issuetypes.fields";
            if (!String.IsNullOrEmpty(projectKey))
            {
                argument += "&projectKeys=" + projectKey;
            }
            string json = RunQuery(JiraResource.issue, argument: argument, method: "GET");
            LogTraceEvent(this.eventLog, json, EventLogEntryType.SuccessAudit);
            return JsonConvert.DeserializeObject<JiraCreateMetaData>(json);
        }

        /// <summary>
        /// Gets the list of JIRA projects that the current user has access to
        /// </summary>
        /// <returns></returns>
        public List<JiraProject> GetProjects()
        {
            string projectsString = RunQuery(JiraResource.project);
            return JsonConvert.DeserializeObject<List<JiraProject>>(projectsString);
        }

        /// <summary>
        /// Adds an issue link between two JIRA issues
        /// </summary>
        /// <param name="issueLinkType">The name of the type of issue link (e.g. 'Duplicate')</param>
        /// <param name="sourceIssueKey">The key of the source issue (e.g. DEMO-1)</param>
        /// <param name="destIssueKey">The key of the destination issue (e.g. DEMO-2)</param>
        /// <param name="comment">The comment for the association</param>
        public void AddIssueLink(string issueLinkType, string sourceIssueKey, string destIssueKey, string comment)
        {
            //Create the serialized object
            JObject jIssueLink = new JObject();
            jIssueLink["type"] = new JObject();
            jIssueLink["type"]["name"] = issueLinkType;
            jIssueLink["inwardIssue"] = new JObject();
            jIssueLink["inwardIssue"]["key"] = sourceIssueKey;
            jIssueLink["outwardIssue"] = new JObject();
            jIssueLink["outwardIssue"]["key"] = destIssueKey;
            jIssueLink["comment"] = new JObject();
            jIssueLink["comment"]["body"] = comment;

            string json = JsonConvert.SerializeObject(jIssueLink);
            LogTraceEvent(this.eventLog, json, EventLogEntryType.Information);
            json = RunQuery(JiraResource.issueLink, null, json, "POST");
        }

        /// <summary>
        /// Adds a remote link to a JIRA issue
        /// </summary>
        /// <param name="issueKey">The key of the issue we're adding the link to(e.g. DEMO-1)</param>
        /// <param name="url">The URL for the association</param>
        /// <param name="label">The label for the link</param>
        public void AddWebLink(string issueKey, string url, string label)
        {
            //Create the serialized object
            JObject jWebLink = new JObject();
            jWebLink["object"] = new JObject();
            jWebLink["object"]["url"] = url;
            jWebLink["object"]["title"] = label;

            string json = JsonConvert.SerializeObject(jWebLink);
            LogTraceEvent(this.eventLog, json, EventLogEntryType.Information);
            json = RunQuery(JiraResource.issue, issueKey + "/remotelink", json, "POST");
        }

        /// <summary>
        /// Adds a JIRA Version into the system
        /// </summary>
        /// <param name="jiraVersion">The version object</param>
        public JiraVersion AddVersion(JiraVersion jiraVersion)
        {
            string json = JsonConvert.SerializeObject(jiraVersion);
            LogTraceEvent(this.eventLog, json, EventLogEntryType.Information);
            json = RunQuery(JiraResource.version, null, json, "POST");

            JiraVersion jiraVersionWithId = JsonConvert.DeserializeObject<JiraVersion>(json);
            return jiraVersionWithId;
        }

        /// <summary>
        /// Looks up a JIRA custom field option id from its name
        /// </summary>
        /// <param name="name">The name of the custom field option value</param>
        /// <param name="jIssueTypeFields">The meta-data for the fields</param>
        /// <returns>The id</returns>
        private int? LookupCustomFieldOptionId(string name, JObject jIssueTypeFields, string customFieldName)
        {
            //We need to locate the field in question
            if (jIssueTypeFields != null)
            {
                JObject customFieldDefinition = (JObject)jIssueTypeFields[customFieldName];
                if (customFieldDefinition != null)
                {
                    JArray allowedValues = (JArray)customFieldDefinition["allowedValues"];
                    if (allowedValues != null)
                    {
                        foreach (JObject allowedValue in allowedValues)
                        {
                            if (allowedValue["value"].Value<string>() == name)
                            {
                                string idString = allowedValue["id"].Value<string>();
                                int id;
                                if (!String.IsNullOrEmpty(idString) && Int32.TryParse(idString, out id))
                                {
                                    return id;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Looks up a JIRA custom field option name from its id
        /// </summary>
        /// <param name="jIssueTypeFields">The meta-data for the fields</param>
        /// <param name="id">The id of the custom field option value</param>
        /// <returns>The name</returns>
        private string LookupCustomFieldOptionName(int id, JObject jIssueTypeFields, string customFieldName)
        {
            //We need to locate the field in question
            if (jIssueTypeFields != null)
            {
                JObject customFieldDefinition = (JObject)jIssueTypeFields[customFieldName];
                if (customFieldDefinition != null)
                {
                    JArray allowedValues = (JArray)customFieldDefinition["allowedValues"];
                    if (allowedValues != null)
                    {
                        foreach (JObject allowedValue in allowedValues)
                        {
                            if (allowedValue["id"] != null && allowedValue["id"].Type == JTokenType.String && allowedValue["value"] != null && allowedValue["value"].Type == JTokenType.String)
                            {
                                if (allowedValue["id"].Value<string>() == id.ToString())
                                {
                                    string optionName = allowedValue["value"].Value<string>();
                                    if (!String.IsNullOrEmpty(optionName))
                                    {
                                        return optionName;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a JIRA Issue into the system
        /// </summary>
        /// <param name="jiraIssue">The issue object</param>
        /// <param name="jiraCreateMetaData">The creation meta-data</param>
        /// 
        public JiraIssueLink CreateIssue(JiraIssue jiraIssue, JiraCreateMetaData jiraCreateMetaData)
        {
            //First convert from the JiraIssue to a generic json object
            JObject jsonIssue = JObject.FromObject(jiraIssue);

            JObject jsonIssueFields = (JObject)jsonIssue["fields"];
            JObject jIssueTypeFields = null;

            //Next validate the fields
            List<string> messages = new List<string>();
            //Find the current project and issue type in the meta-data
            IssueType issueType = null;
            if (jiraCreateMetaData != null && jsonIssueFields != null)
            {
                JiraProject project = jiraCreateMetaData.Projects.FirstOrDefault(p => p.Id == jiraIssue.Fields.Project.Id);
                if (project != null)
                {
                    issueType = project.IssueTypes.FirstOrDefault(t => t.Id == jiraIssue.Fields.IssueType.Id);
                    if (issueType != null)
                    {
                        //See if we are missing any required properties (not custom fields at this point)
                        jIssueTypeFields = issueType.Fields;
                        foreach (KeyValuePair<string, JToken> field in issueType.Fields)
                        {
                            string fieldName = field.Key;
                            JToken fieldVaue = field.Value;
                            bool required = fieldVaue["required"].Value<bool>();
                            if (required && !fieldName.Contains(JiraCustomFieldValue.CUSTOM_FIELD_PREFIX) && !jsonIssueFields.Properties().Any(p => p.Name == fieldName))
                            {
                                messages.Add(String.Format("JIRA field '{0}' is required for project '{1}' and issue type {2} but was not provided.", fieldName, jiraIssue.Fields.Project.Key, jiraIssue.Fields.IssueType.Id));
                            }
                        }

                        //Remove any properties that are not listed in the meta-data (never issue-type)
                        List<string> fieldsToRemove = new List<string>();
                        foreach (KeyValuePair<string, JToken> field in jsonIssueFields)
                        {
                            string fieldName = field.Key;
                            if (issueType.Fields[fieldName] == null && fieldName != "issuetype")
                            {
                                fieldsToRemove.Add(fieldName);  //Remove the property

                            }
                        }

                        //Now do the removes
                        foreach (string fieldToRemove in fieldsToRemove)
                        {
                            jsonIssueFields.Remove(fieldToRemove);
                        }
                    }
                }
            }

            //Throw an exception if we have any messages
            if (messages.Count > 0)
            {
                throw new ApplicationException(String.Join(" \n", messages));
            }

            //Now we need to add the custom properties, which are not automatically serialized
            foreach (JiraCustomFieldValue jiraCustomFieldValue in jiraIssue.CustomFieldValues)
            {
                if (jiraCustomFieldValue.Value != null)
                {
                    int id = jiraCustomFieldValue.CustomFieldId;
                    string fieldName = jiraCustomFieldValue.CustomFieldName;

                    //Make sure JIRA expects this field for this issue type
                    if (issueType != null && issueType.Fields[fieldName] != null)
                    {
                        //Add to the issue json fields, handling the appropriate types correctly
                        switch (jiraCustomFieldValue.Value.CustomPropertyType)
                        {
                            case CustomPropertyValue.CustomPropertyTypeEnum.Boolean:
                                if (jiraCustomFieldValue.Value.BooleanValue.HasValue)
                                {
                                    jsonIssueFields.Add(fieldName, jiraCustomFieldValue.Value.BooleanValue.Value);
                                }
                                break;

                            case CustomPropertyValue.CustomPropertyTypeEnum.Date:
                                if (jiraCustomFieldValue.Value.DateTimeValue.HasValue)
                                {
                                    jsonIssueFields.Add(fieldName, jiraCustomFieldValue.Value.DateTimeValue.Value);
                                }
                                break;

                            case CustomPropertyValue.CustomPropertyTypeEnum.Decimal:
                                if (jiraCustomFieldValue.Value.DecimalValue.HasValue)
                                {
                                    jsonIssueFields.Add(fieldName, jiraCustomFieldValue.Value.DecimalValue.Value);
                                }
                                break;

                            case CustomPropertyValue.CustomPropertyTypeEnum.Integer:
                                if (jiraCustomFieldValue.Value.IntegerValue.HasValue)
                                {
                                    jsonIssueFields.Add(fieldName, jiraCustomFieldValue.Value.IntegerValue.Value);
                                }
                                break;

                            case CustomPropertyValue.CustomPropertyTypeEnum.List:
                                {
                                    //JIRA expects an object with an 'id' property set
                                    if (!String.IsNullOrEmpty(jiraCustomFieldValue.Value.StringValue))
                                    {
                                        //Need to lookup the id of the custom field option
                                        int? customFieldOptionId = LookupCustomFieldOptionId(jiraCustomFieldValue.Value.StringValue, jIssueTypeFields, jiraCustomFieldValue.CustomFieldName);
                                        if (customFieldOptionId.HasValue)
                                        {
                                            JObject jOption = new JObject();
                                            jOption["id"] = customFieldOptionId.Value.ToString();
                                            jsonIssueFields.Add(fieldName, jOption);
                                        }
                                    }
                                }
                                break;

                            case CustomPropertyValue.CustomPropertyTypeEnum.MultiList:
                                {
                                    //JIRA expects an array of objects with an 'id' property set
                                    if (jiraCustomFieldValue.Value.MultiListValue.Count > 0)
                                    {
                                        JArray jOptions = new JArray();
                                        foreach (string optionName in jiraCustomFieldValue.Value.MultiListValue)
                                        {
                                            //Need to lookup the id of the custom field option
                                            int? customFieldOptionId = LookupCustomFieldOptionId(optionName, jIssueTypeFields, jiraCustomFieldValue.CustomFieldName);
                                            if (customFieldOptionId.HasValue)
                                            {
                                                JObject jOption = new JObject();
                                                jOption["id"] = customFieldOptionId.Value.ToString();
                                                jOptions.Add(jOption);
                                            }
                                        }
                                        jsonIssueFields.Add(fieldName, jOptions);
                                    }
                                }
                                break;

                            case CustomPropertyValue.CustomPropertyTypeEnum.Text:
                                {
                                    if (!String.IsNullOrEmpty(jiraCustomFieldValue.Value.StringValue))
                                    {
                                        jsonIssueFields.Add(fieldName, jiraCustomFieldValue.Value.StringValue);
                                    }
                                }
                                break;

                            case CustomPropertyValue.CustomPropertyTypeEnum.User:
                                {
                                    //JIRA expects a user object with an 'name' property set
                                    if (!String.IsNullOrEmpty(jiraCustomFieldValue.Value.StringValue))
                                    {
                                        JObject jUser = new JObject();
                                        jUser["name"] = jiraCustomFieldValue.Value.StringValue;
                                        jsonIssueFields.Add(fieldName, jUser);
                                    }
                                }
                                break;
                        }
                    }
                }
            }

            string json = JsonConvert.SerializeObject(jsonIssue, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            LogTraceEvent(this.eventLog, json, EventLogEntryType.Information);
            json = RunQuery(JiraResource.issue, null, json, "POST");

            JiraIssueLink jiraIssueLink = JsonConvert.DeserializeObject<JiraIssueLink>(json);
            return jiraIssueLink;
        }

        /// <summary>
        /// Adds an attachment to the issue
        /// </summary>
        /// <param name="issueKey">The JIRA issue key</param>
        /// <param name="filename">The filename</param>
        /// <param name="binaryData">The contents of the file</param>
        /// <returns>The attachment definition</returns>
        public List<JiraAttachment> AddAttachmentsToIssue(string issueKey, string filename, byte[] binaryData)
        {
            string json = RunMultiPartQuery(JiraResource.issue, issueKey + "/attachments", binaryData, filename);
            LogTraceEvent(this.eventLog, json, EventLogEntryType.Information);

            List<JiraAttachment> jiraAttachments = JsonConvert.DeserializeObject<List<JiraAttachment>>(json);
            if (jiraAttachments == null || jiraAttachments.Count < 1)
            {
                LogErrorEvent("The attachment '" + filename + "'was not successfully uploaded to JIRA issue " + issueKey, EventLogEntryType.Warning);
            }
            return jiraAttachments;
        }

        /// <summary>
        /// Gets the raw content from the attachment
        /// </summary>
        /// <param name="attachmentUrl">The URL of the attachment</param>
        /// <returns>The binary data</returns>
        public byte[] GetAttachment(string attachmentUrl, long numberBytes)
        {
            HttpWebRequest request = WebRequest.Create(attachmentUrl) as HttpWebRequest;
            request.Method = "GET";

            //Add headers and credentials
            request.Headers.Add("X-Atlassian-Token: nocheck");
            request.UseDefaultCredentials = this.UseDefaultCredentials;
            string base64Credentials = GetEncodedCredentials();
            request.Headers.Add("Authorization", "Basic " + base64Credentials);

            HttpWebResponse response = request.GetResponse() as HttpWebResponse;

            if (response == null)
            {
                throw new Exception("Null Response received from JIRA API");
            }

            byte[] result = null;
            using (BinaryReader reader = new BinaryReader(response.GetResponseStream()))
            {
                result = reader.ReadBytes((int)numberBytes);
            }

            return result;
        }

        /// <summary>
        /// Gets the list of jira versions in the project
        /// </summary>
        /// <param name="project">The project key</param>
        public List<JiraVersion> GetVersions(string project)
        {
            string json = RunQuery(JiraResource.project, project + "/versions", null, "Get");

            List<JiraVersion> jiraVersions = JsonConvert.DeserializeObject<List<JiraVersion>>(json);
            return jiraVersions;
        }

        /// <summary>
        /// Gets the list of jira components in the project
        /// </summary>
        /// <param name="project">The project key</param>
        public List<JiraComponent> GetComponents(string project)
        {
            string json = RunQuery(JiraResource.project, project + "/components", null, "Get");

            List<JiraComponent> jiraComponents = JsonConvert.DeserializeObject<List<JiraComponent>>(json);
            return jiraComponents;
        }

        /// <summary>
        /// Gets the current user's permissions as raw JSON
        /// </summary>
        /// <remarks>We use this to test the different security transport protocols supported</remarks>
        public string GetPermissions()
        {
            string json;

            //First try TLS 1.2
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                json = RunQuery(JiraResource.mypermissions, null, null, "Get");
            }
            catch (WebException)
            {
                try
                {
                    //Then try TLS 1.1
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11;
                    json = RunQuery(JiraResource.mypermissions, null, null, "Get");
                }
                catch (WebException)
                {
                    try
                    {
                        //Then fallback to TLS 1.0
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
                        json = RunQuery(JiraResource.mypermissions, null, null, "Get");
                    }
                    catch (WebException)
                    {
                        //Finally, use SSL 3.0, let this exception throw
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3;
                        json = RunQuery(JiraResource.mypermissions, null, null, "Get");
                    }
                }
            }

            return json;
        }

        /// <summary>
        /// Returns a single JIRA issue by its KEY. Includes all fields and child collections (e.g. comments)
        /// </summary>
        /// <param name="issueKey">The JIRA issue key</param>
        /// <param name="jiraCreateMetaData">The creation meta-data</param>
        /// <returns>The JIRA issue object</returns>
        public JiraIssue GetIssueByKey(string issueKey, JiraCreateMetaData jiraCreateMetaData)
        {
            string result = RunQuery(resource: JiraResource.issue, argument: issueKey, method: "GET");
            LogTraceEvent(this.eventLog, result, EventLogEntryType.SuccessAudit);

            //First deserialize into a generic JObject;
            JObject jIssue = JObject.Parse(result);

            //For the standard fields we use the standard deserializer
            JiraIssue issue = jIssue.ToObject<JiraIssue>();

            //We need to get a handle on the fields meta-data for use later
            JObject jIssueTypeFields = null;
            if (jiraCreateMetaData != null)
            {
                JiraProject project = jiraCreateMetaData.Projects.FirstOrDefault(p => p.Id == issue.Fields.Project.Id);
                if (project != null)
                {
                    IssueType issueType = project.IssueTypes.FirstOrDefault(t => t.Id == issue.Fields.IssueType.Id);
                    if (issueType != null)
                    {
                        //See if we are missing any required properties
                        jIssueTypeFields = issueType.Fields;
                    }
                }
            }


            //Now we need to get the custom fields from the JObject directly
            JObject jFields = (JObject)jIssue["fields"];
            if (jFields != null)
            {
                foreach (JProperty jProperty in jFields.Properties())
                {
                    //Make sure it's a custom field
                    if (jProperty.Name.StartsWith(JiraCustomFieldValue.CUSTOM_FIELD_PREFIX))
                    {
                        JiraCustomFieldValue customFieldValue = new JiraCustomFieldValue(jProperty.Name);
                        CustomPropertyValue cpv = new CustomPropertyValue();

                        //We need to try and match the type of value
                        if (jProperty.Value != null && jProperty.Value.Type != JTokenType.None && jProperty.Value.Type != JTokenType.Null)
                        {
                            LogTraceEvent(eventLog, String.Format("Found custom field '{0}' of JProperty type " + jProperty.Value.Type, jProperty.Name), EventLogEntryType.Information);
                            if (jProperty.Value.Type == JTokenType.Array && jProperty.Value is JArray)
                            {
                                //Iterate through the list of values
                                List<string> listOptionValueNames = new List<string>();

                                JArray jOptions = (JArray)jProperty.Value;
                                foreach (JToken jToken in jOptions)
                                {
                                    if (jToken is JObject)
                                    {
                                        JObject jOption = (JObject)jToken;
                                        //If we have an object that has an ID property then we have a multi-list
                                        if (jOption["id"] != null && jOption["id"].Type == JTokenType.String)
                                        {
                                            LogTraceEvent(eventLog, String.Format("Found custom field '{0}' that is an array of objects with ID field ({1})", jProperty.Name, jOption["id"].Type), EventLogEntryType.Information);
                                            string id = (string)jOption["id"];
                                            if (!String.IsNullOrEmpty(id))
                                            {
                                                //Need to convert into an integer and set the list value
                                                int idAsInt;
                                                if (Int32.TryParse(id, out idAsInt))
                                                {
                                                    LogTraceEvent(eventLog, String.Format("Looking for custom field value name that matches custom field {0} option value id {1}", jProperty.Name, idAsInt), EventLogEntryType.Information);
                                                    string optionName = LookupCustomFieldOptionName(idAsInt, jIssueTypeFields, customFieldValue.CustomFieldName);
                                                    listOptionValueNames.Add(optionName);
                                                    LogTraceEvent(eventLog, String.Format("Found JIRA custom field {2} value name that matches custom field {0} option value id {1}", jProperty.Name, idAsInt, optionName), EventLogEntryType.Information);
                                                }
                                            }
                                        }
                                    }
                                }

                                if (listOptionValueNames.Count > 0)
                                {
                                    cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.MultiList;
                                    cpv.MultiListValue = listOptionValueNames;
                                }
                            }
                            else if (jProperty.Value.Type == JTokenType.Object)
                            {
                                //If we have an object that has an ID property then we have a single-list
                                //If we have a 'name' property then we have a user field
                                if (jProperty.Value is JObject)
                                {
                                    JObject jOption = (JObject)jProperty.Value;
                                    if (jOption["id"] != null && jOption["id"].Type == JTokenType.String)
                                    {
                                        LogTraceEvent(eventLog, String.Format("Found custom field '{0}' that is an object with ID field ({1})", jProperty.Name, jOption["id"].Type), EventLogEntryType.Information);
                                        string id = (string)jOption["id"];
                                        if (!String.IsNullOrEmpty(id))
                                        {
                                            //Need to convert into an integer and get the name from the meta-data
                                            int idAsInt;
                                            if (Int32.TryParse(id, out idAsInt))
                                            {
                                                LogTraceEvent(eventLog, String.Format("Looking for custom field value name that matches custom field {0} option value id {1}", jProperty.Name, idAsInt), EventLogEntryType.Information);
                                                string optionName = LookupCustomFieldOptionName(idAsInt, jIssueTypeFields, customFieldValue.CustomFieldName);
                                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.List;
                                                cpv.StringValue = optionName;
                                                LogTraceEvent(eventLog, String.Format("Found JIRA custom field {2} value name that matches custom field {0} option value id {1}", jProperty.Name, idAsInt, optionName), EventLogEntryType.Information);
                                            }
                                        }
                                    }
                                    else if (jOption["name"] != null && jOption["name"].Type == JTokenType.String)
                                    {
                                        LogTraceEvent(eventLog, String.Format("Found custom field '{0}' that is an object with NAME field ({1})", jProperty.Name, jOption["name"].Type), EventLogEntryType.Information);
                                        string username = (string)jOption["name"];
                                        if (!String.IsNullOrEmpty(username))
                                        {
                                            cpv.StringValue = username;
                                            cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.User;
                                        }
                                    }
                                }
                            }
                            else if (jProperty.Value.Type == JTokenType.Boolean)
                            {
                                //Simple integer property
                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.Boolean;
                                cpv.BooleanValue = (bool?)jProperty.Value;
                            }
                            else if (jProperty.Value.Type == JTokenType.Integer)
                            {
                                //Simple integer property
                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.Integer;
                                cpv.IntegerValue = (int?)jProperty.Value;
                            }
                            else if (jProperty.Value.Type == JTokenType.Float)
                            {
                                //Simple float property
                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.Decimal;
                                cpv.DecimalValue = (decimal?)((float?)jProperty.Value);
                            }
                            else if (jProperty.Value.Type == JTokenType.Date)
                            {
                                //Simple date/time property
                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.Date;
                                cpv.DateTimeValue = (DateTime?)jProperty.Value;
                            }
                            else if (jProperty.Value.Type == JTokenType.String)
                            {
                                //Simple string property
                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.Text;
                                cpv.StringValue = (string)jProperty.Value;
                            }
                        }
                        customFieldValue.Value = cpv;
                        issue.CustomFieldValues.Add(customFieldValue);
                    }
                }
            }

            return issue;
        }

        /// <summary>
        /// Gets a list of JIRA issues that match the provided JQL query
        /// </summary>
        /// <param name="jql">The jql query</param>
        /// <param name="fields">The list of fields (leave empty for all fields)</param>
        /// <param name="startAt">The starting index</param>
        /// <param name="maxResult">The number of records to return</param>
        /// <returns></returns>
        public List<JiraIssue> GetIssues(
                                        string jql,
                                        List<string> fields = null,
                                        int startAt = 0,
                                        int maxResult = 50)
        {
            JiraSearchRequest request = new JiraSearchRequest();
            request.Fields = fields;
            request.JQL = jql;
            request.MaxResults = maxResult;
            request.StartAt = startAt;

            string data = JsonConvert.SerializeObject(request);
            string result = RunQuery(JiraResource.search, data: data, method: "POST");

            JiraSearchResponse response = JsonConvert.DeserializeObject<JiraSearchResponse>(result);

            return response.Issues;
        }


        /// <summary>
        /// Runs a generic JIRA REST query
        /// </summary>
        /// <param name="resource">The resource we're accessing</param>
        /// <param name="argument">The URL querystring arguments</param>
        /// <param name="data">The POST data</param>
        /// <param name="method">The HTTP method</param>
        protected string RunQuery(JiraResource resource, string argument = null, string data = null, string method = "GET")
        {
            try
            {
                string url = string.Format("{0}{1}{2}/", baseUrl, API_PATH, resource.ToString());

                if (argument != null)
                {
                    url = string.Format("{0}{1}", url, argument);
                }

                HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                request.ContentType = "application/json";
                request.Method = method;
                request.UseDefaultCredentials = this.UseDefaultCredentials;

                if (data != null)
                {
                    using (StreamWriter writer = new StreamWriter(request.GetRequestStream()))
                    {
                        writer.Write(data);
                    }
                }

                string base64Credentials = GetEncodedCredentials();
                request.Headers.Add("Authorization", "Basic " + base64Credentials);

                HttpWebResponse response = request.GetResponse() as HttpWebResponse;

                if (response == null)
                {
                    throw new Exception("Null Response received from JIRA API");
                }

                string result = string.Empty;
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    result = reader.ReadToEnd();
                }

                return result;
            }
            catch (WebException webException)
            {
                //See if we have a Response
                if (webException.Response != null)
                {
                    //Log the message with response and rethrow
                    HttpWebResponse errorResponse = webException.Response as HttpWebResponse;
                    string details = string.Empty;
                    using (StreamReader reader = new StreamReader(errorResponse.GetResponseStream()))
                    {
                        details = reader.ReadToEnd();
                    }
                    LogErrorEvent("Web Exception Error calling JIRA REST API: '" + webException.Message + "' Details: " + details);
                    throw new ApplicationException("Web Exception Error calling JIRA REST API: '" + webException.Message + "' Details: " + details);
                }

                //Log the basic message and rethrow
                LogErrorEvent("Web Exception Error calling JIRA REST API: " + webException.Message);
                throw new ApplicationException("Web Exception Error calling JIRA REST API: " + webException.Message);
            }
            catch (Exception exception)
            {
                //Log the message and rethrow
                LogErrorEvent("Error calling JIRA REST API: " + exception.Message);
                throw exception;
            }
        }

        /// <summary>
        /// Runs a generic JIRA REST POST with multi-part data
        /// </summary>
        /// <param name="resource">The resource we're accessing</param>
        /// <param name="argument">The URL querystring arguments</param>
        /// <param name="data">The POST multi-part data</param>
        /// <param name="method">The HTTP method</param>
        protected string RunMultiPartQuery(JiraResource resource, string argument = null, byte[] data = null, string filename = "")
        {
            string url = string.Format("{0}{1}{2}/", baseUrl, API_PATH, resource.ToString());

            if (argument != null)
            {
                url = string.Format("{0}{1}", url, argument);
            }

            //Create the request
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;

            //Add headers and credentials
            request.Headers.Add("X-Atlassian-Token: nocheck");
            request.UseDefaultCredentials = this.UseDefaultCredentials;
            string base64Credentials = GetEncodedCredentials();

            request.Headers.Add("Authorization", "Basic " + base64Credentials);

            Stream stream = new MemoryStream(data);
            UploadFile file = new UploadFile(stream, "file", filename, "application/octet-stream");
            HttpWebResponse response = HttpUploadHelper.Upload(request, new UploadFile[] { file }); 
    
            string result = string.Empty;
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
            {
                result = reader.ReadToEnd();
            }

            return result;
        }

        /// <summary>
        /// Gets the base64-encoded login/password
        /// </summary>
        /// <returns></returns>
        private string GetEncodedCredentials()
        {
            string mergedCredentials = string.Format("{0}:{1}", this.username, this.password);
            byte[] byteCredentials = UTF8Encoding.UTF8.GetBytes(mergedCredentials);
            return Convert.ToBase64String(byteCredentials);
        }

        #region Logging Functions

        /// <summary>
        /// Logs an error event message
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="type">The type of event</param>
        public void LogErrorEvent(string message, EventLogEntryType type = EventLogEntryType.Error)
        {
            if (this.eventLog != null)
            {
                if (message.Length > 31000)
                {
                    //Split into smaller lengths
                    int index = 0;
                    while (index < message.Length)
                    {
                        try
                        {
                            string messageElement = message.Substring(index, 31000);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            string messageElement = message.Substring(index);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        index += 31000;
                    }
                }
                else
                {
                    this.eventLog.WriteEntry(message, type);
                }
            }
        }

        /// <summary>
        /// Logs a trace event message if the configuration option is set
        /// </summary>
        /// <param name="eventLog">The event log handle</param>
        /// <param name="message">The message to log</param>
        /// <param name="type">The type of event</param>
        public void LogTraceEvent(EventLog eventLog, string message, EventLogEntryType type)
        {
            if (traceLogging && this.eventLog != null)
            {
                if (message.Length > 31000)
                {
                    //Split into smaller lengths
                    int index = 0;
                    while (index < message.Length)
                    {
                        try
                        {
                            string messageElement = message.Substring(index, 31000);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            string messageElement = message.Substring(index);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        index += 31000;
                    }
                }
                else
                {
                    this.eventLog.WriteEntry(message, type);
                }
            }
        }

        #endregion
    }
}
