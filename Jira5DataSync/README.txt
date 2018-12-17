JIRA 5.x Plug-In for SpiraTeam(R)
======== ======= === ============

The latest documentation for using this Data-Synchronization Plug-In
can be found at http://www.inflectra.com/SpiraTeam/Documentation.aspx

The PDF guide contains details instructions for importing and synchronizing
data between SpiraTest(R), SpiraPlan(R) or SpiraTeam(R) and a variety of other systems, including Atlassian JIRA 5.x.

(C) Copyright 2006-2013 Inflectra Corporation

Release Notes
======= =====

v4.0.0
======
~ Migrated to the Spira 4.0 API and the JIRA 5.x REST API
! Fixes issue where queries against JIRA that exceeded the allowed record count failed
+ Added the ability to have specific JIRA issue types to come across to Spira as requirements (instead of incidents)
+ Option to allow Spira incident-to-incident associations to be added as new JIRA issue links
+ Added the ability to have Spira URL attachments get added to JIRA issues as web links
~ The links from a JIRA issue back to Spira (incident, requirement and test run) are now real web links rather than
  being embedded in the JIRA issue description
+ Support for new Spira 4.0 custom property types (multi-select, user, integer, decimal, date, etc.)
