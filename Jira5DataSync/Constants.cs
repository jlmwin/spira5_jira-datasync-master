using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync
{
    /// <summary>
    /// Stores the constants used by the DataSync class
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// The path to the Spira web service relative to the application's base URL
        /// </summary>
        public const string WEB_SERVICE_URL_SUFFIX = "/Services/v5_0/SoapService.svc";

        //Spira artifact prefixes
        public const string INCIDENT_PREFIX = "IN";
        public const string REQUIREMENT_PREFIX = "RQ";
        public const string TEST_RUN_PREFIX = "TR";

        //Other constants
        public const int INCIDENT_PAGE_SIZE_SPIRA = 15;
        public const int INCIDENT_PAGE_SIZE_JIRA = 100;
        public const int REQUIREMENT_STATUS_DEFAULT = 1; /* Requested */
        public const int REQUIREMENT_TYPE_DEFAULT = 4;  /* User Story */

        #region Enumerations

        /// <summary>
        /// The artifact types used in the data-sync
        /// </summary>
        public enum ArtifactType
        {
            Requirement = 1,
            TestCase = 2,
            Incident = 3,
            Release = 4,
            TestRun = 5,
            Task = 6,
            TestStep = 7,
            TestSet = 8,
            AutomationHost = 9,
            AutomationEngine = 10
        }

        /// <summary>
        /// The artifact field ids used in the data-sync
        /// </summary>
        public enum ArtifactField
        {
            Incident_Severity = 1,
            Incident_Priority = 2,
            Incident_Status = 3,
            Incident_Type = 4,
            Incident_Component = 148,
            Requirement_Status = 16,
            Requirement_Importance = 18,
            Requirement_Type = 140,
            Requirement_Component = 141
        }

        /// <summary>
        /// The various custom property types
        /// </summary>
        public enum CustomPropertyType
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
        /// The various custom property options
        /// </summary>
        public enum CustomPropertyOption
        {
            AllowEmpty = 1,
            MaxLength = 2,
            MinLength = 3,
            RichText = 4,
            Default = 5,
            MaxValue = 6,
            MinValue = 7,
            Precision = 8
        }

        /// <summary>
        /// The different types of attachment
        /// </summary>
        public enum AttachmentType
        {
            File = 1,
            URL = 2
        }

        /// <summary>
        /// Statuses
        /// </summary>
        public enum ReleaseStatusEnum
        {
            Planned = 1,
            InProgress = 2,
            Completed = 3,
            Closed = 4,
            Deferred = 5,
            Cancelled = 6
        }

        /// <summary>
        /// Release Types
        /// </summary>
        public enum ReleaseTypeEnum
        {
            MajorRelease = 1,
            MinorRelease = 2,
            Iteration = 3,
            Phase = 4
        }

        #endregion
    }
}
