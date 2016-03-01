// Copyright(c) 2016 Google Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not
// use this file except in compliance with the License. You may obtain a copy of
// the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
// License for the specific language governing permissions and limitations under
// the License.

using Microsoft.Practices.EnterpriseLibrary.Logging;
using Microsoft.Practices.EnterpriseLibrary.Logging.Formatters;
using Microsoft.Practices.EnterpriseLibrary.Logging.TraceListeners;
using ShoutLib;
using System;

namespace ShoutCmd
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            using (var logWriter = CreateStandardOutputLogWriter())
            {
                var shouter = new Shouter(logWriter);
                while (true)
                {
                    shouter.Shout(new System.Threading.CancellationToken());
                }
            }
        }

        private static LogWriter CreateStandardOutputLogWriter()
        {
            var traceListener = new FormattedTextWriterTraceListener(
                Console.OpenStandardOutput(), new TextFormatter("{timestamp}:\t{message}\n"));
            var config = new LoggingConfiguration();
            config.AddLogSource("StandardOutput", System.Diagnostics.SourceLevels.All, true)
                .AddAsynchronousTraceListener(traceListener);
            return new LogWriter(config);
        }
    }
}