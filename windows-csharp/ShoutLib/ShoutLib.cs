// Copyright 2015 Google Inc. All Rights Reserved.
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
using Google.Apis.Pubsub.v1;
using Google.Apis.Pubsub.v1.Data;
using Google.Apis.Services;
using Microsoft.Practices.EnterpriseLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace ShoutLib
{
    /// <summary>
    /// The worker class that converts lowercase to uppercase.
    /// </summary>
    public class Shouter
    {
        /// <summary>
        /// Parameters that configure Shouter.
        /// </summary>
        public class Initializer
        {
            /// <summary>
            /// The project id as listed in the Google Developer Console.
            /// </summary>
            public string ProjectId = Constants.ProjectId;

            /// <summary>
            /// The name of the pubsub subscription that serves as our work queue.
            /// </summary>
            public string SubscriptionName = Constants.Subscription;

            /// <summary>
            /// Various status notices will be written here.  May be null.
            /// </summary>
            public LogWriter LogWriter;

            /// <summary>
            /// Call <c>InitializeServices()</c> for an easy way to fill.
            /// </summary>
            public PubsubService PubsubService;

            /// <summary>
            /// The authenticated http client we use to call our own app engine code.
            /// Call <c>InitializeServices()</c> for an easy way to fill.
            /// </summary>
            public Google.Apis.Http.ConfigurableHttpClient HttpClient;

            /// <summary>
            /// A random number generator.
            /// </summary>
            public System.Random Random;
        }

        private Initializer init;

        public Shouter(LogWriter logWriter = null)
        {
            init = new Initializer();
            init.LogWriter = logWriter;
            init.Random = new Random();
            var authInit = new Auth.Initializer();
            authInit.LogWriter = logWriter;
            var credentials = Auth.GetCredentials(authInit);
            init.PubsubService = new PubsubService(new BaseClientService.Initializer()
            {
                ApplicationName = Constants.UserAgent,
                HttpClientInitializer = credentials,
            });
            var args = new Google.Apis.Http.CreateHttpClientArgs
            {
                ApplicationName = Constants.UserAgent,
                GZipEnabled = true,
            };
            args.Initializers.Add(credentials);
            var factory = new Google.Apis.Http.HttpClientFactory();
            init.HttpClient = factory.CreateHttpClient(args);
        }

        /// <summary>
        /// Waits for a task on the queue.  Converts the text to uppercase and posts the results
        /// to the browser's topic.
        /// </summary>
        /// <returns>
        /// The number of tasks pulled from the queue, or -1 if an expected error
        /// occurred.
        /// </returns>
        public int ShoutOrThrow(System.Threading.CancellationToken cancellationToken)
        {
            // Pull a task from the queue.
            WriteLog("Pulling tasks...", TraceEventType.Verbose);
            var pullTask = init.PubsubService.Projects.Subscriptions.Pull(
                new PullRequest()
            {
                MaxMessages = 1,
                ReturnImmediately = false
            }, MakeSubscriptionPath(init.SubscriptionName)).ExecuteAsync();
            Task.WaitAll(new Task[] { pullTask }, cancellationToken);
            var pullResponse = pullTask.Result;
            int messageCount = pullResponse.ReceivedMessages == null ? 0
                : pullResponse.ReceivedMessages.Count;
            WriteLog("Received " + messageCount + " tasks.", TraceEventType.Information);
            if (messageCount < 1)
                return 0;  // No tasks pulled.  Nothing to do.

            // Examine the received message.
            var task = pullResponse.ReceivedMessages[0];
            var attributes = task.Message.Attributes;
            string postStatusUrl;
            string postStatusToken;
            DateTime taskDeadline;
            try
            {
                postStatusUrl = attributes["postStatusUrl"];
                postStatusToken = attributes["postStatusToken"];
                long unixDeadline = Convert.ToInt64(attributes["deadline"]);
                taskDeadline = FromUnixTime(unixDeadline);
            }
            catch (Exception e)
            {
                WriteLog("Bad task attributes.\n" + e.ToString(), TraceEventType.Warning);
                DeleteTask(task.AckId);
                return -1;
            }

            // Tell the world we are shouting this task.
            PublishStatus(postStatusUrl, postStatusToken, "shouting");
            WriteLog("Shouting " + postStatusUrl, TraceEventType.Verbose);

            try
            {
                // Decode the payload, the string we want to shout.
                byte[] data = Convert.FromBase64String(task.Message.Data);
                string decodedString = Encoding.UTF8.GetString(data);

                // Watch the clock and cancellation token as we work.  We need to extend the
                // ack deadline if the task takes a while.
                var tenSeconds = TimeSpan.FromSeconds(10);
                DateTime ackDeadline = DateTime.UtcNow + tenSeconds;
                ThrowIfAborted throwIfAborted = () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var now = DateTime.UtcNow;
                    if (taskDeadline < now)
                        throw new FatalException("Task timed out.");
                    if (ackDeadline < now)
                    {
                        // Tell the queue we need more time:
                        WriteLog("Need more time...", TraceEventType.Verbose);
                        init.PubsubService.Projects.Subscriptions.ModifyAckDeadline(
                            new ModifyAckDeadlineRequest
                        {
                            AckIds = new string[] { task.AckId },
                            AckDeadlineSeconds = 15,
                        }, MakeSubscriptionPath(init.SubscriptionName)).Execute();
                        ackDeadline = now + tenSeconds;
                    }
                };

                // Shout it.
                string upperText = ShoutString(decodedString, throwIfAborted);

                // Publish the result.
                PublishStatus(postStatusUrl, postStatusToken, "success", upperText);
                DeleteTask(task.AckId);
                return 1;
            }
            catch (OperationCanceledException)
            {
                return 1;  // Service stopped.  Nothing to report.
            }
            catch (FatalException e)
            {
                WriteLog("Fatal exception while shouting:\n" + e.Message, TraceEventType.Error);
                DeleteTask(task.AckId);
                PublishStatus(postStatusUrl, postStatusToken, "fatal", e.Message);
                return -1;
            }
            catch (Exception e)
            {
                // Something went wrong while shouting.  Report the error.
                WriteLog("Exception while shouting:\n" + e.Message, TraceEventType.Error);
                PublishStatus(postStatusUrl, postStatusToken, "error", e.Message);
                return -1;
            }
        }

        private delegate void ThrowIfAborted();

        /// <summary>
        /// Converts a string from lowercase to uppercase, taking an extra long time.
        /// During that time, it periodically calls testCancel so that the caller can
        /// abort the operation if it's taking too long.
        /// </summary>
        /// <returns>An uppercase string.</returns>
        private string ShoutString(string text, ThrowIfAborted throwIfAborted)
        {
            string upperText = text.ToUpper();

            // Pretend like we're working hard and taking time.  Time is a function of
            // the number of letters in the word.
            var workDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(
                 upperText.Length * (int)Math.Log(upperText.Length));
            var oneSecond = TimeSpan.FromSeconds(1);
            while (workDeadline > DateTime.UtcNow)
            {
                throwIfAborted();
                System.Threading.Thread.Sleep(oneSecond);
            }

            // Simulate different kinds of errors depending on text.
            if (upperText.Contains("CHICKEN"))
            {
                // Simulate a fatal error that cannot possible be handled.
                throw new FatalException("Oh no!  Not chickens!");
            }
            if (upperText.Contains("CORN"))
            {
                // Simulate a flaky error that happens sometimes, but not always.
                if (init.Random.Next(3) > 0)
                    throw new CornException();
            }
            if (upperText.Contains("COW"))
            {
                // Simulate an error that eventually times out with error.
                throw new CowException();
            }
            return upperText;
        }

        /// <summary>
        /// Posts a message back to the browser, via App Engine.
        /// </summary>
        /// <param name="postStatusUrl">Where to post the status to?</param>
        /// <param name="postStatusToken">An additional value to post.</param>
        /// <param name="status">The status to report to the browser.</param>
        /// <param name="result">The result or error message to report to the browser.</param>
        private void PublishStatus(string postStatusUrl, string postStatusToken,
            string status, string result = null)
        {
            var content = new System.Net.Http.FormUrlEncodedContent(
                new Dictionary<string, string>() {
                {"status", status},
                {"token", postStatusToken},
                {"result", result},
                {"host", System.Environment.MachineName}});
            var task = init.HttpClient.PostAsync(postStatusUrl, content);
            task.Wait();
            if (task.Result.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new FatalException(task.Result.ToString());
            }
        }

        /// <summary>
        /// Deletes a task from the queue.
        /// </summary>
        /// <param name="taskId">The id of the task to delete.</param>
        private void DeleteTask(string taskId)
        {
            WriteLog("Deleting task...", TraceEventType.Verbose);
            init.PubsubService.Projects.Subscriptions.Acknowledge(new AcknowledgeRequest()
            {
                AckIds = new string[] { taskId }
            }, MakeSubscriptionPath(init.SubscriptionName)).Execute();
        }

        /// <summary>
        /// Waits for a task on the queue.  Converts the text to uppercase and posts the results
        /// to the browser's topic.
        /// </summary>
        /// <remarks>
        /// Nothing more than a wrapper around ShoutOrThrow() to catch unexpected exceptions.
        /// </remarks>
        /// <returns>The number of tasks pulled, or -1 if an error occurred.</returns>
        public int Shout(System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                return ShoutOrThrow(cancellationToken);
            }
            catch (Exception e)
            {
                // Something went really wrong.  Don't attempt to touch PublishStatus because
                // that could be what broke.
                WriteLog(e.Message + "\n" + e.StackTrace, TraceEventType.Error);
            }
            return -1;
        }

        /// <summary>
        /// Writes a message to the log.
        /// </summary>
        private void WriteLog(string message, TraceEventType severity = TraceEventType.Information)
        {
            if (init.LogWriter == null || !init.LogWriter.IsLoggingEnabled())
                return;
            LogEntry entry = new LogEntry()
            {
                Message = message,
                Severity = severity
            };
            init.LogWriter.Write(entry);
        }

        /// <summary>
        /// Converts a unix time (seconds since the epoch) to a DateTime.
        /// </summary>
        /// <param name="unixTime">Seconds since the epoch.</param>
        static public DateTime FromUnixTime(long unixTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(unixTime);
        }

        /// <summary>
        /// Makes a full subscription path given a subscription name.
        /// </summary>
        private string MakeSubscriptionPath(string subscription)
        {
            return "projects/" + init.ProjectId + "/subscriptions/" + subscription;
        }

        /// <summary>
        /// Makes a full topic path given a topic name.
        /// </summary>
        private string MakeTopicPath(string topic)
        {
            return "projects/" + init.ProjectId + "/topics/" + topic;
        }
    }

    /// <summary>
    /// Used to exercise error handling code.
    /// </summary>
    internal class CornException : Exception
    {
        public CornException()
            : base("I don't like corn flakes.") { }
    }

    /// <summary>
    /// Used to exercise error handling code.
    /// </summary>
    internal class CowException : Exception
    {
        public CowException()
            : base("Mooooooo.") { }
    }

    /// <summary>
    /// Used to exercise error handling code.
    /// </summary>
    internal class FatalException : Exception
    {
        public FatalException(string message)
            : base(message) { }
    }
}