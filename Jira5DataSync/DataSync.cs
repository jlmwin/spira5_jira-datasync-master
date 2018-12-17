using System;
using System.Net;
using System.Web;
using System.Web.Services;
using System.Web.Services.Protocols;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Configuration;
using System.IO;
using System.Globalization;
using System.Linq;

using Inflectra.SpiraTest.PlugIns;
using Inflectra.SpiraTest.PlugIns.Jira5DataSync.SpiraSoapService;
using System.ServiceModel;
using System.Net.Security;
using Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync
{
    /// <summary>
    /// Contains all the logic necessary to sync SpiraTest 4.x with Jira 5.x
    /// </summary>
    public class DataSync : IDataSyncPlugIn
    {
        //Constant containing data-sync name
        private const string DATA_SYNC_NAME = "JiraDataSync";

        private static List<string> jiraKeyFields = new List<string>() { "key" };

        //Special JIRA fields that we map to Spira custom properties
        private const string JIRA_SPECIAL_FIELD_ENVIRONMENT = "Environment";
        private const string JIRA_SPECIAL_FIELD_COMPONENT = "Component";
        private const string JIRA_SPECIAL_FIELD_RESOLUTION = "Resolution";
        private const string JIRA_SPECIAL_FIELD_SECURITY_LEVEL = "SecurityLevel";
        private const string JIRA_SPECIAL_FIELD_ISSUE_KEY = "JiraIssueKey";

        // Track whether Dispose has been called.
        private bool disposed = false;

        //Configuration data passed through from calling service
        private EventLog eventLog;
        private bool traceLogging;
        private int dataSyncSystemId;
        private string webServiceBaseUrl;
        private string internalLogin;
        private string internalPassword;
        private string connectionString;
        private string externalLogin;
        private string externalPassword;
        private int timeOffsetHours;
        private bool autoMapUsers;
        private int? severityCustomFieldId = null;
        private bool useSecurityLevel = false;
        private bool onlyCreateNewItemsInJira = false;
        private string issueLinkType = null;
        private List<int> requirementIssueTypes = new List<int>();

        //Settings that are no longer configurable
        private const bool SYNC_ATTACHMENTS = true;

        protected JiraCreateMetaData jiraCreateMetaData = null;

        /// <summary>
        /// Constructor, does nothing - all setup in the Setup() method instead
        /// </summary>
        public DataSync()
        {
            //Does Nothing - all setup in the Setup() method instead
        }

        /// <summary>
        /// Loads in all the configuration information passed from the calling service
        /// </summary>
        /// <param name="eventLog">Handle to the event log to use</param>
        /// <param name="dataSyncSystemId">The id of the plug-in used when accessing the mapping repository</param>
        /// <param name="webServiceBaseUrl">The base URL of the Spira web service</param>
        /// <param name="internalLogin">The login to Spira</param>
        /// <param name="internalPassword">The password used for the Spira login</param>
        /// <param name="connectionString">The web service URL for the JIRA SOAP API</param>
        /// <param name="externalLogin">The login used for accessing JIRA</param>
        /// <param name="externalPassword">The password for the JIRA login</param>
        /// <param name="timeOffsetHours">Any time offset to apply between Spira and JIRA</param>
        /// <param name="autoMapUsers">Should we auto-map users</param>
        /// <param name="custom01">The name of the JIRA custom property to map to Spira incident severity</param>
        /// <param name="custom02">Set to 'true' to specify that we need to set the JIRA 'security level'</param>
        /// <param name="custom03">Set to 'true' if we only want new items to flow from Spira > JIRA</param>
        /// <param name="custom04">Set to a comma-separated list of JIRA issue types that should be added as requirements to Spira</param>
        /// <param name="custom05">Set to the ID of the JIRA issue link type we want associations to map to</param>
        public void Setup(
            EventLog eventLog,
            bool traceLogging,
            int dataSyncSystemId,
            string webServiceBaseUrl,
            string internalLogin,
            string internalPassword,
            string connectionString,
            string externalLogin,
            string externalPassword,
            int timeOffsetHours,
            bool autoMapUsers,
            string custom01,
            string custom02,
            string custom03,
            string custom04,
            string custom05
            )
        {
            //Make sure the object has not been already disposed
            if (this.disposed)
            {
                throw new ObjectDisposedException(DATA_SYNC_NAME + " has been disposed already.");
            }

            try
            {
                //Set the member variables from the passed-in values
                this.eventLog = eventLog;
                this.traceLogging = traceLogging;
                this.dataSyncSystemId = dataSyncSystemId;
                this.webServiceBaseUrl = webServiceBaseUrl;
                this.internalLogin = internalLogin;
                this.internalPassword = internalPassword;
                this.connectionString = connectionString;
                this.externalLogin = externalLogin;
                this.externalPassword = externalPassword;
                this.timeOffsetHours = timeOffsetHours;
                this.autoMapUsers = autoMapUsers;
                int intValue;
                if (Int32.TryParse(custom01, out intValue))
                {
                    this.severityCustomFieldId = intValue;
                }
                this.useSecurityLevel = (custom02 != null && custom02.ToLowerInvariant() == "true");
                this.onlyCreateNewItemsInJira = (custom03 != null && custom03.ToLowerInvariant() == "true");

                //See if we have any issue types specified for mapping to requirements
                if (!String.IsNullOrWhiteSpace(custom04))
                {
                    string[] reqIssueTypes = custom04.Split(',');
                    foreach(string reqIssueType in reqIssueTypes)
                    {
                        int reqIssueTypeInt;
                        if (Int32.TryParse(reqIssueType, out reqIssueTypeInt))
                        {
                            this.requirementIssueTypes.Add(reqIssueTypeInt);
                        }
                    }
                }

                //See if we have an issue link type specified
                if (!String.IsNullOrWhiteSpace(custom05))
                {
                    this.issueLinkType = custom05.Trim();
                }
            }
            catch (Exception exception)
            {
                //Log and rethrow the exception
                LogErrorEvent("Unable to setup the " + DATA_SYNC_NAME + " plug-in ('" + exception.Message + "')\n" + exception.StackTrace, EventLogEntryType.Error);
                throw exception;
            }
        }

        /// <summary>
        /// Adds the JIRA issue attachments as Spira document attachments
        /// </summary>
        private void ProcessJiraIssueAttachments(int projectId, string productName, SpiraSoapService.SoapServiceClient spiraSoapClient, JiraManager jiraManager, JiraIssue jiraIssue, Constants.ArtifactType artifactType, int artifactId)
        {
            //Make sure we have attachments
            if (jiraIssue.Fields.Attachments != null && jiraIssue.Fields.Attachments.Count > 0)
            {
                foreach (JiraAttachment jiraAttachment in jiraIssue.Fields.Attachments)
                {
                    try
                    {
                        //For now we just use the sync user as the author
                        string filename = jiraAttachment.Filename;
                        DateTime createdDate = jiraAttachment.Created;
                        long size = jiraAttachment.Size;
                        string attachmentUrl = jiraAttachment.ContentUrl;

                        //We now need to physically get the attachment bytes
                        byte[] data = jiraManager.GetAttachment(attachmentUrl, size);

                        //Now upload to Spira
                        RemoteDocument remoteDocument = new RemoteDocument();
                        remoteDocument.FilenameOrUrl = filename;
                        remoteDocument.UploadDate = createdDate;
                        remoteDocument.EditedDate = DateTime.UtcNow;
                        remoteDocument.Description = "Synchronized from JIRA";
                        RemoteLinkedArtifact artifactAttachment = new RemoteLinkedArtifact();
                        artifactAttachment.ArtifactId = artifactId;
                        artifactAttachment.ArtifactTypeId = (int)artifactType;
                        remoteDocument.AttachedArtifacts = new RemoteLinkedArtifact[1] { artifactAttachment };
                        spiraSoapClient.Document_AddFile(remoteDocument, data);
                    }
                    catch (Exception exception)
                    {
                        //Log a message that describes why it's not working
                        LogErrorEvent("Unable to add JIRA attachment to the " + productName + " incident/requirement, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                        //Just continue with the rest since it's optional.
                    }
                }
            }
        }

        /// <summary>
        /// Updates the Spira artifact from the custom property mappings
        /// </summary>
        /// <param name="ignoreComponentCustomProperty">Should we ignore the Component custom property mappings - usually because the new standard field was mapped instead</param>
        private List<JiraCustomFieldValue> ProcessJiraIssueCustomFields(int projectId, string productName, JiraIssue jiraIssue, RemoteCustomProperty[] customProperties, Dictionary<int, RemoteDataMapping> customPropertyMappingList, RemoteDataMapping[] userMappings, SpiraSoapService.SoapServiceClient spiraSoapService, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteArtifact remoteArtifact, bool ignoreComponentCustomProperty)
        {
            List<JiraCustomFieldValue>  jiraCustomFieldValues = jiraIssue.CustomFieldValues;
            foreach (SpiraSoapService.RemoteCustomProperty customProperty in customProperties)
            {
                //Get the external key of this custom property
                if (customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                {
                    SpiraSoapService.RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                    if (customPropertyDataMapping != null)
                    {
                        LogTraceEvent(eventLog, "Found value for JIRA custom field " + customPropertyDataMapping.ExternalKey + "\n", EventLogEntryType.Information);
                        string externalKey = customPropertyDataMapping.ExternalKey;
                        //See if we have a list, multi-list or user custom field as they need to be handled differently
                        if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.List)
                        {
                            LogTraceEvent(eventLog, "JIRA custom field " + customPropertyDataMapping.ExternalKey + " is a LIST property\n", EventLogEntryType.Information);

                            //First the single-list fields
                            if (externalKey == JIRA_SPECIAL_FIELD_RESOLUTION)
                            {
                                if (jiraIssue.Fields.Resolution == null || jiraIssue.Fields.Resolution.Id < 1)
                                {
                                    InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (int?)null);
                                }
                                else
                                {
                                    //Now we need to set the value on the SpiraTest incident - using the built-in JIRA resolution field
                                    SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                    SpiraSoapService.RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Fields.Resolution.Id.ToString(), customPropertyValueMappings, false);
                                    if (customPropertyValueMapping != null)
                                    {
                                        InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, customPropertyValueMapping.InternalId);
                                    }
                                }
                            }
                            else if (externalKey == JIRA_SPECIAL_FIELD_SECURITY_LEVEL)
                            {
                                //Ignore as only used Spira > JIRA
                            }
                            else
                            {
                                //Now we need to set the value on the SpiraTest incident
                                bool matchFound = false;
                                foreach (JiraCustomFieldValue jiraCustomFieldValue in jiraCustomFieldValues)
                                {
                                    if (jiraCustomFieldValue.CustomFieldId.ToString() == externalKey)
                                    {
                                        matchFound = true;
                                        if (jiraCustomFieldValue.Value != null)
                                        {
                                            //We need to get the Spira custom property value id from the equivalent Jira one
                                            string jiraCustomFieldValueId = jiraCustomFieldValue.Value.StringValue;
                                            SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                            SpiraSoapService.RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraCustomFieldValueId, customPropertyValueMappings, false);
                                            if (customPropertyValueMapping != null)
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, customPropertyValueMapping.InternalId);
                                            }
                                        }
                                    }
                                }
                                if (!matchFound)
                                {
                                    LogErrorEvent("JIRA custom field " + customPropertyDataMapping.ExternalKey + " was not found in custom field meta-data\n", EventLogEntryType.Warning);
                                }
                            }
                        }
                        else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.User)
                        {
                            LogTraceEvent(eventLog, "JIRA custom field " + customPropertyDataMapping.ExternalKey + " is a USER property\n", EventLogEntryType.Information);

                            //Now we need to set the value on the SpiraTest incident
                            foreach (JiraCustomFieldValue jiraCustomFieldValue in jiraCustomFieldValues)
                            {
                                if ((jiraCustomFieldValue.CustomFieldId.ToString() == externalKey) && jiraCustomFieldValue.Value != null)
                                {
                                    //We need to get the Spira user id from the equivalent Jira login name
                                    string jiraUserLogin = jiraCustomFieldValue.Value.StringValue;
                                    if (!String.IsNullOrEmpty(jiraUserLogin))
                                    {
                                        RemoteDataMapping customPropertyValueMapping = FindUserMappingByExternalKey(jiraCustomFieldValue.Value.StringValue, userMappings, spiraSoapService);
                                        if (customPropertyValueMapping != null)
                                        {
                                            InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, customPropertyValueMapping.InternalId);
                                        }
                                        else
                                        {
                                            LogErrorEvent("Unable to find a matching " + productName + " user that matches JIRA user with login name=" + jiraCustomFieldValue.Value.StringValue + " so leaving property null.", EventLogEntryType.Warning);
                                        }
                                    }
                                }
                            }
                        }
                        else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.MultiList)
                        {
                            LogTraceEvent(eventLog, "JIRA custom field '" + customPropertyDataMapping.ExternalKey + "' is a MULTILIST property\n", EventLogEntryType.Information);

                            //Next the multi-list fields
                            if (externalKey == JIRA_SPECIAL_FIELD_COMPONENT)
                            {
                                if (jiraIssue.Fields.Components.Count > 0)
                                {
                                    LogTraceEvent(eventLog, "Setting JIRA Component values on '" + productName + "' artifact custom properties\n", EventLogEntryType.Information);

                                    //Now we need to set the value on the SpiraTest incident
                                    List<int> customPropertyValueIds = new List<int>();
                                    foreach (JiraComponent jiraComponent in jiraIssue.Fields.Components)
                                    {
                                        string jiraComponentName = jiraComponent.Name;
                                        SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                        SpiraSoapService.RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraComponentName, customPropertyValueMappings, false);
                                        if (customPropertyValueMapping != null)
                                        {
                                            customPropertyValueIds.Add(customPropertyValueMapping.InternalId);
                                        }
                                    }
                                    LogTraceEvent(eventLog, "Setting JIRA Component values (" + customPropertyValueIds.Count + ") on " + productName + " artifact custom property " + customProperty.PropertyNumber + "\n", EventLogEntryType.Information);
                                    InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, customPropertyValueIds);
                                }
                                else
                                {
                                    InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (List<int>)null);
                                }
                            }
                            else
                            {
                                //Now we need to set the value on the SpiraTest incident
                                bool matchFound = false;
                                foreach (JiraCustomFieldValue jiraCustomFieldValue in jiraCustomFieldValues)
                                {
                                    if (jiraCustomFieldValue.CustomFieldId.ToString() == externalKey)
                                    {
                                        matchFound = true;
                                        if (jiraCustomFieldValue.Value != null)
                                        {
                                            List<string> jiraCustomFieldValueNames = jiraCustomFieldValue.Value.MultiListValue;
                                            if (jiraCustomFieldValueNames != null && jiraCustomFieldValueNames.Count > 0)
                                            {
                                                //Now we need to set the value on the SpiraTest incident
                                                List<int> customPropertyValueIds = new List<int>();

                                                foreach (string jiraCustomFieldValueName in jiraCustomFieldValueNames)
                                                {
                                                    SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                                    SpiraSoapService.RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraCustomFieldValueName, customPropertyValueMappings, false);
                                                    if (customPropertyValueMapping != null)
                                                    {
                                                        customPropertyValueIds.Add(customPropertyValueMapping.InternalId);
                                                    }
                                                }
                                                LogTraceEvent(eventLog, "Setting JIRA custom field " + customPropertyDataMapping.ExternalKey + " values on " + productName + " artifact custom property " + customProperty.PropertyNumber + "\n", EventLogEntryType.Information);
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, customPropertyValueIds);
                                            }
                                            else
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (List<int>)null);
                                            }
                                        }
                                    }
                                }
                                if (!matchFound)
                                {
                                    LogErrorEvent("JIRA custom field '" + customPropertyDataMapping.ExternalKey + "' was not found in custom field meta-data\n", EventLogEntryType.Warning);
                                }
                            }
                        }
                        else
                        {
                            LogTraceEvent(eventLog, "JIRA custom field " + customPropertyDataMapping.ExternalKey + " is a VALUE property\n", EventLogEntryType.Information);

                            //Now the other fields
                            if (externalKey == JIRA_SPECIAL_FIELD_ENVIRONMENT)
                            {
                                //Now we need to set the value on the SpiraTest incident
                                LogTraceEvent(eventLog, "Setting JIRA Environment value '" + jiraIssue.Fields.Environment + "' on " + productName + " artifact custom property\n", EventLogEntryType.Information);
                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, jiraIssue.Fields.Environment);
                            }
                            else if (externalKey == JIRA_SPECIAL_FIELD_ISSUE_KEY)
                            {
                                //Now we need to set the value on the SpiraTest incident
                                LogTraceEvent(eventLog, "Setting JIRA Issue Key '" + jiraIssue.Key.ToString() + "' on " + productName + " artifact custom property\n", EventLogEntryType.Information);
                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, jiraIssue.Key.ToString());
                            }
                            else
                            {
                                //Now we need to set the value on the SpiraTest incident
                                bool matchFound = false;
                                foreach (JiraCustomFieldValue jiraCustomFieldValue in jiraCustomFieldValues)
                                {
                                    if ((jiraCustomFieldValue.CustomFieldId.ToString() == externalKey))
                                    {
                                        matchFound = true;
                                        LogTraceEvent(eventLog, "JIRA custom field " + customPropertyDataMapping.ExternalKey + " was found in custom field meta-data\n", EventLogEntryType.Information);

                                        switch (jiraCustomFieldValue.Value.CustomPropertyType)
                                        {
                                            case CustomPropertyValue.CustomPropertyTypeEnum.Boolean:
                                                LogTraceEvent(eventLog, "Setting JIRA custom field " + customPropertyDataMapping.ExternalKey + " value '" + jiraCustomFieldValue.Value.BooleanValue + "' on " + productName + " artifact\n", EventLogEntryType.Information);
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, jiraCustomFieldValue.Value.BooleanValue);
                                                break;

                                            case CustomPropertyValue.CustomPropertyTypeEnum.Date:
                                                LogTraceEvent(eventLog, "Setting JIRA custom field " + customPropertyDataMapping.ExternalKey + " value '" + jiraCustomFieldValue.Value.DateTimeValue + "' on " + productName + " artifact\n", EventLogEntryType.Information);
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, jiraCustomFieldValue.Value.DateTimeValue);
                                                break;

                                            case CustomPropertyValue.CustomPropertyTypeEnum.Decimal:
                                                LogTraceEvent(eventLog, "Setting JIRA custom field " + customPropertyDataMapping.ExternalKey + " value '" + jiraCustomFieldValue.Value.DecimalValue + "' on " + productName + " artifact\n", EventLogEntryType.Information);
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, jiraCustomFieldValue.Value.DecimalValue);
                                                break;

                                            case CustomPropertyValue.CustomPropertyTypeEnum.Integer:
                                                LogTraceEvent(eventLog, "Setting JIRA custom field " + customPropertyDataMapping.ExternalKey + " value '" + jiraCustomFieldValue.Value.IntegerValue + "' on " + productName + " artifact\n", EventLogEntryType.Information);
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, jiraCustomFieldValue.Value.IntegerValue);
                                                break;

                                            case CustomPropertyValue.CustomPropertyTypeEnum.Text:
                                                {
                                                    //For strings we need to double-check the expected Spira custom property type to see if we have a value that really needs parsing
                                                    LogTraceEvent(eventLog, "Setting JIRA custom field " + customPropertyDataMapping.ExternalKey + " value '" + jiraCustomFieldValue.Value.StringValue + "' on " + productName + " artifact\n", EventLogEntryType.Information);
                                                    if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.Boolean)
                                                    {
                                                        bool booleanValue;
                                                        if (Boolean.TryParse(jiraCustomFieldValue.Value.StringValue, out booleanValue))
                                                        {
                                                            InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, booleanValue);
                                                        }
                                                    }
                                                    else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.Integer)
                                                    {
                                                        int intValue;
                                                        if (Int32.TryParse(jiraCustomFieldValue.Value.StringValue, out intValue))
                                                        {
                                                            InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, intValue);
                                                        }
                                                    }
                                                    else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.Decimal)
                                                    {
                                                        decimal decimalValue;
                                                        if (Decimal.TryParse(jiraCustomFieldValue.Value.StringValue, out decimalValue))
                                                        {
                                                            InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, decimalValue);
                                                        }
                                                    }
                                                    else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.Date)
                                                    {
                                                        DateTime dateTimeValue;
                                                        if (DateTime.TryParse(jiraCustomFieldValue.Value.StringValue, out dateTimeValue))
                                                        {
                                                            //Need to convert to UTC
                                                            InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, dateTimeValue.ToUniversalTime());
                                                        }
                                                    }
                                                    else
                                                    {
                                                        InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, jiraCustomFieldValue.Value.StringValue);
                                                    }
                                                    break;
                                                }

                                            default:
                                                LogTraceEvent(eventLog, "JIRA custom field " + customPropertyDataMapping.ExternalKey + " had an unknown custom field type when parsing the JIRA issue data\n", EventLogEntryType.Warning);
                                                break;
                                        }
                                    }
                                }
                                if (!matchFound)
                                {
                                    LogErrorEvent("JIRA custom field " + customPropertyDataMapping.ExternalKey + " was not found in custom field meta-data\n", EventLogEntryType.Warning);
                                }

                            }
                        }
                    }
                }
            }

            return jiraCustomFieldValues;
        }

        /// <summary>
        /// Processes a JIRA issue as an Incident (either inserting or updating as necessary)
        /// </summary>
        private void ProcessJiraIssueAsIncident(int projectId, SoapServiceClient spiraImportExport, JiraIssue jiraIssue, List<RemoteDataMapping> newIncidentMappings, List<RemoteDataMapping> newReleaseMappings, List<RemoteDataMapping> oldReleaseMappings, Dictionary<int, RemoteDataMapping> customPropertyMappingList, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteCustomProperty[] incidentCustomProperties, RemoteDataMapping[] incidentMappings, JiraManager jiraManager, string productName, RemoteDataMapping[] severityMappings, RemoteDataMapping[] priorityMappings, RemoteDataMapping[] statusMappings, RemoteDataMapping[] typeMappings, RemoteDataMapping[] userMappings, RemoteDataMapping[] releaseMappings, RemoteDataMapping[] incidentComponentMappings)
        {
            LogTraceEvent(eventLog, "Processing JIRA issue " + jiraIssue.Key + " as a " + productName + " incident.", EventLogEntryType.Information);

            //See if we have an existing mapping or not
            SpiraSoapService.RemoteDataMapping incidentMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Key.ToString(), incidentMappings, false);

            int incidentId = -1;
            SpiraSoapService.RemoteIncident remoteIncident = null;
            if (incidentMapping == null)
            {
                //This issue needs to be inserted into SpiraTest
                //Make sure that the configuration settings allow new issues to flow from JIRA > SpiraTest
                if (!this.onlyCreateNewItemsInJira)
                {
                    //Specify the project and creation date for the new incident
                    remoteIncident = new SpiraSoapService.RemoteIncident();
                    remoteIncident.ProjectId = projectId;
                    //Need to convert to UTC
                    remoteIncident.CreationDate = jiraIssue.Fields.Created.ToUniversalTime();

                    //Set the dectector for new incidents
                    if (jiraIssue.Fields.Reporter != null && !String.IsNullOrEmpty(jiraIssue.Fields.Reporter.Name))
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = FindUserMappingByExternalKey(jiraIssue.Fields.Reporter.Name, userMappings, spiraImportExport);
                        if (dataMapping == null)
                        {
                            //We can't find the matching user so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for Jira user " + jiraIssue.Fields.Reporter.Name + " so using synchronization user as detector.", EventLogEntryType.Warning);
                        }
                        else
                        {
                            remoteIncident.OpenerId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Got the detector " + remoteIncident.OpenerId.ToString() + "\n", EventLogEntryType.Information);
                        }
                    }
                }
            }
            else
            {
                //We need to load the matching SpiraTest incident and update
                incidentId = incidentMapping.InternalId;

                //Now retrieve the SpiraTest incident using the Import APIs
                try
                {
                    remoteIncident = spiraImportExport.Incident_RetrieveById(incidentId);
                }
                catch (Exception)
                {
                    //Ignore as it will leave the remoteIncident as null
                }
            }

            try
            {
                //Make sure we have retrieved or created the incident
                if (remoteIncident != null)
                {
                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Retrieved incident in " + productName + "\n", EventLogEntryType.Information);

                    //Update the name of the incident
                    if (String.IsNullOrEmpty(jiraIssue.Fields.Summary))
                    {
                        //If new incident, add dummy name, otherwise just leave
                        if (incidentId == -1)
                        {
                            remoteIncident.Name = "Untitled JIRA Issue: " + jiraIssue.Key.ToString();
                        }
                    }
                    else
                    {
                        remoteIncident.Name = jiraIssue.Fields.Summary;
                    }

                    //Update the description of the incident
                    if (String.IsNullOrEmpty(jiraIssue.Fields.Description))
                    {
                        //If new incident, add dummy description, otherwise just leave
                        if (incidentId == -1)
                        {
                            remoteIncident.Description = "Empty Description in JIRA";
                        }
                    }
                    else
                    {
                        //Need to encode as HTML for Spira
                        remoteIncident.Description = HttpUtility.HtmlEncode(jiraIssue.Fields.Description);
                    }

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the name and description\n", EventLogEntryType.Information);

                    //Update the start-date if necessary
                    if (jiraIssue.Fields.DueDate.HasValue)
                    {
                        remoteIncident.StartDate = jiraIssue.Fields.DueDate.Value;
                    }

                    //Update the start-date if necessary
                    if (jiraIssue.Fields.ResolutionDate.HasValue)
                    {
                        remoteIncident.ClosedDate = jiraIssue.Fields.ResolutionDate.Value;
                    }


                    //Now get the issue priority from the mapping (if priority is set)
                    if (jiraIssue.Fields.Priority == null || jiraIssue.Fields.Priority.Id < 1)
                    {
                        remoteIncident.PriorityId = null;
                    }
                    else
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Fields.Priority.Id.ToString(), priorityMappings, true);
                        if (dataMapping == null)
                        {
                            //We can't find the matching item so log and just don't set the priority
                            LogErrorEvent("Unable to locate mapping entry for issue priority " + jiraIssue.Fields.Priority.Name + " (ID=" + jiraIssue.Fields.Priority.Id + ") in " + productName + " project PR" + projectId, EventLogEntryType.Warning);
                        }
                        else
                        {
                            remoteIncident.PriorityId = dataMapping.InternalId;
                        }
                    }

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the priority\n", EventLogEntryType.Information);

                    //Now get the issue status from the mapping
                    if (jiraIssue.Fields.Status != null && jiraIssue.Fields.Status.Id > 0)
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Fields.Status.Id.ToString(), statusMappings, true);
                        if (dataMapping == null)
                        {
                            //We can't find the matching item so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for issue status " + jiraIssue.Fields.Status.Name + " (ID=" + jiraIssue.Fields.Status.Id + ") in " + productName + " project PR" + projectId, EventLogEntryType.Error);
                        }
                        else
                        {
                            remoteIncident.IncidentStatusId = dataMapping.InternalId;
                        }
                    }

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the status\n", EventLogEntryType.Information);

                    //Now get the issue type from the mapping
                    if (jiraIssue.Fields.IssueType != null && jiraIssue.Fields.IssueType.Id > 0)
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Fields.IssueType.Id.ToString(), typeMappings, true);
                        if (dataMapping == null)
                        {
                            //If this is a new issue and we don't have the type mapped
                            //it means that they don't want them getting added to SpiraTest
                            if (incidentId == -1)
                            {
                                LogTraceEvent(eventLog, String.Format("Ignoring JIRA issue {0} because its issue type has not been mapped.\n", jiraIssue.Id), EventLogEntryType.Warning);
                                return;
                            }
                            //We can't find the matching item so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for issue type " + jiraIssue.Fields.IssueType.Name + " (ID=" + jiraIssue.Fields.Status.Id + ") in " + productName + " project PR" + projectId, EventLogEntryType.Error);
                        }
                        else
                        {
                            remoteIncident.IncidentTypeId = dataMapping.InternalId;
                        }
                    }
                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the type\n", EventLogEntryType.Information);

                    //Now get the component from the mapping (introduced in Spira 5.0 instead of using a special custom property)
                    if (jiraIssue.Fields.Components != null && jiraIssue.Fields.Components.Count > 0)
                    {
                        List<int> componentsIds = new List<int>();
                        foreach (JiraComponent jiraComponent in jiraIssue.Fields.Components)
                        {
                            if (jiraComponent.Id.HasValue)
                            {
                                SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraComponent.Id.ToString(), incidentComponentMappings, true);
                                if (dataMapping != null && !componentsIds.Contains(dataMapping.InternalId))
                                {
                                    componentsIds.Add(dataMapping.InternalId);
                                }
                            }
                        }
                        remoteIncident.ComponentIds = componentsIds.ToArray();
                    }
                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the component\n", EventLogEntryType.Information);

                    //Now we need to get all the comments attached to the issue in Jira
                    List<JiraComment> jiraComments = jiraIssue.Fields.Comment.Comments;

                    //Now get the list of comments and associations attached to the SpiraTest incident
                    //If this is the new incident case, just leave as null
                    SpiraSoapService.RemoteComment[] incidentComments = null;
                    if (incidentId != -1)
                    {
                        incidentComments = spiraImportExport.Incident_RetrieveComments(incidentId);
                    }

                    //Iterate through all the comments and see if we need to add any to SpiraTest
                    List<SpiraSoapService.RemoteComment> newIncidentComments = new List<SpiraSoapService.RemoteComment>();
                    if (jiraComments != null && jiraComments.Count > 0)
                    {
                        foreach(JiraComment jiraComment in jiraComments)
                        {
                            //Add the author, date and body to the resolution
                            //See if we already have this resolution inside SpiraTest
                            bool alreadyAdded = false;
                            if (incidentComments != null)
                            {
                                foreach (SpiraSoapService.RemoteComment incidentComment in incidentComments)
                                {
                                    if (incidentComment.Text == jiraComment.Body)
                                    {
                                        alreadyAdded = true;
                                    }
                                }
                            }
                            if (!alreadyAdded)
                            {
                                //Get the resolution author mapping
                                LogTraceEvent(eventLog, "Looking for comments author: '" + jiraComment.Author.Name + "'\n", EventLogEntryType.Information);
                                int? creatorId = null;
                                SpiraSoapService.RemoteDataMapping dataMapping = FindUserMappingByExternalKey(jiraComment.Author.Name, userMappings, spiraImportExport);
                                if (dataMapping == null)
                                {
                                    LogTraceEvent(eventLog, "Looking for comments update-author: '" + jiraComment.UpdateAuthor.Name + "'\n", EventLogEntryType.Information);
                                    dataMapping = FindUserMappingByExternalKey(jiraComment.UpdateAuthor.Name, userMappings, spiraImportExport);
                                    if (dataMapping != null)
                                    {
                                        creatorId = dataMapping.InternalId;
                                    }
                                }
                                else
                                {
                                    creatorId = dataMapping.InternalId;
                                }
                                if (creatorId.HasValue)
                                {
                                    LogTraceEvent(eventLog, "Got the resolution creator: " + creatorId.ToString() + "\n", EventLogEntryType.Information);
                                }

                                //Handle nullable dates safely
                                DateTime createdDate = DateTime.UtcNow;
                                if (jiraComment.Created.HasValue)
                                {
                                    //Need to convert back into UTC
                                    createdDate = jiraComment.Created.Value.ToUniversalTime();
                                }

                                //Add the comment to SpiraTest
                                SpiraSoapService.RemoteComment newIncidentComment = new SpiraSoapService.RemoteComment();
                                newIncidentComment.ArtifactId = incidentId;
                                newIncidentComment.UserId = creatorId;
                                newIncidentComment.CreationDate = createdDate;
                                newIncidentComment.Text = jiraComment.Body;
                                newIncidentComments.Add(newIncidentComment);
                            }
                        }
                    }
                    //The resolutions will actually get added later when we insert/update the incident record itself

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the comments/resolution\n", EventLogEntryType.Information);

                    if (jiraIssue.Fields.Assignee == null || String.IsNullOrEmpty(jiraIssue.Fields.Assignee.Name))
                    {
                        remoteIncident.OwnerId = null;
                    }
                    else
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = FindUserMappingByExternalKey(jiraIssue.Fields.Assignee.Name, userMappings, spiraImportExport);
                        if (dataMapping == null)
                        {
                            //We can't find the matching user so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for Jira user " + jiraIssue.Fields.Assignee.Name + " so ignoring the assignee change", EventLogEntryType.Warning);
                        }
                        else
                        {
                            remoteIncident.OwnerId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Got the assignee " + remoteIncident.OwnerId.ToString() + "\n", EventLogEntryType.Information);
                        }
                    }

                    //Specify the detected-in release if applicable
                    if (jiraIssue.Fields.Versions != null && jiraIssue.Fields.Versions.Count > 0)
                    {
                        //Get the most recent (last) affected version
                        //TODO: Once JIRA Greenhopper API extended to allow you to see which versions are children of each other
                        //then we shall be able to more intelligently set the SpiraTest release to the
                        //lowest level item in the tree (useful when using Greenhopper)
                        string affectedJiraVersionId = jiraIssue.Fields.Versions[jiraIssue.Fields.Versions.Count - 1].Id.ToString();

                        //See if we have a mapped SpiraTest release in either the existing list of
                        //mapped releases or the list of newly added ones
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, affectedJiraVersionId, releaseMappings, false);
                        if (dataMapping == null)
                        {
                            dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, affectedJiraVersionId, newReleaseMappings.ToArray(), false);
                        }
                        if (dataMapping == null)
                        {
                            //We can't find the matching item so need to create a new release in SpiraTest and add to mappings
                            JiraVersion jiraVersion = jiraIssue.Fields.Versions[jiraIssue.Fields.Versions.Count - 1];
                            LogTraceEvent(eventLog, "Adding new release in " + productName + " for version " + affectedJiraVersionId + "\n", EventLogEntryType.Information);
                            SpiraSoapService.RemoteRelease remoteRelease = new SpiraSoapService.RemoteRelease();
                            remoteRelease.Name = jiraVersion.Name;
                            remoteRelease.ReleaseTypeId = (int)Constants.ReleaseTypeEnum.MajorRelease;
                            remoteRelease.ReleaseStatusId = (int)Constants.ReleaseStatusEnum.Planned;
                            if (jiraVersion.Name.Length > 10)
                            {
                                remoteRelease.VersionNumber = jiraVersion.Name.Substring(0, 10);
                            }
                            else
                            {
                                remoteRelease.VersionNumber = jiraVersion.Name;
                            }
                            remoteRelease.Active = true;
                            if (jiraVersion.ReleaseDate.HasValue)
                            {
                                remoteRelease.StartDate = jiraVersion.ReleaseDate.Value.AddDays(-1);
                                remoteRelease.EndDate = jiraVersion.ReleaseDate.Value;
                            }
                            else
                            {
                                remoteRelease.StartDate = DateTime.UtcNow.Date;
                                remoteRelease.EndDate = DateTime.UtcNow.Date.AddDays(5);
                            }
                            remoteRelease.CreatorId = remoteIncident.OpenerId;
                            remoteRelease.CreationDate = DateTime.UtcNow;
                            remoteRelease.ResourceCount = 1;
                            remoteRelease.DaysNonWorking = 0;
                            remoteRelease = spiraImportExport.Release_Create(remoteRelease, null);

                            //Add a new mapping entry
                            SpiraSoapService.RemoteDataMapping newReleaseMapping = new SpiraSoapService.RemoteDataMapping();
                            newReleaseMapping.ProjectId = projectId;
                            newReleaseMapping.InternalId = remoteRelease.ReleaseId.Value;
                            newReleaseMapping.ExternalKey = jiraVersion.Id.ToString();
                            newReleaseMappings.Add(newReleaseMapping);
                            remoteIncident.DetectedReleaseId = newReleaseMapping.InternalId;
                            LogTraceEvent(eventLog, "Setting detected release id to  " + newReleaseMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                        }
                        else
                        {
                            remoteIncident.DetectedReleaseId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Setting detected release id to  " + dataMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                        }
                    }

                    //Specify the resolved-in release if applicable
                    if (jiraIssue.Fields.FixVersions != null && jiraIssue.Fields.FixVersions.Count > 0)
                    {
                        //Get the most recent (last) fixed version
                        //TODO: Once Greenhopper JIRA API extended to allow you to see which versions are children of each other
                        //then we shall be able to more intelligently set the SpiraTest release to the
                        //lowest level item in the tree (useful when using Greenhopper)
                        string fixJiraVersionId = jiraIssue.Fields.FixVersions[jiraIssue.Fields.FixVersions.Count - 1].Id.ToString();

                        //See if we have a mapped SpiraTest release in either the existing list of
                        //mapped releases or the list of newly added ones
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, fixJiraVersionId, releaseMappings, false);
                        if (dataMapping == null)
                        {
                            dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, fixJiraVersionId, newReleaseMappings.ToArray(), false);
                        }
                        if (dataMapping == null)
                        {
                            //We can't find the matching item so need to create a new release in SpiraTest and add to mappings
                            JiraVersion jiraVersion = jiraIssue.Fields.FixVersions[jiraIssue.Fields.FixVersions.Count - 1];
                            LogTraceEvent(eventLog, "Adding new release in " + productName + " for version " + fixJiraVersionId + "\n", EventLogEntryType.Information);
                            SpiraSoapService.RemoteRelease remoteRelease = new SpiraSoapService.RemoteRelease();
                            remoteRelease.Name = jiraVersion.Name;
                            remoteRelease.ReleaseTypeId = (int)Constants.ReleaseTypeEnum.MajorRelease;
                            remoteRelease.ReleaseStatusId = (int)Constants.ReleaseStatusEnum.Planned;
                            if (jiraVersion.Name.Length > 10)
                            {
                                remoteRelease.VersionNumber = jiraVersion.Name.Substring(0, 10);
                            }
                            else
                            {
                                remoteRelease.VersionNumber = jiraVersion.Name;
                            }
                            remoteRelease.Active = true;
                            if (jiraVersion.ReleaseDate.HasValue)
                            {
                                remoteRelease.StartDate = jiraVersion.ReleaseDate.Value.AddDays(-1);
                                remoteRelease.EndDate = jiraVersion.ReleaseDate.Value;
                            }
                            else
                            {
                                remoteRelease.StartDate = DateTime.UtcNow.Date;
                                remoteRelease.EndDate = DateTime.UtcNow.Date.AddDays(5);
                            }
                            remoteRelease.CreatorId = remoteIncident.OpenerId;
                            remoteRelease.CreationDate = DateTime.UtcNow;
                            remoteRelease.ResourceCount = 1;
                            remoteRelease.DaysNonWorking = 0;
                            remoteRelease = spiraImportExport.Release_Create(remoteRelease, null);

                            //Add a new mapping entry
                            SpiraSoapService.RemoteDataMapping newReleaseMapping = new SpiraSoapService.RemoteDataMapping();
                            newReleaseMapping.ProjectId = projectId;
                            newReleaseMapping.InternalId = remoteRelease.ReleaseId.Value;
                            newReleaseMapping.ExternalKey = jiraVersion.Id.ToString();
                            newReleaseMappings.Add(newReleaseMapping);
                            remoteIncident.ResolvedReleaseId = newReleaseMapping.InternalId;
                            LogTraceEvent(eventLog, "Setting resolved release id to  " + newReleaseMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                        }
                        else
                        {
                            remoteIncident.ResolvedReleaseId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Setting resolved release id to  " + dataMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                        }
                    }

                    //Now we need to see if any of the SpiraTest custom properties that map to JIRA-specific fields or custom properties have changed
                    List<JiraCustomFieldValue> jiraCustomFieldValues = ProcessJiraIssueCustomFields(projectId, productName, jiraIssue, incidentCustomProperties, customPropertyMappingList, userMappings, spiraImportExport, customPropertyValueMappingList, remoteIncident, (incidentComponentMappings != null && incidentComponentMappings.Length > 0));

                    //Now need to see if we have a custom field in JIRA mapped to Severity in SpiraTest
                    //and whether that field has changed or not
                    if (severityCustomFieldId.HasValue)
                    {
                        foreach (JiraCustomFieldValue jiraCustomFieldValue in jiraCustomFieldValues)
                        {
                            //Need to see if this is mapped to severity
                            string jiraCustomFieldId = jiraCustomFieldValue.CustomFieldId.ToString();
                            if (jiraCustomFieldId == severityCustomFieldId.Value.ToString())
                            {
                                //We only sync up the first of a multi-value custom field
                                string jiraCustomFieldValueId = jiraCustomFieldValue.Value.StringValue;
                                SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraCustomFieldValueId, severityMappings, true);
                                if (dataMapping != null)
                                {
                                    //Set the incident's severity
                                    remoteIncident.SeverityId = dataMapping.InternalId;
                                    LogTraceEvent(eventLog, "Set the incident's severity\n", EventLogEntryType.Information);
                                }
                            }
                        }
                    }

                    //Finally add or update the incident in SpiraTest
                    if (incidentId == -1)
                    {
                        //Debug logging - comment out for production code
                        try
                        {
                            remoteIncident = spiraImportExport.Incident_Create(remoteIncident);
                        }
                        catch (FaultException<ValidationFaultMessage> validationException)
                        {
                            string message = "";
                            ValidationFaultMessage validationFaultMessage = validationException.Detail;
                            message = validationFaultMessage.Summary + ": \n";
                            {
                                foreach (ValidationFaultMessageItem messageItem in validationFaultMessage.Messages)
                                {
                                    message += messageItem.FieldName + "=" + messageItem.Message + " \n";
                                }
                            }
                            LogErrorEvent("Error Adding Jira issue " + jiraIssue.Key.ToString() + " to " + productName + " (" + message + ")\n" + validationException.StackTrace, EventLogEntryType.Error);
                            return;
                        }
                        catch (Exception exception)
                        {
                            LogErrorEvent("Error Adding Jira issue " + jiraIssue.Key.ToString() + " to " + productName + " (" + exception.Message + ")\n" + exception.StackTrace, EventLogEntryType.Error);
                            return;
                        }
                        LogTraceEvent(eventLog, "Successfully added Jira issue " + jiraIssue.Key.ToString() + " to " + productName + "\n", EventLogEntryType.Information);

                        //Extract the SpiraTest incident and add to mappings table
                        SpiraSoapService.RemoteDataMapping newIncidentMapping = new SpiraSoapService.RemoteDataMapping();
                        newIncidentMapping.ProjectId = projectId;
                        newIncidentMapping.InternalId = remoteIncident.IncidentId.Value;
                        newIncidentMapping.ExternalKey = jiraIssue.Key.ToString();
                        newIncidentMappings.Add(newIncidentMapping);

                        //Now add any resolutions (need to set the ID)
                        foreach (SpiraSoapService.RemoteComment newComment in newIncidentComments)
                        {
                            newComment.ArtifactId = remoteIncident.IncidentId.Value;
                        }
                        spiraImportExport.Incident_AddComments(newIncidentComments.ToArray());

                        //Next add a link to the JIRA issue to the Spira incident
                        try
                        {
                            string issueUrl = this.connectionString + "/browse/" + jiraIssue.Key.ToString();
                            SpiraSoapService.RemoteDocument remoteUrl = new SpiraSoapService.RemoteDocument();
                            remoteUrl.Description = "Link to issue in JIRA";
                            remoteUrl.FilenameOrUrl = issueUrl;
                            RemoteLinkedArtifact artifactAttachment = new RemoteLinkedArtifact();
                            artifactAttachment.ArtifactId = remoteIncident.IncidentId.Value;
                            artifactAttachment.ArtifactTypeId = (int)Constants.ArtifactType.Incident;
                            remoteUrl.AttachedArtifacts = new RemoteLinkedArtifact[1] { artifactAttachment };
                            spiraImportExport.Document_AddUrl(remoteUrl);
                        }
                        catch (Exception exception)
                        {
                            //Log a message that describes why it's not working
                            LogErrorEvent("Unable to add JIRA hyperlink to the " + productName + " incident, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                            //Just continue with the rest since it's optional.
                        }

                        //Next add a link to the Spira incident from the JIRA issue
                        try
                        {
                            string baseUrl = spiraImportExport.System_GetWebServerUrl();
                            string incidentUrl = spiraImportExport.System_GetArtifactUrl((int)Constants.ArtifactType.Incident, projectId, remoteIncident.IncidentId.Value, "").Replace("~", baseUrl);
                            jiraManager.AddWebLink(jiraIssue.Key.ToString(), incidentUrl, productName + " " + Constants.INCIDENT_PREFIX + ":" + remoteIncident.IncidentId.Value);
                        }
                        catch (Exception exception)
                        {
                            //Log a message that describes why it's not working
                            LogErrorEvent("Unable to add " + productName + " hyperlink to the JIRA issue, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                            //Just continue with the rest since it's optional.
                        }

                        //Finally try adding any attachments to Spira
                        ProcessJiraIssueAttachments(projectId, productName, spiraImportExport, jiraManager, jiraIssue, Constants.ArtifactType.Incident, remoteIncident.IncidentId.Value);
                    }
                    else
                    {
                        spiraImportExport.Incident_Update(remoteIncident);

                        //Now add any resolutions
                        spiraImportExport.Incident_AddComments(newIncidentComments.ToArray());

                        //Debug logging - comment out for production code
                        LogTraceEvent(eventLog, "Successfully updated\n", EventLogEntryType.Information);
                    }
                }
            }
            catch (FaultException<ValidationFaultMessage> validationException)
            {
                string message = "";
                ValidationFaultMessage validationFaultMessage = validationException.Detail;
                message = validationFaultMessage.Summary + ": \n";
                {
                    foreach (ValidationFaultMessageItem messageItem in validationFaultMessage.Messages)
                    {
                        message += messageItem.FieldName + "=" + messageItem.Message + " \n";
                    }
                }
                LogErrorEvent("Error Inserting/Updating JIRA Issue " + jiraIssue.Key.ToString() + " in " + productName + " (" + message + ")\n" + validationException.StackTrace, EventLogEntryType.Error);
            }
            catch (Exception exception)
            {
                //Log and continue execution
                LogErrorEvent("Error Inserting/Updating JIRA Issue in " + productName + ": " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
            }
        }

        /// <summary>
        /// Processes a JIRA issue as a Requirement (either inserting or updating as necessary)
        /// </summary>
        private void ProcessJiraIssueAsRequirement(int projectId, SoapServiceClient spiraImportExport, JiraIssue jiraIssue, List<RemoteDataMapping> newRequirementMappings, List<RemoteDataMapping> newReleaseMappings, List<RemoteDataMapping> oldReleaseMappings, Dictionary<int, RemoteDataMapping> customPropertyMappingList, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteCustomProperty[] requirementCustomProperties, RemoteDataMapping[] requirementMappings, JiraManager jiraManager, string productName, RemoteDataMapping[] priorityMappings, RemoteDataMapping[] statusMappings, RemoteDataMapping[] userMappings, RemoteDataMapping[] releaseMappings, RemoteDataMapping[] requirementTypeMappings, RemoteDataMapping[] requirementComponentMappings)
        {
            LogTraceEvent(eventLog, "Processing JIRA issue " + jiraIssue.Key + " as a " + productName + " requirement.", EventLogEntryType.Information);

            //See if we have an existing mapping or not
            SpiraSoapService.RemoteDataMapping requirementMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Key.ToString(), requirementMappings, false);

            int requirementId = -1;
            SpiraSoapService.RemoteRequirement remoteRequirement = null;
            if (requirementMapping == null)
            {
                //This issue needs to be inserted into SpiraTest
                //Make sure that the configuration settings allow new issues to flow from JIRA > SpiraTest
                //Also make sure we've not already encountered in this run (could allow duplicates)
                if (!this.onlyCreateNewItemsInJira && InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Key.ToString(), newRequirementMappings, false) == null)
                {
                    //Specify the project and creation date for the new requirement
                    remoteRequirement = new SpiraSoapService.RemoteRequirement();
                    remoteRequirement.ProjectId = projectId;
                    //Convert to UTC
                    remoteRequirement.CreationDate = jiraIssue.Fields.Created.ToUniversalTime();

                    //Set the dectector for new requirements
                    if (jiraIssue.Fields.Reporter != null && !String.IsNullOrEmpty(jiraIssue.Fields.Reporter.Name))
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = FindUserMappingByExternalKey(jiraIssue.Fields.Reporter.Name, userMappings, spiraImportExport);
                        if (dataMapping == null)
                        {
                            //We can't find the matching user so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for Jira user " + jiraIssue.Fields.Reporter.Name + " so using synchronization user as detector.", EventLogEntryType.Warning);
                        }
                        else
                        {
                            remoteRequirement.AuthorId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Got the author " + remoteRequirement.AuthorId.ToString() + "\n", EventLogEntryType.Information);
                        }
                    }
                }
            }
            else
            {
                //We need to load the matching SpiraTest requirement and update
                requirementId = requirementMapping.InternalId;

                //Now retrieve the SpiraTest requirement using the Import APIs
                try
                {
                    remoteRequirement = spiraImportExport.Requirement_RetrieveById(requirementId);
                }
                catch (Exception)
                {
                    //Ignore as it will leave the remoteRequirement as null
                }
            }

            try
            {
                //Make sure we have retrieved or created the requirement
                if (remoteRequirement != null)
                {
                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Retrieved requirement in " + productName + "\n", EventLogEntryType.Information);

                    //Update the name of the requirement
                    if (String.IsNullOrEmpty(jiraIssue.Fields.Summary))
                    {
                        //If new requirement, add dummy name, otherwise just leave
                        if (requirementId == -1)
                        {
                            remoteRequirement.Name = "Untitled JIRA Issue: " + jiraIssue.Key.ToString();
                        }
                    }
                    else
                    {
                        remoteRequirement.Name = jiraIssue.Fields.Summary;
                    }

                    //Update the description of the requirement
                    if (String.IsNullOrEmpty(jiraIssue.Fields.Description))
                    {
                        //If new requirement, add dummy description, otherwise just leave
                        if (requirementId == -1)
                        {
                            remoteRequirement.Description = "Empty Description in JIRA";
                        }
                    }
                    else
                    {
                        //Need to encode the description as HTML for Spira
                        remoteRequirement.Description = HttpUtility.HtmlEncode(jiraIssue.Fields.Description);
                    }

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the name and description\n", EventLogEntryType.Information);

                    //Now get the issue priority from the mapping (if priority is set)
                    if (jiraIssue.Fields.Priority == null || jiraIssue.Fields.Priority.Id < 1)
                    {
                        remoteRequirement.ImportanceId = null;
                    }
                    else
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Fields.Priority.Id.ToString(), priorityMappings, true);
                        if (dataMapping == null)
                        {
                            //We can't find the matching item so log and just don't set the priority
                            LogErrorEvent("Unable to locate mapping entry for issue priority " + jiraIssue.Fields.Priority.Name + " (ID=" + jiraIssue.Fields.Priority.Id + ") in " + productName + " project PR" + projectId, EventLogEntryType.Warning);
                        }
                        else
                        {
                            remoteRequirement.ImportanceId = dataMapping.InternalId;
                        }
                    }

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the importance\n", EventLogEntryType.Information);

                    //Now get the issue status from the mapping
                    if (jiraIssue.Fields.Status != null && jiraIssue.Fields.Status.Id > 0)
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Fields.Status.Id.ToString(), statusMappings, true);
                        if (dataMapping == null)
                        {
                            //Default the requirement to 'Requested'
                            LogErrorEvent("Unable to locate requirement mapping entry for issue status " + jiraIssue.Fields.Status.Name + " (ID=" + jiraIssue.Fields.Status.Id + ") in " + productName + " project PR" + projectId + " so setting requirement to 'Requested' status.", EventLogEntryType.Warning);
                            remoteRequirement.StatusId = Constants.REQUIREMENT_STATUS_DEFAULT;
                        }
                        else
                        {
                            remoteRequirement.StatusId = dataMapping.InternalId;
                        }
                    }

                    //Now get the issue type from the mapping
                    if (jiraIssue.Fields.IssueType != null && jiraIssue.Fields.IssueType.Id > 0)
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraIssue.Fields.IssueType.Id.ToString(), requirementTypeMappings, true);
                        if (dataMapping == null)
                        {
                            //Default the requirement to 'User Story'
                            LogErrorEvent("Unable to locate requirement mapping entry for issue type " + jiraIssue.Fields.IssueType.Name + " (ID=" + jiraIssue.Fields.IssueType.Id + ") in " + productName + " project PR" + projectId + " so setting requirement to 'User Story' type.", EventLogEntryType.Warning);
                            remoteRequirement.RequirementTypeId = Constants.REQUIREMENT_TYPE_DEFAULT;
                        }
                        else
                        {
                            remoteRequirement.RequirementTypeId = dataMapping.InternalId;
                        }
                    }
                    else
                    {
                        remoteRequirement.RequirementTypeId = Constants.REQUIREMENT_TYPE_DEFAULT;
                    }

                    //Now get the component from the mapping (introduced in Spira 5.0 instead of using a special custom property)
                    if (jiraIssue.Fields.Components != null && jiraIssue.Fields.Components.Count > 0)
                    {
                        //Spira requirements only have one component
                        JiraComponent jiraComponent = jiraIssue.Fields.Components[0];

                        if (jiraComponent.Id.HasValue)
                        {
                            SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraComponent.Id.ToString(), requirementComponentMappings, true);
                            if (dataMapping != null)
                            {
                                remoteRequirement.ComponentId = dataMapping.InternalId;
                            }
                        }
                    }

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the status\n", EventLogEntryType.Information);

                    //Now we need to get all the comments attached to the issue in Jira
                    List<JiraComment> jiraComments = jiraIssue.Fields.Comment.Comments;

                    //Now get the list of comments and associations attached to the SpiraTest requirement
                    //If this is the new requirement case, just leave as null
                    SpiraSoapService.RemoteComment[] requirementComments = null;
                    if (requirementId != -1)
                    {
                        requirementComments = spiraImportExport.Requirement_RetrieveComments(requirementId);
                    }

                    //Iterate through all the comments and see if we need to add any to SpiraTest
                    List<SpiraSoapService.RemoteComment> newRequirementComments = new List<SpiraSoapService.RemoteComment>();
                    if (jiraComments != null && jiraComments.Count > 0)
                    {
                        foreach (JiraComment jiraComment in jiraComments)
                        {
                            //Add the author, date and body to the resolution
                            //See if we already have this resolution inside SpiraTest
                            bool alreadyAdded = false;
                            if (requirementComments != null)
                            {
                                foreach (SpiraSoapService.RemoteComment requirementComment in requirementComments)
                                {
                                    if (requirementComment.Text == jiraComment.Body)
                                    {
                                        alreadyAdded = true;
                                    }
                                }
                            }
                            if (!alreadyAdded)
                            {
                                //Get the resolution author mapping
                                LogTraceEvent(eventLog, "Looking for comments author: '" + jiraComment.Author.Name + "'\n", EventLogEntryType.Information);
                                int? creatorId = null;
                                SpiraSoapService.RemoteDataMapping dataMapping = FindUserMappingByExternalKey(jiraComment.Author.Name, userMappings, spiraImportExport);
                                if (dataMapping == null)
                                {
                                    LogTraceEvent(eventLog, "Looking for comments update-author: '" + jiraComment.UpdateAuthor.Name + "'\n", EventLogEntryType.Information);
                                    dataMapping = FindUserMappingByExternalKey(jiraComment.UpdateAuthor.Name, userMappings, spiraImportExport);
                                    if (dataMapping != null)
                                    {
                                        creatorId = dataMapping.InternalId;
                                    }
                                }
                                else
                                {
                                    creatorId = dataMapping.InternalId;
                                }
                                if (creatorId.HasValue)
                                {
                                    LogTraceEvent(eventLog, "Got the resolution creator: " + creatorId.ToString() + "\n", EventLogEntryType.Information);
                                }

                                //Handle nullable dates safely
                                DateTime createdDate = DateTime.UtcNow;
                                if (jiraComment.Created.HasValue)
                                {
                                    //Need to convert back into UTC
                                    createdDate = jiraComment.Created.Value.ToUniversalTime();
                                }

                                //Add the comment to SpiraTest
                                SpiraSoapService.RemoteComment newRequirementComment = new SpiraSoapService.RemoteComment();
                                newRequirementComment.ArtifactId = requirementId;
                                newRequirementComment.UserId = creatorId;
                                newRequirementComment.CreationDate = createdDate;
                                newRequirementComment.Text = jiraComment.Body;
                                newRequirementComments.Add(newRequirementComment);
                            }
                        }
                    }
                    //The resolutions will actually get added later when we insert/update the requirement record itself

                    //Debug logging - comment out for production code
                    LogTraceEvent(eventLog, "Got the comments/resolution\n", EventLogEntryType.Information);

                    if (jiraIssue.Fields.Assignee == null || String.IsNullOrEmpty(jiraIssue.Fields.Assignee.Name))
                    {
                        remoteRequirement.OwnerId = null;
                    }
                    else
                    {
                        SpiraSoapService.RemoteDataMapping dataMapping = FindUserMappingByExternalKey(jiraIssue.Fields.Assignee.Name, userMappings, spiraImportExport);
                        if (dataMapping == null)
                        {
                            //We can't find the matching user so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for Jira user " + jiraIssue.Fields.Assignee.Name + " so ignoring the assignee change", EventLogEntryType.Warning);
                        }
                        else
                        {
                            remoteRequirement.OwnerId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Got the assignee " + remoteRequirement.OwnerId.ToString() + "\n", EventLogEntryType.Information);
                        }
                    }

                     //Specify the release if applicable. We use the affects version unless there is a fix-version
                    if ((jiraIssue.Fields.FixVersions != null && jiraIssue.Fields.FixVersions.Count > 0)
                        || (jiraIssue.Fields.Versions != null && jiraIssue.Fields.Versions.Count > 0))
                    {
                        //Get the most recent (last) fixed version
                        //TODO: Once Greenhopper JIRA API extended to allow you to see which versions are children of each other
                        //then we shall be able to more intelligently set the SpiraTest release to the
                        //lowest level item in the tree (useful when using Greenhopper)
                        string jiraVersionId;
                        if (jiraIssue.Fields.FixVersions != null && jiraIssue.Fields.FixVersions.Count > 0)
                        {
                            jiraVersionId = jiraIssue.Fields.FixVersions[jiraIssue.Fields.FixVersions.Count - 1].Id.ToString();
                        }
                        else
                        {
                            jiraVersionId = jiraIssue.Fields.Versions[jiraIssue.Fields.Versions.Count - 1].Id.ToString();
                        }

                        //See if we have a mapped SpiraTest release in either the existing list of
                        //mapped releases or the list of newly added ones
                        SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraVersionId, releaseMappings, false);
                        if (dataMapping == null)
                        {
                            dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, jiraVersionId, newReleaseMappings.ToArray(), false);
                        }
                        if (dataMapping == null)
                        {
                            //We can't find the matching item so need to create a new release in SpiraTest and add to mappings
                            JiraVersion jiraVersion;
                            if (jiraIssue.Fields.FixVersions != null && jiraIssue.Fields.FixVersions.Count > 0)
                            {
                                jiraVersion = jiraIssue.Fields.FixVersions[jiraIssue.Fields.FixVersions.Count - 1];
                            }
                            else
                            {
                                jiraVersion = jiraIssue.Fields.Versions[jiraIssue.Fields.Versions.Count - 1];
                            }
                            LogTraceEvent(eventLog, "Adding new release in " + productName + " for version " + jiraVersionId + "\n", EventLogEntryType.Information);
                            SpiraSoapService.RemoteRelease remoteRelease = new SpiraSoapService.RemoteRelease();
                            remoteRelease.Name = jiraVersion.Name;
                            remoteRelease.ReleaseTypeId = (int)Constants.ReleaseTypeEnum.MajorRelease;
                            remoteRelease.ReleaseStatusId = (int)Constants.ReleaseStatusEnum.Planned;
                            if (jiraVersion.Name.Length > 10)
                            {
                                remoteRelease.VersionNumber = jiraVersion.Name.Substring(0, 10);
                            }
                            else
                            {
                                remoteRelease.VersionNumber = jiraVersion.Name;
                            }
                            remoteRelease.Active = true;
                            if (jiraVersion.ReleaseDate.HasValue)
                            {
                                remoteRelease.StartDate = jiraVersion.ReleaseDate.Value.AddDays(-1);
                                remoteRelease.EndDate = jiraVersion.ReleaseDate.Value;
                            }
                            else
                            {
                                remoteRelease.StartDate = DateTime.UtcNow.Date;
                                remoteRelease.EndDate = DateTime.UtcNow.Date.AddDays(5);
                            }
                            remoteRelease.CreatorId = remoteRequirement.AuthorId;
                            remoteRelease.CreationDate = DateTime.UtcNow;
                            remoteRelease.ResourceCount = 1;
                            remoteRelease.DaysNonWorking = 0;
                            remoteRelease = spiraImportExport.Release_Create(remoteRelease, null);

                            //Add a new mapping entry
                            SpiraSoapService.RemoteDataMapping newReleaseMapping = new SpiraSoapService.RemoteDataMapping();
                            newReleaseMapping.ProjectId = projectId;
                            newReleaseMapping.InternalId = remoteRelease.ReleaseId.Value;
                            newReleaseMapping.ExternalKey = jiraVersion.Id.ToString();
                            newReleaseMappings.Add(newReleaseMapping);
                            remoteRequirement.ReleaseId = newReleaseMapping.InternalId;
                            LogTraceEvent(eventLog, "Setting requirement release id to  " + newReleaseMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                        }
                        else
                        {
                            remoteRequirement.ReleaseId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Setting requirement release id to  " + dataMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                        }
                    }

                    //Now we need to see if any of the SpiraTest custom properties that map to JIRA-specific Fields have changed
                    List<JiraCustomFieldValue> jiraCustomFieldValues = ProcessJiraIssueCustomFields(projectId, productName, jiraIssue, requirementCustomProperties, customPropertyMappingList, userMappings, spiraImportExport, customPropertyValueMappingList, remoteRequirement, (requirementComponentMappings != null && requirementComponentMappings.Length > 0));

                    //Finally add or update the requirement in SpiraTest
                    if (requirementId == -1)
                    {
                        //Debug logging - comment out for production code
                        try
                        {
                            remoteRequirement = spiraImportExport.Requirement_Create2(remoteRequirement, null);
                        }
                        catch (FaultException<ValidationFaultMessage> validationException)
                        {
                            string message = "";
                            ValidationFaultMessage validationFaultMessage = validationException.Detail;
                            message = validationFaultMessage.Summary + ": \n";
                            {
                                foreach (ValidationFaultMessageItem messageItem in validationFaultMessage.Messages)
                                {
                                    message += messageItem.FieldName + "=" + messageItem.Message + " \n";
                                }
                            }
                            LogErrorEvent("Error Adding Jira issue " + jiraIssue.Key.ToString() + " to " + productName + " as a new requirement (" + message + ")\n" + validationException.StackTrace, EventLogEntryType.Error);
                            return;
                        }
                        catch (Exception exception)
                        {
                            LogErrorEvent("Error Adding Jira issue " + jiraIssue.Key.ToString() + " to " + productName + " as a new requirement (" + exception.Message + ")\n" + exception.StackTrace, EventLogEntryType.Error);
                            return;
                        }
                        LogTraceEvent(eventLog, "Successfully added Jira issue " + jiraIssue.Key.ToString() + " to " + productName + " as a new requirement.\n", EventLogEntryType.Information);

                        //Extract the SpiraTest requirement and add to mappings table
                        SpiraSoapService.RemoteDataMapping newRequirementMapping = new SpiraSoapService.RemoteDataMapping();
                        newRequirementMapping.ProjectId = projectId;
                        newRequirementMapping.InternalId = remoteRequirement.RequirementId.Value;
                        newRequirementMapping.ExternalKey = jiraIssue.Key.ToString();
                        newRequirementMappings.Add(newRequirementMapping);

                        //Now add any resolutions (need to set the ID)
                        foreach (SpiraSoapService.RemoteComment newComment in newRequirementComments)
                        {
                            newComment.ArtifactId = remoteRequirement.RequirementId.Value;
                            spiraImportExport.Requirement_CreateComment(newComment);
                        }

                        //Next add a link to the JIRA issue to the Spira requirement
                        try
                        {
                            string issueUrl = this.connectionString + "/browse/" + jiraIssue.Key.ToString();
                            SpiraSoapService.RemoteDocument remoteUrl = new SpiraSoapService.RemoteDocument();
                            remoteUrl.Description = "Link to issue in JIRA";
                            remoteUrl.FilenameOrUrl = issueUrl;
                            RemoteLinkedArtifact artifactAttachment = new RemoteLinkedArtifact();
                            artifactAttachment.ArtifactId = remoteRequirement.RequirementId.Value;
                            artifactAttachment.ArtifactTypeId = (int)Constants.ArtifactType.Requirement;
                            remoteUrl.AttachedArtifacts = new RemoteLinkedArtifact[1] { artifactAttachment };
                            spiraImportExport.Document_AddUrl(remoteUrl);
                        }
                        catch (Exception exception)
                        {
                            //Log a message that describes why it's not working
                            LogErrorEvent("Unable to add JIRA hyperlink to the " + productName + " requirement, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                            //Just continue with the rest since it's optional.
                        }

                        //Next add a link to the Spira requirement from the JIRA issue
                        try
                        {
                            string baseUrl = spiraImportExport.System_GetWebServerUrl();
                            string requirementUrl = spiraImportExport.System_GetArtifactUrl((int)Constants.ArtifactType.Requirement, projectId, remoteRequirement.RequirementId.Value, "").Replace("~", baseUrl);
                            jiraManager.AddWebLink(jiraIssue.Key.ToString(), requirementUrl, productName + " " + Constants.REQUIREMENT_PREFIX + ":" + remoteRequirement.RequirementId.Value);
                        }
                        catch (Exception exception)
                        {
                            //Log a message that describes why it's not working
                            LogErrorEvent("Unable to add " + productName + " hyperlink to the JIRA issue, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                            //Just continue with the rest since it's optional.
                        }

                        //Finally try adding any attachments to Spira
                        ProcessJiraIssueAttachments(projectId, productName, spiraImportExport, jiraManager, jiraIssue, Constants.ArtifactType.Requirement, remoteRequirement.RequirementId.Value);
                    }
                    else
                    {
                        spiraImportExport.Requirement_Update(remoteRequirement);

                        //Now add any resolutions
                        foreach (SpiraSoapService.RemoteComment newComment in newRequirementComments)
                        {
                            spiraImportExport.Requirement_CreateComment(newComment);
                        }

                        //Debug logging - comment out for production code
                        LogTraceEvent(eventLog, "Successfully updated\n", EventLogEntryType.Information);
                    }
                }
            }
            catch (FaultException<ValidationFaultMessage> validationException)
            {
                string message = "";
                ValidationFaultMessage validationFaultMessage = validationException.Detail;
                message = validationFaultMessage.Summary + ": \n";
                {
                    foreach (ValidationFaultMessageItem messageItem in validationFaultMessage.Messages)
                    {
                        message += messageItem.FieldName + "=" + messageItem.Message + " \n";
                    }
                }
                LogErrorEvent("Error Inserting/Updating JIRA Issue " + jiraIssue.Key.ToString() + " as a requirement in " + productName + " (" + message + ")\n" + validationException.StackTrace, EventLogEntryType.Error);
            }
            catch (Exception exception)
            {
                //Log and continue execution
                LogErrorEvent("Error Inserting/Updating JIRA Issue in " + productName + " as a requirement: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
            }
        }


        /// <summary>
        /// Processes a new SpiraTest incident record
        /// </summary>
        /// <param name="remoteIncident">The Spira incident</param>
        private void ProcessIncident(int projectId, SoapServiceClient spiraImportExport, RemoteIncident remoteIncident, List<RemoteDataMapping> newIncidentMappings, List<RemoteDataMapping> newReleaseMappings, List<RemoteDataMapping> oldReleaseMappings, Dictionary<int, RemoteDataMapping> customPropertyMappingList, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteCustomProperty[] incidentCustomProperties, RemoteDataMapping[] incidentMappings, JiraProject jiraProject, JiraManager jiraManager, string productName, RemoteDataMapping[] severityMappings, RemoteDataMapping[] priorityMappings, RemoteDataMapping[] statusMappings, RemoteDataMapping[] typeMappings, RemoteDataMapping[] userMappings, RemoteDataMapping[] releaseMappings, RemoteDataMapping[] incidentComponentMappings)
        {
            //Get certain incident fields into local variables (if used more than once)
            int incidentId = remoteIncident.IncidentId.Value;
            int incidentStatusId = remoteIncident.IncidentStatusId.Value;

            //Make sure we've not already loaded this issue
            if (InternalFunctions.FindMappingByInternalId(projectId, incidentId, incidentMappings) == null)
            {
                LogTraceEvent(eventLog, "Creating new JIRA issue for " + productName + " incident IN" + incidentId + "\n", EventLogEntryType.Information);

                //See if this incident has any associations
                SpiraSoapService.RemoteSort associationSort = new RemoteSort();
                associationSort.SortAscending = true;
                associationSort.PropertyName = "CreationDate";
                SpiraSoapService.RemoteAssociation[] remoteIncidentAssociations = spiraImportExport.Association_RetrieveForArtifact((int)Constants.ArtifactType.Incident, incidentId, null, associationSort);

                //Convert the incident description from HTML > Plain Text
                string baseUrl = spiraImportExport.System_GetWebServerUrl();
                LogTraceEvent("Rich Text description = " + remoteIncident.Description);
                string description = InternalFunctions.HtmlRenderAsPlainText(remoteIncident.Description);

                //WINDSTREAM - Description additions per Janet McKee
                description += " -  Incident " + remoteIncident.IncidentId.ToString() + " originally created in SpiraTeam project " + remoteIncident.ProjectName + " by " + remoteIncident.OpenerName ;
                LogTraceEvent("Plain Text description = " + description);

                //If we need to sync attachments, see if any are attached
                SpiraSoapService.RemoteDocument[] remoteDocuments = null;
                if (SYNC_ATTACHMENTS)
                {
                    SpiraSoapService.RemoteSort attachmentSort = new RemoteSort();
                    attachmentSort.SortAscending = true;
                    attachmentSort.PropertyName = "AttachmentId";
                    remoteDocuments = spiraImportExport.Document_RetrieveForArtifact((int)Constants.ArtifactType.Incident, incidentId, null, attachmentSort);
                }

                //Create the JIRA issue and populate the standard fields (that don't need mapping)
                JiraIssue jiraIssue = new JiraIssue();
                jiraIssue.Fields.Project = new JiraProject(jiraProject.Id.Value);
                if (remoteIncident.CreationDate.HasValue)
                {
                    jiraIssue.Fields.Created = remoteIncident.CreationDate.Value;
                }
                else
                {
                    jiraIssue.Fields.Created = DateTime.UtcNow;
                }
                jiraIssue.Fields.Summary = remoteIncident.Name;
                jiraIssue.Fields.Description = description;
                LogTraceEvent(eventLog, "Created JIRA issue and populated Summary and Description\n", EventLogEntryType.Information);

                //Populate the due-date
                if (remoteIncident.StartDate.HasValue)
                {
                    jiraIssue.Fields.DueDate = remoteIncident.StartDate.Value;
                    LogTraceEvent(eventLog, "Populated Due-Date\n", EventLogEntryType.Information);
                }

                //Populate the resolution-date
                if (remoteIncident.ClosedDate.HasValue)
                {
                    jiraIssue.Fields.ResolutionDate = remoteIncident.ClosedDate.Value;
                    LogTraceEvent(eventLog, "Populated Resolution-Date\n", EventLogEntryType.Information);
                }

                //Now get the issue type from the mapping
                SpiraSoapService.RemoteDataMapping dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.IncidentTypeId.Value, typeMappings);
                if (dataMapping == null)
                {
                    //We can't find the matching item so log and move to the next incident
                    LogErrorEvent("Unable to locate mapping entry for incident type " + remoteIncident.IncidentTypeId + " in project " + projectId, EventLogEntryType.Error);
                    return;
                }

                jiraIssue.Fields.IssueType = new IssueType(dataMapping.ExternalKey);
                LogTraceEvent(eventLog, "Set issue type\n", EventLogEntryType.Information);

                //Now get the issue status from the mapping
                dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.IncidentStatusId.Value, statusMappings);
                if (dataMapping == null)
                {
                    //We can't find the matching item so log and move to the next incident
                    LogErrorEvent("Unable to locate mapping entry for incident status " + remoteIncident.IncidentStatusId + " in project " + projectId, EventLogEntryType.Error);
                    return;
                }
                jiraIssue.Fields.Status = new Status(dataMapping.ExternalKey);
                LogTraceEvent(eventLog, "Set issue status\n", EventLogEntryType.Information);

                //Now get the issue priority from the mapping (if priority is set)
                if (remoteIncident.PriorityId.HasValue)
                {
                    dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.PriorityId.Value, priorityMappings);
                    if (dataMapping == null)
                    {
                        //We can't find the matching item so log and just don't set the priority
                        LogErrorEvent("Unable to locate mapping entry for incident priority " + remoteIncident.PriorityId.Value + " in project " + projectId, EventLogEntryType.Warning);
                    }
                    else
                    {
                        jiraIssue.Fields.Priority = new Priority(dataMapping.ExternalKey);
                    }
                }

                //Get a list of JIRA components in the project (used in custom properties)
                List<JiraComponent> jiraComponents = jiraManager.GetComponents(jiraProject.Key);

                //Now get the components from the mapping (introduced in Spira 5.0 instead of using a special custom property)
                if (remoteIncident.ComponentIds != null && remoteIncident.ComponentIds.Length > 0)
                {
                    List<JiraComponent> jiraIssueComponents = new List<JiraComponent>();
                    foreach (int componentId in remoteIncident.ComponentIds)
                    {
                        dataMapping = InternalFunctions.FindMappingByInternalId(projectId, componentId, incidentComponentMappings);
                        if (dataMapping != null)
                        {
                            //Make sure this is actually a JIRA component
                            if (jiraComponents.Any(c => c.IdString == dataMapping.ExternalKey))
                            {
                                jiraIssueComponents.Add(new JiraComponent(dataMapping.ExternalKey));
                            }
                        }
                    }
                    jiraIssue.Fields.Components = jiraIssueComponents;
                }
                //Debug logging - comment out for production code
                LogTraceEvent(eventLog, "Got the component\n", EventLogEntryType.Information);

                //Now set the reporter of the issue
                dataMapping = FindUserMappingByInternalId(remoteIncident.OpenerId.Value, userMappings, spiraImportExport);
                if (dataMapping == null)
                {
                    //We can't find the matching user so just use the external login
                    LogErrorEvent("Unable to locate mapping entry for opener user id " + remoteIncident.OpenerId + " so using the synchronization user for reporter", EventLogEntryType.Warning);
                    jiraIssue.Fields.Reporter = new User(externalLogin);
                }
                else
                {
                    jiraIssue.Fields.Reporter = new User(dataMapping.ExternalKey);
                    LogTraceEvent(eventLog, "Set issue reporter\n", EventLogEntryType.Information);
                }

                //Now set the assignee - set NULL if no assignee (owner) in SpiraTest
                if (remoteIncident.OwnerId.HasValue)
                {
                    dataMapping = FindUserMappingByInternalId(remoteIncident.OwnerId.Value, userMappings, spiraImportExport);
                    if (dataMapping == null)
                    {
                        //We can't find the matching user so just use the external login
                        LogErrorEvent("Unable to locate mapping entry for owner user id " + remoteIncident.OwnerId.Value + " so using the synchronization user for assignee", EventLogEntryType.Warning);
                        jiraIssue.Fields.Assignee = new User(externalLogin);
                    }
                    else
                    {
                        jiraIssue.Fields.Assignee = new User(dataMapping.ExternalKey);
                        LogTraceEvent(eventLog, "Set issue assignee\n", EventLogEntryType.Information);
                    }
                }
                else
                {
                    jiraIssue.Fields.Assignee = null;
                    LogTraceEvent(eventLog, "Set issue as unassigneed\n", EventLogEntryType.Information);
                }

                //Specify the detected-in version/release if applicable
                if (remoteIncident.DetectedReleaseId.HasValue)
                {
                    int detectedReleaseId = remoteIncident.DetectedReleaseId.Value;
                    dataMapping = InternalFunctions.FindMappingByInternalId(projectId, detectedReleaseId, releaseMappings);
                    if (dataMapping == null)
                    {
                        //See if it's a newly added version in this sync cycle
                        dataMapping = InternalFunctions.FindMappingByInternalId(projectId, detectedReleaseId, newReleaseMappings.ToArray());
                    }
                    string jiraVersionId = null;
                    if (dataMapping == null)
                    {
                        //We can't find the matching item so need to create a new version in JIRA and add to mappings
                        //Since version numbers are now unique in both systems, we can simply use that
                        LogTraceEvent(eventLog, "Adding new version in jira for release " + detectedReleaseId + "\n", EventLogEntryType.Information);
                        JiraVersion jiraVersion = new JiraVersion();
                        jiraVersion.Project = jiraProject.Key;
                        jiraVersion.Name = remoteIncident.DetectedReleaseVersionNumber;
                        jiraVersion.Archived = false;
                        jiraVersion.Released = false;
                        try
                        {
                            jiraVersion = jiraManager.AddVersion(jiraVersion);

                            //Add a new mapping entry
                            /* WINDSTREAM dont add  any new  releases to SPIRA
                            SpiraSoapService.RemoteDataMapping newReleaseMapping = new SpiraSoapService.RemoteDataMapping();
                            newReleaseMapping.ProjectId = projectId;
                            newReleaseMapping.InternalId = detectedReleaseId;
                            newReleaseMapping.ExternalKey = jiraVersion.Id.ToString();
                            newReleaseMappings.Add(newReleaseMapping);
                            WINDSTREAM */
                            jiraVersionId = jiraVersion.Id.ToString();
                        }
                        catch (Exception )
                        {
                            jiraVersionId = dataMapping.ExternalKey;
                        }
                    }
                    else
                    {
                        jiraVersionId = dataMapping.ExternalKey;
                    }
                    //Get the list of versions from the server and find the one that corresponds to the SpiraTest Release
                    LogTraceEvent(eventLog, "Looking for JIRA affected version: " + jiraVersionId + "\n", EventLogEntryType.Information);
                    List<JiraVersion> jiraVersions = jiraManager.GetVersions(jiraProject.Key);
                    if (jiraVersions != null && jiraVersions.Count > 0)
                    {
                        bool matchFound = false;
                        foreach (JiraVersion jiraVersion in jiraVersions)
                        {
                            //See if we have an match, if not remove
                            if (jiraVersion.Id.ToString() == jiraVersionId)
                            {
                                if (jiraIssue.Fields.Versions == null)
                                {
                                    jiraIssue.Fields.Versions = new List<JiraVersion>();
                                }
                                jiraIssue.Fields.Versions.Add(jiraVersion);
                                LogTraceEvent(eventLog, "Found JIRA affected version: " + jiraVersionId + "\n", EventLogEntryType.Information);
                                matchFound = true;
                            }
                        }
                        if (!matchFound)
                        {
                            //We can't find the matching item so log and just don't set the release
                            LogErrorEvent("Unable to locate JIRA affected version " + jiraVersionId + " in project " + jiraProject, EventLogEntryType.Warning);

                            //Add this to the list of mappings to remove
                            SpiraSoapService.RemoteDataMapping oldReleaseMapping = new SpiraSoapService.RemoteDataMapping();
                            oldReleaseMapping.ProjectId = projectId;
                            oldReleaseMapping.InternalId = detectedReleaseId;
                            oldReleaseMapping.ExternalKey = jiraVersionId;
                            oldReleaseMappings.Add(oldReleaseMapping);
                        }
                    }
                    else
                    {
                        LogTraceEvent(eventLog, "No versions retrieved from JIRA" + "\n", EventLogEntryType.Information);
                    }
                }
                LogTraceEvent(eventLog, "Set issue affected version\n", EventLogEntryType.Information);

                //Specify the resolved-in version/release if applicable
                if (remoteIncident.ResolvedReleaseId.HasValue)
                {
                    int resolvedReleaseId = remoteIncident.ResolvedReleaseId.Value;
                    dataMapping = InternalFunctions.FindMappingByInternalId(projectId, resolvedReleaseId, releaseMappings);
                    if (dataMapping == null)
                    {
                        //See if it's a newly added version in this sync cycle
                        dataMapping = InternalFunctions.FindMappingByInternalId(projectId, resolvedReleaseId, newReleaseMappings.ToArray());
                    }
                    string jiraVersionId = null;
                    if (dataMapping == null)
                    {
                        //We can't find the matching item so need to create a new version in JIRA and add to mappings
                        //Since version numbers are now unique in both systems, we can simply use that
                        LogTraceEvent(eventLog, "Adding new version in jira for release " + resolvedReleaseId + "\n", EventLogEntryType.Information);
                        JiraVersion jiraVersion = new JiraVersion();
                        jiraVersion.Name = remoteIncident.ResolvedReleaseVersionNumber;
                        jiraVersion.Project = jiraProject.Key;
                        jiraVersion.Archived = false;
                        jiraVersion.Released = false;
                        jiraVersion = jiraManager.AddVersion(jiraVersion);

                        //Add a new mapping entry
                        /* WINDSTREAM dont add new releases to SPIRA
                        SpiraSoapService.RemoteDataMapping newReleaseMapping = new SpiraSoapService.RemoteDataMapping();
                        newReleaseMapping.ProjectId = projectId;
                        newReleaseMapping.InternalId = resolvedReleaseId;
                        newReleaseMapping.ExternalKey = jiraVersion.Id.ToString();
                        newReleaseMappings.Add(newReleaseMapping);
                        WINDSTREAM */
                        jiraVersionId = jiraVersion.Id.ToString();
                    }
                    else
                    {
                        jiraVersionId = dataMapping.ExternalKey;
                    }
                    //Get the list of versions from the server and find the one that corresponds to the SpiraTest Release
                    List<JiraVersion> jiraVersions = jiraManager.GetVersions(jiraProject.Key);
                    LogTraceEvent(eventLog, "Looking for JIRA fix version: " + jiraVersionId + "\n", EventLogEntryType.Information);
                    if (jiraVersions != null && jiraVersions.Count > 0)
                    {
                        bool matchFound = false;
                        foreach (JiraVersion jiraVersion in jiraVersions)
                        {
                            //See if we have an match, if not remove
                            if (jiraVersion.Id.ToString() == jiraVersionId)
                            {
                                if (jiraIssue.Fields.FixVersions == null)
                                {
                                    jiraIssue.Fields.FixVersions = new List<JiraVersion>();
                                }
                                jiraIssue.Fields.FixVersions.Add(jiraVersion);
                                LogTraceEvent(eventLog, "Found JIRA fix version: " + jiraVersionId + "\n", EventLogEntryType.Information);
                                matchFound = true;
                            }
                        }
                        if (!matchFound)
                        {
                            //We can't find the matching item so log and just don't set the release
                            LogErrorEvent("Unable to locate JIRA fix version " + jiraVersionId + " in project " + jiraProject, EventLogEntryType.Warning);

                            //Add this to the list of mappings to remove
                            SpiraSoapService.RemoteDataMapping oldReleaseMapping = new SpiraSoapService.RemoteDataMapping();
                            oldReleaseMapping.ProjectId = projectId;
                            oldReleaseMapping.InternalId = resolvedReleaseId;
                            oldReleaseMapping.ExternalKey = jiraVersionId;
                            oldReleaseMappings.Add(oldReleaseMapping);
                        }
                    }
                    else
                    {
                        LogTraceEvent(eventLog, "No versions retrieved from JIRA" + "\n", EventLogEntryType.Information);
                    }
                }
                LogTraceEvent(eventLog, "Set issue fix version\n", EventLogEntryType.Information);

                //Setup the dictionary to hold the various custom properties to set on the Jira issue
                Dictionary<int, CustomPropertyValue> customPropertyValues = new Dictionary<int, CustomPropertyValue>();

                //Now iterate through the incident custom properties
                long securityLevelId = 0;
                if (remoteIncident.CustomProperties != null && remoteIncident.CustomProperties.Length > 0)
                {
                    foreach (RemoteArtifactCustomProperty artifactCustomProperty in remoteIncident.CustomProperties)
                    {
                        //Handle user, list and non-list separately since only the list types need to have value mappings
                        RemoteCustomProperty customProperty = artifactCustomProperty.Definition;
                        if (customProperty != null && customProperty.CustomPropertyId.HasValue)
                        {
                            if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.List)
                            {
                                //Single-Select List
                                LogTraceEvent(eventLog, "Checking list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);

                                //See if we have a custom property value set
                                //Get the corresponding external custom field (if there is one)
                                if (artifactCustomProperty.IntegerValue.HasValue && customPropertyMappingList != null && customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                                {
                                    LogTraceEvent(eventLog, "Got value for list custom property: " + customProperty.Name + " (" + artifactCustomProperty.IntegerValue.Value + ")\n", EventLogEntryType.Information);
                                    SpiraSoapService.RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                                    if (customPropertyDataMapping != null)
                                    {
                                        string externalCustomField = customPropertyDataMapping.ExternalKey;

                                        //Get the corresponding external custom field value (if there is one)
                                        if (!String.IsNullOrEmpty(externalCustomField) && customPropertyValueMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                                        {
                                            SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                            if (customPropertyValueMappings != null)
                                            {
                                                SpiraSoapService.RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByInternalId(projectId, artifactCustomProperty.IntegerValue.Value, customPropertyValueMappings);
                                                if (customPropertyValueMapping != null)
                                                {
                                                    string externalCustomFieldValue = customPropertyValueMapping.ExternalKey;

                                                    //See if we have one of the special standard Jira field that it maps to
                                                    if (!String.IsNullOrEmpty(externalCustomFieldValue))
                                                    {
                                                        if (externalCustomField == JIRA_SPECIAL_FIELD_COMPONENT)
                                                        {
                                                            //Make sure we've not already handled the components using the new Spira standard field
                                                            if (remoteIncident.ComponentIds == null || remoteIncident.ComponentIds.Length == 0)
                                                            {
                                                                //Now set the value of the jira issue's component
                                                                JiraComponent jiraComponent = new JiraComponent(externalCustomFieldValue);
                                                                LogTraceEvent(eventLog, "Added JIRA component: " + jiraComponent.IdString + "\n", EventLogEntryType.Information);
                                                                if (jiraIssue.Fields.Components == null)
                                                                {
                                                                    jiraIssue.Fields.Components = new List<JiraComponent>();
                                                                }
                                                                jiraIssue.Fields.Components.Add(jiraComponent);
                                                            }
                                                        }
                                                        else if (externalCustomField == JIRA_SPECIAL_FIELD_RESOLUTION)
                                                        {
                                                            //Now set the value of the jira issue's resolution
                                                            LogTraceEvent(eventLog, "Added JIRA resolution: " + externalCustomFieldValue + ")\n", EventLogEntryType.Information);
                                                            jiraIssue.Fields.Resolution = new Resolution(externalCustomFieldValue);
                                                        }
                                                        else if (externalCustomField == JIRA_SPECIAL_FIELD_SECURITY_LEVEL)
                                                        {
                                                            //Now set the value of the jira issue's security level id
                                                            long externalCustomFieldLongValue;
                                                            if (Int64.TryParse(externalCustomFieldValue, out externalCustomFieldLongValue))
                                                            {
                                                                securityLevelId = externalCustomFieldLongValue;
                                                            }
                                                            else
                                                            {
                                                                LogErrorEvent("The Security Level external key '" + externalCustomFieldValue + "' needs to be an integer, so ignoring", EventLogEntryType.Warning);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            int customFieldId;
                                                            if (Int32.TryParse(externalCustomField, out customFieldId))
                                                            {
                                                                //This needs to be added to the list of JIRA custom properties
                                                                CustomPropertyValue cpv = new CustomPropertyValue();
                                                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.List;
                                                                cpv.StringValue = externalCustomFieldValue;
                                                                customPropertyValues.Add(customFieldId, cpv);
                                                                LogTraceEvent(eventLog, "Added single-list custom property field value: " + customProperty.Name + " (Value=" + externalCustomFieldValue + ")\n", EventLogEntryType.Information);
                                                            }
                                                            else
                                                            {
                                                                LogErrorEvent("Unable to set a value on JIRA custom field '" + externalCustomField + "' because the custom field id is not an integer.", EventLogEntryType.Warning);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                LogTraceEvent(eventLog, "Finished with list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);

                            }
                            else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.MultiList)
                            {
                                //Multi-Select List
                                LogTraceEvent(eventLog, "Checking multi-list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);

                                //See if we have a custom property value set
                                //Get the corresponding external custom field (if there is one)
                                if (artifactCustomProperty.IntegerListValue != null && artifactCustomProperty.IntegerListValue.Length > 0 && customPropertyMappingList != null && customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                                {
                                    LogTraceEvent(eventLog, "Got values for multi-list custom property: " + customProperty.Name + " (Count=" + artifactCustomProperty.IntegerListValue.Length + ")\n", EventLogEntryType.Information);
                                    SpiraSoapService.RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                                    if (customPropertyDataMapping != null && !String.IsNullOrEmpty(customPropertyDataMapping.ExternalKey))
                                    {
                                        string externalCustomField = customPropertyDataMapping.ExternalKey;
                                        LogTraceEvent(eventLog, "Got external key for multi-list custom property: " + customProperty.Name + " = " + externalCustomField + "\n", EventLogEntryType.Information);

                                        //Loop through each value in the list
                                        List<string> externalCustomFieldValues = new List<string>();
                                        foreach (int customPropertyListValue in artifactCustomProperty.IntegerListValue)
                                        {
                                            //Get the corresponding external custom field value (if there is one)
                                            if (customPropertyValueMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                                            {
                                                SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                                if (customPropertyValueMappings != null)
                                                {
                                                    SpiraSoapService.RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByInternalId(projectId, customPropertyListValue, customPropertyValueMappings);
                                                    if (customPropertyValueMapping != null)
                                                    {
                                                        LogTraceEvent(eventLog, "Added multi-list custom property field value: " + customProperty.Name + " (Value=" + customPropertyValueMapping.ExternalKey + ")\n", EventLogEntryType.Information);
                                                        externalCustomFieldValues.Add(customPropertyValueMapping.ExternalKey);
                                                    }
                                                }
                                            }
                                        }

                                        //See if we have one of the special standard Jira field that it maps to
                                        LogTraceEvent(eventLog, "Got mapped values for multi-list custom property: " + customProperty.Name + " (Count=" + externalCustomFieldValues.Count + ")\n", EventLogEntryType.Information);
                                        if (externalCustomFieldValues.Count > 0)
                                        {
                                            if (externalCustomField == JIRA_SPECIAL_FIELD_COMPONENT)
                                            {
                                                LogTraceEvent(eventLog, "Custom property is special JIRA Component field.\n", EventLogEntryType.Information);
                                                //Now set the value of the jira issue's component
                                                foreach (string externalCustomFieldValue in externalCustomFieldValues)
                                                {
                                                    //Need to get the component ID from the value
                                                    if (jiraComponents != null)
                                                    {
                                                        JiraComponent jiraComponent = jiraComponents.FirstOrDefault(c => c.Name == externalCustomFieldValue);
                                                        if (jiraComponent != null)
                                                        {
                                                            LogTraceEvent(eventLog, "Added JIRA component: " + jiraComponent.IdString + "\n", EventLogEntryType.Information);
                                                            JiraComponent newJiraComponent = new JiraComponent(jiraComponent.IdString);
                                                            if (jiraIssue.Fields.Components == null)
                                                            {
                                                                jiraIssue.Fields.Components = new List<JiraComponent>();
                                                            }
                                                            jiraIssue.Fields.Components.Add(newJiraComponent);
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                int customFieldId;
                                                if (Int32.TryParse(externalCustomField, out customFieldId))
                                                {
                                                    //This needs to be added to the list of JIRA custom properties
                                                    CustomPropertyValue cpv = new CustomPropertyValue();
                                                    cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.MultiList;
                                                    cpv.MultiListValue = new List<string>();
                                                    foreach (string externalCustomFieldValue in externalCustomFieldValues)
                                                    {
                                                        cpv.MultiListValue.Add(externalCustomFieldValue);
                                                    }
                                                    customPropertyValues.Add(customFieldId, cpv);
                                                }
                                                else
                                                {
                                                    LogErrorEvent("Unable to set a value on JIRA custom field '" + externalCustomField + "' because the custom field id is not an integer.", EventLogEntryType.Warning);
                                                }
                                            }
                                        }
                                    }
                                }
                                LogTraceEvent(eventLog, "Finished with multi-list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);
                            }
                            else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.User)
                            {
                                LogTraceEvent(eventLog, "Checking user custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);
                                //See if we have a custom property value set
                                if (artifactCustomProperty.IntegerValue.HasValue)
                                {
                                    SpiraSoapService.RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                                    if (customPropertyDataMapping != null && !String.IsNullOrEmpty(customPropertyDataMapping.ExternalKey))
                                    {
                                        string externalCustomField = customPropertyDataMapping.ExternalKey;
                                        LogTraceEvent(eventLog, "Got external key for user custom property: " + customProperty.Name + " = " + externalCustomField + "\n", EventLogEntryType.Information);

                                        LogTraceEvent(eventLog, "Got value for user custom property: " + customProperty.Name + " (" + artifactCustomProperty.IntegerValue.Value + ")\n", EventLogEntryType.Information);
                                        //Get the corresponding JIRA user (if there is one)
                                        dataMapping = FindUserMappingByInternalId(artifactCustomProperty.IntegerValue.Value, userMappings, spiraImportExport);
                                        if (dataMapping != null)
                                        {
                                            int customFieldId;
                                            if (Int32.TryParse(externalCustomField, out customFieldId))
                                            {
                                                //This needs to be added to the list of JIRA custom properties
                                                CustomPropertyValue cpv = new CustomPropertyValue();
                                                cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.User;
                                                cpv.StringValue = dataMapping.ExternalKey;
                                                customPropertyValues.Add(customFieldId, cpv);
                                                LogTraceEvent(eventLog, "Added user custom property field value: " + customProperty.Name + " (Value=" + dataMapping.ExternalKey + ")\n", EventLogEntryType.Information);
                                            }
                                            else
                                            {
                                                LogErrorEvent("Unable to set a value on JIRA custom field '" + externalCustomField + "' because the custom field id is not an integer.", EventLogEntryType.Warning);
                                            }
                                        }
                                        else
                                        {
                                            LogErrorEvent("Unable to find a matching JIRA user for " + productName + " user with ID=" + artifactCustomProperty.IntegerValue.Value + " so leaving property null.", EventLogEntryType.Warning);
                                        }
                                    }
                                }
                                LogTraceEvent(eventLog, "Finished with user custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);
                            }
                            else
                            {
                                LogTraceEvent(eventLog, "Checking non-list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);
                                //See if we have a custom property value set
                                if (!String.IsNullOrEmpty(artifactCustomProperty.StringValue) || artifactCustomProperty.BooleanValue.HasValue
                                    || artifactCustomProperty.DateTimeValue.HasValue || artifactCustomProperty.DecimalValue.HasValue
                                    || artifactCustomProperty.IntegerValue.HasValue)
                                {
                                    LogTraceEvent(eventLog, "Got value for non-list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);
                                    //Get the corresponding external custom field (if there is one)
                                    if (customPropertyMappingList != null && customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                                    {
                                        SpiraSoapService.RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                                        if (customPropertyDataMapping != null)
                                        {
                                            string externalCustomField = customPropertyDataMapping.ExternalKey;

                                            //See if we have one of the special standard Jira field that it maps to
                                            if (!String.IsNullOrEmpty(externalCustomField))
                                            {
                                                if (externalCustomField == JIRA_SPECIAL_FIELD_ENVIRONMENT)
                                                {
                                                    jiraIssue.Fields.Environment = artifactCustomProperty.StringValue;
                                                }
                                                else if (externalCustomField == JIRA_SPECIAL_FIELD_ISSUE_KEY)
                                                {
                                                    //Handled later
                                                }
                                                else
                                                {
                                                    int customFieldId;
                                                    if (Int32.TryParse(externalCustomField, out customFieldId))
                                                    {
                                                        //This needs to be added to the list of JIRA custom properties
                                                        customPropertyValues.Add(customFieldId, new CustomPropertyValue(artifactCustomProperty));
                                                    }
                                                    else
                                                    {
                                                        LogErrorEvent("Unable to set a value on JIRA custom field '" + externalCustomField + "' because the custom field id is not an integer.", EventLogEntryType.Warning);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                LogTraceEvent(eventLog, "Finished with text custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);
                            }
                        }
                    }
                }
                LogTraceEvent(eventLog, "Finished processing issue custom values\n", EventLogEntryType.Information);

                //The special case of SpiraTest Severity needs to be checked
                //This allows us to populate a Jira custom field from the standard SpiraTest Severity field
                if (severityCustomFieldId.HasValue && remoteIncident.SeverityId.HasValue)
                {
                    dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.SeverityId.Value, severityMappings);
                    if (dataMapping == null)
                    {
                        //We can't find the matching item so log and just don't set the severity
                        LogErrorEvent("Unable to locate mapping entry for incident severity " + remoteIncident.SeverityId.Value + " in project " + projectId, EventLogEntryType.Warning);
                    }
                    else
                    {
                        //Set the appropriate custom property in JIRA
                        string severityCustomFieldValueId = dataMapping.ExternalKey;
                        LogTraceEvent(eventLog, "Added severity to JIRA custom property field: id=" + severityCustomFieldId.Value + " (Value=" + severityCustomFieldValueId + ")\n", EventLogEntryType.Information);
                        CustomPropertyValue cpv = new CustomPropertyValue();
                        cpv.CustomPropertyType = CustomPropertyValue.CustomPropertyTypeEnum.List;
                        cpv.StringValue = severityCustomFieldValueId;
                        customPropertyValues.Add(severityCustomFieldId.Value, cpv);
                    }
                    LogTraceEvent(eventLog, "Set issue custom severity\n", EventLogEntryType.Information);
                }

                //Now populate all the custom property values onto the Jira issue object
                if (customPropertyValues.Count > 0)
                {
                    foreach (KeyValuePair<int, CustomPropertyValue> customPropertyValue in customPropertyValues)
                    {
                        JiraCustomFieldValue customFieldValue = new JiraCustomFieldValue(customPropertyValue.Key);
                        customFieldValue.Value = customPropertyValue.Value;
                        jiraIssue.CustomFieldValues.Add(customFieldValue);
                    }
                    LogTraceEvent(eventLog, "Set custom values onto JIRA issue object\n", EventLogEntryType.Information);
                }

                //Finally create the issue itself (note that we don't populate any resolution
                //information, as that is 'owned' JIRA and passed back to SpiraTest
                //Use the appropriate function depending on whether we're using security levels
                if (this.useSecurityLevel && securityLevelId != 0)
                {
                    jiraIssue.Fields.Security = new SecurityLevel(securityLevelId.ToString());
                }
                JiraIssueLink jiraIssueLink = jiraManager.CreateIssue(jiraIssue, this.jiraCreateMetaData);

                //Add attachments to the issue if appropriate
                if (remoteDocuments != null)
                {
                    foreach (SpiraSoapService.RemoteDocument remoteDocument in remoteDocuments)
                    {
                        //See if we have a file attachment or simple URL
                        if (remoteDocument.AttachmentTypeId == (int)Constants.AttachmentType.File)
                        {
                            try
                            {
                                //Get the binary data for the attachment
                                byte[] binaryData = spiraImportExport.Document_OpenFile(remoteDocument.AttachmentId.Value);
                                if (binaryData != null && binaryData.Length > 0)
                                {
                                    string filename = remoteDocument.FilenameOrUrl;
                                    jiraManager.AddAttachmentsToIssue(jiraIssueLink.Key.ToString(), filename, binaryData);
                                }
                            }
                            catch (Exception exception)
                            {
                                //Log an error and continue because this can fail if the files are too large
                                LogErrorEvent("Error adding " + productName + " incident attachment DC" + remoteDocument.AttachmentId.Value + " to JIRA: " + exception.Message + "\n. (The issue itself was added.)\n Stack Trace: " + exception.StackTrace, EventLogEntryType.Error);
                            }
                        }
                        if (remoteDocument.AttachmentTypeId == (int)Constants.AttachmentType.URL)
                        {
                            try
                            {
                                //Add as a web link
                                string url = remoteDocument.FilenameOrUrl;
                                jiraManager.AddWebLink(jiraIssueLink.Key.ToString(), url, url);
                            }
                            catch (Exception exception)
                            {
                                //Log an error and continue because this can fail if the files are too large
                                LogErrorEvent("Error adding " + productName + " incident attachment DC" + remoteDocument.AttachmentId.Value + " to JIRA: " + exception.Message + "\n. (The issue itself was added.)\n Stack Trace: " + exception.StackTrace, EventLogEntryType.Error);
                            }
                        }
                    }
                }

                //We also add a link to the JIRA issue to the Spira incident
                //Needs to happen after attachments are added so that we don't get a self-referential link added to JIRA !!
                try
                {
                    string issueUrl = this.connectionString + "/browse/" + jiraIssueLink.Key.ToString();
                    SpiraSoapService.RemoteDocument remoteUrl = new SpiraSoapService.RemoteDocument();
                    remoteUrl.Description = "Link to issue in JIRA";
                    remoteUrl.FilenameOrUrl = issueUrl;
                    RemoteLinkedArtifact artifactAttachment = new RemoteLinkedArtifact();
                    artifactAttachment.ArtifactId = incidentId;
                    artifactAttachment.ArtifactTypeId = (int)Constants.ArtifactType.Incident;
                    remoteUrl.AttachedArtifacts = new RemoteLinkedArtifact[1] { artifactAttachment };
                    spiraImportExport.Document_AddUrl(remoteUrl);
                }
                catch (Exception exception)
                {
                    //Log a message that describes why it's not working
                    LogErrorEvent("Unable to add JIRA hyperlink to the " + productName + " incident, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                    //Just continue with the rest since it's optional.
                }

                //Now add a link to the Spira incident from the JIRA issue
                try
                {
                    string incidentUrl = spiraImportExport.System_GetArtifactUrl((int)Constants.ArtifactType.Incident, projectId, incidentId, "").Replace("~", baseUrl);
                    jiraManager.AddWebLink(jiraIssueLink.Key, incidentUrl, productName + " " + Constants.INCIDENT_PREFIX + ":" + incidentId);
                }
                catch (Exception exception)
                {
                    //Log a message that describes why it's not working
                    LogErrorEvent("Unable to add " + productName + " hyperlink to the JIRA issue, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                    //Just continue with the rest since it's optional.
                }

                //Iterate through the associations and see if we need to add any to the new JIRA issue
                if (remoteIncidentAssociations != null)
                {
                    foreach (RemoteAssociation remoteIncidentAssociation in remoteIncidentAssociations)
                    {
                        //See what type of association we have
                        if (remoteIncidentAssociation.DestArtifactTypeId == (int)Constants.ArtifactType.Incident)
                        {
                            //Add the incident-to-incident association as an issue-to-issue association
                            if (!String.IsNullOrEmpty(this.issueLinkType))
                            {
                                //We need to find the JIRA issue that corresponds to the destination Spira ID
                                string destIssueKey = "";
                                dataMapping = InternalFunctions.FindMappingByInternalId(remoteIncidentAssociation.DestArtifactId, incidentMappings);
                                if (dataMapping != null)
                                {
                                    destIssueKey = dataMapping.ExternalKey;
                                }
                                else
                                {
                                    dataMapping = InternalFunctions.FindMappingByInternalId(remoteIncidentAssociation.DestArtifactId, newIncidentMappings.ToArray());
                                    if (dataMapping != null)
                                    {
                                        destIssueKey = dataMapping.ExternalKey;
                                    }
                                }
                                if (!String.IsNullOrEmpty(destIssueKey))
                                {
                                    try
                                    {
                                        jiraManager.AddIssueLink(this.issueLinkType, jiraIssueLink.Key, destIssueKey, remoteIncidentAssociation.Comment);
                                    }
                                    catch (Exception exception)
                                    {
                                        //If the instance of JIRA is not configured to allow links to be added, just log and continue
                                        LogErrorEvent("Unable to add JIRA issue-to-issue link in project " + jiraProject.Key + ", error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                                    }
                                }
                            }
                        }
                        if (remoteIncidentAssociation.DestArtifactTypeId == (int)Constants.ArtifactType.Requirement)
                        {
                            string requirementUrl = spiraImportExport.System_GetArtifactUrl((int)Constants.ArtifactType.Requirement, projectId, remoteIncidentAssociation.DestArtifactId, "").Replace("~", baseUrl);

                            //Add as hyperlink from to the Spira requirement
                            try
                            {
                                jiraManager.AddWebLink(jiraIssueLink.Key, requirementUrl, productName + " " + Constants.REQUIREMENT_PREFIX + ":" + remoteIncidentAssociation.DestArtifactId);
                            }
                            catch (Exception exception)
                            {
                                //If the instance of JIRA is not configured to allow links to be added, just log and continue
                                LogErrorEvent("Unable to add JIRA web link to issue " + jiraIssueLink.Key + ", error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                            }
                        }
                        if (remoteIncidentAssociation.DestArtifactTypeId == (int)Constants.ArtifactType.TestRun)
                        {
                            //Add as hyperlink inside the text of the JIRA issue
                            string testRunUrl = spiraImportExport.System_GetArtifactUrl((int)Constants.ArtifactType.TestRun, projectId, remoteIncidentAssociation.DestArtifactId, "").Replace("~", baseUrl);

                            //Add as hyperlink from to the Spira test run
                            try
                            {
                                jiraManager.AddWebLink(jiraIssueLink.Key, testRunUrl, productName + " " + Constants.TEST_RUN_PREFIX + ":" + remoteIncidentAssociation.DestArtifactId);
                            }
                            catch (Exception exception)
                            {
                                //If the instance of JIRA is not configured to allow links to be added, just log and continue
                                LogErrorEvent("Unable to add JIRA web link to issue " + jiraIssueLink.Key + ", error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                            }
                        }
                    }
                }

                //Extract the jira Issue id and add to mappings table
                SpiraSoapService.RemoteDataMapping newIncidentMapping = new SpiraSoapService.RemoteDataMapping();
                newIncidentMapping.ProjectId = projectId;
                newIncidentMapping.InternalId = incidentId;
                newIncidentMapping.ExternalKey = jiraIssueLink.Key.ToString();
                newIncidentMappings.Add(newIncidentMapping);

                //If we have a mapping for the JIRA KEY also set this custom property on the SpiraTest incident
                if (customPropertyMappingList != null && customPropertyMappingList.Count > 0 && customPropertyMappingList.Values != null && incidentCustomProperties != null && incidentCustomProperties.Length > 0)
                {
                    RemoteDataMapping jiraKeyDataMapping = null;
                    foreach (RemoteDataMapping val in customPropertyMappingList.Values)
                    {
                        if (val != null && val.ExternalKey == JIRA_SPECIAL_FIELD_ISSUE_KEY)
                        {
                            jiraKeyDataMapping = val;
                        }
                    }
                    if (jiraKeyDataMapping != null)
                    {
                        //Find the custom property name
                        RemoteCustomProperty jiraKeyCustomProp = incidentCustomProperties.FirstOrDefault(cp => cp.CustomPropertyId == jiraKeyDataMapping.InternalId);
                        if (jiraKeyCustomProp != null)
                        {
                            InternalFunctions.SetCustomPropertyValue(remoteIncident, jiraKeyCustomProp.PropertyNumber, jiraIssueLink.Key.ToString());
                            spiraImportExport.Incident_Update(remoteIncident);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Executes the data-sync functionality between the two systems
        /// </summary>
        /// <param name="LastSyncDate">The last date/time the plug-in was successfully executed (UTC)</param>
        /// <param name="serverDateTime">The current date/time on the server (UTC)</param>
        /// <returns>Code denoting success, failure or warning</returns>
        public ServiceReturnType Execute(Nullable<DateTime> lastSyncDate, DateTime serverDateTime)
        {
            //Make sure the object has not been already disposed
            if (this.disposed)
            {
                throw new ObjectDisposedException(DATA_SYNC_NAME + " has been disposed already.");
            }

            try
            {
                LogTraceEvent(eventLog, "Starting " + DATA_SYNC_NAME + " data synchronization", EventLogEntryType.Information);

                //Instantiate the SpiraTest web-service proxy class
                Uri spiraUri = new Uri(this.webServiceBaseUrl + Constants.WEB_SERVICE_URL_SUFFIX);
                SpiraSoapService.SoapServiceClient spiraImportExport = SpiraClientFactory.CreateClient(spiraUri);

                //Allow JIRA self-signed certs
                //http://stackoverflow.com/questions/2859790/the-request-was-aborted-could-not-create-ssl-tls-secure-channel
                ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback(Certificates.ValidateRemoteCertificate);

                //Instantiate the JIRA web service proxy class
                JiraClient.JiraManager jiraManager = new JiraClient.JiraManager(this.connectionString, this.externalLogin, this.externalPassword, this.eventLog, this.traceLogging);                

                //First lets get the product name we should be referring to
                string productName = spiraImportExport.System_GetProductName();

                //**** Next lets load in the project and user mappings ****
                bool success = spiraImportExport.Connection_Authenticate2(internalLogin, internalPassword, DATA_SYNC_NAME);
                if (!success)
                {
                    //We can't authenticate so end
                    LogErrorEvent("Unable to authenticate with " + productName + " API, stopping data-synchronization", EventLogEntryType.Error);
                    return ServiceReturnType.Error;
                }
                SpiraSoapService.RemoteDataMapping[] projectMappings = spiraImportExport.DataMapping_RetrieveProjectMappings(dataSyncSystemId);
                SpiraSoapService.RemoteDataMapping[] userMappings = spiraImportExport.DataMapping_RetrieveUserMappings(dataSyncSystemId);

                //Next verify that we can connect to JIRA by making a very simple request
                string jiraPermissions = "";
                try
                {
                    jiraManager.UseDefaultCredentials = true;   //Needed for JIRA SSO situations
                    jiraPermissions = jiraManager.GetPermissions();
                }
                catch (WebException exception)
                {
                    //We can't authenticate so end
                    LogErrorEvent("Unable to connect to JIRA, please check that the external login has the appropriate permissions (" + exception.Message + ")", EventLogEntryType.Error);
                    return ServiceReturnType.Error;
                }
                if (String.IsNullOrEmpty(jiraPermissions))
                {
                    //We can't authenticate so end
                    LogErrorEvent("Unable to connect to JIRA, please check that the external login has the appropriate permissions", EventLogEntryType.Error);
                    return ServiceReturnType.Error;
                }

                //Get the list of projects the user has access to
                List<JiraProject> jiraProjects = jiraManager.GetProjects();

                //Get the create incident meta-data for all projects (so that we can verify which fields we need to provide)
                this.jiraCreateMetaData = jiraManager.GetCreateMetaData();

                //Loop for each of the projects in the project mapping
                foreach (SpiraSoapService.RemoteDataMapping projectMapping in projectMappings)
                {
                    //Get the SpiraTest project id equivalent Jira project identifier
                    int projectId = projectMapping.InternalId;
                    string jiraProjectKey = projectMapping.ExternalKey;

                    //The following line was recommended as being slightly better in terms of not getting errors
                    //when JIRA is misconfigured in other projects, but we need to test it thoroughly first
                    //Get the create incident meta-data for the specific (so that we can verify which fields we need to provide)
                    //this.jiraCreateMetaData = jiraManager.GetCreateMetaData(jiraProjectKey);

                    //Re-authenticate with Spira to avoid potential timeout issues
                    success = spiraImportExport.Connection_Authenticate2(internalLogin, internalPassword, DATA_SYNC_NAME);
                    if (!success)
                    {
                        //We can't authenticate so end
                        LogErrorEvent("Unable to authenticate with " + productName + " API, stopping data-synchronization", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }

                    //Connect to the SpiraTest project
                    success = spiraImportExport.Connection_ConnectToProject(projectId);
                    if (!success)
                    {
                        //We can't connect so go to next project
                        LogErrorEvent("Unable to connect to " + productName + " project PR" + projectId + ", please check that the " + productName + " login has the appropriate permissions", EventLogEntryType.Error);
                        continue;
                    }
                     

                    //Get the list of project-specific mappings from the data-mapping repository
                    SpiraSoapService.RemoteDataMapping[] incidentSeverityMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Incident_Severity);
                    SpiraSoapService.RemoteDataMapping[] incidentPriorityMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Incident_Priority);
                    SpiraSoapService.RemoteDataMapping[] incidentStatusMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Incident_Status);
                    SpiraSoapService.RemoteDataMapping[] incidentTypeMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Incident_Type);
                    SpiraSoapService.RemoteDataMapping[] incidentComponentMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Incident_Component);
                    SpiraSoapService.RemoteDataMapping[] requirementImportanceMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Requirement_Importance);
                    SpiraSoapService.RemoteDataMapping[] requirementStatusMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Requirement_Status);
                    SpiraSoapService.RemoteDataMapping[] requirementTypeMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Requirement_Type);
                    SpiraSoapService.RemoteDataMapping[] requirementComponentMappings = spiraImportExport.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Requirement_Component);

                    //Get the list of custom properties configured for this project and the corresponding data mappings

                    //First for incidents
                    RemoteCustomProperty[] incidentCustomProperties = spiraImportExport.CustomProperty_RetrieveForArtifactType((int)Constants.ArtifactType.Incident, false);
                    Dictionary<int, RemoteDataMapping> incidentCustomPropertyMappingList = new Dictionary<int, SpiraSoapService.RemoteDataMapping>();
                    Dictionary<int, RemoteDataMapping[]> incidentCustomPropertyValueMappingList = new Dictionary<int, SpiraSoapService.RemoteDataMapping[]>();
                    foreach (SpiraSoapService.RemoteCustomProperty customProperty in incidentCustomProperties)
                    {
                        //Get the mapping for this custom property
                        if (customProperty.CustomPropertyId.HasValue)
                        {
                            SpiraSoapService.RemoteDataMapping customPropertyMapping = spiraImportExport.DataMapping_RetrieveCustomPropertyMapping(dataSyncSystemId, (int)Constants.ArtifactType.Incident, customProperty.CustomPropertyId.Value);
                            incidentCustomPropertyMappingList.Add(customProperty.CustomPropertyId.Value, customPropertyMapping);

                            //For list types need to also get the property value mappings
                            if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.List || customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.MultiList)
                            {
                                SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = spiraImportExport.DataMapping_RetrieveCustomPropertyValueMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident, customProperty.CustomPropertyId.Value);
                                incidentCustomPropertyValueMappingList.Add(customProperty.CustomPropertyId.Value, customPropertyValueMappings);
                            }
                        }
                    }

                    //Next for requirements
                    RemoteCustomProperty[] requirementCustomProperties = spiraImportExport.CustomProperty_RetrieveForArtifactType((int)Constants.ArtifactType.Requirement, false);
                    Dictionary<int, RemoteDataMapping> requirementCustomPropertyMappingList = new Dictionary<int, SpiraSoapService.RemoteDataMapping>();
                    Dictionary<int, RemoteDataMapping[]> requirementCustomPropertyValueMappingList = new Dictionary<int, SpiraSoapService.RemoteDataMapping[]>();
                    foreach (SpiraSoapService.RemoteCustomProperty customProperty in requirementCustomProperties)
                    {
                        //Get the mapping for this custom property
                        if (customProperty.CustomPropertyId.HasValue)
                        {
                            SpiraSoapService.RemoteDataMapping customPropertyMapping = spiraImportExport.DataMapping_RetrieveCustomPropertyMapping(dataSyncSystemId, (int)Constants.ArtifactType.Requirement, customProperty.CustomPropertyId.Value);
                            requirementCustomPropertyMappingList.Add(customProperty.CustomPropertyId.Value, customPropertyMapping);

                            //For list types need to also get the property value mappings
                            if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.List || customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.MultiList)
                            {
                                SpiraSoapService.RemoteDataMapping[] customPropertyValueMappings = spiraImportExport.DataMapping_RetrieveCustomPropertyValueMappings(dataSyncSystemId, (int)Constants.ArtifactType.Requirement, customProperty.CustomPropertyId.Value);
                                requirementCustomPropertyValueMappingList.Add(customProperty.CustomPropertyId.Value, customPropertyValueMappings);
                            }
                        }
                    }

                    //Now get the list of releases and incidents that have already been mapped
                    SpiraSoapService.RemoteDataMapping[] incidentMappings = spiraImportExport.DataMapping_RetrieveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident);
                    SpiraSoapService.RemoteDataMapping[] releaseMappings = spiraImportExport.DataMapping_RetrieveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release);
                    
                    //If we don't have a last-sync data, default to 1/1/1950
                    if (!lastSyncDate.HasValue)
                    {
                        lastSyncDate = DateTime.ParseExact("1/1/1950", "M/d/yyyy", CultureInfo.InvariantCulture);
                    }

                    string jiraProjectName = ""; //WINDSTREAM New Project Name for Incident

                    //Get the incidents in batches of 100
                    List<RemoteIncident> incidentList = new List<RemoteIncident>();
                    long incidentCount = spiraImportExport.Incident_Count(null);
                    for (int startRow = 1; startRow <= incidentCount; startRow += Constants.INCIDENT_PAGE_SIZE_SPIRA)
                    {
                        //RemoteIncident[] incidentBatch = spiraImportExport.Incident_RetrieveNew(lastSyncDate.Value, startRow, Constants.INCIDENT_PAGE_SIZE_SPIRA);
                        /************************************ WINDSTREAM *****************************************************************/
                        var filters = new List<RemoteFilter>();
                        var sort = new RemoteSort() { PropertyName = "Name", SortAscending = true };

                        RemoteIncident[] incidentBatch = spiraImportExport.Incident_Retrieve(filters.ToArray(), sort, startRow, Constants.INCIDENT_PAGE_SIZE_SPIRA);
                        /************************************ WINDSTREAM *****************************************************************/

                        for (int index = 0; index < incidentBatch.Length; index++)
                        {
                            /************************************ WINDSTREAM *****************************************************************/

                            //Get the Incident Custom Property for the JIRA Sync flag  
                            var customProp = incidentBatch[index].CustomProperties.Where(i => i.Definition.Name.Equals("JIRA Sync Flag"));
                            try
                            {
                                //Get the Integer value for the JIRA Sync flag custom property
                                int? syncFlagValue = customProp.FirstOrDefault().IntegerValue;

                                //Get  the Incident Custom Property for the JIRA Project ID
                                customProp = incidentBatch[index].CustomProperties.Where(i => i.Definition.Name.Equals("Jira Project ID"));

                                //Get the JIRA Project Name for the custom Property
                                jiraProjectName = customProp.FirstOrDefault().StringValue;

                                string flagValueName = "";
                                //Check for a valid JIRA sync flag value
                                if (syncFlagValue != null)
                                {
                                    //Loop through the Remote Custom Properties
                                    foreach (RemoteCustomProperty remoteCustomProp in incidentCustomProperties)
                                    {
                                        //Check for JIRA Sync Flag Remote custom Property
                                        if (remoteCustomProp.Name == "JIRA Sync Flag")
                                        {
                                            //Check the first list value and assign if there is a match
                                            RemoteCustomListValue listValue = (RemoteCustomListValue)remoteCustomProp.CustomList.Values.GetValue(0);
                                            if (listValue.CustomPropertyValueId == syncFlagValue)
                                            {
                                                flagValueName = listValue.Name;
                                            }
                                            else //Otherwise ...get the second list value
                                            {
                                                listValue = (RemoteCustomListValue)remoteCustomProp.CustomList.Values.GetValue(1);
                                                flagValueName = listValue.Name;
                                            }
                                            //If JIRA Sync flag is set to "Y" and the JIRA Project Name is not blank... add to the Incident List to be processed
                                            if (flagValueName == "Y" && jiraProjectName != null)
                                                incidentList.Add(incidentBatch[index]);

                                        }
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                //Catch any exception and Log and continue execution
                                LogErrorEvent("Exception Getting Custom JIRA Properties: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                            }
                            /************************************ WINDSTREAM *****************************************************************/
                        }

                    }
                    LogTraceEvent(eventLog, "Found " + incidentList.Count + " new incidents in " + productName, EventLogEntryType.Information);


                    //Create the mapping collections to hold any new items that need to get added to the mappings
                    //or any old items that need to get removed from the mappings
                    List<RemoteDataMapping> newIncidentMappings = new List<SpiraSoapService.RemoteDataMapping>();
                    List<RemoteDataMapping> newReleaseMappings = new List<SpiraSoapService.RemoteDataMapping>();
                    List<RemoteDataMapping> oldReleaseMappings = new List<SpiraSoapService.RemoteDataMapping>();

                    //Iterate through each record
                    foreach (SpiraSoapService.RemoteIncident remoteIncident in incidentList)
                    {
                        try
                        {
                            //WINDSTREAM - Get Custom Property - Jira Project ID
                            var customProp = remoteIncident.CustomProperties.Where(i => i.Definition.Name.Equals("Jira Project ID"));
                            ///WINDSTREAM - Retrieve JIRA Project Name
                            jiraProjectName = customProp.FirstOrDefault().StringValue;
                            ///WINDSTREAM - Check for non blank project name
                            if (jiraProjectName != null)
                            {
                                ///WINDSTREAM - Get the JIRA Project object make Project name upper case 
                                JiraProject customJiraProject = jiraProjects.FirstOrDefault(j => j.Key.Equals(jiraProjectName.ToUpper()));

                                if (customJiraProject == null)
                                {
                                    //We can't connect so go to next project
                                    LogErrorEvent("Unable to connect to Custom JIRA project " + customJiraProject.Name + ", please check that the JIRA login '" + externalLogin + "' has the permissions to access this project", EventLogEntryType.Error);
                                    continue;
                                }
                                else
                                {
                                    ProcessIncident(projectId, spiraImportExport, remoteIncident, newIncidentMappings, newReleaseMappings, oldReleaseMappings, incidentCustomPropertyMappingList,
                                                incidentCustomPropertyValueMappingList, incidentCustomProperties, incidentMappings, customJiraProject, jiraManager, productName, incidentSeverityMappings,
                                                incidentPriorityMappings, incidentStatusMappings, incidentTypeMappings, userMappings, releaseMappings, incidentComponentMappings);
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            //Log and continue execution
                            LogErrorEvent("Error Adding " + productName + " Incident to JIRA: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
                        }
                    }

                    //Re-authenticate with Spira and reconnect to the project to avoid potential timeout issues
                    success = spiraImportExport.Connection_Authenticate2(internalLogin, internalPassword, DATA_SYNC_NAME);
                    if (!success)
                    {
                        //We can't authenticate so end
                        LogErrorEvent("Unable to authenticate with " + productName + " API, stopping data-synchronization", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }
                    success = spiraImportExport.Connection_ConnectToProject(projectId);
                    if (!success)
                    {
                        //We can't connect so go to next project
                        LogErrorEvent("Unable to connect to " + productName + " project PR" + projectId + ", please check that the " + productName + " login has the appropriate permissions", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }

                    //Finally we need to update the mapping data on the server before starting the second phase
                    //of the data-synchronization
                    //At this point we have potentially added incidents, added releases and removed releases
                    spiraImportExport.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident, newIncidentMappings.ToArray());
                    spiraImportExport.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release, newReleaseMappings.ToArray());
                    //WINDSTREAM do not remove mappings spiraImportExport.DataMapping_RemoveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release, oldReleaseMappings.ToArray());

                    //**** Next we need to see if any of the previously mapped incidents/requirements has changed or any new items added to JIRA ****
                    incidentMappings = spiraImportExport.DataMapping_RetrieveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident);
                    RemoteDataMapping[] requirementMappings = spiraImportExport.DataMapping_RetrieveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Requirement);

                    //Need to create a list to hold any new releases, requirements and incidents
                    newIncidentMappings = new List<SpiraSoapService.RemoteDataMapping>();
                    newReleaseMappings = new List<SpiraSoapService.RemoteDataMapping>();
                    List<RemoteDataMapping> newRequirementMappings = new List<RemoteDataMapping>();

                    //Call the JIRA API to get the list of recently added/changed issues
                    //We need to convert from UTC to local time since the JIRA REST API expects the date-time in local time
                    DateTime filterDate = lastSyncDate.Value.AddHours(-timeOffsetHours).ToLocalTime();
                    string jqlDateTime = filterDate.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture.DateTimeFormat);
                    // WINDSTREAM - old JQL filter send to JIRA to get updated records (Removed filter for project)
                    // WINDSTREAM string jqlFilter = "project = '" + jiraProject.Key.Replace("'", "''") + "' and updated >= '" + jqlDateTime + "' order by updated asc";
                    string jqlFilter = "updated >= '" + jqlDateTime + "' order by updated asc";

                    //TODO: In the future we should consider getting the JIRA user's timezone and explicitly converting to
                    //that timezone. However the JIRA timezone names are not the same as .NET (e.g. "timeZone":"America/New_York")

                    //This collection will store the complete JIRA issues
                    List<JiraIssue> jiraIssues = new List<JiraIssue>();

                    //First get just the list of issue keys (e.g. DEMO-1) in the pagination range
                    bool moreResults = true;
                    int startIndex = 0;
                    while (moreResults)
                    {
                        List<JiraIssue> jiraIssueKeys = jiraManager.GetIssues(jqlFilter, jiraKeyFields, startIndex, Constants.INCIDENT_PAGE_SIZE_JIRA);
                        foreach (JiraIssue jiraIssueKey in jiraIssueKeys)
                        {
                            //Now get the full record, including non-navigable fields and comments
                            JiraIssue jiraIssue = jiraManager.GetIssueByKey(jiraIssueKey.Key.ToString(), this.jiraCreateMetaData);
                            jiraIssues.Add(jiraIssue);
                        }
                        if (jiraIssueKeys.Count < Constants.INCIDENT_PAGE_SIZE_JIRA)
                        {
                            moreResults = false;
                        }
                        else
                        {
                            startIndex += Constants.INCIDENT_PAGE_SIZE_JIRA;
                        }
                    }
                    LogTraceEvent(eventLog, "Found " + jiraIssues.Count + " new issues in JIRA", EventLogEntryType.Information);

                    //Re-authenticate with Spira and reconnect to the project to avoid potential timeout issues
                    success = spiraImportExport.Connection_Authenticate2(internalLogin, internalPassword, DATA_SYNC_NAME);
                    if (!success)
                    {
                        //We can't authenticate so end
                        LogErrorEvent("Unable to authenticate with " + productName + " API, stopping data-synchronization", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }
                    success = spiraImportExport.Connection_ConnectToProject(projectId);
                    if (!success)
                    {
                        //We can't connect so go to next project
                        LogErrorEvent("Unable to connect to " + productName + " project PR" + projectId + ", please check that the " + productName + " login has the appropriate permissions", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }

                    //Iterate through these items
                    foreach (JiraIssue jiraIssue in jiraIssues)
                    {
                                                 
                            //See if this incident should be treated as an incident or requirement
                            if (jiraIssue.Fields.IssueType.Id.HasValue && this.requirementIssueTypes.Contains(jiraIssue.Fields.IssueType.Id.Value))
                            {
                                ProcessJiraIssueAsRequirement(projectId, spiraImportExport, jiraIssue, newRequirementMappings, newReleaseMappings, oldReleaseMappings, requirementCustomPropertyMappingList, requirementCustomPropertyValueMappingList, requirementCustomProperties, requirementMappings, jiraManager, productName, requirementImportanceMappings, requirementStatusMappings, userMappings, releaseMappings, requirementTypeMappings, requirementComponentMappings);
                            }
                            else
                            {
                                ProcessJiraIssueAsIncident(projectId, spiraImportExport, jiraIssue, newIncidentMappings, newReleaseMappings, oldReleaseMappings, incidentCustomPropertyMappingList, incidentCustomPropertyValueMappingList, incidentCustomProperties, incidentMappings, jiraManager, productName, incidentSeverityMappings, incidentPriorityMappings, incidentStatusMappings, incidentTypeMappings, userMappings, releaseMappings, incidentComponentMappings);
                            }
                         
                    }

                    //Re-authenticate with Spira and reconnect to the project to avoid potential timeout issues
                    success = spiraImportExport.Connection_Authenticate2(internalLogin, internalPassword, DATA_SYNC_NAME);
                    if (!success)
                    {
                        //We can't authenticate so end
                        LogErrorEvent("Unable to authenticate with " + productName + " API, stopping data-synchronization", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }
                    success = spiraImportExport.Connection_ConnectToProject(projectId);
                    if (!success)
                    {
                        //We can't connect so go to next project
                        LogErrorEvent("Unable to connect to " + productName + " project PR" + projectId + ", please check that the " + productName + " login has the appropriate permissions", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }

                    //Finally we need to update the mapping data on the server
                    //At this point we have potentially added releases, requirements and incidents
                    spiraImportExport.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release, newReleaseMappings.ToArray());
                    spiraImportExport.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident, newIncidentMappings.ToArray());
                    spiraImportExport.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Requirement, newRequirementMappings.ToArray());
                }

                //The following code is only needed during debugging
                LogTraceEvent(eventLog, "Import Completed", EventLogEntryType.Warning);

                //Mark objects ready for garbage collection
                spiraImportExport = null;
                jiraManager = null;

                //Let the service know that we ran correctly
                return ServiceReturnType.Success;
            }
            catch (Exception exception)
            {
                //Log the exception and return as a failure
                LogErrorEvent("General Error: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
                return ServiceReturnType.Error;
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

        /// <summary>
        /// Logs a trace event message if the configuration option is set
        /// </summary>
        /// <param name="eventLog">The event log handle</param>
        /// <param name="message">The message to log</param>
        /// <param name="type">The type of event</param>
        public void LogTraceEvent(string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            if (this.eventLog != null)
            {
                LogTraceEvent(this.eventLog, message, type);
            }
        }

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

        // Implement IDisposable.
        // Do not make this method virtual.
        // A derived class should not be able to override this method.
        public void Dispose()
        {
            Dispose(true);
            // Take yourself off the Finalization queue 
            // to prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        // Use C# destructor syntax for finalization code.
        // This destructor will run only if the Dispose method 
        // does not get called.
        // It gives your base class the opportunity to finalize.
        // Do not provide destructors in types derived from this class.
        ~DataSync()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        /// <summary>
        /// Finds a user mapping entry from the internal id
        /// </summary>
        /// <param name="internalId">The internal id</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <returns>The matching entry or Null if none found</returns>
        /// <remarks>If we are auto-mapping users, it will lookup the user-id instead</remarks>
        protected RemoteDataMapping FindUserMappingByInternalId(int internalId, RemoteDataMapping[] dataMappings, SoapServiceClient client)
        {
            if (this.autoMapUsers)
            {
                RemoteUser remoteUser = client.User_RetrieveById(internalId);
                if (remoteUser == null)
                {
                    return null;
                }
                RemoteDataMapping userMapping = new RemoteDataMapping();
                userMapping.InternalId = remoteUser.UserId.Value;
                userMapping.ExternalKey = remoteUser.UserName;
                return userMapping;
            }
            else
            {
                return InternalFunctions.FindMappingByInternalId(internalId, dataMappings);
            }
        }

        /// <summary>
        /// Finds a user mapping entry from the external key
        /// </summary>
        /// <param name="externalKey">The external key</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <returns>The matching entry or Null if none found</returns>
        /// <remarks>If we are auto-mapping users, it will lookup the username instead</remarks>
        protected RemoteDataMapping FindUserMappingByExternalKey(string externalKey, RemoteDataMapping[] dataMappings, SoapServiceClient client)
        {
            if (this.autoMapUsers)
            {
                try
                {
                    RemoteUser remoteUser = client.User_RetrieveByUserName(externalKey, true);
                    if (remoteUser == null)
                    {
                        return null;
                    }
                    RemoteDataMapping userMapping = new RemoteDataMapping();
                    userMapping.InternalId = remoteUser.UserId.Value;
                    userMapping.ExternalKey = remoteUser.UserName;
                    return userMapping;
                }
                catch (Exception)
                {
                    //User could not be found so return null
                    return null;
                }
            }
            else
            {
                return InternalFunctions.FindMappingByExternalKey(externalKey, dataMappings);
            }
        }

        // Dispose(bool disposing) executes in two distinct scenarios.
        // If disposing equals true, the method has been called directly
        // or indirectly by a user's code. Managed and unmanaged resources
        // can be disposed.
        // If disposing equals false, the method has been called by the 
        // runtime from inside the finalizer and you should not reference 
        // other objects. Only unmanaged resources can be disposed.
        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                // If disposing equals true, dispose all managed 
                // and unmanaged resources.
                if (disposing)
                {
                    //Remove the event log reference
                    this.eventLog = null;
                }
                // Release unmanaged resources. If disposing is false, 
                // only the following code is executed.

                //This class doesn't have any unmanaged resources to worry about
            }
            disposed = true;
        }
    }
}