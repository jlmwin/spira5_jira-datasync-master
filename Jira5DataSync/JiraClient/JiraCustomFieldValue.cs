using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.JiraClient
{
    /// <summary>
    /// Represents a JIRA custom field
    /// </summary>
    public class JiraCustomFieldValue
    {
        public const string CUSTOM_FIELD_PREFIX = "customfield_";

        public JiraCustomFieldValue(string fieldName)
        {
            string idAsString = fieldName.Replace(CUSTOM_FIELD_PREFIX, "");
            int id;
            if (Int32.TryParse(idAsString, out id))
            {
                this.CustomFieldId = id;
            }
        }

        public JiraCustomFieldValue(int id)
        {
            this.CustomFieldId = id;
        }

        /// <summary>
        /// The name of the custom field
        /// </summary>
        public int CustomFieldId { get; set; }

        /// <summary>
        /// The id of the custom field
        /// </summary>
        public string CustomFieldName
        {
            get
            {
                return CUSTOM_FIELD_PREFIX + CustomFieldId;
            }
        }

        /// <summary>
        /// The type and value of the custom field
        /// </summary>
        public CustomPropertyValue Value { get; set; }
    }
}
