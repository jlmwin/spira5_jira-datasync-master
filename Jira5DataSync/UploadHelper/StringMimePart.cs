using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Inflectra.SpiraTest.PlugIns.Jira5DataSync.UploadHelper
{
    public class StringMimePart : MimePart
    {
        Stream _data;

        public string StringData
        {
            set
            {
                _data = new MemoryStream(Encoding.UTF8.GetBytes(value));
            }
        }

        public override Stream Data
        {
            get
            {
                return _data;
            }
        }
    }
}
