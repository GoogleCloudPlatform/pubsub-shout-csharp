﻿// Copyright(c) 2016 Google Inc.
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

using Google.Apis.Pubsub.v1;
using Google.Apis.Pubsub.v1.Data;
using Google.Apis.Services;
using Microsoft.Practices.EnterpriseLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
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
            /// The name of the pubsub subscription where we pull shout
            /// request messages from.
            /// </summary>
            public string SubscriptionName = Constants.Subscription;

            /// <summary>
            /// Various status notices will be written here.  May be null.
            /// </summary>
            public LogWriter LogWriter;

            /// <summary>
            /// The PubsubService API.
            /// </summary>
            public PubsubService PubsubService;

            /// <summary>
            /// An http client we use to call our own app engine code.
            /// </summary>
            public HttpClient HttpClient;

            /// <summary>
            /// A random number generator.
            /// </summary>
            public System.Random Random;

            public static Initializer CreateDefault()
            {
                var init = new Initializer();
                init.Random = new Random();
                var credentials = Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefaultAsync().Result;
                credentials = credentials.CreateScoped(new[] { PubsubService.Scope.Pubsub });
                init.PubsubService = new PubsubService(new BaseClientService.Initializer()
                {
                    ApplicationName = Constants.UserAgent,
                    HttpClientInitializer = credentials,
                });
                init.HttpClient = new HttpClient();
                return init;
            }
        }

        private readonly Initializer _init;

        public Shouter(Initializer init = null)
        {
            _init = init ?? Initializer.CreateDefault();
        }

        public Shouter(LogWriter logWriter)
        {
            _init = Initializer.CreateDefault();
            _init.LogWriter = logWriter;
        }

        /// <summary>
        /// Waits for a shout request message to arrive in the Pub/Sub
        /// subscription.  Converts the text to uppercase and posts the results
        /// back to the website.
        /// </summary>
        /// <returns>
        /// The number of messages pulled from the subscription,
        /// or -1 if an expected error occurred.
        /// </returns>
        public int ShoutOrThrow(System.Threading.CancellationToken cancellationToken)
        {
            // Pull a shout request message from the subscription.
            string subscriptionPath = MakeSubscriptionPath(_init.SubscriptionName);
            WriteLog("Pulling shout request messages from " + subscriptionPath + "...",
                TraceEventType.Verbose);
            var pullRequest = _init.PubsubService.Projects.Subscriptions.Pull(
                new PullRequest()
                {
                    MaxMessages = 1,
                    ReturnImmediately = false
                }, subscriptionPath).ExecuteAsync();
            Task.WaitAny(new Task[] { pullRequest }, cancellationToken);
            var pullResponse = pullRequest.Result;

            int messageCount = pullResponse.ReceivedMessages == null ? 0
                : pullResponse.ReceivedMessages.Count;
            WriteLog("Received " + messageCount + " messages.",
                     TraceEventType.Information);
            if (messageCount < 1)
                return 0;  // Nothing pulled.  Nothing to do.

            // Examine the received message.
            var shoutRequestMessage = pullResponse.ReceivedMessages[0];
            var attributes = shoutRequestMessage.Message.Attributes;
            string postStatusUrl;
            string postStatusToken;
            DateTime requestDeadline;
            try
            {
                postStatusUrl = attributes["postStatusUrl"];
                postStatusToken = attributes["postStatusToken"];
                long unixDeadline = Convert.ToInt64(attributes["deadline"]);
                requestDeadline = FromUnixTime(unixDeadline);
            }
            catch (Exception e)
            {
                WriteLog("Bad shout request message attributes.\n" + e.ToString(),
                    TraceEventType.Warning);
                Acknowledge(shoutRequestMessage.AckId);
                return -1;
            }

            // Tell the world we are shouting this request.
            PublishStatus(postStatusUrl, postStatusToken, "shouting");
            WriteLog("Shouting " + postStatusUrl, TraceEventType.Verbose);

            try
            {
                // Decode the payload, the string we want to shout.
                byte[] data = Convert.FromBase64String(shoutRequestMessage.Message.Data);
                string decodedString = Encoding.UTF8.GetString(data);

                // Watch the clock and cancellation token as we work.  We need to extend the
                // ack deadline if the request takes a while.
                var tenSeconds = TimeSpan.FromSeconds(10);
                DateTime ackDeadline = DateTime.UtcNow + tenSeconds;
                ThrowIfAborted throwIfAborted = () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var now = DateTime.UtcNow;
                    if (requestDeadline < now)
                        throw new FatalException("Request timed out.");
                    if (ackDeadline < now)
                    {
                        // Tell the subscription we need more time:
                        WriteLog("Need more time...", TraceEventType.Verbose);
                        _init.PubsubService.Projects.Subscriptions.ModifyAckDeadline(
                            new ModifyAckDeadlineRequest
                            {
                                AckIds = new string[] { shoutRequestMessage.AckId },
                                AckDeadlineSeconds = 15,
                            }, MakeSubscriptionPath(_init.SubscriptionName)).Execute();
                        ackDeadline = now + tenSeconds;
                    }
                };

                // Shout it.
                string upperText = ShoutString(decodedString, throwIfAborted);

                // Publish the result.
                PublishStatus(postStatusUrl, postStatusToken, "success", upperText);
                Acknowledge(shoutRequestMessage.AckId);
                return 1;
            }
            catch (OperationCanceledException)
            {
                return 1;  // Service stopped.  Nothing to report.
            }
            catch (FatalException e)
            {
                WriteLog("Fatal exception while shouting:\n" + e.Message, TraceEventType.Error);
                Acknowledge(shoutRequestMessage.AckId);
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
                if (_init.Random.Next(3) > 0)
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
            var httpPost = _init.HttpClient.PostAsync(postStatusUrl, content);
            httpPost.Wait();
            if (httpPost.Result.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new FatalException(httpPost.Result.ToString());
            }
        }

        /// <summary>
        /// Removes a shout request message from the subscription.
        /// </summary>
        /// <param name="ackId">The id of the message to remove.</param>
        private void Acknowledge(string ackId)
        {
            WriteLog("Deleting shout request message...", TraceEventType.Verbose);
            _init.PubsubService.Projects.Subscriptions.Acknowledge(new AcknowledgeRequest()
            {
                AckIds = new string[] { ackId }
            }, MakeSubscriptionPath(_init.SubscriptionName)).Execute();
        }

        /// <summary>
        /// Waits for a shout request message to arrive in the Pub/Sub subscription.
        /// Converts the text to uppercase and posts the results
        /// back to the website.
        /// </summary>
        /// <remarks>
        /// Nothing more than a wrapper around ShoutOrThrow() to catch unexpected exceptions.
        /// </remarks>
        /// <returns>The number of messages pulled, or -1 if an error occurred.</returns>
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
            if (_init.LogWriter == null || !_init.LogWriter.IsLoggingEnabled())
                return;
            LogEntry entry = new LogEntry()
            {
                Message = message,
                Severity = severity
            };
            _init.LogWriter.Write(entry);
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
            return "projects/" + _init.ProjectId + "/subscriptions/" + subscription;
        }

        /// <summary>
        /// Makes a full topic path given a topic name.
        /// </summary>
        private string MakeTopicPath(string topic)
        {
            return "projects/" + _init.ProjectId + "/topics/" + topic;
        }
    }

    /// <summary>
    /// Used to exercise error handling code.
    /// </summary>
    internal class CornException : Exception
    {
        public CornException()
            : base("I don't like corn flakes.")
        { }
    }

    /// <summary>
    /// Used to exercise error handling code.
    /// </summary>
    internal class CowException : Exception
    {
        public CowException()
            : base("Mooooooo.")
        { }
    }

    /// <summary>
    /// Used to exercise error handling code.
    /// </summary>
    internal class FatalException : Exception
    {
        public FatalException(string message)
            : base(message)
        { }
    }
}