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

//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.Practices.EnterpriseLibrary.Logging;
using Microsoft.Practices.EnterpriseLibrary.Logging.TraceListeners;
using ShoutLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace ShoutService
{
    public partial class ShoutService : ServiceBase
    {
        public ShoutService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            _workerThread = new Thread(new ThreadStart(this.ShoutLoop));
            _workerThread.Start();
        }

        protected override void OnStop()
        {
            _cancellationTokenSource.Cancel();
            _workerThread.Join();
        }

        protected void ShoutLoop()
        {
            // Configue a LogWriter to write to the Windows Event Log.
            var eventLog = new EventLog("Application", ".", "ShoutService");
            var eventLogTraceListener = new FormattedEventLogTraceListener(eventLog)
            {
                Filter = new SeverityFilter(new TraceEventType[] {
                        TraceEventType.Error, TraceEventType.Information, TraceEventType.Warning})
            };
            var config = new LoggingConfiguration();
            config.AddLogSource("All", SourceLevels.All, true)
              .AddTraceListener(eventLogTraceListener);
            // A using statement ensures the log gets flushed when this process exits.
            using (var logWriter = new LogWriter(config))
            {
                var shouter = new Shouter(logWriter);
                do
                {
                    shouter.Shout(_cancellationTokenSource.Token);
                } while (!_cancellationTokenSource.Token.IsCancellationRequested);
            }
        }

        private Thread _workerThread;
        private readonly System.Threading.CancellationTokenSource _cancellationTokenSource =
            new System.Threading.CancellationTokenSource();
    }

    // We don't want to fill the Windows Event Log with verbose messages
    // Every second.  So, create a filter that will filter by severity.
    internal class SeverityFilter : TraceFilter
    {
        // Only messages with matching severities will be logged.
        public SeverityFilter(IEnumerable<TraceEventType> severities)
        {
            _severities = severities.ToArray();
        }

        public override bool ShouldTrace(TraceEventCache cache, string source,
            TraceEventType eventType, int id, string formatOrMessage, object[] args,
            object data1, object[] data)
        {
            return _severities.Contains(eventType);
        }

        private readonly TraceEventType[] _severities;
    };
}
