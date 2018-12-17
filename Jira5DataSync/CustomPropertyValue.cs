using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Inflectra.SpiraTest.PlugIns.Jira5DataSync.SpiraSoapService;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync
{
    /// <summary>
    /// Represents the different possible types of custom property values
    /// </summary>
    public class CustomPropertyValue
    {
        /// <summary>
        /// The various custom property types
        /// </summary>
        public enum CustomPropertyTypeEnum
        {
            Text = 1,
            Integer = 2,
            Decimal = 3,
            Boolean = 4,
            Date = 5,
            List = 6,
            MultiList = 7,
            User = 8
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        public CustomPropertyValue()
        {
        }

        /// <summary>
        /// Populates from the equivalent API object
        /// </summary>
        /// <param name="remoteArtifactCustomProperty">The api custom property value object</param>
        public CustomPropertyValue(RemoteArtifactCustomProperty remoteArtifactCustomProperty)
        {
            this.StringValue = remoteArtifactCustomProperty.StringValue;
            this.IntegerValue = remoteArtifactCustomProperty.IntegerValue;
            this.BooleanValue = remoteArtifactCustomProperty.BooleanValue;
            this.DateTimeValue = remoteArtifactCustomProperty.DateTimeValue;
            this.DecimalValue = remoteArtifactCustomProperty.DecimalValue;
            //We don't automatically convert multi-list values since they need to be data-mapped
            if (remoteArtifactCustomProperty.Definition != null)
            {
                this.CustomPropertyType = (CustomPropertyTypeEnum)remoteArtifactCustomProperty.Definition.CustomPropertyTypeId;
            }
        }

        /// <summary>
        /// The type of custom property
        /// </summary>
        public CustomPropertyTypeEnum CustomPropertyType { get; set; }

        /// <summary>
        /// The value of a string custom property (or single-select list)
        /// </summary>
        public string StringValue { get; set; }

        /// <summary>
        /// The value of an integer custom property
        /// </summary>
        public int? IntegerValue { get; set; }

        /// <summary>
        /// The value of a boolean custom property
        /// </summary>
        public bool? BooleanValue { get; set; }

        /// <summary>
        /// The value of a date-time custom property
        /// </summary>
        public DateTime? DateTimeValue { get; set; }

        /// <summary>
        /// The value of a decimal custom property
        /// </summary>
        public Decimal? DecimalValue { get; set; }

        /// <summary>
        /// The value of a multi-list custom property
        /// </summary>
        public List<string> MultiListValue { get; set; }
    }
}
